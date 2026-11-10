# Changelog

Entries describe repository changes, not package publication. Confirm available versions on the configured destination feed.

## Unreleased

### Packaging

- Point templates, `init` and the consumer workflow at nuget.org instead of the
  private GitHub feed and its credentials; the workflow still accepts that feed with
  `package-read-token`. The package version moves to `1.0.0-preview.2`. It is not
  published yet, so until it is, restore from a configured feed that carries it.
- Keep strict verification with the project that references SharpProof. The
  package's MSBuild files now ship in `build/`, passed to the direct consumer by
  `include="All"` on the SharpProof -> SharpProof.Verifier dependency, instead of
  `buildTransitive/`. A project that references a verified library, or a package
  that depends on SharpProof, gets the contract API but no longer inherits strict
  verification, the worker run or the SDK 10 requirement.
- Smoke-test the packed packages on Windows and macOS in the release workflow, and
  attest package provenance before publishing.
- Extend the native verifier payload/matrix to Windows, macOS, and glibc Linux on x64/ARM64. Windows solver payloads link their C++ runtime statically, removing the separate Visual C++ redistributable prerequisite. .NET SDK 10.0.100+ remains required; musl and other OS/CPU combinations are outside this support boundary. Exact-artifact native ABI checks establish release qualification separately from compilation/import audits.
- Make the main `SharpProof` package the single-reference standard setup with automatic strict full verification and transitive contract/analyzer dependencies. The cycle-free graph is `SharpProof -> SharpProof.Verifier -> SharpProof.Attributes`; Verifier remains a compatibility entrypoint. Advisory/off are explicit opt-outs. Existing main-package consumers without an explicit profile now receive strict policy and require SDK 10; review upgrade guidance.
- Move repository toolchain and verifier tasks/worker to .NET 10; full verification now requires SDK 10.0.100+ with the .NET 10 runtime. Portable analyzer minimum remains SDK 9.0.300+/Roslyn 4.14; application targets are independent.
- Move to SDK 10.0.403 (.NET 10.0.14, a security release) and re-audit the
  pinned CoreLib facts; patch `libbrotli1` in the tooling image.
- Run the `net8.0` test projects on .NET 8.0.33, the final .NET 8 release, and drop
  the scanner exception for the 8.0.32 Brotli advisory (GHSA-p3jp-9v83-76w9). 8.0.33
  changed no CoreLib or CoreCLR source, so the resource-bound facts reviewed
  against 8.0.32 and their model identifiers are unchanged.

### Contract API

- Rename the closed contract `SharpProof.Attributes.NotNullAttribute` to
  `NonNullAttribute`. A file that imported both `SharpProof.Attributes` and
  `System.Diagnostics.CodeAnalysis` could not write `[NotNull]` without error
  CS0104, because the two namespaces each declare a `NotNullAttribute`. Replace
  `[NotNull]` with `[NonNull]` wherever it named the SharpProof contract. The
  nullable-analysis `[NotNull]` still selects no SharpProof obligation.

### Verification

- Route `new string(char, int)` faults through surrounding handlers and model its
  maximum length, so a handled negative length is no longer treated as escaping.
- Record a captured instance field read as a read for effect contracts.
- Treat collection stores as possible aliases when either element type is open, so
  `List<T>` and `List<int>` can no longer be assumed independent.
- Stop an open generic thrown or caught type from excluding a `catch` handler, so a
  handler that runs for some instantiation is no longer treated as unreachable.
- Match `catch` handlers by runtime type, so tuple element names no longer make
  `E<(int a, int b)>` and `E<(int, int)>` look unrelated.
- State the assumption that the ambient culture has no user-defined callbacks when
  the CLR raises an index or division fault.
- Match receiver writes by source site, so an allowed receiver write inside a loop no
  longer becomes a false refutation.
- Check `Requires` at modeled calls in the strict worker path, as `SEMANTICS.md` promised.
  Each clause of a call is a `CallPrecondition` claim of the caller, and a body with no
  contract of its own is selected automatically when it makes such a call. This can fail
  existing builds: a method that forwards its input to a `Requires` without restating it is
  now refuted with SP0051 under every verifier policy, and a call that cannot be proven
  fails `require-proven`. Calls in bodies that do not lower exactly, and calls to virtual,
  interface or other-assembly callees, stay unchecked; the build summary counts the ones the
  compiler can see. Protocol 46, manifest 7, cache 69 and compiler artifact 57.
- Keep those call claims from failing a build on size or timing alone. Bodies selected only
  for their calls are left out once the compiler artifact reaches 24 MiB, instead of
  raising SP0049, and a call claim that runs out of time does not fail `require-proven`.
  Both cases count as unchecked calls. Declared claims are unaffected.

### Resource bounds

- Stop reporting false `Proven` resource bounds. A local written anywhere in an
  enclosing loop body no longer keeps its pre-loop value, and a loop bound, loop
  start or array dimension can no longer allocate without being charged. Both
  shapes, and methods in a type with a static initializer, are now Unknown.
- Add a nightly runtime oracle for resource bounds. It generates methods, then
  fails if a compiled method's loop entries, arrays, elements or allocated bytes
  exceed the bound proved by SP0060 or the worker. Its run result has its own
  schema, and the campaign summary moves to schema 6.

### Developer experience

- Add a workspace template with pinned local tools, optional editor assistance,
  feed configuration defaulting to nuget.org, and portable/container profiles; add preview-first
  `init` for existing projects and text/JSON/Markdown command reports.
- Add exact-base-SHA consumer CI comparison with report mode by default and an
  explicit enforcement mode. Baselines come from trusted successful source-bound
  runs; missing evidence requires initialization rather than a current-run fallback.
- Make same-root NUnit project references relative and add workspace export guidance
  so a complete workspace can move between host paths. Export still checks captured
  compiler inputs and supports the documented scalar postcondition subset only;
  standalone cross-drive Windows exports retain the absolute-reference fallback.
- Add optional analyzer-backed companion assistance alongside the existing
  refactoring. Preserve conservative action eligibility, ordinary editable source,
  and separate Result-type Fix All behavior.
- Separate official private-preview version/package/image/evidence publication from
  public CI validation versions. Add source/workflow identity, offline signed manifest,
  symbol retention, private read-back/access receipts, and a three-editor Windows x64
  acceptance runbook. Owner manual promotion on exact private main is the approval
  boundary; a separate reviewer rule is optional. All new release and
  interactive-editor evidence is pending.
- Rebuild the nightly fuzz runner with the solution-build allowance (600 seconds),
  instead of the worker's per-project limit (300 seconds), and retain its build streams.
  Retained/rotating run budgets, seeds, case counts, and proof budgets are unchanged.
- Separate portable first feedback, full verification, and incremental adoption instructions.
- Reconcile qualified consumer hosts with the container-only contributor workflow.
- Remove the checkout-only `templates/` (`sp-verified`/`sp-portable`) and its installer;
  the packaged `sharpproof` and `sharpproof-workspace` templates cover both the verified
  and the portable (`--verification portable`) setups.
- Add upgrade guidance, contributor navigation, structured issue forms, and guidance for preparing sanitized reproductions.
