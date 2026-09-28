export type SpcPoint = {
  index: number
  value: number
  time: string
  cycleId: string
}

export type SpcChart = {
  points: SpcPoint[]
  mean: number
  ucl: number
  cl: number
  lcl: number
  stdDev: number
  cpk: number | null
  usL: number | null
  lsL: number | null
  sampleCount: number
  metric: string
}

export type HourBucket = { hourLabel: string; pcs: number }

async function json<T>(res: Response): Promise<T> {
  if (!res.ok) throw new Error(`${res.status} ${await res.text()}`)
  return res.json() as Promise<T>
}

export const spcApi = {
  chart: (params: {
    product?: string
    connector?: string
    metric?: string
    usl?: number
    lsl?: number
  }) => {
    const q = new URLSearchParams()
    if (params.product) q.set('product', params.product)
    if (params.connector) q.set('connector', params.connector)
    if (params.metric) q.set('metric', params.metric)
    if (params.usl != null) q.set('usl', String(params.usl))
    if (params.lsl != null) q.set('lsl', String(params.lsl))
    q.set('limit', '200')
    return fetch(`/api/v1/spc/chart?${q}`).then((r) => json<SpcChart>(r))
  },
  hourly: (day?: string, product?: string) => {
    const q = new URLSearchParams()
    if (day) q.set('day', day)
    if (product) q.set('product', product)
    return fetch(`/api/v1/spc/hourly?${q}`).then((r) =>
      json<{ day: string; buckets: HourBucket[] }>(r),
    )
  },
}
