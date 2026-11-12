# Changelog

Entries describe repository changes, not package publication. Confirm available versions on the configured destination feed.

## Unreleased

### Packaging

- Drop the separate logging assembly: `SharpProof.Logging.dll` is now part of
  the worker and is no longer in the verifier package. Nothing loads it by path
  outside the worker.
- Merge `SharpProof.Analyzer.Core.dll` into `SharpProof.Analyzer.dll`: the
  package's analyzer folder now ships one analyzer assembly instead of two.
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
- Move to SDK 10.0.404 (.NET 10.0.15), the 9.0.321 minimum-SDK fixture and the
  final .NET 8.0.34 test runtime: the November 2026 servicing fixes
  the high-severity ASP.NET Core advisory and others the image scan reports. Between
  10.0.14 and 10.0.15 CoreLib changes only floating-point formatting and the VM
  changes do not touch the audited resource-lock path or fault kinds, so the
  CoreLib pins move to the 10.0.15 module; 8.0.34 changes no CoreLib source.
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

- Prove effect claims in assemblies with more than 4,096 symbols. The check that
  entry initialization has no effects walked at most 4,096 symbols looking for a
  `[ModuleInitializer]` and treated a longer walk as finding one. So every purity,
  allocation, capability and effect-contract claim in a larger assembly stayed
  Unknown, and so did exception claims, whose module check shared the bound. The
  search now visits every method member, accessors and operators included, once
  per compilation, with no symbol bound. Two NodaTime
  corpus methods, `JulianYearMonthDayCalculator.IsLeapYear` and
  `RegularYearMonthDayCalculator.GetMonthsInYear`, are now Proven. A third,
  `ValueCursor.Match`, is now Refuted: it advances the cursor.
- Name the entry initialization that leaves an effect claim Unknown. When the
  analyzer finds no site that could violate a purity, allocation, capability or
  effect-contract claim, but the worker cannot prove it because the type or module
  initializer is not known to be effect-free, SP0047 now reports
  `Advisory:EntryInitialization:TypeInitializer`, `ModuleInitializer` or
  `CalleeTypeInitializer`. Previously the claim was Unknown with no diagnostic. The
  corpus no longer has silent Unknowns: five trivially pure methods from MoreLINQ,
  NodaTime and Humanizer, in types with constructed static fields, now carry this
  reason. Type initializers are still treated as entry effects, so these methods
  remain Unknown.
- Check the `Contract.Requires` of project references built in the same build. A
  verified project now writes a contract export per target framework
  (`obj/.../SharpProof/contracts-export.json`) with the clauses of its public and
  internal callables that read only parameters, as portable IR. An MSBuild step
  inside each compilation stamps it with the produced reference and implementation
  images, or removes it when the compilation did not complete a capture. A
  referencing project passes each project reference's export to its compiler as an
  additional file and accepts it only when the stamp names the exact reference image
  it compiled against. A call that is not dispatched then checks the exported
  clauses; the claim names the exporting project (`ExportedBy`) and cites the
  clause's file and line there. A stale, forged or incomplete export, and a clause
  that reads a field or `this`, leave the call unchecked and counted; nothing is
  assumed after the call. Packages, separately built assemblies and other target
  frameworks have no export. This can fail existing builds: a call that violates a
  referenced project's `Requires` is now refuted. Protocol 49, manifest 9, cache 72
  and compiler artifact 60.
- Check preconditions at calls into other assemblies. A call that is not dispatched
  and not generic now checks the callee's closed parameter attributes (`[NonNull]`,
  `[Positive]`, `[InRange]`), read from the referenced image whose hash the compiler
  artifact records, so reference assemblies work too. It also checks the `Requires`
  of a `[ContractFor]` companion the calling project declares for the callee; those
  claims are marked as declared by the calling project. A metadata clause has no
  source span, so its claim names the assembly, member, parameter and attribute.
  Calls that cannot be checked stay in the unchecked count, and nothing is assumed
  after the call. A library's `Contract.Requires` is compiled out of its binary and
  remains unchecked. This can fail existing builds: a caller that passes a value the
  library's attributes reject is now refuted. A metadata body the verifier cannot
  expand is now an opaque call instead of making its caller's body abstract, and a
  library method that a companion targets but does not cover is no longer selected
  (it produced an invalid manifest). Protocol 47, manifest 8, cache 70 and compiler
  artifact 59.
- Remove the Legacy IR execution mode. Every IR factory now uses the Total
  semantics the verifier already required: 32-bit default integers, defined
  division and access results with faults as separate guarded terms. Compiler
  artifacts drop the graph `semantics` field (artifact schema 58).
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

- Report measured solver work (Z3 resource units) and time per verified callable in
  the worker result's `summary.costs`, absent on cache hits (protocol 48, cache 71).
  A new `perf-baseline` gate, run nightly, verifies the pinned corpus on one solver
  lane, writes `artifacts/perf/baseline.json` and a step summary, and compares total
  solver work with an advisory reference in `eng/acceptance/perf-baseline.json`.
- Add an advisory nightly `perf-self` step that verifies SharpProof's own `src/`
  projects and summarizes per-project solver work, time and the costliest callables
  in `artifacts/perf/self-summary.md`. It never fails on outcomes or cost.

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
