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

## Diagnostic Reference

The generated [diagnostic example gallery](diagnostic-examples.md) contains at
least one code-plus-output example for every public analyzer diagnostic from
`SP0002` through `SP0025`.

The gallery is generated from committed example inputs and committed output
snapshots, and the test suite verifies that it stays current.

## Configuration

SharpProof uses `sharpproof_*` analyzer configuration keys. SMT-related keys
control bounded proof work, including mode, timeout, method budget, path
condition budget, and expression-node budget. Unsupported or over-budget proof
obligations remain conservative.

Use the root README for quick start instructions and the proof-query docs when
you need to inspect why a contract failed.
