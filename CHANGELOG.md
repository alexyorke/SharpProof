# Changelog

Entries describe repository changes, not package publication. Confirm available versions on the configured destination feed.

## Unreleased

### Developer experience

- Extend the native verifier payload/matrix to Windows, macOS, and glibc Linux on x64/ARM64. Windows solver payloads link their C++ runtime statically, removing the separate Visual C++ redistributable prerequisite. .NET SDK 10.0.100+ remains required; musl and other OS/CPU combinations are outside this support boundary. Exact-artifact native ABI checks establish release qualification separately from compilation/import audits.

- Make the main `SharpProof` package the single-reference standard setup with automatic strict full verification and transitive contract/analyzer dependencies. The cycle-free graph is `SharpProof -> SharpProof.Verifier -> SharpProof.Attributes`; Verifier remains a compatibility entrypoint. Advisory/off are explicit opt-outs. Existing main-package consumers without an explicit profile now receive strict policy and require SDK 10; review upgrade guidance.

- Separate portable first feedback, full verification, and incremental adoption instructions.
- Reconcile qualified consumer hosts with the container-only contributor workflow.
- Add local project templates using the authoritative checkout package version, plus a packed-consumer smoke check.
- Add upgrade guidance, contributor navigation, structured issue forms, and guidance for preparing sanitized reproductions.
- Move repository toolchain and verifier tasks/worker to .NET 10; full verification now requires SDK 10.0.100+ with the .NET 10 runtime. Portable analyzer minimum remains SDK 9.0.300+/Roslyn 4.14; application targets are independent.

### Release review

Before a release, replace this review note with the concrete shipped changes, required SDK/runtime, diagnostic/configuration changes, semantic/evidence changes, migration steps, known limitations and exact qualification evidence. Do not infer a publication date or artifact availability from this entry.
