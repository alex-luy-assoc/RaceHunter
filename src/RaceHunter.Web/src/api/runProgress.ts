import { getRun, getRunEvents } from './client'
import type { RunEvent, RunResponse } from './contracts'

export type RunProgressLoader = {
  getRun(runId: string): Promise<RunResponse>
  getEvents(runId: string, after: number): Promise<RunEvent[]>
}

const defaultLoader: RunProgressLoader = { getRun, getEvents: getRunEvents }

export async function loadPersistedRunProgress(runId: string, loader: RunProgressLoader = defaultLoader) {
  await loader.getRun(runId)
  const events: RunEvent[] = []
  let after = 0
  while (true) {
    const page = await loader.getEvents(runId, after)
    for (const item of page) {
      if (!events.some(existing => existing.cursor === item.cursor)) events.push(item)
    }
    if (page.length < 100) break
    const next = page.at(-1)?.cursor ?? after
    if (next <= after) break
    after = next
  }
  events.sort((left, right) => left.cursor - right.cursor)
  const run = await loader.getRun(runId)
  return { run, events, after: events.at(-1)?.cursor ?? 0 }
}

export function appendPersistedEvent(events: RunEvent[], incoming: RunEvent) {
  if (events.some(item => item.cursor === incoming.cursor)) return events
  return [...events, incoming].sort((left, right) => left.cursor - right.cursor)
}
