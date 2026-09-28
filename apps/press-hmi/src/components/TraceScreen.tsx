import { useEffect, useState } from 'react'
import { traceApi, type CycleDetail, type CycleRecord } from '../api/trace'
import { ForceStrokeChart } from './ForceStrokeChart'

export function TraceScreen() {
  const [rows, setRows] = useState<CycleRecord[]>([])
  const [pcb, setPcb] = useState('')
  const [errorsOnly, setErrorsOnly] = useState(false)
  const [selected, setSelected] = useState<CycleDetail | null>(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)

  async function refresh(nextPcb = pcb, nextErrors = errorsOnly) {
    setLoading(true)
    setError('')
    try {
      const list = await traceApi.listCycles({
        pcb: nextPcb || undefined,
        errorsOnly: nextErrors,
      })
      setRows(list)
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void refresh()
  }, [])

  async function openCycle(id: string) {
    setError('')
    try {
      setSelected(await traceApi.getCycle(id))
    } catch (e) {
      setError((e as Error).message)
    }
  }

  return (
    <div className="run-screen">
      <header className="topbar">
        <div>
          <div className="brand">Traceability</div>
          <div className="sub">Cycle records · curve replay · barcode lookup</div>
        </div>
      </header>

      <div className="trace-toolbar">
        <input
          value={pcb}
          onChange={(e) => setPcb(e.target.value)}
          type="text" placeholder="PCB barcode"
          onKeyDown={(e) => {
            if (e.key === 'Enter') void refresh()
          }}
        />
        <label className="check">
          <input
            type="checkbox"
            checked={errorsOnly}
            onChange={(e) => {
              setErrorsOnly(e.target.checked)
              void refresh(pcb, e.target.checked)
            }}
          />
          Errors only
        </label>
        <button type="button" onClick={() => void refresh()} disabled={loading}>
          Search
        </button>
      </div>

      <div className="main-grid">
        <div className="panel">
          <div className="panel-head">
            <h2>Cycles</h2>
            <span>{rows.length} rows</span>
          </div>
          <div className="table-wrap">
            <table className="data-table">
              <thead>
                <tr>
                  <th>Time</th>
                  <th>Result</th>
                  <th>PCB</th>
                  <th>Product</th>
                  <th>Force</th>
                  <th>Stroke</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((r) => (
                  <tr
                    key={r.cycleId}
                    className={selected?.record.cycleId === r.cycleId ? 'active' : ''}
                    onClick={() => void openCycle(r.cycleId)}
                  >
                    <td>{fmtTime(r.createdAt)}</td>
                    <td className={r.result === 'pass' ? 'pass' : 'fail'}>{r.result.toUpperCase()}</td>
                    <td>{r.productSn}</td>
                    <td>{r.productName}</td>
                    <td>{r.endForceN.toFixed(1)}</td>
                    <td>{r.endPositionMm.toFixed(3)}</td>
                  </tr>
                ))}
                {rows.length === 0 ? (
                  <tr>
                    <td colSpan={6}>No cycles yet — run a press on the Run screen.</td>
                  </tr>
                ) : null}
              </tbody>
            </table>
          </div>
          {error ? <div className="error-box">{error}</div> : null}
        </div>

        <aside className="panel side-panel">
          <div className="panel-head">
            <h2>Detail</h2>
          </div>
          {selected ? (
            <>
              <div className="meta-block">
                <ul>
                  <li>ID: {selected.record.cycleId.slice(0, 12)}…</li>
                  <li>Result: {selected.record.result.toUpperCase()}</li>
                  <li>Recipe: {selected.record.recipeVersionId}</li>
                  <li>Fail codes: {selected.record.failCodes || '—'}</li>
                  <li>Samples: {selected.samples.length}</li>
                </ul>
              </div>
              <ForceStrokeChart samples={selected.samples} title="Saved curve" />
            </>
          ) : (
            <p className="muted">Select a cycle to replay its curve.</p>
          )}
        </aside>
      </div>
    </div>
  )
}

function fmtTime(iso: string) {
  try {
    return new Date(iso).toLocaleString()
  } catch {
    return iso
  }
}
