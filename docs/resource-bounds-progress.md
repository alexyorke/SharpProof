# Resource bounds: milestone one

Base: latest fetched `origin/master`, cd3363c63 (2026-10-18).
Branch: `codex/resource-bounds-milestone-one`.

## Goal and semantics

Add opt-in, built-in analyzer results for proven loop execution upper bounds and
cumulative managed array allocation bounds. Results must explicitly say Unknown
when the closed supported subset cannot justify a bound. This is static analysis,
not benchmarking. Bounds cover all execution prefixes, including exceptional
exits, under ordinary synchronous CLR execution; they are upper bounds, not exact
trip counts. Input sizes are entry values. Symbolic arithmetic uses mathematical
nonnegative integers, independently of CLR counter arithmetic.

The first milestone uses conservative compiler-bound structural proof rules in
the portable analyzer. It does not add Z3 queries or worker protocol claims. This
keeps native libraries out of Roslyn and avoids presenting new results as existing
worker verification evidence. No changes to existing contract verdicts.

Memory units are allocation events (arrays) and array elements, not bytes, peak
live heap, retained output, or stack space. Primitive one-dimensional arrays only;
unknown constructors, calls, boxing, and other implicit allocation paths must
abstain. Allocations are charged each time their site executes.

## Implementation plan and status

- [x] Fetch master and create an isolated branch without disturbing existing work.
- [x] Commit this plan before implementation.
- [x] Add a bounded symbolic cost algebra and explicit Proven/Unknown result model.
- [x] Prove canonical int counted loops (zero start, strict upper comparison,
  unit increment, stable bound clamped at zero), including independent nesting.
- [x] Compose sequential work and worst-case branches; count primitive arrays
  and their cumulative elements under loop multiplicities.
- [x] Reject unsupported control flow, mutation, calls, recursion, numeric shapes,
  implicit allocations, and analysis budget exhaustion with explicit reasons.
- [x] Expose opt-in analyzer diagnostics with machine-readable properties and
  document activation and the exact supported subset.
- [x] Test supported results, adversarial Unknown cases, analyzer integration,
  cancellation, disabled behavior, and independently counted small executions.
- [x] Run targeted tests, relevant broader checks, and review the final diff.
- [x] Record evidence, limitations, remaining work, and commit implementation.

## Verification procedure

Use the canonical Linux amd64 tooling container. Run the resource-bound test
fixture first, then the analyzer project suite and the smallest relevant
architecture/acceptance checks. Record exact commands, counts, and failures here.
Do not broaden checks after a passing gate without a specific unresolved concern.

## Remaining after milestone one

- Non-unit strides, descending/logarithmic loops, dependent nested bounds, and
  ranking-function synthesis beyond the checked canonical rule.
- Source/metadata call cost summaries and recursion/recurrence solving.
- Object/closure/string/boxing allocation costs, runtime-dependent byte layouts,
  liveness, escape/alias analysis, peak live memory, and stack bounds.
- Resource contracts, native-worker proof records, artifact/protocol/cache
  integration, and SARIF evidence beyond analyzer diagnostic properties.
- Tight asymptotic classification, amortized/average-case analysis, async and
  concurrent execution, and wall-clock performance measurements.

## Evidence and checkpoints

- Initial checkout has unrelated changes; all task edits live in the isolated
  worktree `artifacts/resource-bounds-milestone-one`.
- SP0060 is disabled by default; enable its severity through normal Roslyn
  diagnostic configuration. No new contract attributes or worker claim kinds.
- Resource model and diagnostics are classified in the TCB inventory. The
  generated diagnostic source reference and user documentation are updated.
- Initial builds exposed repository formatting/guard requirements and a banned
  semantic-diagnostics API. Production code now builds without that dependency;
  unsupported operations themselves drive abstention. Test delegate overloads
  use the pinned NUnit Action API.
- Planning document committed first as `488f4300e`. This completed checkpoint is
  committed with the implementation and tests.

## Completed verification

All commands ran in the canonical Linux amd64 container. The existing
`sharpproof-tooling:local` image passed the repository's container-contract gate.
The worktree-aware host wrapper mounts Git metadata read-only and runs each
finite task in its own private container workspace. Set
`SHARPPROOF_TOOLING_IMAGE=sharpproof-tooling:local` and
`COMPOSE_PROJECT_NAME=resource-bounds-milestone-one` to reuse this configuration.
The commands below are arguments to `eng/scripts/Invoke-SharpProof.ps1`:

| Command | Result |
| --- | --- |
| `test -Target src/SharpProof.Analyzer.Tests/SharpProof.Analyzer.Tests.csproj -TestFilter FullyQualifiedName~ResourceBoundsTests` | Initial 45 resource tests passed; final fixture adds 4 cases covered by the full suite below |
| `restore -Target src/SharpProof.Worker.Tests/SharpProof.Worker.Tests.csproj` | Passed; populated the pinned Microsoft.Z3 asset required by an existing native-PE boundary test |
| `test -Target src/SharpProof.Analyzer.Tests/SharpProof.Analyzer.Tests.csproj` | 1184 passed, 0 failed, 0 skipped; 49 resource cases included; build had 0 warnings and 0 errors |
| `layout` | Passed: 2607 source files, 59 projects evaluated, 151 pinned Arcade common files |
| `docs` | Passed: source-reference drift check, 714 local links, automation and updater lifecycle regressions |
| `contract` | Passed: canonical container contract |
| `git diff --check` | Passed |

The first full-suite run found stale descriptor counts/severity metadata and a
documentation example that needed a class wrapper. Both were corrected and the
entire analyzer suite rerun successfully. The fresh NuGet volume initially lacked
the pre-existing native-PE test prerequisite; restoring the pinned worker
dependencies resolved it without changing that test or weakening its assertion.

The resource tests cover constant/symbolic loop and array bounds, independent
nesting, branches, sequential loops, early returns, mathematical cost arithmetic
beyond Int64, 121 independently executed small input pairs, unsupported mutation
and aliasing, overflow-prone loop shapes, opaque/recursive calls, implicit
allocations, cancellation, budget exhaustion, generated-code policy, compiler
recovery, profile-off/default behavior, editorconfig suppression, and analysis
without a contract API reference. Unsupported results carry no partial bounds.

Milestone one is complete for the documented closed subset. No performance,
universal C# coverage, native-worker integration, or general memory-space proof
claim is made. Subsequent work remains listed above.
