import type { FindingResponse } from '../api/contracts'

export function AgentActivity({ finding }: { finding: FindingResponse }) {
  return <section aria-label="Agent Activity">
    <h2>Agent Activity</h2>
    <p>Gemini interpretation is advisory. Deterministic evidence establishes finding truth.</p>
    <ol>{finding.agentActivity.map(item => <li key={`${item.iteration}:${item.modelInvocationId}`}>
      <strong>{item.action}</strong> — {item.rationaleSummary}
      <small>{item.modelId} · {item.schemaVersion} · {item.modelInvocationId}</small>
    </li>)}</ol>
  </section>
}
