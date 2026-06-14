# Implementation todo

Execution rule: land these in small green checkpoints, highest-signal items first.

## P0 - immediate signal and trust

- [x] 01. Add negative `PS0010` fuzz families:
  - caught internal throw
  - dead-branch throw
  - divide-by-zero excluded by guard
  - null dereference excluded by guard
- [x] 02. Add positive `PS0010` fuzz families beyond direct `throw`:
  - definite divide-by-zero
  - definite null dereference
  - `using` / `Dispose()` exception propagation
  - nested local-function and lambda throw propagation
- [x] 03. Reclassify noisy Roslyn operation kinds out of the "missing coverage" bucket where they are only lowered, parent-handled, or non-C# surfaces.
- [x] 04. Add a small overnight-run aggregation tool for fuzz artifacts:
  - merge phase summaries
  - report throughput and totals
  - report remaining unobserved operations and generator-backed gaps
- [x] 05. Add deterministic evidence-completeness assertions to fuzz output for `PS0002` and `PS0010`.

## P1 - semantic proof and durability

- [x] 06. Add test-only SMT-backed exact path tests in `PurelySharp.Test`.
- [x] 07. Promote semantic metamorphic test suites for purity and exception behavior.
  - [x] invoked local-function wrapper coverage
  - [x] invoked lambda wrapper coverage
  - [x] unused nested-callable suppression coverage
  - [x] dead-branch elimination coverage
  - [x] exact-catch suppression coverage
  - [x] temp-local wrapper parity
  - [x] conditional-expression parity
  - [x] base-catch suppression parity
  - [x] rethrow wrapper parity
  - [x] filter wrapper parity
- [x] 08. Add effect-summary end-to-end fixture tests with small compiled assemblies.
- [x] 09. Tighten effect-summary trust rules:
  - require full identity match
  - reject incomplete identity entries

## P2 - analyzer correctness targets

- [x] 10. Tighten delegate target flow across reassignment and branch merges.
- [x] 11. Add `using(existingLocal)` implicit `Dispose()` analysis.
- [x] 12. Tighten virtual / interface property getter dispatch analysis.
- [x] 13. Improve LINQ source enumeration effect analysis.
- [ ] 14. Finish the remaining fresh ownership backlog with narrower scope:
  - [x] direct and aliased fresh mutable-object return escape coverage
  - [x] direct and aliased fresh-array return escape coverage
  - [x] escaping closure capture of owned local arrays
  - [x] constructor-mediated fresh mutable-object escape coverage
  - [x] stable local immutable-wrapper constructor escape parity
  - [x] stable local init-only wrapper initializer parity
  - [x] delegate-capture fresh mutable-object escape parity
  - [ ] fresh object ownership beyond arrays
  - [x] one-step readonly-wrapper field ownership flow
  - [x] one-step getter-wrapper property ownership flow
  - [x] non-escaping direct and aliased fresh local object field-mutation consistency
  - [ ] deeper multi-step ownership parity

## P3 - lower-value or guarded work

- [ ] 15. Add more random fuzz families only where they expand semantic coverage rather than raw counts.
- [ ] 16. Revisit broad catalog whitelisting only with proof-quality evidence and conflict tests.
- [ ] 17. Keep live-analyzer Z3 integration out of the default path; if explored at all, make it a bounded opt-in experiment with strict fallbacks.
