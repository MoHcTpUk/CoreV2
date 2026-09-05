#!/bin/bash
cd "$(dirname "$0")" || exit 1

set -a
source ../project.env
set +a

scp -P "$STAGE_SSH_PORT" \
../deploy/docker-compose.app.stage.yml \
../deploy/docker-compose.infra.stage.yml \
../deploy/docker-compose.management.yml \
../deploy/docker-compose.infra.yml \
../deploy/docker-compose.app.yml \
../deploy/start_stage.sh \
"$STAGE_SSH_HOST:~/dogapp/deploy"

scp -P "$STAGE_SSH_PORT" \
../project.env \
"$STAGE_SSH_HOST:~/dogapp"

ssh "$STAGE_SSH_HOST" -p "$STAGE_SSH_PORT" "
cd /home/user/dogapp/deploy &&
bash start_stage.sh"
