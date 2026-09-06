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

docker stack deploy --detach=false --compose-file docker-compose.management.yml --compose-file docker-compose.management.local.yml management
docker stack deploy --detach=false --compose-file docker-compose.infra.yml --compose-file docker-compose.infra.local.yml infra

# Порядок важен: сервисы не создают свои базы (Database.MigrateAsync требует уже
# существующей БД), поэтому app поднимаем только после того, как identity/users/sample
# точно есть. Скрипт идемпотентен и чинит как пустой том, так и уже существующий.
bash ./ensure-databases.sh || exit 1

docker stack deploy --detach=false --compose-file docker-compose.app.yml --compose-file docker-compose.app.local.yml app
