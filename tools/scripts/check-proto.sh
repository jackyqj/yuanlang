#!/usr/bin/env bash
# Generate stubs once tooling is installed:
#   buf generate
# Expected plugins (configure in contracts/buf.gen.yaml later):
#   - C# for press-service
#   - ts-proto or connect-es for press-hmi
set -euo pipefail
cd "$(dirname "$0")/../../contracts"
if ! command -v buf >/dev/null; then
  echo "Install buf: https://buf.build/docs/installation"
  exit 1
fi
buf lint
echo "Lint OK. Add buf.gen.yaml before running buf generate."
