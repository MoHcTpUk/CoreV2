#!/bin/bash
cd "$(dirname "$0")" || exit 1

set -a
source ../project.env
set +a

scp -P "$STAGE_SSH_PORT" \
../deploy/docker-compose.app.stage.yml \
../deploy/docker-compose.infra.stage.yml \
../deploy/docker-compose.infra.dashboard.yml \
../deploy/docker-compose.management.yml \
../deploy/docker-compose.infra.yml \
../deploy/docker-compose.app.yml \
../deploy/start_stage.sh \
../deploy/ensure-databases.sh \
../deploy/generate-identity-certs.sh \
../deploy/.env.example \
"$STAGE_SSH_HOST:~/dogapp/deploy"

# Конфигурация NATS (аутентификация) монтируется в контейнер как ./nats/nats-server.conf.
# Секретов в ней нет: логин/пароль подставляются из окружения.
scp -P "$STAGE_SSH_PORT" -r \
../deploy/nats \
"$STAGE_SSH_HOST:~/dogapp/deploy"

# Init-скрипт создания баз. Монтируется в postgres как ./postgres/initdb ->
# /docker-entrypoint-initdb.d, поэтому файл обязан лежать на сервере рядом с compose;
# его же прогоняет ensure-databases.sh. Секретов не содержит.
scp -P "$STAGE_SSH_PORT" -r \
../deploy/postgres \
"$STAGE_SSH_HOST:~/dogapp/deploy"

# deploy/.env НЕ копируется: он создаётся на сервере один раз и содержит реальные пароли.

scp -P "$STAGE_SSH_PORT" \
../project.env \
"$STAGE_SSH_HOST:~/dogapp"

ssh "$STAGE_SSH_HOST" -p "$STAGE_SSH_PORT" "
cd /home/user/dogapp/deploy &&
bash start_stage.sh"
