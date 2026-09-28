import { useEffect, useState, type ReactNode } from 'react'
import {
  blankRecipe,
  recipeApi,
  type RecipeDocument,
  type RecipeSummary,
} from '../api/recipe'

type Props = {
  onApplied?: () => void
}

export function RecipeScreen({ onApplied }: Props) {
  const [list, setList] = useState<RecipeSummary[]>([])
  const [form, setForm] = useState<RecipeDocument>(blankRecipe())
  const [selectedId, setSelectedId] = useState<string>('')
  const [message, setMessage] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  async function refreshList(preferId?: string) {
    const rows = await recipeApi.list()
    setList(rows)
    const id = preferId || selectedId || rows[0]?.recipeVersionId
    if (id) {
      setSelectedId(id)
      setForm(await recipeApi.get(id))
    }
  }

  useEffect(() => {
    void refreshList().catch((e: Error) => setError(e.message))
  }, [])

  function setField<K extends keyof RecipeDocument>(key: K, value: RecipeDocument[K]) {
    setForm((prev) => ({ ...prev, [key]: value }))
  }

  async function onSelect(id: string) {
    setError('')
    setMessage('')
    setSelectedId(id)
    setForm(await recipeApi.get(id))
  }

  async function onNew() {
    const doc = blankRecipe()
    setForm(doc)
    setSelectedId(doc.recipeVersionId)
    setMessage('New draft — edit then Save')
  }

  async function onSave() {
    setBusy(true)
    setError('')
    setMessage('')
    try {
      const exists = list.some((r) => r.recipeVersionId === form.recipeVersionId)
      const saved = exists ? await recipeApi.save(form) : await recipeApi.create(form)
      setForm(saved)
      setSelectedId(saved.recipeVersionId)
      await refreshList(saved.recipeVersionId)
      setMessage(`Saved ${saved.recipeVersionId}`)
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  async function onSaveAsNew() {
    setBusy(true)
    setError('')
    try {
      const doc = {
        ...form,
        recipeVersionId: `rv-${Date.now()}`,
        productId: form.productId || form.productName.toLowerCase().replace(/\s+/g, '-'),
      }
      const saved = await recipeApi.create(doc)
      setForm(saved)
      setSelectedId(saved.recipeVersionId)
      await refreshList(saved.recipeVersionId)
      setMessage(`Created ${saved.recipeVersionId}`)
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  async function onDelete() {
    if (!selectedId) return
    if (!confirm(`Delete recipe ${selectedId}?`)) return
    setBusy(true)
    try {
      await recipeApi.remove(selectedId)
      setSelectedId('')
      setForm(blankRecipe())
      await refreshList()
      setMessage('Deleted')
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  async function onApply() {
    setBusy(true)
    setError('')
    try {
      await recipeApi.save(form)
      const res = await recipeApi.apply(form.recipeVersionId)
      setMessage(`Applied → runtime ${res.state}`)
      onApplied?.()
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
          <div className="brand">Recipe editor</div>
          <div className="sub">Connector · force windows · speed · PVFS · hold</div>
        </div>
      </header>

      <div className="main-grid recipe-grid">
        <aside className="panel">
          <div className="panel-head">
            <h2>Recipes</h2>
            <button type="button" onClick={() => void onNew()} disabled={busy}>
              New
            </button>
          </div>
          <ul className="recipe-list">
            {list.map((r) => (
              <li key={r.recipeVersionId}>
                <button
                  type="button"
                  className={selectedId === r.recipeVersionId ? 'active' : ''}
                  onClick={() => void onSelect(r.recipeVersionId)}
                >
                  <strong>{r.productName}</strong>
                  <span>
                    {r.connectorName} · {r.recipeVersionId}
                  </span>
                </button>
              </li>
            ))}
            {list.length === 0 ? <li className="muted">No recipes yet</li> : null}
          </ul>
        </aside>

        <div className="panel">
          <div className="panel-head">
            <h2>Parameters</h2>
            <span>{form.publishedAt ? new Date(form.publishedAt).toLocaleString() : 'draft'}</span>
          </div>

          <div className="form-grid">
            <Field label="Recipe ID">
              <input
                value={form.recipeVersionId}
                onChange={(e) => setField('recipeVersionId', e.target.value)}
              />
            </Field>
            <Field label="Product name">
              <input value={form.productName} onChange={(e) => setField('productName', e.target.value)} />
            </Field>
            <Field label="Product ID">
              <input value={form.productId} onChange={(e) => setField('productId', e.target.value)} />
            </Field>
            <Field label="Connector">
              <input
                value={form.connectorName}
                onChange={(e) => setField('connectorName', e.target.value)}
              />
            </Field>
            <Field label="PCB barcode">
              <input value={form.pcbBarcode} onChange={(e) => setField('pcbBarcode', e.target.value)} />
            </Field>
            <Field label="Connector barcode">
              <input
                value={form.connectorBarcode}
                onChange={(e) => setField('connectorBarcode', e.target.value)}
              />
            </Field>
          </div>

          <h3 className="form-section">Force / stroke</h3>
          <div className="form-grid">
            <Num label="Contact force (N)" value={form.contactForceN} onChange={(v) => setField('contactForceN', v)} />
            <Num label="Max force (N)" value={form.maxForceN} onChange={(v) => setField('maxForceN', v)} />
            <Num label="Min force (N)" value={form.minForceN} onChange={(v) => setField('minForceN', v)} />
            <Num label="Work origin (mm)" value={form.workOriginMm} onChange={(v) => setField('workOriginMm', v)} />
            <Num label="End position (mm)" value={form.endPositionMm} onChange={(v) => setField('endPositionMm', v)} />
            <Num label="End pos tol (mm)" value={form.endPosTolMm} onChange={(v) => setField('endPosTolMm', v)} />
            <Num label="Check tol (mm)" value={form.checkTolMm} onChange={(v) => setField('checkTolMm', v)} />
            <Num label="Hold delay (s)" value={form.holdDelayS} onChange={(v) => setField('holdDelayS', v)} />
          </div>

          <h3 className="form-section">Speed</h3>
          <div className="form-grid">
            <Num label="Approach (mm/s)" value={form.approachSpeedMmS} onChange={(v) => setField('approachSpeedMmS', v)} />
            <Num label="Press (mm/s)" value={form.pressSpeedMmS} onChange={(v) => setField('pressSpeedMmS', v)} />
            <Num label="Retract (mm/s)" value={form.retractSpeedMmS} onChange={(v) => setField('retractSpeedMmS', v)} />
          </div>

          <h3 className="form-section">PVFS / angle</h3>
          <div className="form-grid">
            <Num label="PVFS %" value={form.pvfsPercent} onChange={(v) => setField('pvfsPercent', v)} />
            <Num label="PVFS start (mm)" value={form.pvfsStartMm} onChange={(v) => setField('pvfsStartMm', v)} />
            <Num label="PVFS distance (mm)" value={form.pvfsDistanceMm} onChange={(v) => setField('pvfsDistanceMm', v)} />
            <Num
              label="Stop angle (deg)"
              value={form.stopAngleDeg ?? 0}
              onChange={(v) => setField('stopAngleDeg', v <= 0 ? null : v)}
            />
            <label className="check field-span">
              <input
                type="checkbox"
                checked={form.pvfsAutoLocate}
                onChange={(e) => setField('pvfsAutoLocate', e.target.checked)}
              />
              Auto judge sampling location
            </label>
          </div>

          <div className="actions recipe-actions">
            <button type="button" className="primary" onClick={() => void onSave()} disabled={busy}>
              Save
            </button>
            <button type="button" onClick={() => void onSaveAsNew()} disabled={busy}>
              Save as new version
            </button>
            <button type="button" className="primary" onClick={() => void onApply()} disabled={busy}>
              Apply to Run
            </button>
            <button type="button" className="danger" onClick={() => void onDelete()} disabled={busy || !selectedId}>
              Delete
            </button>
          </div>

          {message ? <div className="result-line">{message}</div> : null}
          {error ? <div className="error-box">{error}</div> : null}
        </div>
      </div>
    </div>
  )
}

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <label className="field">
      <span>{label}</span>
      {children}
    </label>
  )
}

function Num({
  label,
  value,
  onChange,
}: {
  label: string
  value: number
  onChange: (v: number) => void
}) {
  return (
    <label className="field">
      <span>{label}</span>
      <input
        type="number"
        step="any"
        value={Number.isFinite(value) ? value : 0}
        onChange={(e) => onChange(Number(e.target.value))}
      />
    </label>
  )
}
