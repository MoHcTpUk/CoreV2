#!/bin/bash
cd "$(dirname "$0")" || exit 1

bash ../deploy/stop.sh
bash ./build_backend.sh
bash ../deploy/start_local.sh
