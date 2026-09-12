#!/bin/bash
# Сертификат TLS для шины NATS.
#
# Шина возит одноразовые ticket-ы на выпуск токенов (Identity.WebApi ↔ Users ↔ Identity),
# поэтому открытого трафика внутри overlay-сети быть не должно. Сервер слушает 4222 только
# по TLS (deploy/nats/nats-server.conf), клиенты подключаются с NatsTlsEnabled=true.
#
# Результат (в git не попадает, см. deploy/certs/.gitignore):
#   deploy/certs/nats/ca.pem         — корневой сертификат, его же монтируем сервисам
#   deploy/certs/nats/nats-cert.pem  — сертификат сервера (SAN: nats, localhost, 127.0.0.1)
#   deploy/certs/nats/nats-key.pem   — ключ сервера
#
# Скрипт идемпотентен: если сертификат уже есть и не просрочен, он ничего не трогает.
# Пересоздать принудительно: bash generate-nats-certs.sh --force
set -e

CERT_DIR="$(cd "$(dirname "$0")" && pwd)/certs/nats"
FORCE=0
[ "$1" = "--force" ] && FORCE=1

mkdir -p "$CERT_DIR"

if [ $FORCE -eq 0 ] && [ -f "$CERT_DIR/nats-cert.pem" ] && [ -f "$CERT_DIR/nats-key.pem" ] && [ -f "$CERT_DIR/ca.pem" ]; then
    if openssl x509 -checkend 86400 -noout -in "$CERT_DIR/nats-cert.pem" > /dev/null 2>&1; then
        echo "✅ Сертификат NATS уже есть: $CERT_DIR/nats-cert.pem"
        exit 0
    fi
    echo "♻️  Сертификат NATS просрочен или истекает — перевыпускаем..."
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

    openssl req -x509 -nodes -days 3650 -newkey rsa:2048 \
        -keyout "$CERT_DIR/ca-key.pem" \
        -out    "$CERT_DIR/ca.pem" \
        -subj "/C=RU/ST=Local/L=Local/O=Core Dev/CN=Core NATS CA" \
        -addext "basicConstraints=critical,CA:TRUE" \
        -addext "keyUsage=critical,keyCertSign,cRLSign" 2>/dev/null

    openssl req -nodes -newkey rsa:2048 \
        -keyout "$CERT_DIR/nats-key.pem" \
        -out    "$CERT_DIR/nats.csr" \
        -subj "/C=RU/ST=Local/L=Local/O=Core Dev/CN=nats" 2>/dev/null

    openssl x509 -req -days 825 \
        -in  "$CERT_DIR/nats.csr" \
        -CA  "$CERT_DIR/ca.pem" \
        -CAkey "$CERT_DIR/ca-key.pem" \
        -CAcreateserial \
        -out "$CERT_DIR/nats-cert.pem" \
        -extfile <(printf "subjectAltName=DNS:nats,DNS:localhost,IP:127.0.0.1,IP:::1\nextendedKeyUsage=serverAuth\n") 2>/dev/null

    rm -f "$CERT_DIR/nats.csr" "$CERT_DIR/ca.srl"

    echo "✅ Сертификат NATS создан (собственный CA)"
fi

echo ""
echo "💡 Сервисам в docker CA передаётся автоматически (NatsTlsCaFile=/etc/nats/certs/ca.pem),"
echo "   конфигурациям запуска из IDE — через .run/*.run.xml. Если сервис запускается"
echo "   как-то иначе, укажите тот же CA переменной окружения:"
echo "     export NatsTlsCaFile=\"$CERT_DIR/ca.pem\""
echo ""
echo "   Корневой сертификат mkcert стоит в доверенных на самой машине, но .NET на macOS"
echo "   на это полагаться не даёт — путь к ca.pem нужен и в этом случае."

chmod 644 "$CERT_DIR/ca.pem" "$CERT_DIR/nats-cert.pem"
# Ключ сервера читает процесс nats внутри контейнера — он работает не от root.
chmod 644 "$CERT_DIR/nats-key.pem"

echo "   📄 CA:          $CERT_DIR/ca.pem"
echo "   📄 Сертификат:  $CERT_DIR/nats-cert.pem"
echo "   🔑 Ключ:        $CERT_DIR/nats-key.pem"
