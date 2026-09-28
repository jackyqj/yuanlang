export type OutboxMessage = {
  messageId: string
  status: string
  createdAt: string
  lastAttemptAt: string | null
  retryCount: number
  lastError: string | null
  payloadJson: string
  idempotencyKey: string
}

export type MesSyncStatus = {
  adapter: string
  online: boolean
  pendingCount: number
  deadCount: number
  lastAckAt: string | null
}

async function json<T>(res: Response): Promise<T> {
  if (!res.ok) throw new Error(`${res.status} ${await res.text()}`)
  return res.json() as Promise<T>
}

export const mesApi = {
  list: (status?: string) => {
    const q = new URLSearchParams()
    if (status) q.set('status', status)
    q.set('limit', '50')
    return fetch(`/api/v1/mes/outbox?${q}`).then((r) => json<{ items: OutboxMessage[] }>(r))
  },
  syncStatus: () => fetch('/api/v1/mes/sync-status').then((r) => json<MesSyncStatus>(r)),
  retry: (messageId: string) =>
    fetch(`/api/v1/mes/outbox/${encodeURIComponent(messageId)}/retry`, { method: 'POST' }).then((r) =>
      json<OutboxMessage>(r),
    ),
}
