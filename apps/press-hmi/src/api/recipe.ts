export type RecipeDocument = {
  recipeVersionId: string
  productId: string
  productName: string
  connectorName: string
  pcbBarcode: string
  connectorBarcode: string
  contactForceN: number
  maxForceN: number
  minForceN: number
  endPositionMm: number
  endPosTolMm: number
  checkTolMm: number
  workOriginMm: number
  pvfsPercent: number
  pvfsStartMm: number
  pvfsDistanceMm: number
  pvfsAutoLocate: boolean
  stopAngleDeg: number | null
  holdDelayS: number
  approachSpeedMmS: number
  pressSpeedMmS: number
  retractSpeedMmS: number
  algorithmVersion: string
  publishedAt?: string | null
}

export type RecipeSummary = {
  recipeVersionId: string
  productId: string
  productName: string
  connectorName: string
  publishedAt: string
}

async function json<T>(res: Response): Promise<T> {
  if (!res.ok) throw new Error(`${res.status} ${await res.text()}`)
  return res.json() as Promise<T>
}

export function blankRecipe(): RecipeDocument {
  return {
    recipeVersionId: `rv-${Date.now()}`,
    productId: 'new-product',
    productName: 'New Product',
    connectorName: 'J1',
    pcbBarcode: 'PCB-001',
    connectorBarcode: 'CN-001',
    contactForceN: 50,
    maxForceN: 5000,
    minForceN: 100,
    endPositionMm: 36,
    endPosTolMm: 0.5,
    checkTolMm: 1,
    workOriginMm: 40,
    pvfsPercent: 25,
    pvfsStartMm: 37,
    pvfsDistanceMm: 0.2,
    pvfsAutoLocate: false,
    stopAngleDeg: null,
    holdDelayS: 0.15,
    approachSpeedMmS: 15,
    pressSpeedMmS: 2,
    retractSpeedMmS: 20,
    algorithmVersion: 'sim-1',
  }
}

export const recipeApi = {
  list: () => fetch('/api/v1/recipes').then((r) => json<RecipeSummary[]>(r)),
  get: (id: string) =>
    fetch(`/api/v1/recipes/${encodeURIComponent(id)}`).then((r) => json<RecipeDocument>(r)),
  save: (doc: RecipeDocument) =>
    fetch(`/api/v1/recipes/${encodeURIComponent(doc.recipeVersionId)}`, {
      method: 'PUT',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(doc),
    }).then((r) => json<RecipeDocument>(r)),
  create: (doc: RecipeDocument) =>
    fetch('/api/v1/recipes', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(doc),
    }).then((r) => json<RecipeDocument>(r)),
  remove: (id: string) =>
    fetch(`/api/v1/recipes/${encodeURIComponent(id)}`, { method: 'DELETE' }).then((r) => {
      if (!r.ok && r.status !== 204) throw new Error(`${r.status}`)
    }),
  apply: (id: string) =>
    fetch(`/api/v1/recipes/${encodeURIComponent(id)}/apply`, { method: 'POST' }).then((r) =>
      json<{ ok: boolean; recipeVersionId: string; state: string }>(r),
    ),
}
