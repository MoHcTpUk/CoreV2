#!/bin/bash
# Сертификат TLS для шины NATS.
#
# Шина возит одноразовые ticket-ы на выпуск токенов (Identity.WebApi ↔ Users ↔ Identity),
# поэтому открытого трафика внутри overlay-сети быть не должно. Сервер слушает 4222 только
# по TLS (deploy/nats/nats-server.conf), клиенты подключаются с NatsTlsEnabled=true.
#
# Результат (в git не попадает, см. deploy/certs/.gitignore):
#   deploy/certs/nats/ca.pem         — корневой сертификат (0644); сервисам приложения монтируется
#                                      только этот файл
#   deploy/certs/nats/nats-cert.pem  — сертификат сервера (0644, SAN: nats, localhost, 127.0.0.1)
#   deploy/certs/nats/nats-key.pem   — ключ сервера (0600); нужен только контейнеру nats
#
# Закрытого ключа CA в deploy/certs/nats нет — этот каталог целиком смонтирован в контейнер nats.
#   * mkcert: ключ CA остаётся в $(mkcert -CAROOT) пользователя и сюда не копируется.
#   * openssl: CA создаётся во временном каталоге (mktemp -d), подписывает сертификат сервера
#     и УДАЛЯЕТСЯ вместе с каталогом сразу после подписи. Ключ нужен ровно на один выпуск,
#     а оставленный на диске он позволил бы десять лет выпускать сертификат `nats`, которому
#     доверяют все сервисы, — и смена пароля шины этого бы не отменила.
#     Следствие: перевыпуск (--force или истёкший сертификат) создаёт НОВЫЙ CA. После него
#     перезапустите nats и все сервисы приложения: до рестарта они держат прежний ca.pem.
#
# Скрипт идемпотентен: если сертификат уже есть и не просрочен, он ничего не выпускает
# (только убирает ключ CA, оставленный прежней версией скрипта, и выставляет права).
# Пересоздать принудительно: bash generate-nats-certs.sh --force
set -e

CERT_DIR="$(cd "$(dirname "$0")" && pwd)/certs/nats"
FORCE=0
[ "$1" = "--force" ] && FORCE=1

mkdir -p "$CERT_DIR"

# Права на файлы в CERT_DIR; вызывается и когда выпуск не понадобился.
secure_cert_dir() {
    # Прежняя версия скрипта оставляла ключ CA рядом с сертификатами.
    if [ -f "$CERT_DIR/ca-key.pem" ]; then
        rm -f "$CERT_DIR/ca-key.pem"
        echo "🧹 Удалён закрытый ключ CA из $CERT_DIR (каталог монтируется в контейнер nats)."
    fi

    chmod 644 "$CERT_DIR/ca.pem" "$CERT_DIR/nats-cert.pem"
    # Ключ сервера читает только процесс nats: образ nats запускается от root, 0600 ему достаточно.
    chmod 600 "$CERT_DIR/nats-key.pem"
}

REISSUE=0
if [ -f "$CERT_DIR/nats-cert.pem" ] && [ -f "$CERT_DIR/nats-key.pem" ] && [ -f "$CERT_DIR/ca.pem" ]; then
    REISSUE=1

    if [ $FORCE -eq 0 ]; then
        if openssl x509 -checkend 86400 -noout -in "$CERT_DIR/nats-cert.pem" > /dev/null 2>&1; then
            secure_cert_dir
            echo "✅ Сертификат NATS уже есть: $CERT_DIR/nats-cert.pem"
            exit 0
        fi
        echo "♻️  Сертификат NATS просрочен или истекает — перевыпускаем..."
    fi
fi

echo "🔐 Генерация TLS-сертификата для шины NATS..."

# mkcert предпочтительнее: его корневой сертификат уже стоит в доверенных на машине
# разработчика, поэтому сервисы, запущенные из IDE (профиль Local), подключаются к
# localhost:4223 без дополнительной настройки.
if command -v mkcert &> /dev/null; then
    mkcert -install
    mkcert -cert-file "$CERT_DIR/nats-cert.pem" \
           -key-file  "$CERT_DIR/nats-key.pem" \
           nats localhost 127.0.0.1 ::1
    cp "$(mkcert -CAROOT)/rootCA.pem" "$CERT_DIR/ca.pem"

    echo "✅ Сертификат NATS создан с mkcert"
else
    echo "⚠️  mkcert не найден — выпускаем собственный CA через openssl."

    # Ключи создаются сразу недоступными для остальных пользователей (mkcert делает это сам).
    umask 077

    # Всё, что нужно только на время подписи (ключ CA, CSR, серийный номер), живёт вне
    # смонтированного каталога и удаляется при любом выходе из скрипта, в том числе по ошибке.
    WORK_DIR="$(mktemp -d)"
    trap 'rm -rf "$WORK_DIR"' EXIT

    openssl req -x509 -nodes -days 3650 -newkey rsa:2048 \
        -keyout "$WORK_DIR/ca-key.pem" \
        -out    "$WORK_DIR/ca.pem" \
        -subj "/C=RU/ST=Local/L=Local/O=Core Dev/CN=Core NATS CA" \
        -addext "basicConstraints=critical,CA:TRUE" \
        -addext "keyUsage=critical,keyCertSign,cRLSign" 2>/dev/null

    openssl req -nodes -newkey rsa:2048 \
        -keyout "$WORK_DIR/nats-key.pem" \
        -out    "$WORK_DIR/nats.csr" \
        -subj "/C=RU/ST=Local/L=Local/O=Core Dev/CN=nats" 2>/dev/null

    openssl x509 -req -days 825 \
        -in  "$WORK_DIR/nats.csr" \
        -CA  "$WORK_DIR/ca.pem" \
        -CAkey "$WORK_DIR/ca-key.pem" \
        -CAserial "$WORK_DIR/ca.srl" \
        -CAcreateserial \
        -out "$WORK_DIR/nats-cert.pem" \
        -extfile <(printf "subjectAltName=DNS:nats,DNS:localhost,IP:127.0.0.1,IP:::1\nextendedKeyUsage=serverAuth\n") 2>/dev/null

    # Ключ CA в CERT_DIR не переносится: он удаляется вместе с WORK_DIR.
    mv -f "$WORK_DIR/ca.pem"        "$CERT_DIR/ca.pem"
    mv -f "$WORK_DIR/nats-cert.pem" "$CERT_DIR/nats-cert.pem"
    mv -f "$WORK_DIR/nats-key.pem"  "$CERT_DIR/nats-key.pem"

    rm -rf "$WORK_DIR"
    trap - EXIT

    echo "✅ Сертификат NATS создан (собственный CA, закрытый ключ CA удалён)"
fi

secure_cert_dir

echo ""
echo "💡 Сервисам в docker CA передаётся автоматически (NatsTlsCaFile=/etc/nats/certs/ca.pem),"
echo "   конфигурациям запуска из IDE — через .run/*.run.xml. Если сервис запускается"
echo "   как-то иначе, укажите тот же CA переменной окружения:"
echo "     export NatsTlsCaFile=\"$CERT_DIR/ca.pem\""
echo ""
echo "   Корневой сертификат mkcert стоит в доверенных на самой машине, но .NET на macOS"
echo "   на это полагаться не даёт — путь к ca.pem нужен и в этом случае."

echo "   📄 CA:          $CERT_DIR/ca.pem"
echo "   📄 Сертификат:  $CERT_DIR/nats-cert.pem"
echo "   🔑 Ключ:        $CERT_DIR/nats-key.pem"

if [ $REISSUE -eq 1 ]; then
    echo ""
    echo "⚠️  Сертификат ПЕРЕВЫПУЩЕН. Перезапустите nats и все сервисы приложения"
    echo "   (gateway, users, sample, identity, identity-webapi): до рестарта они держат прежние"
    echo "   ca.pem и сертификат сервера, а при выпуске через openssl CA теперь другой —"
    echo "   старый ca.pem новый сертификат не примет. Например, на stage:"
    echo "     docker service update --force infra_nats"
    echo "     for s in gateway users sample identity identity-webapi; do docker service update --force app_\$s; done"
fi
