import { useEffect, useState } from 'react'
import type { RunEvent } from '../api/contracts'

const eventKinds = ['campaign-started', 'agent-decision', 'deterministic-violation-observed', 'campaign-no-finding', 'budget-exhausted', 'model-failed', 'worker-failed']

export function LiveCampaignPage({ runId }: { runId: string }) {
  const cursorKey = `racehunter:run-cursor:${runId}`
  const [events, setEvents] = useState<RunEvent[]>([])

  useEffect(() => {
    const after = Number(localStorage.getItem(cursorKey) ?? '0')
    const source = new EventSource(`/api/runs/${runId}/events?after=${after}`)
    const receive = (incoming: Event) => {
      const item = JSON.parse((incoming as MessageEvent<string>).data) as RunEvent
      setEvents(current => current.some(existing => existing.cursor === item.cursor) ? current : [...current, item])
      localStorage.setItem(cursorKey, String(item.cursor))
    }
    eventKinds.forEach(kind => source.addEventListener(kind, receive))
    return () => source.close()
  }, [cursorKey, runId])

  return <main>
    <p className="eyebrow">LIVE CAMPAIGN</p>
    <h1 className="page-title">Bounded agent activity</h1>
    <section aria-live="polite">
      {events.length === 0 && <p>Waiting for persisted progress…</p>}
      <ol>{events.map(item => <li key={item.cursor}><strong>{item.kind}</strong> — {item.message}</li>)}</ol>
    </section>
  </main>
}
