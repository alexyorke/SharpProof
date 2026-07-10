# SharpProof - Symbolic C# Contracts Backed By Bounded Proof

SharpProof is a beta Roslyn analyzer for enforceable C# contracts. You add
attributes such as `[EnforcePure]`, `[Requires]`, `[Ensures]`,
`[ZeroAllocations]`, `[AllowedCapabilities]`, `[DoesNotThrow]`,
`[AllowedExceptions]`, or `[ExpectedComplexity]`; the analyzer reports build
diagnostics; the CLI and .NET API let you inspect the bounded proof evidence.

## Preview Status

> [!WARNING]
> SharpProof is still preview software. Treat the current branch and packages
> as alpha/beta quality rather than production-hardened tooling.
>
> The project has also been developed through rapid AI-assisted iteration, or
> "vibe-coded" development in the informal sense: broad feature growth, fast
> refactoring, and heavy test coverage, but not the kind of long-lived
> stabilization and compatibility discipline you would expect from a mature
> analysis platform.
>
> Expect rough edges:
> - analyzer false positives and false negatives
> - unsupported C# or library shapes that stay conservative or unknown
> - public API, CLI, configuration, and diagnostic-surface changes between preview releases
>
> The analyzer does not execute user code and does not attempt unbounded
> whole-program proof. When it cannot prove a fact within the implemented rules
> and budgets, it stays conservative.

## What SharpProof Does

SharpProof is more than just a purity checker. Its intended developer workflow
is:

```text
Write contracts -> build gets diagnostics -> inspect proof/evidence -> query deeper with CLI/API
```

The analyzer answers contract questions during normal builds:

- can this method be proven pure?
- do calls satisfy declared `[Requires("...")]` preconditions?
- which direct allocation sites violate `[ZeroAllocations]`?
- which capability categories does this method use?
- does every return satisfy `[Ensures("...")]`?
- can this method only throw its declared exception set?
- is the method within the declared `[ExpectedComplexity(...)]` bound?

The CLI and library API answer proof-inspection questions:

- what facts hold at this line?
- is this branch reachable?
- can this operation provably throw at runtime?
- what asymptotic complexity can be justified conservatively?

Under the hood the intended spine is:

```text
Roslyn/C# -> Symbolic IR -> normalized symbolic state -> proof service -> Z3-backed conclusions -> analyzer/API/CLI outputs
```

## Who It Is For

SharpProof is for .NET developers who want static guarantees or conservative
evidence around behavior without running the code:

- library authors enforcing purity or low-allocation contracts
- teams auditing runtime hazards and side effects during builds
- engineers exploring invariants and proof results from a CLI or .NET API
- contributors expanding symbolic reasoning over C# and the .NET SDK

Use something else if you need whole-program execution prediction, exact
performance profiling, or a full borrow checker today.

## Quick Start

The intended public packages are `SharpProof` and `SharpProof.Attributes`, both
at `0.1.0-preview.1`, but they are not published to NuGet.org yet.

For local preview use, build a local feed from this repo and install from it:

```powershell
.\build-nuget.ps1 -Configuration Release
dotnet add package SharpProof --version 0.1.0-preview.1 --source .\artifacts\nuget
```

The main analyzer package already includes the attributes assembly for normal
consumers. Add `SharpProof.Attributes` separately only when you want the
attributes without the analyzer package:

```powershell
dotnet add package SharpProof.Attributes --version 0.1.0-preview.1 --source .\artifacts\nuget
```

Minimal source example:

```csharp
using System;
using SharpProof.Attributes;

public sealed class Calculator
{
    [EnforcePure]
    public int Add(int left, int right) => left + right;

    [EnforcePure]
    public int ReadClock() => DateTime.Now.Second; // SP0002
}
```

## Selected Examples

These curated blocks are generated from committed example inputs and committed
output snapshots. Each example is backed by a regression test so the README can
fail fast when the public behavior or documentation drifts.

<!-- README_EXAMPLES -->

For the full generated galleries:

- [Diagnostic example gallery](docs/diagnostic-examples.md)
- [Symbolic query examples](docs/symbolic-query-examples.md)

## How To Inspect Proof Results

Use analyzer diagnostics for build enforcement, then use the symbolic CLI when
you need the reason behind a result:

```powershell
dotnet run --project .\Tools\SharpProof.SymbolicCli\SharpProof.SymbolicCli.csproj -- explain --file Example.cs --line 42
```

The `explain` mode summarizes nearby invariants, reachability, runtime hazards,
capabilities, and complexity for the selected line or position. Lower-level
query modes such as `--runtime-hazards`, `--capabilities`, `--complexity`,
`--check-reachability`, and `--implies` remain available for focused output and
JSON automation.

For applications and tooling that need direct queries, install the supported
library package:

```powershell
dotnet add package SharpProof.Symbolic --version 0.1.0-preview.1
```

```csharp
using SharpProof.Symbolic;

var result = new SymbolicQueryService().Query(
    new SymbolicQueryRequest(
        SymbolicSourceInput.FromText(sourceText, "Example.cs"),
        SymbolicQueryTarget.Point(line: 42)));
```

The package includes XML documentation, nullable API annotations, portable
Source Link symbols, and an executable sample under
`samples/SharpProof.Symbolic`.

## What It Can Prove Today

- Analyzer contracts:
  `[EnforcePure]`, `[Pure]`, `[ZeroAllocations]`,
  `[AllowedCapabilities(...)]`, `[Requires(...)]`, `[Ensures(...)]`,
  `[DoesNotThrow]`, `[AllowedExceptions(...)]`, `[ExpectedComplexity(...)]`,
  and related diagnostics from `SP0002` through `SP0033`.
- Symbolic queries:
  line/position invariants, implication checks, reachability checks, runtime
  hazards, capability summaries, and conservative complexity queries.
- Runtime hazards:
  direct throws, divide-by-zero, null dereference, nullable value access,
  index/range issues, checked overflow, negative lengths, and other bounded
  source-visible hazards when the current evidence supports them.
- Summary-backed metadata reasoning:
  generated built-in effect summaries embedded during build/test plus optional
  external `*.SharpProof.EffectSummary.json` additional files.
- Conservative fallback behavior:
  unsupported library shapes, unknown external calls, unsupported regex or
  pattern shapes, and budget/time-limit cases stay unknown or unproven rather
  than being upgraded optimistically.

## Deeper Docs

- [Contracts and analyzer diagnostics](docs/contracts.md)
- [Complete analyzer configuration reference](docs/configuration-reference.md)
- [Migration, audit, CI, and strict configuration profiles](docs/configuration-profiles.md)
- [Proof query CLI and API workflow](docs/proof-queries.md)
- [Project-aware MSBuild proof queries](docs/project-aware-queries.md)
- [Configurable bounded-analysis limits and truncation evidence](docs/analysis-limits.md)
- [SMT solver lifecycle, recovery, and health](docs/smt-lifecycle.md)
- [Solver witnesses and conservative input domains](docs/input-witnesses.md)
- [Stable unknown-reason taxonomy](docs/unknown-reasons.md)
- [Shared nullable-flow facts and CodeAnalysis contracts](docs/nullable-flow-facts.md)
- [Proof/evidence schema and compatibility policy](docs/evidence-schema.md)
- [Coverage, limits, and conservative fallback](docs/coverage-and-limits.md)
- [Modern C# language-surface tracking matrix](docs/modern-csharp-surface.md)
- [Diagnostic example gallery](docs/diagnostic-examples.md)
- [Symbolic query examples](docs/symbolic-query-examples.md)
- [Symbolic invariants and runtime-hazard query behavior](docs/symbolic-invariants.md)
- [Capability analysis and `[AllowedCapabilities]`](docs/capability-analysis.md)
- [Complexity query behavior](docs/complexity-queries.md)
- [Effect summaries and generated metadata behavior](docs/effect-summary.md)

## Current Limits

- SharpProof is bounded and conservative, not a whole-program execution engine.
- There is no meaningful "percent of the .NET SDK covered" claim yet; coverage
  is member-level and evidence-backed.
- Regex support is partial.
- Ownership and mutation reasoning is useful but still local; there is no full
  Rust-style borrow checker.
- Deep dispatch, hidden runtime behavior, reflection-heavy flows, dynamic
  behavior, and unsupported Roslyn shapes can remain conservative.

## Development And Validation

Use the repo wrappers for local validation so long-running .NET work runs under
the expected Windows Job Object:

```powershell
.\scripts\Invoke-SharpProofDotnet.ps1 build SharpProof.sln --configuration Release
.\scripts\Invoke-SharpProofTests.ps1 -Configuration Release -NoBuild -TestLane All
```

The impacted-test wrapper can accelerate local loops, but full CI remains the
truth source before merge:

```powershell
.\scripts\Invoke-SharpProofImpactedTests.ps1 -NoBuild -ListOnly -Explain
.\scripts\Invoke-SharpProofImpactedTests.ps1 -NoBuild
```

## Help And Feedback

- Open a bug report or feature request in the
  [GitHub issue tracker](https://github.com/alexyorke/SharpProof/issues).
- Use pull requests for fixes, test additions, and analyzer/symbolic
  improvements.
- Treat the current README as a landing page; the linked docs are the better
  place for detailed behavior and edge-case reference.
