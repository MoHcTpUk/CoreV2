#!/bin/bash
# Генерирует ключевой материал OpenIddict для Identity.WebApi на не-development стендах:
# сертификат подписи токенов и сертификат шифрования (он же защищает key ring DataProtection).
#
# Файлы кладутся в deploy/certs/ и НЕ попадают в git (deploy/certs/.gitignore).
# На сервер они переезжают как docker-секреты, содержимое которых нигде не коммитится:
#
#   bash deploy/generate-identity-certs.sh --create-secrets
#
# Без флага скрипт только генерирует файлы и печатает команды `docker secret create`.
#
# Ротация: сертификат шифрования защищает key ring DataProtection. Если заменить его,
# ранее сохранённые ключи расшифровать будет нечем — выданные refresh-токены станут
# невалидными. Меняйте его осознанно, в момент, когда обнуление сессий допустимо.
set -e

CERT_DIR="$(cd "$(dirname "$0")" && pwd)/certs"
DAYS="${IDENTITY_CERT_DAYS:-3650}"
SUBJECT_PREFIX="${IDENTITY_CERT_SUBJECT:-/C=RU/O=Core/CN=core-identity}"
CREATE_SECRETS=0

[ "$1" = "--create-secrets" ] && CREATE_SECRETS=1

mkdir -p "$CERT_DIR"

generate() {
    local name="$1" usage="$2"
    local key="$CERT_DIR/$name.key.pem"
    local crt="$CERT_DIR/$name.crt.pem"
    local pfx="$CERT_DIR/$name.pfx"
    local pass="$CERT_DIR/$name.pass"

    if [ -f "$pfx" ]; then
        echo "⏭  $pfx уже существует — пропускаю генерацию (удалите файл, чтобы перевыпустить)."
        return
    fi

    # Пароль генерируется здесь и нигде не хранится, кроме файла рядом с PFX.
    openssl rand -base64 32 | tr -d '\n' > "$pass"
    chmod 600 "$pass"

    openssl req -x509 -nodes -days "$DAYS" -newkey rsa:2048 \
        -keyout "$key" -out "$crt" \
        -subj "$SUBJECT_PREFIX-$usage" >/dev/null 2>&1

    openssl pkcs12 -export -out "$pfx" -inkey "$key" -in "$crt" \
        -passout "file:$pass" >/dev/null 2>&1

    chmod 600 "$pfx"
    rm -f "$key" "$crt"

    echo "✅ $usage: $pfx (пароль в $name.pass)"
}

echo "🔐 Генерация ключевого материала Identity.WebApi..."
generate identity-signing signing
generate identity-encryption encryption

echo ""
if [ "$CREATE_SECRETS" -eq 1 ]; then
    if ! docker node ls &> /dev/null; then
        echo "ERROR: docker swarm не инициализирован — секреты создать нельзя." >&2
        exit 1
    fi

    create() {
        local secret="$1" file="$2"
        if docker secret inspect "$secret" &> /dev/null; then
            echo "⏭  секрет $secret уже существует."
        else
            docker secret create "$secret" "$file" >/dev/null
            echo "✅ секрет $secret создан."
        fi
    }

    create identity_signing_certificate           "$CERT_DIR/identity-signing.pfx"
    create identity_signing_certificate_password  "$CERT_DIR/identity-signing.pass"
    create identity_encryption_certificate           "$CERT_DIR/identity-encryption.pfx"
    create identity_encryption_certificate_password  "$CERT_DIR/identity-encryption.pass"

    echo ""
    echo "🎉 Готово. Файлы в $CERT_DIR можно удалить — секреты уже в swarm."
else
    echo "Создайте секреты на сервере:"
    echo "  docker secret create identity_signing_certificate           $CERT_DIR/identity-signing.pfx"
    echo "  docker secret create identity_signing_certificate_password  $CERT_DIR/identity-signing.pass"
    echo "  docker secret create identity_encryption_certificate           $CERT_DIR/identity-encryption.pfx"
    echo "  docker secret create identity_encryption_certificate_password  $CERT_DIR/identity-encryption.pass"
    echo ""
    echo "или перезапустите скрипт с флагом --create-secrets."
fi
