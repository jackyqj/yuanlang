# Module interfaces (summary)

Authoritative machine-readable contracts live under `/contracts` and `packages/press-adapters`.

## gRPC services (`press.v1`)

| Service | RPCs (main) | Notes |
|---|---|---|
| `AuthService` | Login, Logout, CheckPermission, UpsertUser | Session + permission keys |
| `RecipeService` | List/Upsert products, PublishRecipeVersion, ResolveByBarcode, Program CRUD | Published versions are immutable |
| `RuntimeService` | GetSnapshot, SubscribeEvents, LoadJob, Start/Stop/Abort, ResetFault, Jog, Home | Server is source of truth for state |
| `TraceService` | QueryCycles, OpenCurve, ExportCyclesExcel, QueryWorkLog, GetSpcChart | Aligns with legacy Access cycle fields |

### Runtime event stream

`SubscribeEvents` yields a `MachineEvent` oneof:

- `snapshot` — full resync after connect
- `state` / `metrics` — high-rate UI updates
- `curve_batch` — display (downsampled) or raw (debug)
- `alarm_raised` / `alarm_cleared`
- `cycle_completed` — pass/fail + curve id

### Command safety rules

- All commands carry `RequestMeta.operator`
- Reject motion commands in `ESTOP` / `FAULTED` until `ResetFault`
- `LoadJob` binds an immutable `recipe_version_id` into the active snapshot

## Device adapters (C#)

See `DeviceContracts.cs`:

- `IMotionDevice` — enable, home, absolute move, velocity profile, stop
- `IForceDevice` — acquire stream of `(t, F, s)`
- `IDigitalIoDevice` — inputs/outputs; `GetSafetyStatus` read-only
- `IBarcodeDevice` — scan stream
- `ICalibratorDevice` — external load-cell COM
- `IMesPublisher` — deliver outbox payload; ACK owned by service

## HTTP

- `mes-outbox.v1.yaml` — list/retry outbox, sync status, enqueue cycle result
- `calibration.v1.yaml` — session / run / apply compensation

## Judge library boundary

`press-judge` should expose pure functions (no I/O):

- `EvaluateLive(samples, recipe) -> LiveJudgement`
- `FinalizeCycle(samples, recipe) -> CycleJudgement`
- `BuildEnvelope(curves, tol) -> Envelope`
- `CalcPvfs` / `CalcAngle`

Inputs always include `algorithm_version` from the recipe snapshot.
