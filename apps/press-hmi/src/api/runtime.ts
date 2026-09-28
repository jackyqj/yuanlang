export type RuntimeState =
  | 'Offline'
  | 'Idle'
  | 'Loading'
  | 'Ready'
  | 'Approaching'
  | 'Pressing'
  | 'Holding'
  | 'Retracting'
  | 'Completed'
  | 'Faulted'
  | 'Estop'

export type SamplePoint = {
  timeS: number
  forceN: number
  positionMm: number
}

export type RuntimeSnapshot = {
  state: RuntimeState
  job: {
    productId: string
    productName: string
    recipeVersionId: string
    connectorName: string
    pcbBarcode: string
  } | null
  metrics: {
    forceN: number
    positionMm: number
    speedMmS: number
    maxForceN: number
    maxPositionMm: number
    cycleElapsedS: number
  }
  axis: {
    positionMm: number
    speedMmS: number
    enabled: boolean
  }
  safety: {
    eStop: boolean
    lightCurtainOk: boolean
    doorClosed: boolean
    startEnabled: boolean
  }
  alarms: Array<{ alarmId: string; code: string; message: string }>
  activeCycleId: string | null
  softwareVersion: string
}

export type RuntimeSseEvent =
  | { type: 'snapshot'; data: RuntimeSnapshot }
  | { type: 'state'; data: { state: string } }
  | { type: 'metrics'; data: RuntimeSnapshot['metrics'] }
  | { type: 'curve'; data: { cycleId: string; count: number; samples: SamplePoint[] } }
  | { type: 'cycle_completed'; data: {
      cycleId: string
      pass: boolean
      failCodes: string[]
      endForceN: number
      endPositionMm: number
      cycleTimeS: number
    } }
  | { type: 'alarm'; data: { code: string; message: string } }
  | { type: string; data: unknown }

async function json<T>(res: Response): Promise<T> {
  if (!res.ok) {
    const text = await res.text()
    throw new Error(`${res.status} ${text}`)
  }
  return res.json() as Promise<T>
}

export const runtimeApi = {
  health: () => fetch('/health').then((r) => json<{ status: string; mode: string }>(r)),
  snapshot: () => fetch('/api/v1/runtime/snapshot').then((r) => json<RuntimeSnapshot>(r)),
  loadJob: (body: Record<string, unknown> = {}) =>
    fetch('/api/v1/runtime/jobs', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(body),
    }).then((r) => json<RuntimeSnapshot>(r)),
  startCycle: () =>
    fetch('/api/v1/runtime/cycles/start', { method: 'POST' }).then((r) =>
      json<{ ok: boolean; cycleId: string }>(r),
    ),
  abort: (reason?: string) =>
    fetch('/api/v1/runtime/abort', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ reason: reason ?? 'hmi abort' }),
    }).then((r) => json<{ ok: boolean }>(r)),
  reset: () =>
    fetch('/api/v1/runtime/reset', { method: 'POST' }).then((r) => json<RuntimeSnapshot>(r)),
}
