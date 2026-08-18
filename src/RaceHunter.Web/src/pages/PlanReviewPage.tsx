import { useEffect, useState } from 'react'
import { approvePlan, getPlan } from '../api/client'
import type { PlanResponse } from '../api/contracts'

export function PlanReviewPage({ huntId }: { huntId: string }) {
  const [plan, setPlan] = useState<PlanResponse>()
  const [error, setError] = useState('')
  const [approved, setApproved] = useState(false)

  useEffect(() => {
    let stopped = false
    let source: EventSource | undefined
    async function load() {
      try {
        const ready = await getPlan(huntId)
        if (!stopped && ready) setPlan(ready)
        else if (!stopped) {
          source = new EventSource(`/api/hunts/${huntId}/events?after=0`)
          source.addEventListener('plan-ready', () => { source?.close(); void load() })
          source.addEventListener('model-failed', () => { source?.close(); setError('Plan generation failed schema validation.') })
        }
      } catch (reason) {
        if (!stopped) setError(reason instanceof Error ? reason.message : 'Plan generation failed.')
      }
    }
    void load()
    return () => { stopped = true; source?.close() }
  }, [huntId])

  async function approve() {
    if (!plan || approved) return
    setApproved(true)
    const storageKey = `racehunter:approval:${huntId}`
    const idempotencyKey = localStorage.getItem(storageKey) ?? crypto.randomUUID()
    localStorage.setItem(storageKey, idempotencyKey)
    try {
      const result = await approvePlan(huntId, plan.planVersion, idempotencyKey)
      window.location.assign(`/runs/${result.runId}`)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Approval failed.')
      setApproved(false)
    }
  }

  return <main>
    <p className="eyebrow">PLAN REVIEW</p>
    <h1 className="page-title">Approve once. Run unattended.</h1>
    {!plan && !error && <section aria-live="polite"><p>Gemini is generating a schema-constrained plan…</p></section>}
    {plan && <section>
      <p><strong>Plan version</strong> {plan.planVersion}</p>
      <p><strong>Model contract</strong> {plan.modelId} · {plan.schemaVersion}</p>
      <p><strong>Strategy</strong> {plan.strategy.kind} · {plan.strategy.actorCount} actors · seed {plan.strategy.seed}</p>
      <p><strong>Invariant</strong> {plan.invariant.metric} ≤ {plan.invariant.maximum}</p>
      <button type="button" disabled={approved} onClick={approve}>{approved ? 'Approval recorded…' : 'Approve & Run'}</button>
    </section>}
    {error && <p role="alert">{error}</p>}
  </main>
}
