import { NewHuntPage } from './pages/NewHuntPage'
import { PlanReviewPage } from './pages/PlanReviewPage'
import { LiveCampaignPage } from './pages/LiveCampaignPage'
import { FindingPage } from './pages/FindingPage'

export function App() {
  const path = window.location.pathname
  if (path === '/hunts/new') return <NewHuntPage />
  const plan = path.match(/^\/hunts\/([0-9a-f-]+)\/plan$/i)
  if (plan) return <PlanReviewPage huntId={plan[1]} />
  const run = path.match(/^\/runs\/([0-9a-f-]+)$/i)
  if (run) return <LiveCampaignPage runId={run[1]} />
  const finding = path.match(/^\/findings\/([0-9a-f-]+)$/i)
  if (finding) return <FindingPage findingId={finding[1]} />
  return (
    <main>
      <header>
        <p className="eyebrow">AUTONOMOUS CONCURRENCY CORRECTNESS</p>
        <h1>RaceHunter</h1>
        <p className="lede">Find the schedules that make valid requests produce invalid business outcomes.</p>
      </header>
      <section aria-labelledby="foundation-heading">
        <h2 id="foundation-heading">Walking skeleton online</h2>
        <p>The API, private worker, PostgreSQL foundation, and controlled inventory target share one Docker-first architecture.</p>
        <a href="/hunts/new" aria-label="Start a new concurrency hunt">New Hunt</a>
      </section>
    </main>
  )
}
