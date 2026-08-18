import { describe, expect, it } from 'vitest'
import { appendPersistedEvent, loadPersistedRunProgress } from './runProgress'
import type { RunEvent, RunResponse } from './contracts'

const run: RunResponse = {
  id: 'run-1',
  status: 'Running',
  maxActors: 10,
  maxConcurrentActors: 10,
  maxRequests: 40,
  maxModelCalls: 5,
  maxDurationSeconds: 90,
  createdAtUtc: '2026-08-18T12:00:00Z',
  startedAtUtc: '2026-08-18T12:00:01Z',
  completedAtUtc: null,
  cancellationRequestedAtUtc: null
}

const event = (cursor: number): RunEvent => ({ cursor, kind: 'agent-decision', message: `event-${cursor}`, occurredAtUtc: '2026-08-18T12:00:02Z' })

describe('persisted run progress', () => {
  it('loads the current run and every PostgreSQL history page before choosing the SSE cursor', async () => {
    const calls: string[] = []
    const firstPage = Array.from({ length: 100 }, (_, index) => event(index + 1))
    const result = await loadPersistedRunProgress('run-1', {
      getRun: async () => { calls.push('run'); return run },
      getEvents: async (_, after) => {
        calls.push(`events:${after}`)
        return after === 0 ? firstPage : [event(101)]
      }
    })

    expect(calls).toEqual(['run', 'events:0', 'events:100'])
    expect(result.events).toHaveLength(101)
    expect(result.after).toBe(101)
  })

  it('deduplicates and orders a reconnect event against hydrated history', () => {
    const history = [event(1), event(3)]
    expect(appendPersistedEvent(history, event(3))).toBe(history)
    expect(appendPersistedEvent(history, event(2)).map(item => item.cursor)).toEqual([1, 2, 3])
  })
})
