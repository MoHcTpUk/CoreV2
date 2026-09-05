#!/bin/bash
cd "$(dirname "$0")" || exit 1

set -a
source ../project.env
set +a

if [ -n "$1" ]; then
  PLATFORMS="$1"
else
  PLATFORMS="linux/amd64"
fi

CONTEXT="../src/backend/Core"

docker buildx build \
  --platform $PLATFORMS \
  -f ../build/backend/Gateway.Dockerfile \
  -t "$REGISTRY/$IMAGE_PREFIX-gateway:latest" \
  --push \
  "$CONTEXT" &

docker buildx build \
  --platform $PLATFORMS \
  -f ../build/backend/Identity.Dockerfile \
  -t "$REGISTRY/$IMAGE_PREFIX-identity:latest" \
  --push \
  "$CONTEXT" &

docker buildx build \
  --platform $PLATFORMS \
  -f ../build/backend/Identity.WebApi.Dockerfile \
  -t "$REGISTRY/$IMAGE_PREFIX-identity-webapi:latest" \
  --push \
  "$CONTEXT" &

docker buildx build \
  --platform $PLATFORMS \
  -f ../build/backend/Users.Dockerfile \
  -t "$REGISTRY/$IMAGE_PREFIX-users:latest" \
  --push \
  "$CONTEXT" &

docker buildx build \
  --platform $PLATFORMS \
  -f ../build/backend/Sample.Dockerfile \
  -t "$REGISTRY/$IMAGE_PREFIX-sample:latest" \
  --push \
  "$CONTEXT" &

wait
echo "All builds completed!"
