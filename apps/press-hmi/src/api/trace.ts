export type CycleRecord = {
  cycleId: string
  productName: string
  productSn: string
  connectBar: string
  connectName: string
  recipeVersionId: string
  result: string
  endForceN: number
  endPositionMm: number
  maxForceN: number
  cycleTimeS: number
  failCodes: string
  curvePath: string
  softwareVersion: string
  operatorName: string
  createdAt: string
}

export type CycleDetail = {
  record: CycleRecord
  samples: Array<{ timeS: number; forceN: number; positionMm: number }>
}

async function json<T>(res: Response): Promise<T> {
  if (!res.ok) throw new Error(`${res.status} ${await res.text()}`)
  return res.json() as Promise<T>
}

export const traceApi = {
  listCycles: (params: { pcb?: string; product?: string; errorsOnly?: boolean } = {}) => {
    const q = new URLSearchParams()
    if (params.pcb) q.set('pcb', params.pcb)
    if (params.product) q.set('product', params.product)
    if (params.errorsOnly) q.set('errorsOnly', 'true')
    q.set('limit', '50')
    return fetch(`/api/v1/trace/cycles?${q}`).then((r) => json<CycleRecord[]>(r))
  },
  getCycle: (cycleId: string) =>
    fetch(`/api/v1/trace/cycles/${encodeURIComponent(cycleId)}`).then((r) => json<CycleDetail>(r)),
}
