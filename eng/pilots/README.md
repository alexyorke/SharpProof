# Preview pilot libraries

These five projects are packaged-adoption pilots, not product projects. They
pin real public libraries and consume SharpProof only from the candidate local
NuGet feed. Two are effect-heavy, two are contract-heavy, and one uses strict
mixed mode.

Run:

```powershell
./scripts/Test-SharpProofPilots.ps1 `
  -PackageSource artifacts/pilots/packages `
  -OutputPath artifacts/pilots/report.json
```

The report binds the Git commit and package version and records build status,
diagnostic occurrence counts from the captured SARIF, typed Unknown reasons,
wall time, observed process-tree peak working set, false-positive occurrence
count, and setup friction. Each ledger row reviews one diagnostic ID; marking
it false positive counts every SARIF occurrence for that ID. The reviewed
report records the ledger SHA-256, and the pilots receipt revalidates the
complete ledger and its false-positive totals against that binding. An advisory
Unknown caused by a documented unsupported external callee is a reviewed
limitation, not a false proof or a release defect. The strict pilot must finish
with every selected claim proven.

## Reviewing a release candidate

An annotated release-tag run uploads a `pilot-review-report-<commit>` artifact
containing `report.json` and `review-ledger.template.json`. Inspect every claim
and diagnostic in the report, then set every template row's `disposition` to
`TruePositive` or `FalsePositive`. A blank template row is never treated as an
approval.

Resume the `Package, consumers, and release qualification` workflow manually
on the same release tag. Supply the successful tag-push run ID and the completed
ledger JSON in `pilot_review_source_run_id` and `pilot_review_ledger`. The
workflow checks that the source run succeeded for the same commit, reuses its
exact package bytes and pilot report, and validates every ledger row before it
creates the pilot receipt. Qualification and the protected publication
environment run only after that receipt exists. Missing, stale, mismatched, or
incomplete review input fails closed.

To prepare a ledger locally from a downloaded report, run:

```powershell
./scripts/New-SharpProofPilotReviewLedger.ps1 `
  -SourceReportPath artifacts/pilots/report.json `
  -OutputPath artifacts/pilots/review-ledger.template.json
```
