import { useEffect, useRef } from 'react'
import uPlot from 'uplot'
import 'uplot/dist/uPlot.min.css'
import type { SpcChart } from '../api/spc'

type Props = {
  chart: SpcChart | null
  title?: string
}

export function SpcControlChart({ chart, title = 'Control Chart' }: Props) {
  const rootRef = useRef<HTMLDivElement>(null)
  const plotRef = useRef<uPlot | null>(null)

  useEffect(() => {
    if (!rootRef.current) return

    const opts: uPlot.Options = {
      width: rootRef.current.clientWidth || 720,
      height: 320,
      title,
      scales: { x: { time: false } },
      axes: [
        { label: 'Sample #', stroke: '#5c6570', grid: { stroke: '#e6e9ed' } },
        { label: 'Value', stroke: '#5c6570', grid: { stroke: '#e6e9ed' } },
      ],
      series: [
        {},
        { label: 'Value', stroke: '#214f78', width: 2, points: { show: true, size: 6 } },
        { label: 'UCL', stroke: '#c0392b', width: 1, dash: [6, 4], points: { show: false } },
        { label: 'CL', stroke: '#1f6b3a', width: 1, dash: [4, 4], points: { show: false } },
        { label: 'LCL', stroke: '#c0392b', width: 1, dash: [6, 4], points: { show: false } },
      ],
    }

    plotRef.current = new uPlot(opts, [[], [], [], [], []], rootRef.current)
    const onResize = () => {
      if (!rootRef.current || !plotRef.current) return
      plotRef.current.setSize({ width: rootRef.current.clientWidth, height: 320 })
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
    if (!plot || !chart) return
    const xs = chart.points.map((p) => p.index)
    const ys = chart.points.map((p) => p.value)
    const ucl = chart.points.map(() => chart.ucl)
    const cl = chart.points.map(() => chart.cl)
    const lcl = chart.points.map(() => chart.lcl)
    plot.setData([xs, ys, ucl, cl, lcl])
  }, [chart])

  return <div className="chart-shell" ref={rootRef} />
}
