# RaceHunter Submission Checklist

- [ ] Unedited demo is under four minutes and follows `demo-script.md`.
- [ ] Live journey starts from the plain-language invariant and requires one **Approve & Run** action.
- [ ] Finding shows deterministic evidence, 3/3 measured reproduction, two-actor minimization, causal timeline, agent activity, and immutable fingerprint.
- [ ] Verify Fix shows vulnerable **Fail** and fixed **Pass** for the same artifact.
- [ ] Cloud Proof shows the deployed API revision, private worker, Pub/Sub, Gemini/schema, Cloud SQL, OIDC auth, run, and trace identifiers.
- [ ] Architecture image/diagram matches the three checked-in Dockerfiles and Terraform.
- [ ] README setup was repeated from a clean checkout; local real Playwright and Compose golden path pass.
- [ ] Terraform `fmt`, offline init, and validate pass in the pinned official container; no apply occurs during validation.
- [ ] NuGet/npm vulnerability audits and repository secret scan report no findings.
- [ ] No credentials, personal data, chain-of-thought, fabricated incident, or unsupported reproducibility claim appears in the submission.
- [ ] Google Cloud deployment/apply/smoke was performed only after explicit owner approval.
