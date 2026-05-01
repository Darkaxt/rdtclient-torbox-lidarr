#!/usr/bin/env bash
set -euo pipefail

bind="${RDTCLIENT_BIND:-127.0.0.1}"
port="${RDTCLIENT_PORT:-6500}"

curl --fail --silent --show-error "http://${bind}:${port}/" >/dev/null
echo "RDTClient responded on ${bind}:${port}"
