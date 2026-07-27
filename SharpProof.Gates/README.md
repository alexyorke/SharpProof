# SharpProof release gates

`SharpProof.Gates` is a deterministic console gate:

```powershell
.\scripts\Invoke-SharpProofDotnet.ps1 run --project SharpProof.Gates\SharpProof.Gates.csproj -- corpus
.\scripts\Invoke-SharpProofDotnet.ps1 run --project SharpProof.Gates\SharpProof.Gates.csproj -- corpus-update
.\scripts\Invoke-SharpProofDotnet.ps1 run --project SharpProof.Gates\SharpProof.Gates.csproj -- performance
.\scripts\Invoke-SharpProofDotnet.ps1 run --project SharpProof.Gates\SharpProof.Gates.csproj -- all
```

## Analyzer corpus

The checked-in snapshot covers 228 base methods:

- 200 distinct real-world methods from the MIT-licensed
  `aalhour/C-Sharp-Algorithms` repository, pinned to a full commit and spread
  across 87 upstream source files; and
- 28 focused synthetic semantic seeds: 18 effect-contract cases and 10
  compiler-bound `Requires` call-site cases.

The 200-method release floor applies only to the open-source methods. Their
exact source, file/method hashes, path and line provenance, commit, and license
are checked in under `Corpus/`; generated transformations cannot satisfy that
floor. The runner adds `EnforcePure` to each selected declaration without
rewriting its body or dependencies and analyzes the pinned upstream project as
one compilation.

Every synthetic seed is rendered in ten source forms, for 280 independently
compiled metamorphic cases:

1. baseline;
2. method, class, parameter, and helper rename;
3. escaped C# identifiers;
4. comment and whitespace trivia;
5. redundant parentheses;
6. a local temporary;
7. an `if (true)` wrapper;
8. a named argument replacing a positional argument;
9. alpha-renamed contract formals;
10. reordered independent statements.

Together these produce 480 recorded cases. The runner compares real
`SharpProofAnalyzer` output with
`Corpus/expected.canonical.snapshot`. Each entry records the analyzer's
internal semantic outcome independently of diagnostics, so diagnostic silence
can never be interpreted as proof. Canonical diagnostics include ID, effective
severity, normalized source location, and the invariant-culture message.
Diagnostics and cases are sorted deterministically. The gate also replays every
synthetic baseline against the same Roslyn compilation (the cache path) and
analyzes one synthetic case per variant concurrently. See
`Corpus/README.md` for licensing, instrumentation, and the reproducible import
workflow.

Any diagnostic mismatch fails except an expected `Proven` result becoming
`Unknown` when the exact case is listed in `Corpus/proven-to-unknown.json`
with a non-empty explanation. Unused, stale, duplicate, or unexplained
allowances fail. The checked-in allowlist is intentionally empty.

## Performance protocol

The performance gate reads all limits from
`eng/acceptance/contract.json`. It refuses to run if the release protocol
is not exactly five warmups, 30 samples, and 200 IDE edits.

The package smoke gate separately proves that `SharpProofProfile=off`
contributes no analyzer items and that advisory/strict profiles contribute both
the analyzer and contract generator. The off-profile performance path can
therefore compare two equivalent analyzer-free Roslyn compilations. Samples
are interleaved and each contains 50 compilation/diagnostic runs to suppress
timer quantization and scheduler noise. Managed retained memory holds 40
distinct compilation graphs live after a full collection. Relative and
absolute retained-memory limits are enforced independently.

An independent enabled-analyzer retention probe analyzes 40 distinct
effects-enabled compilations. Each compilation is created in a non-inlined
helper and only a weak reference escapes. After forced full collection, the
gate requires zero compilation graphs to remain reachable and separately
bounds the process-retained managed-memory increase.

The IDE gate applies and analyzes 200 single-token edits with effects enabled.
It enforces p95 and maximum latency. A separate 30-sample worker-core gate
measures cancel-to-exit latency, while a real launcher process test measures
the forced-termination deadline independently. The off-profile and IDE
analyzer performance paths reference neither SMT nor Z3.

Worker/package tests also exercise protocol version 5 manifest equality,
stable claim IDs, policy-controlled SP0047/SP0048 output, cache validation
against the current manifest, fatal run handling, and schema-2 final compiler
artifacts including generated contracts. Lowered obligation IR and SARIF
projection are not implemented release gates yet.
