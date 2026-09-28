import { useCallback, useEffect, useState } from 'react'
import { mesApi, type MesSyncStatus, type OutboxMessage } from '../api/mes'

export function MesScreen() {
  const [items, setItems] = useState<OutboxMessage[]>([])
  const [status, setStatus] = useState<MesSyncStatus | null>(null)
  const [filter, setFilter] = useState<string>('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  const refresh = useCallback(async () => {
    setError('')
    try {
      const [list, sync] = await Promise.all([
        mesApi.list(filter || undefined),
        mesApi.syncStatus(),
      ])
      setItems(list.items)
      setStatus(sync)
    } catch (e) {
      setError((e as Error).message)
    }
  }, [filter])

  useEffect(() => {
    void refresh()
    const t = setInterval(() => void refresh(), 2000)
    return () => clearInterval(t)
  }, [refresh])

  async function onRetry(id: string) {
    setBusy(true)
    try {
      await mesApi.retry(id)
      await refresh()
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="run-screen">
      <header className="topbar">
        <div>
          <div className="brand">MES Outbox</div>
          <div className="sub">Local queue · async publish · retry dead letters</div>
        </div>
        <div className={`link-pill ${status?.online ? 'ok' : 'bad'}`}>
          {status ? `${status.adapter} · pending ${status.pendingCount} · dead ${status.deadCount}` : '…'}
        </div>
      </header>

      <div className="trace-toolbar">
        <select value={filter} onChange={(e) => setFilter(e.target.value)}>
          <option value="">All</option>
          <option value="pending">pending</option>
          <option value="sending">sending</option>
          <option value="acked">acked</option>
          <option value="dead">dead</option>
        </select>
        <button type="button" onClick={() => void refresh()} disabled={busy}>
          Refresh
        </button>
      </div>

      <div className="panel">
        <div className="panel-head">
          <h2>Messages</h2>
          <span>{items.length} rows</span>
        </div>
        <div className="table-wrap">
          <table className="data-table">
            <thead>
              <tr>
                <th>Time</th>
                <th>Status</th>
                <th>Cycle</th>
                <th>Retries</th>
                <th>Error</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {items.map((m) => (
                <tr key={m.messageId}>
                  <td>{new Date(m.createdAt).toLocaleString()}</td>
                  <td className={m.status === 'acked' ? 'pass' : m.status === 'dead' ? 'fail' : ''}>
                    {m.status}
                  </td>
                  <td>{m.idempotencyKey.slice(0, 12)}…</td>
                  <td>{m.retryCount}</td>
                  <td>{m.lastError ?? '—'}</td>
                  <td>
                    {(m.status === 'dead' || m.status === 'pending') && (
                      <button type="button" disabled={busy} onClick={() => void onRetry(m.messageId)}>
                        Retry
                      </button>
                    )}
                  </td>
                </tr>
              ))}
              {items.length === 0 ? (
                <tr>
                  <td colSpan={6}>No outbox messages — run a press cycle first.</td>
                </tr>
              ) : null}
            </tbody>
          </table>
        </div>
        {error ? <div className="error-box">{error}</div> : null}
        {status?.lastAckAt ? (
          <div className="result-line">Last ack: {new Date(status.lastAckAt).toLocaleString()}</div>
        ) : null}
      </div>
    </div>
  )
}
