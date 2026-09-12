#!/bin/bash

cd "$(dirname "$0")" || exit 1

set -a
source ../project.env
# Локальному стенду .env не нужен: dev-значения зашиты в *.local.yml.
# Если файл есть — его значения имеют приоритет (см. deploy/.env.example).
[ -f .env ] && source .env
set +a

if ! docker node ls &> /dev/null; then
    docker swarm init
fi

# Шина работает только по TLS (deploy/nats/nats-server.conf), а сертификата в git нет.
# Скрипт идемпотентен: на втором запуске он видит готовый сертификат и ничего не делает.
bash ./generate-nats-certs.sh || exit 1

docker stack deploy --detach=false --compose-file docker-compose.management.yml --compose-file docker-compose.management.local.yml management
docker stack deploy --detach=false --compose-file docker-compose.infra.yml --compose-file docker-compose.infra.local.yml infra
docker stack deploy --detach=false --compose-file docker-compose.app.yml --compose-file docker-compose.app.local.yml app
