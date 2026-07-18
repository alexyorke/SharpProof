# SharpProof Production Reduction Progress

This is the source of truth for the 20,000-line maintained-production reduction.
Tests are excluded from the metric and must not be deleted.

## Constraints

- Preserve diagnostics, attributes, CLI behavior, serialization, packages, and
  conservative `Unknown` semantics.
- Count tracked handwritten production C#, PowerShell scripts, generators, and
  maintained production specifications.
- Do not obtain reductions through minification, code in strings, hidden
  generated complexity, or moving maintained logic outside the metric.
- Delete superseded implementations in the same green tranche.

## Baseline

- Commit: `e6c324df`.
- Maintained production: 107,626 lines, including the reduction enforcement
  script introduced with this baseline.
- Required reduction: 20,000 lines.
- Completion ceiling: 87,626 lines.
- Baseline tests: 6,147 passing with two documented skips.

## Completed

- [x] Added a tracked maintained-production baseline covering handwritten C#,
  PowerShell build/generator scripts, and explicit production specifications.
- [x] Added a reporting command with milestone and final-target enforcement.
- [x] Added an architecture test that validates the 20,000-line target,
  baseline arithmetic, and non-test production categories.

## Current evidence

- Maintained production: 107,626 lines; reduction: 0; remaining: 20,000.
- Release solution build: zero warnings and errors.
- Six lanes: 6,148 passing tests and two documented skips.

## Milestones

- [ ] 3,000 lines removed: inferred-summary foundation and first migrated rules.
- [ ] 7,000 lines removed: manual semantic catalogs substantially migrated.
- [ ] 12,000 lines removed: canonical symbolic traversal owns all transfer paths.
- [ ] 16,000 lines removed: query, analyzer policy, and EffectSummary duplication removed.
- [ ] 20,000 lines removed: CLI/ProofCore cleanup and final dead-code deletion complete.

## Current tranche

Introduce the inferred method-summary value model, cache identity, conservative
failure semantics, and differential seams without changing analyzer verdicts.
