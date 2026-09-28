import { useState } from 'react'
import { MesScreen } from './components/MesScreen'
import { RecipeScreen } from './components/RecipeScreen'
import { RunScreen } from './components/RunScreen'
import { SpcScreen } from './components/SpcScreen'
import { TraceScreen } from './components/TraceScreen'
import './index.css'

type Page = 'run' | 'recipe' | 'trace' | 'mes' | 'spc'

export default function App() {
  const [page, setPage] = useState<Page>('run')

  return (
    <div className="app-shell">
      <nav className="app-nav">
        <button type="button" className={page === 'run' ? 'active' : ''} onClick={() => setPage('run')}>
          Run
        </button>
        <button
          type="button"
          className={page === 'recipe' ? 'active' : ''}
          onClick={() => setPage('recipe')}
        >
          Recipe
        </button>
        <button
          type="button"
          className={page === 'trace' ? 'active' : ''}
          onClick={() => setPage('trace')}
        >
          Trace
        </button>
        <button type="button" className={page === 'spc' ? 'active' : ''} onClick={() => setPage('spc')}>
          SPC
        </button>
        <button type="button" className={page === 'mes' ? 'active' : ''} onClick={() => setPage('mes')}>
          MES
        </button>
      </nav>
      {page === 'run' ? <RunScreen /> : null}
      {page === 'recipe' ? <RecipeScreen onApplied={() => setPage('run')} /> : null}
      {page === 'trace' ? <TraceScreen /> : null}
      {page === 'spc' ? <SpcScreen /> : null}
      {page === 'mes' ? <MesScreen /> : null}
    </div>
  )
}
