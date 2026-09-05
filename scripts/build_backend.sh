#!/bin/bash
cd "$(dirname "$0")" || exit 1

set -a
source ../project.env
set +a

if [ -n "$1" ]; then
  PLATFORM="$1"
else
  ARCH=$(uname -m)
  if [ "$ARCH" = "arm64" ] || [ "$ARCH" = "aarch64" ]; then
    PLATFORM="linux/arm64"
  else
    PLATFORM="linux/amd64"
  fi
fi

CONTEXT="../src/backend/Core"

docker buildx build \
  --platform $PLATFORM \
  -f ../build/backend/Gateway.Dockerfile \
  -t "$REGISTRY/$IMAGE_PREFIX-gateway:latest" \
  --load \
  "$CONTEXT" &

docker buildx build \
  --platform $PLATFORM \
  -f ../build/backend/Users.Dockerfile \
  -t "$REGISTRY/$IMAGE_PREFIX-users:latest" \
  --load \
  "$CONTEXT" &

docker buildx build \
  --platform $PLATFORM \
  -f ../build/backend/Identity.Dockerfile \
  -t "$REGISTRY/$IMAGE_PREFIX-identity:latest" \
  --load \
  "$CONTEXT" &

docker buildx build \
  --platform $PLATFORM \
  -f ../build/backend/Identity.WebApi.Dockerfile \
  -t "$REGISTRY/$IMAGE_PREFIX-identity-webapi:latest" \
  --load \
  "$CONTEXT" &

docker buildx build \
  --platform $PLATFORM \
  -f ../build/backend/Sample.Dockerfile \
  -t "$REGISTRY/$IMAGE_PREFIX-sample:latest" \
  --load \
  "$CONTEXT"

wait
