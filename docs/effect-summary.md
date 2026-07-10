# Effect summary tooling

SharpProof's `Tools/SharpProof.EffectSummary` tool is the first
step toward evidence-based BCL and framework purity summaries.

The analyzer now regenerates its built-in effect-summary JSON into `obj` during
build/test from `SharpProof.Analyzer/BuiltInEffectSummaryArtifactSpec.json`
and consumes those summaries only as embedded resources from the current build.
Checked-in effect-summary JSON artifacts and the reviewed artifact spec are
gone. Treat ad hoc outputs from this tool as disposable local calibration data.

The MSBuild target is incremental: its artifact-spec copy, generated summary
resources, generation stamp, and tool binary participate in the target's input
and output checks. For a fast inner loop that intentionally skips regeneration,
pass `-p:SharpProofSkipGeneratedEffectSummaries=true`; the analyzer still builds,
but no generated built-in summaries are embedded. If the generator fails, the
target reports the artifact-spec and tool paths in the build error.

The goal is to reduce hand-maintained heuristics by summarizing implementation assemblies and then feeding stable effect facts back into the analyzer/catalog pipeline.
The current first landing is report-first: the tool can now emit fixed-point,
implementation-derived purity classifications without changing live `SP0002`
behavior.

## Why assembly summaries first

Building all of `dotnet/runtime` or the unified .NET source tree is useful for source navigation and calibration, but it is not required for the first durable proof layer. Installed implementation assemblies already contain the IL that user code will execute for a given runtime version.

The summary tool can inspect those assemblies directly and emit JSON facts such as:

- method calls
- virtual calls
- object and array allocation
- static and instance field reads
- static and instance field writes
- indirect memory writes through spans, pointers, refs, arrays, or block operations
- throws
- direct thrown exception types for simple IL patterns such as `new SomeException(...); throw`, `new SomeException(...); stloc; ldloc; throw`, and helper-returned exception objects that stay tracked through locals and stack values
- P/Invoke, native, internal-call, abstract, and no-IL-body roots

Those facts are intentionally lower-level than `pure` or `impure`. Final purity decisions are now generated in a report-only fixed-point classifier layered on top of the emitted evidence, and later analyzer consumption should continue to use the same evidence-first model.

Each method also emits `RootCandidates`, which are explicit review categories derived from low-level effects:

- `pinvoke`
- `runtime_native_or_internal`
- `metadata_only_or_external`
- `global_state_read`
- `global_state_write`
- `object_state_write`
- `caller_visible_memory_write`
- `dynamic_dispatch`
- `throw`
- `unsafe_or_block_memory_write`

These are not final purity verdicts. They are evidence labels used to seed later policy-aware fixed-point classification.

The report-only classifier currently emits one of:

- `pure`
- `impure`
- `conservative_unknown`

It stays conservative whenever evidence is incomplete, unresolved, or identity
validation would be required before trusting the result in the live analyzer.

## Root seed policy

Some roots cannot be proven from managed IL alone:

- P/Invoke and OS calls
- native runtime implementation calls
- JIT intrinsics
- reflection and dynamic dispatch
- environment, current culture, time, randomness, process, and filesystem state
- synchronization, threading, volatile, and interlocked state

These should become explicit root seeds with categories and evidence, not broad guesses hidden in analyzer code. The analyzer has a separate low-confidence BCL fallback diagnostic path for otherwise unknown metadata members, but that path is non-authoritative and does not replace generated summary evidence. The same fallback heuristic can be emitted as a local `BclFallbackInventory` report for auditing how much of a runtime would currently fall to `probably_pure`, `probably_impure`, or `unknown` if stronger evidence were absent.

Examples:

- current-culture formatting is environment-dependent unless an invariant or explicitly analyzable provider is supplied
- file writes are impure because the call chain reaches OS/filesystem mutation roots
- span write helpers are impure because their IL writes through caller-provided memory
- reflection remains conservative unless the target is statically resolved and bounded

## Current CLI

For ad hoc local refreshes, run the tool through the repo's job-object .NET launcher:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-SharpProofDotnet.ps1 run --project Tools\SharpProof.EffectSummary\SharpProof.EffectSummary.csproj -- --framework net8.0 --symbol-prefix System.String.Format --include-callees --max-depth 2 --transitive-roots --output artifacts\effect-summary\string-format.SharpProof.EffectSummary.json
```

Write ad hoc `*.SharpProof.EffectSummary.json` files under ignored `artifacts\effect-summary\...` paths unless a test explicitly needs a temp path. Those outputs are for local inspection only; prefer symbol filters and limits for exploratory runs.

Smoke run against the latest installed .NET 8 `System.Private.CoreLib.dll`:

```powershell
dotnet run --project Tools\SharpProof.EffectSummary -- --framework net8.0 --limit 20
```

Inspect a specific API family such as `System.String.Format`:

```powershell
dotnet run --project Tools\SharpProof.EffectSummary -- --framework net8.0 --symbol-prefix System.String.Format --limit 20
```

Emit the low-confidence fallback inventory for a focused slice:

```powershell
dotnet run --project Tools\SharpProof.EffectSummary -- --framework net8.0 --symbol-prefix System.GC --bcl-fallback-inventory --output artifacts\effect-summary\gc-fallback-inventory.json
```

Audit all System runtime assemblies for the installed target framework. This can be large; write it under ignored `artifacts\` and prefer symbol filters while iterating:

```powershell
dotnet run --project Tools\SharpProof.EffectSummary -- --framework net8.0 --all-runtime-assemblies --bcl-fallback-inventory --output artifacts\effect-summary\net8-bcl-fallback-inventory.json
```

Include same-assembly callees from the matched symbols:

```powershell
dotnet run --project Tools\SharpProof.EffectSummary -- --framework net8.0 --symbol-prefix System.String.Format --include-callees --max-depth 1 --limit 50
```

Artifact-spec runs can record completed output files and resume after an
interrupted or failed later artifact. Keep the progress file beside other
ignored build artifacts:

```powershell
dotnet run --project Tools\SharpProof.EffectSummary -- --artifact-spec SharpProof.Analyzer\BuiltInEffectSummaryArtifactSpec.json --progress artifacts\effect-summary\generation-progress.json --resume
```

The progress record is tied to the artifact-spec SHA-256 and is deleted after
all outputs complete, so a changed spec cannot silently reuse stale progress.

Use `--max-depth -1` when a filtered slice must keep the full reachable same-assembly callee closure instead of a bounded prefix.

Propagate root candidate labels through same-assembly calls:

```powershell
dotnet run --project Tools\SharpProof.EffectSummary -- --framework net8.0 --symbol-prefix System.String.Format --include-callees --max-depth 2 --transitive-roots --limit 50
```

When transitive roots are enabled, the JSON also includes `TransitiveThrownExceptionTypes`. For example, `System.ArgumentNullException.ThrowIfNull(...)` can surface `System.ArgumentNullException` from its helper callee even when the public guard method does not directly contain the `throw` instruction. The tool also emits `TransitiveThrownExceptionEdges`, which preserve the recursive callee chain as structured records with `ExceptionType`, `SourcePath`, `CalleeExactSymbolKey`, and `Depth`.

Exception-edge expansion is bounded per method by `--max-exception-edges` (default
`4096`). This cap applies even when `--max-depth -1` is used, preventing a
high-fan-out or cyclic exception graph from exhausting memory. If the cap is
reached, that method emits `TransitiveThrownExceptionEdgesTruncated: true`; the
edge and transitive exception arrays are then a bounded prefix rather than a
complete closure. Artifact specs can set the same limit with
`MaxExceptionEdges` in `Defaults` or an individual artifact.

Add report-only purity classification to the JSON:

```powershell
dotnet run --project Tools\SharpProof.EffectSummary -- --framework net8.0 --symbol-prefix System.String.Format --include-callees --max-depth 2 --transitive-roots --classify-purity --limit 50
```

Compare emitted methods against the current reviewed manual catalogs:

```powershell
dotnet run --project Tools\SharpProof.EffectSummary -- --framework net8.0 --symbol-prefix System.BitConverter.GetBytes --include-callees --classify-purity --compare-manual-catalogs --limit 50
```

## Analyzer consumption

Built-in analyzer summaries are regenerated into `obj` during build/test and then embedded into the analyzer assembly for that build. The loader does not probe loose files beside the analyzer, `AppContext.BaseDirectory`, or repo artifact directories for built-ins.

The analyzer can also consume generated exception summaries when the JSON is supplied explicitly as an additional file named `SharpProof.EffectSummary.json` or `*.SharpProof.EffectSummary.json`, but the repository no longer ships or tracks checked-in copies of those files.

### Analyzer-host metadata boundary

Roslyn metadata references expose assembly and method metadata, but they do not
expose the PE method-body bytes or full-image hashes needed to validate every
generated summary row. The analyzer therefore keeps the small set of
`RS1035`-annotated file reads required for identity and IL-body-hash validation
inside `EffectSummaryMetadataSupport.cs`. The paths come from Roslyn metadata
references or the trusted runtime assembly resolver; no other analyzer source
may perform `File` or `Directory` I/O. An architecture test keeps this
exception isolated while the rest of the analyzer remains compliant with
Roslyn's host restrictions.

With `sharpproof_report_exceptions = true`, `sharpproof_checked_exceptions = true`, or `sharpproof_runtime_hazard_mode = summaries/sites/all`, `SP0010` and `SP0011` use `ThrownExceptionTypes` and `TransitiveThrownExceptionTypes` for matching metadata/library method calls. Runtime-hazard mode is a clearer opt-in for ordinary-method failure checks: `sites` emits `SP0011`, `summaries` emits `SP0010`, and `all` emits both. This extends exception-flow reporting beyond current-compilation source without doing slow live decompilation inside Roslyn analyzer callbacks. The analyzer also accepts `TransitiveThrownExceptionEdges` as additive metadata and folds their `SourcePath` provenance back into the existing diagnostics model.

The lookup is exact and evidence-based: summaries are keyed by method symbol strings emitted by this tool, and catch filtering still happens at the source call site when the exception type resolves in the current compilation. That means `SP0010` can summarize what a method may let escape, while `SP0011` can still warn on the exact uncaught call or property-access site that propagates the exception.

Depth behavior is different for source and metadata on purpose:

- Same-compilation source analysis walks callees recursively until it reaches the originating throw site or a cycle, so multi-hop chains like `Render -> LoadAcceptedDocument -> RequireAcceptedDocument -> Voucher.AcceptedDocument.get -> throw` stay intact.
- Metadata/library analysis does not recurse by decompiling assemblies inside the analyzer. Instead, it trusts the transitive exception closure already recorded in `TransitiveThrownExceptionTypes`, `TransitiveThrownExceptionSourcePaths`, and the structured `TransitiveThrownExceptionEdges`.

If you want effectively "unbounded" metadata propagation, generate the summary with the full reachable closure for the slice you care about:

```powershell
dotnet run --project Tools\SharpProof.EffectSummary -- --framework net8.0 --symbol-prefix Your.Namespace.YourApi --include-callees --max-depth -1 --transitive-roots --output artifacts\effect-summary\your-api.json
```

Run against a specific assembly:

```powershell
dotnet run --project Tools\SharpProof.EffectSummary -- --assembly "C:\Program Files\dotnet\shared\Microsoft.NETCore.App\8.0.28\System.Private.CoreLib.dll" --output artifacts\effect-summary\corelib-net8.json
```

The output schema is versioned and includes the assembly module version ID so generated summaries can be tied to the exact runtime build.

When purity classification is enabled, schema version `3` adds:

- per-method `PurityClassification`
- top-level `PurityReport`
- optional manual-catalog comparison rows for emitted methods only

When BCL fallback inventory is enabled, schema version `4` also adds:

- top-level `BclFallbackInventory`
- per-entry guess, confidence, reason, category, assembly, display symbol, and exact symbol key
- aggregate counts for `probably_pure`, `probably_impure`, and `unknown`

Summary files are also self-validating enough to cache and share:

- each assembly report includes `AssemblySha256`
- each IL-backed method includes `MethodBodySha256`
- each method includes a stable `CacheKey` made from module version ID, metadata token, and IL hash

Consumers can use those fields to reject summaries generated from a different runtime, SDK, package build, or project assembly. For source or project-specific libraries, regenerate the summary after rebuilds and compare the cache key before trusting a row.

## Remaining work tracking

The root `README.md` is intentionally the landing page. Use this document for
the detailed effect-summary behavior and workflow.

This document stays descriptive: how the tool works, what evidence it emits,
and how the analyzer consumes that evidence. It should not carry a parallel
todo or status list.
