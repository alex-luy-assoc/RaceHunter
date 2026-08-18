import { useEffect, useRef, useState } from 'react'
import { getFinding, verifyFix } from '../api/client'
import type { FindingResponse } from '../api/contracts'
import { ActorTimeline } from '../components/ActorTimeline'
import { AgentActivity } from '../components/AgentActivity'
import { buildReplayComparison, findingHeadline } from './findingView'

export function FindingPage({ findingId }: { findingId: string }) {
  const [finding, setFinding] = useState<FindingResponse>()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const initialLoadStarted = useRef(false)

  async function load() {
    try {
      setFinding(await getFinding(findingId))
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Finding evidence could not be loaded.')
    }
  }

  useEffect(() => {
    if (initialLoadStarted.current) return
    initialLoadStarted.current = true
    void load()
  }, [findingId])

  async function replay() {
    setBusy(true)
    setError('')
    const storageKey = `racehunter:verify-fix:${findingId}`
    const idempotencyKey = localStorage.getItem(storageKey) ?? 'verify-fix-ui'
    localStorage.setItem(storageKey, idempotencyKey)
    try {
      await verifyFix(findingId, idempotencyKey)
      await load()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Fixed-target replay failed.')
    } finally {
      setBusy(false)
    }
  }

  if (!finding) return <main><p className="eyebrow">FINDING &amp; REPLAY</p>{error ? <p role="alert">{error}</p> : <p>Loading persisted finding evidence…</p>}</main>
  const comparison = buildReplayComparison(finding)
  return <main>
    <p className="eyebrow">FINDING &amp; REPLAY</p>
    <h1 className="page-title">{findingHeadline(finding)}</h1>
    {error && <p role="alert">{error}</p>}
    <section aria-labelledby="deterministic-evidence">
      <h2 id="deterministic-evidence">Deterministic invariant evidence</h2>
      <p><strong>{finding.invariantOutcome}</strong> — {finding.invariantSummary}</p>
      <p>Evidence: {finding.traceReferences.join(', ')}</p>
      <ol>{finding.reproductions.map(item => <li key={item.attempt}>Reproduction {item.attempt}/3: <strong>{item.outcome}</strong> · {item.traceReferences.join(', ')}</li>)}</ol>
    </section>
    <section aria-labelledby="replay-artifact">
      <h2 id="replay-artifact">Immutable replay artifact</h2>
      <p><strong>Replay ID</strong> {finding.replayArtifact.id}</p>
      <p><strong>Fingerprint</strong> {finding.replayArtifact.fingerprint}</p>
      <p>{finding.replayArtifact.strategy} · seed {finding.replayArtifact.seed} · {finding.replayArtifact.actorCount} actors · {finding.replayArtifact.stepCount} minimum steps</p>
      <button type="button" onClick={replay} disabled={busy}>{busy ? 'Verifying fix…' : 'Verify Fix'}</button>
    </section>
    <section aria-label="Replay comparison">
      <h2>Replay comparison</h2>
      <div className="comparison">
        <p><strong>Vulnerable failed invariant</strong><span>{comparison.vulnerable}</span></p>
        <p><strong>{comparison.fixed === 'Pass' ? 'Fixed passed invariant' : comparison.fixed ? 'Fixed failed invariant' : 'Fixed invariant outcome'}</strong><span>{comparison.fixed ?? 'Not yet verified'}</span></p>
      </div>
      <p>{comparison.sameArtifact ? 'Both results use the same immutable artifact.' : 'Awaiting a matching fixed-target artifact fingerprint.'}</p>
    </section>
    <ActorTimeline finding={finding} />
    <AgentActivity finding={finding} />
    <section aria-labelledby="agent-interpretation"><h2 id="agent-interpretation">Gemini interpretation</h2><p>{finding.agentInterpretation}</p></section>
  </main>
}
