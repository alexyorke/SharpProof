# SharpProof Direction

SharpProof is an analyzer-first symbolic proof platform for C#.

The primary workflow is:

```text
Write contracts -> build gets diagnostics -> inspect proof/evidence -> query deeper with CLI/API
```

The default user experience should be normal Roslyn analyzer diagnostics from
attributes such as `[EnforcePure]`, `[Ensures]`, `[ZeroAllocations]`,
`[AllowedCapabilities]`, and `[ExpectedComplexity]`. The symbolic CLI and .NET
API exist to explain those results and to answer deeper point-in-code questions
about invariants, reachability, runtime hazards, capabilities, and complexity.

## Architecture Spine

SharpProof should keep moving toward one bounded proof pipeline:

```text
Roslyn/C# -> Symbolic IR -> normalized state -> proof service -> bounded Z3 -> analyzer/API/CLI output
```

The analyzer should consume symbolic services rather than owning separate proof
logic. `SearchLib` should remain the solver backend. Public surfaces should
prefer source-like facts, proof statuses, and unknown reasons instead of raw SMT
terms.

## Near-Term Roadmap

- Make the README and docs contract-first: what to annotate, what diagnostics
  appear, and how to inspect proof evidence.
- Keep generated examples as the evidence surface for every public diagnostic
  and every major symbolic query mode.
- Add focused explanation flows that connect build diagnostics to CLI/API proof
  queries.
- Continue reducing runtime-hazard formula fallbacks by migrating them to IR
  exception-precondition facts.
- Consolidate proof-status, unknown-reason, and fallback wording across
  analyzer diagnostics, CLI output, and public result DTOs.
- Split large symbolic/analyzer files only when the split removes duplicated
  proof behavior or inconsistent fallback handling.

## Non-Goals For The Current Preview

- SharpProof is not a whole-program execution engine.
- It does not claim a precise percent of .NET SDK coverage.
- Unsupported, timed-out, canceled, native-load-failed, or over-budget proof
  obligations must remain conservative.
- A full Rust-style borrow checker remains future work.
