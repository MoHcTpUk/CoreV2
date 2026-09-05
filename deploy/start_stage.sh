#!/bin/bash

cd "$(dirname "$0")" || exit 1

set -a
source ../project.env
set +a

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
