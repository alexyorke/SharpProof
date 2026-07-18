# Comprehensive Refactoring Baseline

Captured on 2026-07-17 from commit `499bf68e` before the project-ownership
refactor.

## Source and architecture

- Handwritten production source: 99,666 lines across 431 files.
- Largest modules: Symbolic 41,257 lines; Analyzer 33,837; EffectSummary 8,786;
  ProofCore 6,766; Symbolic CLI 2,981.
- Loose shared source: 1,160 lines across 10 files.
- Cross-project source includes: 46, including 13 production includes and 33
  test/CLI includes.
- Public API baseline: `SharpProof.Symbolic/PublicAPI.Shipped.txt` and
  `PublicAPI.Unshipped.txt`.

## Validation baseline

- Release solution build: zero warnings and zero errors.
- Established six test lanes: 6,181 passing tests and two documented skips.
- Tooling lane: 606 passing tests.
- External behavior fixtures: generated README examples, Symbolic CLI compact,
  full, explain, Markdown and SARIF snapshots, evidence schema tests, analyzer
  diagnostic tests, seeded fuzz output, EffectSummary golden output, package
  contents, NuGet consumers and VSIX packaging.

The refactor preserves these external behaviors. Public .NET API changes are
allowed and must update the checked-in API snapshots and package consumers.
