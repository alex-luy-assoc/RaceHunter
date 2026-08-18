import { useEffect, useState } from 'react'
import { configureManualTarget, createInventoryHunt, getCapabilities, requestPlan } from '../api/client'

export function NewHuntPage() {
  const [objective, setObjective] = useState('Successful orders must not exceed available inventory.')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [manualAvailable, setManualAvailable] = useState(false)
  const [manual, setManual] = useState(false)
  const [adminToken, setAdminToken] = useState('')
  const [baseUrl, setBaseUrl] = useState('http://reference-target:8080')
  const [secretReference, setSecretReference] = useState('projects/local-demo/secrets/manual-target-token/versions/latest')

  useEffect(() => { void getCapabilities().then(value => setManualAvailable(value.manualTargetsEnabled)).catch(() => setManualAvailable(false)) }, [])

  async function generatePlan() {
    setBusy(true)
    setError('')
    try {
      const target = manual ? await configureManualTarget({
        baseUrl,
        credentialReference: secretReference,
        sensitiveJsonPaths: ['$.token', '$.authorization'],
        operations: [
          { id: 'reset', method: 'POST', path: '/manual/reset', requestTemplateJson: '{"quantity":1,"mode":"vulnerable"}', observationPaths: {}, isSetup: true },
          { id: 'reserve-seat', method: 'POST', path: '/manual/orders', requestTemplateJson: '{"actorId":"{{actorId}}","quantity":1,"checkpoint":"{{checkpoint}}","idempotencyKey":"{{executionKey}}-{{actorId}}","replayScope":"{{executionKey}}"}', observationPaths: { 'reservation-count': '$.actorOrdinal' }, isSetup: false }
        ]
      }, adminToken) : undefined
      const hunt = await createInventoryHunt(objective, target?.id, manual ? adminToken : undefined)
      await requestPlan(hunt.id)
      window.location.assign(`/hunts/${hunt.id}/plan`)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Planning could not be requested.')
      setBusy(false)
    }
  }

  return <main>
    <p className="eyebrow">NEW HUNT · {manual ? 'AUTHORIZED MANUAL TARGET' : 'INVENTORY SANDBOX'}</p>
    <h1 className="page-title">Define the rule</h1>
    <section>
      <label htmlFor="objective">Transactional correctness rule</label>
      <textarea id="objective" value={objective} onChange={event => setObjective(event.target.value)} rows={4} />
      {manualAvailable && <fieldset>
        <legend>Local admin target</legend>
        <label><input type="checkbox" checked={manual} onChange={event => setManual(event.target.checked)} /> Use authorized HTTP/JSON target</label>
        {manual && <>
          <label htmlFor="admin-token">Admin bearer token</label>
          <input id="admin-token" type="password" value={adminToken} onChange={event => setAdminToken(event.target.value)} autoComplete="off" />
          <label htmlFor="target-url">Target base URL</label>
          <input id="target-url" value={baseUrl} onChange={event => setBaseUrl(event.target.value)} />
          <label htmlFor="secret-reference">Secret Manager version reference</label>
          <input id="secret-reference" value={secretReference} onChange={event => setSecretReference(event.target.value)} />
          <p>Operations are allowlisted to reset and reserve-seat; credentials are referenced only and never stored in the browser payload.</p>
        </>}
      </fieldset>}
      <dl className="budgets">
        <div><dt>Actors</dt><dd>10</dd></div><div><dt>Requests</dt><dd>40</dd></div>
        <div><dt>Gemini calls</dt><dd>5</dd></div><div><dt>Duration</dt><dd>90s</dd></div>
      </dl>
      {error && <p role="alert">{error}</p>}
      <button type="button" disabled={busy || objective.trim().length === 0 || (manual && adminToken.length === 0)} onClick={generatePlan}>
        {busy ? 'Requesting plan…' : 'Generate Plan'}
      </button>
    </section>
  </main>
}
