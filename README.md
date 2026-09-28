# Yuanlang Press Software

Industrial press-fit machine software: React HMI + .NET control service + device adapters.

## Repository layout

```
apps/
  press-hmi/          React + TypeScript HMI (Vite)
  press-shell/        .NET WebView2 host (autostart, COM/HID bridge)
services/
  press-service/      Machine control, judge orchestration, SQLite, MES outbox
packages/
  press-adapters/     IMotion / IForce / IIO / IBarcode / ICalibrator / MES publishers
  press-judge/        PVFS, angle, envelope, hold, pass/fail (unit-testable)
tools/
  press-sim/          Simulated motion + force for CI / no-hardware runs
contracts/
  proto/press/v1/     gRPC: Auth, Recipe, Runtime, Trace
  openapi/            MES outbox + calibration HTTP
docs/architecture/    arc42 + module interface notes
data/                 Local runtime dirs (curves/logs gitignored)
```

## Process topology

```
press-hmi  --gRPC/WS-->  press-service  --adapters-->  motion/DAQ/IO/barcode
                |                |
                |                +--> SQLite + curve files
                |                +--> MES outbox (REST/CSV/serial)
             press-shell (hosts WebView2, process watchdog)
```

## Contracts to implement first

| Contract | Path | Consumers |
|---|---|---|
| Runtime gRPC | `contracts/proto/press/v1/runtime.proto` | HMI ↔ service |
| Recipe gRPC | `contracts/proto/press/v1/recipe.proto` | HMI ↔ service |
| Trace gRPC | `contracts/proto/press/v1/trace.proto` | HMI ↔ service |
| Auth gRPC | `contracts/proto/press/v1/auth.proto` | HMI ↔ service |
| Device adapters | `packages/press-adapters/src/Abstractions/DeviceContracts.cs` | service ↔ hardware |
| MES Outbox | `contracts/openapi/mes-outbox.v1.yaml` | service ↔ MES |
| Calibration | `contracts/openapi/calibration.v1.yaml` | HMI/maintenance ↔ service |

## Suggested next implementation order

1. ~~`press-service` host + sim adapters + virtual press cycle~~ ✅
2. Generate C# + TypeScript stubs from `contracts/proto` and map gRPC `RuntimeService`
3. ~~Scaffold `press-hmi` run screen against `/api/v1/runtime/*`~~ ✅
4. ~~SQLite schema for recipe versions + cycle records + Trace HMI~~ ✅
5. Wire gRPC `RuntimeService` (optional; HTTP+SSE is working)
6. Recipe editor UI / MES outbox

### Run simulation smoke

```bash
export PATH="$HOME/.dotnet:$PATH"
# one-shot demo cycle (auto):
PRESS_DEMO=1 ASPNETCORE_URLS=http://127.0.0.1:5080 \
  dotnet services/press-service/bin/Debug/net8.0/Press.Service.dll

# or HTTP smoke script:
./tools/scripts/smoke-runtime.sh
```

### Run HMI (React)

```bash
# terminal 1 — API (disable auto-demo so HMI can drive cycles)
export PATH="$HOME/.dotnet:$PATH"
PRESS_DEMO=0 ASPNETCORE_URLS=http://127.0.0.1:5080 \
  dotnet services/press-service/bin/Debug/net8.0/Press.Service.dll

# terminal 2 — UI
cd apps/press-hmi && npm run dev
# open http://127.0.0.1:5173
```

HTTP surface (dev):

- `GET /health`
- `GET/POST /api/v1/runtime/*`
- `GET /api/v1/trace/cycles`
- `GET/PUT/POST/DELETE /api/v1/recipes` · `POST /api/v1/recipes/{id}/apply`
- `GET /api/v1/mes/outbox` · `POST /api/v1/mes/outbox/{id}/retry` · `GET /api/v1/mes/sync-status`

Set `Mes:Adapter=fail` to simulate outage and produce dead letters.

## Units

- Force: N  
- Length: mm  
- Speed: mm/s  
- Angle: deg  

Safety (E-stop / light curtain) is hardware-owned; software only observes and refuses unsafe commands.
