import { useEffect, useMemo, useState } from 'react'
import { configureManualTarget, createInventoryHunt, getCapabilities, requestPlan } from '../api/client'

type Method = 'GET' | 'POST'
type ObservationType = 'number' | 'text'
type InvariantFamily = 'numeric-boundary' | 'cardinality' | 'cross-observation'

export function NewHuntPage() {
  const [objective, setObjective] = useState('Successful reservations must not exceed capacity.')
  const [busy, setBusy] = useState(false), [error, setError] = useState('')
  const [manualAvailable, setManualAvailable] = useState(false), [manual, setManual] = useState(false)
  const [adminToken, setAdminToken] = useState('')
  const [baseUrl, setBaseUrl] = useState('http://reference-target:8080')
  const [authorizedHost, setAuthorizedHost] = useState('reference-target'), [authorizedPort, setAuthorizedPort] = useState('8080')
  const [secretReference, setSecretReference] = useState('projects/local-demo/secrets/manual-target-token/versions/latest')
  const [setupEnabled, setSetupEnabled] = useState(true), [setupMethod, setSetupMethod] = useState<Method>('POST')
  const [setupPath, setSetupPath] = useState('/manual/reset'), [setupTemplate, setSetupTemplate] = useState('{"quantity":1,"mode":"vulnerable"}')
  const [operationId, setOperationId] = useState('reserve-seat'), [operationMethod, setOperationMethod] = useState<Method>('POST')
  const [operationPath, setOperationPath] = useState('/manual/orders')
  const [operationTemplate, setOperationTemplate] = useState('{"actorId":"{{actorId}}","quantity":1,"checkpoint":"{{checkpoint}}","idempotencyKey":"{{executionKey}}-{{actorId}}","replayScope":"{{executionKey}}"}')
  const [metric, setMetric] = useState('reservation-count'), [observationPath, setObservationPath] = useState('$.reservationCount')
  const [observationType, setObservationType] = useState<ObservationType>('number')
  const [secondMetric, setSecondMetric] = useState('reservation-capacity'), [secondObservationPath, setSecondObservationPath] = useState('$.reservationCapacity')
  const [secondObservationType, setSecondObservationType] = useState<ObservationType>('number')
  const [invariantFamily, setInvariantFamily] = useState<InvariantFamily>('numeric-boundary')
  const [maximum, setMaximum] = useState('1'), [relation, setRelation] = useState('less-than-or-equal')
  const [sensitivePaths, setSensitivePaths] = useState('$.token\n$.authorization')

  useEffect(() => { void getCapabilities().then(value => setManualAvailable(value.manualTargetsEnabled)).catch(() => setManualAvailable(false)) }, [])
  const renderedPreview = useMemo(() => operationTemplate.replaceAll('{{actorId}}', 'actor-1').replaceAll('{{runId}}', 'preview-run')
    .replaceAll('{{executionKey}}', 'preview-key').replaceAll('{{checkpoint}}', 'racehunter:preview'), [operationTemplate])

  async function generatePlan() {
    setBusy(true); setError('')
    try {
      let targetId: string | undefined, planningObjective = objective
      if (manual) {
        const url = new URL(baseUrl), effectivePort = url.port || (url.protocol === 'https:' ? '443' : '80')
        if (url.hostname !== authorizedHost.trim() || effectivePort !== authorizedPort.trim()) throw new Error('The authorized host and port must exactly match the target base URL.')
        JSON.parse(operationTemplate); if (setupEnabled) JSON.parse(setupTemplate)
        if (invariantFamily === 'numeric-boundary' && observationType !== 'number') throw new Error('Numeric boundary requires a numeric primary observation.')
        if (invariantFamily === 'cardinality' && observationType !== 'text') throw new Error('Cardinality requires a text primary observation.')
        if (invariantFamily === 'cross-observation' && (observationType !== 'number' || secondObservationType !== 'number')) throw new Error('Cross observation requires two numeric observations from the same operation.')
        const observationPaths: Record<string, string> = { [metric]: observationPath }
        const observationTypes: Record<string, ObservationType> = { [metric]: observationType }
        if (invariantFamily === 'cross-observation') { observationPaths[secondMetric] = secondObservationPath; observationTypes[secondMetric] = secondObservationType }
        const operations = [
          ...(setupEnabled ? [{ id: 'setup', method: setupMethod, path: setupPath, requestTemplateJson: setupTemplate, observationPaths: {}, observationTypes: {}, isSetup: true }] : []),
          { id: operationId, method: operationMethod, path: operationPath, requestTemplateJson: operationTemplate, observationPaths, observationTypes, isSetup: false }
        ]
        const target = await configureManualTarget({ baseUrl, allowedHosts: [authorizedHost.trim()], credentialReference: secretReference,
          sensitiveJsonPaths: sensitivePaths.split(/[,\n]/).map(value => value.trim()).filter(Boolean), operations }, adminToken)
        targetId = target.id
        planningObjective += ` invariant-family=${invariantFamily}; metric=${metric}; maximum=${maximum}; left-metric=${metric}; right-metric=${secondMetric}; relation=${relation}`
      }
      const hunt = await createInventoryHunt(planningObjective, targetId, manual ? adminToken : undefined)
      await requestPlan(hunt.id); window.location.assign(`/hunts/${hunt.id}/plan`)
    } catch (reason) { setError(reason instanceof Error ? reason.message : 'Planning could not be requested.'); setBusy(false) }
  }

  return <main><p className="eyebrow">NEW HUNT · {manual ? 'AUTHORIZED MANUAL TARGET' : 'INVENTORY SANDBOX'}</p><h1 className="page-title">Define the rule</h1><section>
    <label htmlFor="objective">Transactional correctness rule</label><textarea id="objective" value={objective} onChange={event => setObjective(event.target.value)} rows={4} />
    {manualAvailable && <fieldset><legend>Local admin target</legend><label><input type="checkbox" checked={manual} onChange={event => setManual(event.target.checked)} /> Use authorized HTTP/JSON target</label>
      {manual && <>
        <label htmlFor="admin-token">Admin bearer token</label><input id="admin-token" type="password" value={adminToken} onChange={event => setAdminToken(event.target.value)} autoComplete="off" />
        <label htmlFor="target-url">Target base URL</label><input id="target-url" value={baseUrl} onChange={event => setBaseUrl(event.target.value)} />
        <label htmlFor="authorized-host">Authorized host</label><input id="authorized-host" value={authorizedHost} onChange={event => setAuthorizedHost(event.target.value)} />
        <label htmlFor="authorized-port">Authorized port</label><input id="authorized-port" inputMode="numeric" value={authorizedPort} onChange={event => setAuthorizedPort(event.target.value)} />
        <label htmlFor="secret-reference">Secret Manager version reference</label><input id="secret-reference" value={secretReference} onChange={event => setSecretReference(event.target.value)} />
        <label><input type="checkbox" checked={setupEnabled} onChange={event => setSetupEnabled(event.target.checked)} /> Run a setup operation before each attempt</label>
        {setupEnabled && <><label htmlFor="setup-method">Setup method</label><select id="setup-method" value={setupMethod} onChange={event => setSetupMethod(event.target.value as Method)}><option>POST</option><option>GET</option></select><label htmlFor="setup-path">Setup path</label><input id="setup-path" value={setupPath} onChange={event => setSetupPath(event.target.value)} /><label htmlFor="setup-template">Setup request JSON template</label><textarea id="setup-template" value={setupTemplate} onChange={event => setSetupTemplate(event.target.value)} /></>}
        <label htmlFor="operation-id">Operation ID</label><input id="operation-id" value={operationId} onChange={event => setOperationId(event.target.value)} />
        <label htmlFor="operation-method">Operation method</label><select id="operation-method" value={operationMethod} onChange={event => setOperationMethod(event.target.value as Method)}><option>POST</option><option>GET</option></select>
        <label htmlFor="operation-path">Operation path</label><input id="operation-path" value={operationPath} onChange={event => setOperationPath(event.target.value)} />
        <label htmlFor="operation-template">Operation request JSON template</label><textarea id="operation-template" value={operationTemplate} onChange={event => setOperationTemplate(event.target.value)} rows={4} />
        <label htmlFor="metric">Primary metric</label><input id="metric" value={metric} onChange={event => setMetric(event.target.value)} />
        <label htmlFor="observation-path">Primary observation JSON path</label><input id="observation-path" value={observationPath} onChange={event => setObservationPath(event.target.value)} />
        <label htmlFor="observation-type">Primary observation type</label><select id="observation-type" value={observationType} onChange={event => setObservationType(event.target.value as ObservationType)}><option value="number">number</option><option value="text">text</option></select>
        <label htmlFor="invariant-family">Invariant family</label><select id="invariant-family" value={invariantFamily} onChange={event => setInvariantFamily(event.target.value as InvariantFamily)}><option value="numeric-boundary">numeric boundary</option><option value="cardinality">cardinality</option><option value="cross-observation">cross observation</option></select>
        {invariantFamily === 'numeric-boundary' && <><label htmlFor="maximum">Maximum</label><input id="maximum" type="number" value={maximum} onChange={event => setMaximum(event.target.value)} /></>}
        {invariantFamily === 'cross-observation' && <><label htmlFor="second-metric">Second metric</label><input id="second-metric" value={secondMetric} onChange={event => setSecondMetric(event.target.value)} /><label htmlFor="second-observation-path">Second observation JSON path</label><input id="second-observation-path" value={secondObservationPath} onChange={event => setSecondObservationPath(event.target.value)} /><label htmlFor="second-observation-type">Second observation type</label><select id="second-observation-type" value={secondObservationType} onChange={event => setSecondObservationType(event.target.value as ObservationType)}><option value="number">number</option><option value="text">text</option></select><label htmlFor="relation">Relation</label><select id="relation" value={relation} onChange={event => setRelation(event.target.value)}><option value="equal">equal</option><option value="less-than-or-equal">less than or equal</option><option value="greater-than-or-equal">greater than or equal</option></select></>}
        <label htmlFor="sensitive-paths">Sensitive JSON paths</label><textarea id="sensitive-paths" value={sensitivePaths} onChange={event => setSensitivePaths(event.target.value)} />
        <output aria-label="Rendered request preview"><code>{renderedPreview}</code></output><p>Only the displayed host, port, operations, templates, observations, and secret reference enter the immutable target snapshot.</p>
      </>}
    </fieldset>}
    <dl className="budgets"><div><dt>Actors</dt><dd>10</dd></div><div><dt>Requests</dt><dd>40</dd></div><div><dt>Gemini calls</dt><dd>5</dd></div><div><dt>Duration</dt><dd>90s</dd></div></dl>
    {error && <p role="alert">{error}</p>}<button type="button" disabled={busy || objective.trim().length === 0 || (manual && adminToken.length === 0)} onClick={generatePlan}>{busy ? 'Requesting plan…' : 'Generate Plan'}</button>
  </section></main>
}
