#!/bin/bash
cd "$(dirname "$0")" || exit 1

./build_backend.sh "$1"
