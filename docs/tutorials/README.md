# Learn SharpProof

Start with a caller obligation, then progress to native proofs, branching, loops, state, and reviewed API boundaries. Each lesson includes a complete C# example and captured output from the current implementation.

## Learning path

| Lesson | You will learn |
| --- | --- |
| [1. Your first contract](01-first-contract.md) | Install the packages, declare preconditions, and use closed attributes |
| [2. Catch mistakes while compiling](02-diagnostics.md) | Trigger real diagnostics and configure their severity |
| [3. Prove four effect contracts](03-effects.md) | Enable the worker and distinguish analyzer feedback from proof |
| [4. Verify decision logic](04-branching.md) | Write postconditions for branches, multiple returns, and entry values |
| [5. Verify loops and mutable state](05-loops-and-state.md) | Use supported loop, array, and field reasoning |
| [6. Read failures and Unknown](06-outcomes.md) | Interpret structured evidence and a build that correctly fails |
| [7. Describe an interface contract](07-companion.md) | Bind a companion by compiler symbols |
| [8. Review a trusted boundary](08-trusted-boundary.md) | Declare external effects and understand the assumption policy |

Read [getting started](../getting-started.md) first if you need the package/version overview. [Public API](../public-api.md), [diagnostics](../diagnostic-examples.md), and [semantics](../../SEMANTICS.md) are the detailed references.

## Run the entire series

From the repository root, with Docker Compose v2 and Linux containers:

```text
docker compose build tooling
docker compose run --rm tooling dev -lc 'pwsh -NoLogo -NoProfile -File docs/tutorials/Run-Tutorials.ps1'
```

The runner builds a local three-package feed and creates separate net8.0 consumer projects. It tests portable examples, enables the native worker for proof examples, and checks the intentional failure in lesson 6. It never executes the native function declared in lesson 8.

Builds happen in private temporary directories. The runner snapshots HEAD plus tracked working-tree changes and reads tutorial source files directly. Commit other untracked production source before running; it is not included in that snapshot. This demonstration feed is for local learning, not exact-commit release qualification.

A successful runner includes the expected failing consumer build. Read each lesson's build exit and claim records, rather than interpreting the runner's final success as proof that every example is correct.

## Inspect the evidence

Each run creates `artifacts/tutorials/<run-id>/` containing:

- `transcript.txt`: the runner's stable claim projection, copied verbatim into these lessons.
- `*-build.log` and `*-restore.log`: complete captured .NET output.
- `*-result.json`: native manifest, callable coverage, claim results, assumptions, and summary.
- `*-result.sarif`: the corresponding SARIF.
- `pack-*.log` and `source-commit.txt`: package build logs and snapshot base commit.

Transcript claim columns are: callable identity, claim kind/effect kind, outcome, reason, effect certainty, and vacuity. `Unspecified` in an effect-kind or effect-certainty column on a postcondition is not an Unknown reason; those effect fields do not apply to that claim.

Output excerpts omit restore noise, timing, and random temporary paths unless a diagnostic needs its location. Messages and projected result lines are captured rather than invented. Exact temporary paths, timings, solver choices, and resource counts can differ on another run.

## Results to expect

The captured run checked 14 native claims: 11 Proven from owned source bodies, one Proven from a declared trusted boundary, one replayed Refuted postcondition, and one Unknown volatile-read claim. Portable lessons also emitted the expected precondition/allocation/incomplete-analysis diagnostics. Lesson 6 is the only consumer that intentionally fails its build.

The full series runner exits successfully only when those expected build results and proof outcomes match. Captured output is under each lesson; the command above reproduces the evidence.

## Use the examples in your application

Each lesson's C# fence is a complete compilation unit. Its identical runnable source is under [code](code/). Begin with the Attributes API and portable analyzer. Add Verifier in canonical-container CI when you want proof gates. Keep tool packages private and keep runtime checks where your application's input/security boundary needs them.

Contract statements are static declarations, normally elided by the C# compiler. They are not runtime validation. Do not define `SHARPPROOF_CONTRACTS`.

These examples demonstrate actual supported cases. They do not imply universal support for all loops, metadata calls, object graphs, or C# constructs. The [coverage guide](../coverage-and-limits.md) explains those boundaries.
