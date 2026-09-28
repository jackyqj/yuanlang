import { useEffect, useState } from 'react'
import { spcApi, type HourBucket, type SpcChart } from '../api/spc'
import { SpcControlChart } from './SpcControlChart'

export function SpcScreen() {
  const [product, setProduct] = useState('')
  const [metric, setMetric] = useState('end_force')
  const [usl, setUsl] = useState('1200')
  const [lsl, setLsl] = useState('800')
  const [chart, setChart] = useState<SpcChart | null>(null)
  const [hourly, setHourly] = useState<{ day: string; buckets: HourBucket[] } | null>(null)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  async function refresh() {
    setBusy(true)
    setError('')
    try {
      const [c, h] = await Promise.all([
        spcApi.chart({
          product: product || undefined,
          metric,
          usl: usl === '' ? undefined : Number(usl),
          lsl: lsl === '' ? undefined : Number(lsl),
        }),
        spcApi.hourly(undefined, product || undefined),
      ])
      setChart(c)
      setHourly(h)
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  useEffect(() => {
    void refresh()
  }, [])

  const maxPcs = Math.max(1, ...(hourly?.buckets.map((b) => b.pcs) ?? [1]))

  return (
    <div className="run-screen">
      <header className="topbar">
        <div>
          <div className="brand">SPC</div>
          <div className="sub">Control chart · Cpk · hourly throughput</div>
        </div>
      </header>

      <div className="trace-toolbar">
        <input
          type="text"
          placeholder="Product filter"
          value={product}
          onChange={(e) => setProduct(e.target.value)}
        />
        <select value={metric} onChange={(e) => setMetric(e.target.value)}>
          <option value="end_force">End force (N)</option>
          <option value="max_force">Max force (N)</option>
          <option value="position">End position (mm)</option>
        </select>
        <input
          type="number"
          placeholder="USL"
          value={usl}
          onChange={(e) => setUsl(e.target.value)}
          style={{ width: 100 }}
        />
        <input
          type="number"
          placeholder="LSL"
          value={lsl}
          onChange={(e) => setLsl(e.target.value)}
          style={{ width: 100 }}
        />
        <button type="button" onClick={() => void refresh()} disabled={busy}>
          Analyze
        </button>
      </div>

      <section className="status-strip">
        <Stat label="Samples" value={String(chart?.sampleCount ?? 0)} />
        <Stat label="Mean" value={fmt(chart?.mean)} />
        <Stat label="StdDev" value={fmt(chart?.stdDev)} />
        <Stat label="UCL" value={fmt(chart?.ucl)} />
        <Stat label="LCL" value={fmt(chart?.lcl)} />
        <Stat label="Cpk" value={chart?.cpk == null ? '—' : chart.cpk.toFixed(3)} emphasize />
      </section>

      <div className="main-grid">
        <div className="panel chart-panel">
          <div className="panel-head">
            <h2>Control chart</h2>
            <span>{chart?.metric ?? metric}</span>
          </div>
          <SpcControlChart chart={chart} />
        </div>

        <aside className="panel side-panel">
          <div className="panel-head">
            <h2>Hourly pcs</h2>
            <span>{hourly?.day ?? '—'}</span>
          </div>
          <div className="hourly-bars">
            {(hourly?.buckets ?? []).map((b) => (
              <div key={b.hourLabel} className="hourly-row">
                <span className="hourly-label">{b.hourLabel}</span>
                <div className="hourly-track">
                  <div
                    className="hourly-fill"
                    style={{ width: `${(b.pcs / maxPcs) * 100}%` }}
                  />
                </div>
                <span className="hourly-pcs">{b.pcs}</span>
              </div>
            ))}
          </div>
        </aside>
      </div>

      {error ? <div className="error-box">{error}</div> : null}
    </div>
  )
}

function Stat({
  label,
  value,
  emphasize,
}: {
  label: string
  value: string
  emphasize?: boolean
}) {
  return (
    <div className={`stat ${emphasize ? 'emphasize' : ''}`}>
      <div className="stat-label">{label}</div>
      <div className="stat-value">{value}</div>
    </div>
  )
}

function fmt(n: number | undefined) {
  if (n === undefined || Number.isNaN(n)) return '—'
  return n.toFixed(2)
}
