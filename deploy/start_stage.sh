#!/bin/bash

cd "$(dirname "$0")" || exit 1

set -a
source ../project.env
# Учётные данные инфраструктуры. Файл живёт только на сервере, в git его нет
# и deploy_all_stage.sh его не копирует. Шаблон — deploy/.env.example.
[ -f .env ] && source .env
set +a

# Fail fast: на stage не должно быть ни одного дефолтного/пустого пароля.
missing=()
for var in POSTGRES_USER POSTGRES_PASSWORD NATS_USER NATS_PASSWORD MINIO_ROOT_USER MINIO_ROOT_PASSWORD; do
    [ -z "${!var}" ] && missing+=("$var")
done
if [ ${#missing[@]} -ne 0 ]; then
    echo "ERROR: не заданы обязательные переменные: ${missing[*]}" >&2
    echo "Создайте deploy/.env на этом сервере по образцу deploy/.env.example и заполните значения." >&2
    exit 1
fi

if ! docker node ls &> /dev/null; then
    docker swarm init
fi

docker pull "$REGISTRY/$IMAGE_PREFIX-gateway:latest" &
docker pull "$REGISTRY/$IMAGE_PREFIX-users:latest" &
docker pull "$REGISTRY/$IMAGE_PREFIX-sample:latest" &
docker pull "$REGISTRY/$IMAGE_PREFIX-identity-webapi:latest" &
docker pull "$REGISTRY/$IMAGE_PREFIX-identity:latest" &
wait

docker stack deploy --detach=false --resolve-image always --with-registry-auth --compose-file docker-compose.management.yml management
wait
docker stack deploy --detach=false --resolve-image always --with-registry-auth --compose-file docker-compose.infra.yml --compose-file docker-compose.infra.stage.yml infra
wait
docker stack deploy --detach=false --resolve-image always --with-registry-auth --compose-file docker-compose.app.yml --compose-file docker-compose.app.stage.yml app
wait
