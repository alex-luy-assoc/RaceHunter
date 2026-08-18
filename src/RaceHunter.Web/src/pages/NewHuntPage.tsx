import { useState } from 'react'
import { createInventoryHunt, requestPlan } from '../api/client'

export function NewHuntPage() {
  const [objective, setObjective] = useState('Successful orders must not exceed available inventory.')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  async function generatePlan() {
    setBusy(true)
    setError('')
    try {
      const hunt = await createInventoryHunt(objective)
      await requestPlan(hunt.id)
      window.location.assign(`/hunts/${hunt.id}/plan`)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Planning could not be requested.')
      setBusy(false)
    }
  }

  return <main>
    <p className="eyebrow">NEW HUNT · INVENTORY SANDBOX</p>
    <h1 className="page-title">Define the rule</h1>
    <section>
      <label htmlFor="objective">Transactional correctness rule</label>
      <textarea id="objective" value={objective} onChange={event => setObjective(event.target.value)} rows={4} />
      <dl className="budgets">
        <div><dt>Actors</dt><dd>10</dd></div><div><dt>Requests</dt><dd>40</dd></div>
        <div><dt>Gemini calls</dt><dd>5</dd></div><div><dt>Duration</dt><dd>90s</dd></div>
      </dl>
      {error && <p role="alert">{error}</p>}
      <button type="button" disabled={busy || objective.trim().length === 0} onClick={generatePlan}>
        {busy ? 'Requesting plan…' : 'Generate Plan'}
      </button>
    </section>
  </main>
}
