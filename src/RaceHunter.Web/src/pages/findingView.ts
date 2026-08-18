import type { FindingResponse } from '../api/contracts'

export const verifiedReferenceMessage = 'Race condition verified — reproduced 3/3 and minimized to 2 actors.'

export function findingHeadline(finding: FindingResponse): string {
  const external = finding.agentInterpretation.includes('external-target')
  const failures = finding.reproductions.filter(item => item.outcome === 'Fail').length
  const measured = finding.reproductions.length === 3 && (external ? failures >= 2 : failures === 3)
  return measured && finding.replayArtifact.actorCount === 2
    ? finding.successMessage
    : 'Finding evidence is not yet fully reproduced and minimized.'
}

export function buildActorLanes(finding: FindingResponse): FindingResponse['timeline'] {
  return finding.timeline
    .map(lane => ({ ...lane, events: [...lane.events].sort((left, right) => left.sequence - right.sequence) }))
    .sort((left, right) => left.actorId - right.actorId)
}

export function buildReplayComparison(finding: FindingResponse) {
  const vulnerable = [...finding.replayAttempts].reverse().find(item => item.targetMode === 'Vulnerable')
  const fixed = [...finding.replayAttempts].reverse().find(item => item.targetMode === 'Fixed')
  return {
    vulnerable: vulnerable?.outcome ?? finding.invariantOutcome,
    fixed: fixed?.outcome,
    sameArtifact: Boolean(fixed && fixed.artifactFingerprint === finding.replayArtifact.fingerprint &&
      (!vulnerable || vulnerable.artifactFingerprint === finding.replayArtifact.fingerprint))
  }
}
