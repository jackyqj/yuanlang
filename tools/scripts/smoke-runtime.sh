#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
export PATH="${HOME}/.dotnet:${PATH}"
export ASPNETCORE_ENVIRONMENT=Development
export PRESS_DEMO=0
export ASPNETCORE_URLS=http://127.0.0.1:5080

dotnet build "${ROOT}/services/press-service/Press.Service.csproj" -v q
dotnet "${ROOT}/services/press-service/bin/Debug/net8.0/Press.Service.dll" &
PID=$!
trap 'kill $PID 2>/dev/null || true' EXIT

for i in $(seq 1 50); do
  if curl -sf http://127.0.0.1:5080/health >/dev/null; then break; fi
  sleep 0.2
done

curl -sf http://127.0.0.1:5080/health | tee /tmp/press-health.json
echo
curl -sf -X POST http://127.0.0.1:5080/api/v1/runtime/jobs \
  -H 'content-type: application/json' \
  -d '{"productName":"CPK","endPositionMm":36.0,"minForceN":80,"maxForceN":5000}' >/tmp/press-job.json
curl -sf -X POST http://127.0.0.1:5080/api/v1/runtime/cycles/start | tee /tmp/press-start.json
echo

for i in $(seq 1 80); do
  snap=$(curl -sf http://127.0.0.1:5080/api/v1/runtime/snapshot)
  state=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["state"])' <<<"$snap")
  maxf=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["metrics"]["maxForceN"])' <<<"$snap")
  if [[ "$state" == "Ready" && $(python3 -c "print(1 if float('$maxf')>0 else 0)") == 1 && $i -gt 5 ]]; then
    echo "$snap" | python3 -m json.tool
    echo "SMOKE OK"
    exit 0
  fi
  sleep 0.15
done

echo "SMOKE FAILED" >&2
exit 1
