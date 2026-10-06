# SharpProof

SharpProof checks C# contracts and effects. Its portable analyzer reports usage problems and conservative effect information; its optional build verifier proves selected claims with a bounded native Z3 worker.

This is a preview. `Proven` is conditional on the modeled language subset and recorded assumptions. `Refuted` requires a replayable violation. `Unknown` is an accountable result, not success. See [semantics](SEMANTICS.md) and [coverage and limits](docs/coverage-and-limits.md).

## Packages

| Package | Purpose |
| --- | --- |
| `SharpProof.Attributes` | Application contract API and IntelliSense XML; targets netstandard2.0 |
| `SharpProof` | Portable analyzer, companion validation hook, compiler collector, and build configuration |
| `SharpProof.Verifier` | MSBuild tasks, launcher, worker, and pinned Linux amd64 Z3 payload |

Reference Attributes from application code. Keep analyzer and verifier references private. Full verification requires the [canonical container](docs/container-development.md); the portable analyzer does not load native Z3.

Active analyzer builds require .NET SDK 9.0.300 or newer (Roslyn 4.14 or newer). This is a compiler-host requirement, separate from the application's target framework. The canonical image supplies the pinned SDK.

The checked-in package version is `1.0.0-preview.1`. Use packed local artifacts to evaluate this checkout; package availability on a public feed is not implied.

## A minimal contract

```csharp
using SharpProof.Attributes;

public static class Calculator
{
    public static long Increment(long value)
    {
        Contract.Requires(value >= 0 && value < long.MaxValue);
        Contract.Ensures(Contract.Result<long>() == Contract.Old(value) + 1);
        return checked(value + 1);
    }
}
```

Contract clauses belong in a contiguous method prologue. They express static conditions and are normally removed by the C# compiler. Do not define `SHARPPROOF_CONTRACTS`: SharpProof has no runtime contract-checking mode.

See [getting started](docs/getting-started.md) for package references, profiles, and verifier policy, and [public API](docs/public-api.md) for the full contract surface.

## Learn by doing

From the repository root, with Docker Compose v2 and Linux containers:

```text
docker compose build tooling
docker compose run --rm tooling contract
docker compose run --rm tooling test -Target SharpProof.Analyzer.Test/SharpProof.Analyzer.Test.csproj -TestFilter FullyQualifiedName~DocumentationSnippetTests
docker compose run --rm tooling pr
```

`pr` runs Release build and PR semantic/package gates. Coverage, corpus, fuzzing, and security have separate commands; see the [container command reference](docs/container-development.md). Finite tasks copy source into private container workspaces and export artifacts. Run repository .NET, PowerShell, packaging, and acceptance commands there.

## Understanding results

- Read claim outcomes, reasons, assumptions, and vacuity in worker records. Diagnostic silence alone does not prove anything.
- Compare portable diagnostics with [diagnostic examples](docs/diagnostic-examples.md). Analyzer and worker results have different scopes.
- Inspect [typed Unknown reasons](docs/unknown-reasons.md) before adjusting a budget or changing a contract.
- Review trusted boundaries and API specifications as assumptions. Suppression affects reporting, not evidence.
- Loops, calls, exceptions, and heap operations have modeled cases and conservative limits; language admission does not guarantee a proof.

## Documentation

The [documentation index](docs/README.md) links the current guides, implementation references, and validation fixtures. [Contributing](CONTRIBUTING.md) describes development standards. Report vulnerabilities through the [security policy](SECURITY.md).

Releases use the three-package graph and tag-gated consumer checks described in [the release process](eng/release/README.md).
