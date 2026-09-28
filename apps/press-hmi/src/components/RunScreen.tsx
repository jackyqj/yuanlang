import { useCallback, useEffect, useMemo, useState } from 'react'
import {
  runtimeApi,
  type RuntimeSnapshot,
  type RuntimeSseEvent,
  type SamplePoint,
} from '../api/runtime'
import { ForceStrokeChart } from './ForceStrokeChart'

const defaultJob = {
  productName: 'CPK',
  connectorName: 'J1',
  pcbBarcode: 'PCB-HMI-001',
  endPositionMm: 36.0,
  minForceN: 80,
  maxForceN: 5000,
  holdDelayS: 0.15,
  approachSpeedMmS: 18,
  pressSpeedMmS: 3,
}

export function RunScreen() {
  const [snap, setSnap] = useState<RuntimeSnapshot | null>(null)
  const [samples, setSamples] = useState<SamplePoint[]>([])
  const [lastResult, setLastResult] = useState<string>('')
  const [error, setError] = useState<string>('')
  const [busy, setBusy] = useState(false)
  const [connected, setConnected] = useState(false)

  const applySnapshot = useCallback((s: RuntimeSnapshot) => {
    setSnap(s)
  }, [])

  useEffect(() => {
    let cancelled = false
    runtimeApi
      .snapshot()
      .then((s) => {
        if (!cancelled) {
          applySnapshot(s)
          setConnected(true)
        }
      })
      .catch((e: Error) => {
        if (!cancelled) setError(`Service offline: ${e.message}`)
      })

    const es = new EventSource('/api/v1/runtime/events')
    es.onopen = () => setConnected(true)
    es.onerror = () => setConnected(false)
    es.onmessage = (msg) => {
      try {
        const evt = JSON.parse(msg.data) as RuntimeSseEvent
        if (evt.type === 'snapshot') applySnapshot(evt.data as RuntimeSnapshot)
        if (evt.type === 'state' && snap) {
          setSnap((prev) =>
            prev ? { ...prev, state: (evt.data as { state: string }).state as RuntimeSnapshot['state'] } : prev,
          )
        }
        if (evt.type === 'metrics') {
          setSnap((prev) =>
            prev ? { ...prev, metrics: evt.data as RuntimeSnapshot['metrics'] } : prev,
          )
        }
        if (evt.type === 'curve') {
          const batch = (evt.data as { samples: SamplePoint[] }).samples
          setSamples((prev) => [...prev, ...batch])
        }
        if (evt.type === 'cycle_completed') {
          const d = evt.data as {
            pass: boolean
            endForceN: number
            endPositionMm: number
            cycleTimeS: number
            failCodes: string[]
          }
          setLastResult(
            `${d.pass ? 'PASS' : 'FAIL'}  F=${d.endForceN.toFixed(1)}N  S=${d.endPositionMm.toFixed(3)}mm  t=${d.cycleTimeS.toFixed(2)}s` +
              (d.failCodes?.length ? `  [${d.failCodes.join(',')}]` : ''),
          )
        }
      } catch {
        // ignore malformed
      }
    }
    return () => {
      cancelled = true
      es.close()
    }
  }, [applySnapshot])

  const canStart = useMemo(() => {
    const st = snap?.state
    return !busy && (st === 'Ready' || st === 'Idle' || st === 'Completed')
  }, [busy, snap?.state])

  async function onLoadJob() {
    setBusy(true)
    setError('')
    setSamples([])
    setLastResult('')
    try {
      const s = await runtimeApi.loadJob(defaultJob)
      applySnapshot(s)
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  async function onStart() {
    setBusy(true)
    setError('')
    setSamples([])
    setLastResult('')
    try {
      if (snap?.state === 'Idle' || !snap?.job) {
        await runtimeApi.loadJob(defaultJob)
      }
      await runtimeApi.startCycle()
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  async function onAbort() {
    setBusy(true)
    try {
      await runtimeApi.abort('operator stop from HMI')
      applySnapshot(await runtimeApi.snapshot())
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  async function onReset() {
    setBusy(true)
    try {
      applySnapshot(await runtimeApi.reset())
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
          <div className="brand">Yuanlang Press HMI</div>
          <div className="sub">Simulation runtime · force / stroke monitor</div>
        </div>
        <div className={`link-pill ${connected ? 'ok' : 'bad'}`}>
          {connected ? 'Service linked' : 'Service offline'}
        </div>
      </header>

      <section className="status-strip">
        <Stat label="State" value={snap?.state ?? '—'} emphasize />
        <Stat label="Force" value={fmt(snap?.metrics.forceN, 'N')} />
        <Stat label="Stroke" value={fmt(snap?.metrics.positionMm, 'mm')} />
        <Stat label="Max Force" value={fmt(snap?.metrics.maxForceN, 'N')} />
        <Stat label="Cycle" value={fmt(snap?.metrics.cycleElapsedS, 's')} />
        <Stat label="Product" value={snap?.job?.productName ?? '—'} />
      </section>

      <div className="main-grid">
        <div className="panel chart-panel">
          <div className="panel-head">
            <h2>Live curve</h2>
            <span>{samples.length} samples</span>
          </div>
          <ForceStrokeChart samples={samples} />
          {lastResult ? <div className="result-line">{lastResult}</div> : null}
        </div>

        <aside className="panel side-panel">
          <div className="panel-head">
            <h2>Controls</h2>
          </div>
          <div className="actions">
            <button type="button" onClick={onLoadJob} disabled={busy}>
              Load job
            </button>
            <button type="button" className="primary" onClick={onStart} disabled={busy || !canStart}>
              Start press
            </button>
            <button type="button" className="danger" onClick={onAbort} disabled={busy}>
              Abort
            </button>
            <button type="button" onClick={onReset} disabled={busy}>
              Reset fault
            </button>
          </div>

          <div className="meta-block">
            <h3>Safety</h3>
            <ul>
              <li>E-stop: {yn(!snap?.safety.eStop)}</li>
              <li>Light curtain: {yn(snap?.safety.lightCurtainOk)}</li>
              <li>Door: {yn(snap?.safety.doorClosed)}</li>
              <li>Start enable: {yn(snap?.safety.startEnabled)}</li>
            </ul>
          </div>

          <div className="meta-block">
            <h3>Job</h3>
            <ul>
              <li>Connector: {snap?.job?.connectorName ?? '—'}</li>
              <li>PCB: {snap?.job?.pcbBarcode ?? '—'}</li>
              <li>Recipe: {snap?.job?.recipeVersionId ?? '—'}</li>
              <li>SW: {snap?.softwareVersion ?? '—'}</li>
            </ul>
          </div>

          {error ? <div className="error-box">{error}</div> : null}
        </aside>
      </div>
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

function fmt(n: number | undefined, unit: string) {
  if (n === undefined || Number.isNaN(n)) return `— ${unit}`
  return `${n.toFixed(unit === 's' ? 2 : 3)} ${unit}`
}

function yn(v: boolean | undefined) {
  if (v === undefined) return '—'
  return v ? 'OK' : 'NO'
}
