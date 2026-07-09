# SharpProof Contracts

SharpProof's primary user surface is analyzer contracts: attributes that turn a
bounded proof question into normal build diagnostics.

## Contract Attributes

- `[EnforcePure]` / `[Pure]`: require a method-like member to be proven pure.
  Violations produce `SP0002`; pure-looking members without a contract can
  produce `SP0004`.
- `[Ensures("condition")]`: require every reachable return site to prove a
  C#-like postcondition. Failures produce `SP0018`; unsupported conditions
  produce `SP0019`.
- `[ZeroAllocations]`: require no direct source-visible heap allocation sites
  in the annotated method-like body. Violations produce `SP0013`.
- `[AllowedCapabilities(...)]`: restrict proven side-effect capabilities such
  as `IO`, `Console`, `FileRead`, `FileWrite`, `Network`, `Process`,
  `Environment`, `Reflection`, and `NativeInterop`. Violations produce
  `SP0015`; unverifiable operations produce `SP0016`.
- `[ExpectedComplexity(...)]`: require the best proven method complexity to be
  at or below the declared bound. Exceeded bounds produce `SP0021`; unknown
  bounds produce `SP0022`.

## Attribute Placement Policy

SharpProof intentionally keeps several public contract attributes declared with
`AttributeTargets.All` even though the analyzer only gives them meaning on a
smaller set of declarations. This lets unsupported placements compile far
enough for SharpProof to report fixable `SP*` analyzer diagnostics, include the
misuse in SARIF and baselines, and show the same guidance across IDE and CI
hosts instead of relying on compiler `CS0592` rejection.

The broad-usage attributes and their placement diagnostics are:

| Attribute | Analyzer placement diagnostic | Notes |
| --- | --- | --- |
| `[EnforcePure]` | `SP0003` | Analyzer-validated so misplaced purity contracts can be removed by a code fix. |
| `[Pure]` | `SP0003` | Property and indexer getter contracts are accepted; unsupported targets remain analyzer diagnostics. |
| `[ZeroAllocations]` | `SP0014` | Misplaced allocation contracts stay visible as SharpProof usage errors. |
| `[AllowedCapabilities(...)]` | `SP0017` | Capability contract placement is validated before capability reasoning runs. |
| `[Ensures("condition")]` | `SP0020` | Postconditions are accepted only on method-like declarations. |
| `[ExpectedComplexity(...)]` | `SP0023` | Complexity contracts are accepted only on method-like declarations. |

Attributes whose supported target set is already stable and compiler-enforceable
remain narrowed in metadata, such as `[AllowSynchronization]`,
`[PureExternal]`, and `[Impure]`.

## Diagnostic Reference

The generated [diagnostic example gallery](diagnostic-examples.md) contains at
least one code-plus-output example for every public analyzer diagnostic from
`SP0002` through `SP0025`.

The gallery is generated from committed example inputs and committed output
snapshots, and the test suite verifies that it stays current.

Known diagnostics can be managed with
[SharpProof diagnostic baselines](baselines.md), including generation from SARIF
or current project diagnostics, match explanations, and stale-entry pruning.

SharpProof diagnostics also carry editor/tooling properties for deeper proof
inspection. Diagnostics with a source location include
`sharpproof.explain.file`, `sharpproof.explain.line`,
`sharpproof.explain.column`, and `sharpproof.explain.query`, where the query is
a ready-to-run `SharpProof.SymbolicCli explain --file ... --line ... --column ...`
command. Contract diagnostics also include
`sharpproof.explain.contract`; proof diagnostics include
`sharpproof.explain.proof_status` and, when available,
`sharpproof.explain.unknown_reason` normalized as lower snake case.

## Configuration

SharpProof uses `sharpproof_*` analyzer configuration keys. SMT-related keys
control bounded proof work, including mode, timeout, method budget, path
condition budget, and expression-node budget. Unsupported or over-budget proof
obligations remain conservative.

Use the root README for quick start instructions and the proof-query docs when
you need to inspect why a contract failed.
