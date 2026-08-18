import { describe, expect, it } from 'vitest'
import { appendPersistedEvent, loadPersistedRunProgress, projectLifecycleEvent } from './runProgress'
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
    let runReads = 0
    const result = await loadPersistedRunProgress('run-1', {
      getRun: async () => {
        calls.push('run')
        runReads++
        return runReads === 1 ? run : { ...run, status: 'Completed', findingId: 'finding-1' }
      },
      getEvents: async (_, after) => {
        calls.push(`events:${after}`)
        return after === 0 ? firstPage : [event(101)]
      }
    })

    expect(calls).toEqual(['run', 'events:0', 'events:100', 'run'])
    expect(result.events).toHaveLength(101)
    expect(result.after).toBe(101)
    expect(result.run.findingId).toBe('finding-1')
  })

  it('deduplicates and orders a reconnect event against hydrated history', () => {
    const history = [event(1), event(3)]
    expect(appendPersistedEvent(history, event(3))).toBe(history)
    expect(appendPersistedEvent(history, event(2)).map(item => item.cursor)).toEqual([1, 2, 3])
  })

  it('projects persisted reproduction and minimization vocabulary into the live status', () => {
    const reproduction = { ...event(2), kind: 'reproduction-started' }
    const minimization = { ...event(3), kind: 'minimization-started' }

    expect(projectLifecycleEvent(run, reproduction).status).toBe('Reproducing')
    expect(projectLifecycleEvent({ ...run, status: 'Reproducing' }, minimization).status).toBe('Minimizing')
    expect(projectLifecycleEvent({ ...run, status: 'Minimizing' }, reproduction).status).toBe('Minimizing')
  })

  it.each(['Reproducing', 'Minimizing'])('refresh reconstructs the %s phase from PostgreSQL state', async status => {
    const current = { ...run, status }
    const result = await loadPersistedRunProgress('run-1', {
      getRun: async () => current,
      getEvents: async () => [
        { ...event(1), kind: 'campaign-started' },
        { ...event(2), kind: 'reproduction-started' },
        ...(status === 'Minimizing' ? [{ ...event(3), kind: 'minimization-started' }] : [])
      ]
    })

    expect(result.run.status).toBe(status)
    expect(result.events.map(item => item.cursor)).toEqual(status === 'Minimizing' ? [1, 2, 3] : [1, 2])
  })
})
