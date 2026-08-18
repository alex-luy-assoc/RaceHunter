import { useEffect, useState } from 'react'
import { getRun } from '../api/client'
import { appendPersistedEvent, loadPersistedRunProgress, projectLifecycleEvent } from '../api/runProgress'
import type { RunEvent, RunResponse } from '../api/contracts'

const eventKinds = ['campaign-started', 'agent-decision', 'deterministic-violation-observed', 'reproduction-started', 'minimization-started', 'finding-ready', 'reproduction-inconclusive', 'campaign-no-finding', 'budget-exhausted', 'model-failed', 'worker-failed', 'work-dead-lettered']

export function LiveCampaignPage({ runId }: { runId: string }) {
  const cursorKey = `racehunter:run-cursor:${runId}`
  const [events, setEvents] = useState<RunEvent[]>([])
  const [run, setRun] = useState<RunResponse>()
  const [loadError, setLoadError] = useState<string>()

  useEffect(() => {
    let disposed = false
    let source: EventSource | undefined
    const hydrate = async () => {
      try {
        const persisted = await loadPersistedRunProgress(runId)
        if (disposed) return
        setRun(persisted.run)
        setEvents(persisted.events)
        localStorage.setItem(cursorKey, String(persisted.after))
        source = new EventSource(`/api/runs/${runId}/events?after=${persisted.after}`)
        const receive = (incoming: Event) => {
          const item = JSON.parse((incoming as MessageEvent<string>).data) as RunEvent
          setEvents(current => appendPersistedEvent(current, item))
          setRun(current => current ? projectLifecycleEvent(current, item) : current)
          localStorage.setItem(cursorKey, String(item.cursor))
          if (item.kind === 'finding-ready') {
            void getRun(runId).then(latest => {
              if (!disposed) setRun(latest)
            }).catch(error => {
              if (!disposed) setLoadError(error instanceof Error ? error.message : 'Verified finding could not be loaded.')
            })
          }
        }
        eventKinds.forEach(kind => source?.addEventListener(kind, receive))
      } catch (error) {
        if (!disposed) setLoadError(error instanceof Error ? error.message : 'Persisted run progress could not be loaded.')
      }
    }
    void hydrate()
    return () => { disposed = true; source?.close() }
  }, [cursorKey, runId])

  return <main>
    <p className="eyebrow">LIVE CAMPAIGN</p>
    <h1 className="page-title">Bounded agent activity</h1>
    {run && <p>Persisted run status: <strong>{run.status}</strong></p>}
    {run?.findingId && <a href={`/findings/${run.findingId}`}>Open verified finding</a>}
    {loadError && <p role="alert">{loadError}</p>}
    <section aria-live="polite">
      {!loadError && events.length === 0 && <p>Loading persisted progress…</p>}
      <ol>{events.map(item => <li key={item.cursor}><strong>{item.kind}</strong> — {item.message}</li>)}</ol>
    </section>
  </main>
}
