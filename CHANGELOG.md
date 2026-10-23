# Changelog

Entries describe repository changes, not package publication. Confirm available versions on the configured destination feed.

## Unreleased

### Developer experience

- Add a workspace template with pinned local tools, optional editor assistance,
  private-feed configuration, and portable/container profiles; add preview-first
  `init` for existing projects and text/JSON/Markdown command reports.
- Add exact-base-SHA consumer CI comparison with report mode by default and an
  explicit enforcement mode. Baselines come from trusted successful source-bound
  runs; missing evidence requires initialization rather than a current-run fallback.
- Make exported NUnit project references relative and add workspace export guidance
  so a complete workspace can move between host paths. Export still checks captured
  compiler inputs and supports the documented scalar postcondition subset only.
- Add optional analyzer-backed companion assistance alongside the existing
  refactoring. Preserve conservative action eligibility, ordinary editable source,
  and separate Result-type Fix All behavior.
- Separate official private-preview version/package/image/evidence publication from
  public CI validation versions. Add source/workflow identity, offline signed manifest,
  symbol retention, private read-back/access receipts, and a three-editor Windows x64
  acceptance runbook. All new release and interactive-editor evidence is pending.
- Rebuild the nightly fuzz runner with the solution-build allowance (600 seconds),
  instead of the worker's per-project limit (300 seconds), and retain its build streams.
  Retained/rotating run budgets, seeds, case counts, and proof budgets are unchanged.

- Extend the native verifier payload/matrix to Windows, macOS, and glibc Linux on x64/ARM64. Windows solver payloads link their C++ runtime statically, removing the separate Visual C++ redistributable prerequisite. .NET SDK 10.0.100+ remains required; musl and other OS/CPU combinations are outside this support boundary. Exact-artifact native ABI checks establish release qualification separately from compilation/import audits.

- Make the main `SharpProof` package the single-reference standard setup with automatic strict full verification and transitive contract/analyzer dependencies. The cycle-free graph is `SharpProof -> SharpProof.Verifier -> SharpProof.Attributes`; Verifier remains a compatibility entrypoint. Advisory/off are explicit opt-outs. Existing main-package consumers without an explicit profile now receive strict policy and require SDK 10; review upgrade guidance.

- Separate portable first feedback, full verification, and incremental adoption instructions.
- Reconcile qualified consumer hosts with the container-only contributor workflow.
- Add local project templates using the authoritative checkout package version, plus a packed-consumer smoke check.
- Add upgrade guidance, contributor navigation, structured issue forms, and guidance for preparing sanitized reproductions.
- Move repository toolchain and verifier tasks/worker to .NET 10; full verification now requires SDK 10.0.100+ with the .NET 10 runtime. Portable analyzer minimum remains SDK 9.0.300+/Roslyn 4.14; application targets are independent.

### Release review

Before a release, replace this review note with the concrete shipped changes, required SDK/runtime, diagnostic/configuration changes, semantic/evidence changes, migration steps, known limitations and exact qualification evidence. Do not infer a publication date or artifact availability from this entry.
