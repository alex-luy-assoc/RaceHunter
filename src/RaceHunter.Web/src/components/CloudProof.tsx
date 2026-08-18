import { useEffect, useState } from 'react'
import { getCloudProof } from '../api/client'
import type { CloudProofResponse } from '../api/contracts'

export function CloudProof({ runId }: { runId: string }) {
  const [proof, setProof] = useState<CloudProofResponse>()
  useEffect(() => { void getCloudProof(runId).then(setProof).catch(() => setProof(undefined)) }, [runId])
  if (!proof) return <section aria-labelledby="cloud-proof"><h2 id="cloud-proof">Cloud proof</h2><p>Deployment proof is unavailable in this environment.</p></section>
  return <section aria-labelledby="cloud-proof">
    <h2 id="cloud-proof">Cloud proof</h2>
    <dl className="budgets">
      <div><dt>API revision</dt><dd>{proof.apiRevision}</dd></div>
      <div><dt>Private worker</dt><dd>{proof.workerService}</dd></div>
      <div><dt>Dispatch</dt><dd>{proof.pubSubTopic} · RunRequested</dd></div>
      <div><dt>Persistence</dt><dd>{proof.cloudSqlInstance}</dd></div>
      <div><dt>Gemini</dt><dd>{proof.modelId} · {proof.schemaVersions}</dd></div>
      <div><dt>Worker auth</dt><dd>{proof.workerAuthentication}</dd></div>
      <div><dt>Persisted run</dt><dd>{proof.runStatus} · {proof.planVersion}</dd></div>
      <div><dt>Worker execution</dt><dd>{proof.workerExecution}</dd></div>
      <div><dt>Evidence</dt><dd>{proof.traceEventCount} trace events · {proof.modelInvocationId}</dd></div>
    </dl>
    <p>Run {proof.runId} · evidence {proof.evidenceCorrelationId}{proof.requestTraceId ? ` · proof request trace ${proof.requestTraceId}` : ''}</p>
  </section>
}
