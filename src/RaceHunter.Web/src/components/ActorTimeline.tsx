import type { FindingResponse } from '../api/contracts'
import { buildActorLanes } from '../pages/findingView'

export function ActorTimeline({ finding }: { finding: FindingResponse }) {
  return <section aria-label="Causal actor-lane timeline">
    <h2>Causal actor-lane timeline</h2>
    <div className="actor-lanes">
      {buildActorLanes(finding).map(lane => <article className="actor-lane" key={lane.actorId}>
        <h3>Actor {lane.actorId}</h3>
        <ol>{lane.events.map(event => <li key={event.sequence}>
          <strong>#{event.sequence} · {event.stepId}</strong>
          <span>{event.kind} · attempt {event.attemptId} · request {event.requestId}</span>
        </li>)}</ol>
      </article>)}
    </div>
  </section>
}
