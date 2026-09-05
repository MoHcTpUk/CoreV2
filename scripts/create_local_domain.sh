#!/bin/bash
cd "$(dirname "$0")" || exit 1

set -a
source ../project.env
set +a

for HOST in "api.$LOCAL_DOMAIN" "auth.$LOCAL_DOMAIN"; do
    if ! grep -q "127.0.0.1 $HOST" /etc/hosts; then
        echo "127.0.0.1 $HOST" | sudo tee -a /etc/hosts
    fi
done
