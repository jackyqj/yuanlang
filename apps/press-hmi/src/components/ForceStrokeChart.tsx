import { useEffect, useRef } from 'react'
import uPlot from 'uplot'
import 'uplot/dist/uPlot.min.css'
import type { SamplePoint } from '../api/runtime'

type Props = {
  samples: SamplePoint[]
  title?: string
}

export function ForceStrokeChart({ samples, title = 'Pressure vs Stroke' }: Props) {
  const rootRef = useRef<HTMLDivElement>(null)
  const plotRef = useRef<uPlot | null>(null)

  useEffect(() => {
    if (!rootRef.current) return

    const opts: uPlot.Options = {
      width: rootRef.current.clientWidth || 720,
      height: 360,
      title,
      scales: {
        x: { time: false },
      },
      axes: [
        { label: 'Stroke (mm)', stroke: '#5c6570', grid: { stroke: '#e6e9ed' } },
        { label: 'Force (N)', stroke: '#5c6570', grid: { stroke: '#e6e9ed' } },
      ],
      series: [
        {},
        {
          label: 'Force',
          stroke: '#c0392b',
          width: 2,
          points: { show: false },
        },
      ],
    }

    plotRef.current = new uPlot(opts, [[], []], rootRef.current)

    const onResize = () => {
      if (!rootRef.current || !plotRef.current) return
      plotRef.current.setSize({
        width: rootRef.current.clientWidth,
        height: 360,
      })
    }
    window.addEventListener('resize', onResize)
    return () => {
      window.removeEventListener('resize', onResize)
      plotRef.current?.destroy()
      plotRef.current = null
    }
  }, [title])

  useEffect(() => {
    const plot = plotRef.current
    if (!plot) return
    const xs = samples.map((s) => s.positionMm)
    const ys = samples.map((s) => s.forceN)
    plot.setData([xs, ys])
  }, [samples])

  return <div className="chart-shell" ref={rootRef} />
}
