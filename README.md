# SharpProof

SharpProof checks C# contracts and effects. Add `SharpProof` to an SDK-style project and build to run the analyzer and full verification automatically.

**This is a preview and very, very likely (read: does) have many bugs.** The way I would use this project is, if SharpProof claims there is an issue, then get your AI tool of choice to use it as a guide for a test case to prove that that specific test case fails. `Proven` is conditional on the modeled language subset and recorded assumptions. `Refuted` requires a replayable violation. `Unknown` is an accountable result, not success. See [semantics](SEMANTICS.md) and [coverage and limits](docs/coverage-and-limits.md).

## Packages

The standard setup is `dotnet add package SharpProof` using your configured feed, or this one reference with no SharpProof configuration properties:

```xml
<ItemGroup>
  <PackageReference Include="SharpProof" Version="1.0.0-preview.1" />
</ItemGroup>
```

It restores the contract API, analyzer, worker, and pinned native solver automatically and defaults to strict verification. Keep the standard reference without `PrivateAssets="all"` so application contract dependencies propagate. The internal dependency graph remains version-matched; no separate installation is needed.

| Package | Purpose |
| --- | --- |
| `SharpProof.Attributes` | Application contract API and IntelliSense XML; targets netstandard2.0 |
| `SharpProof` | Standard complete entrypoint: automatically restores the API, analyzer, build tasks, worker and pinned native solver |
| `SharpProof.Verifier` | Compatibility entrypoint and implementation payload for analysis and verification |

Full verification covers six native OS/architecture combinations: Windows, macOS, and glibc Linux, each on x64 and ARM64, plus the canonical Linux container, within the [host and filesystem support matrix](docs/preview-support.md). Windows solver payloads include the C++ runtime statically, so no separate Visual C++ redistributable installation is needed. Linux requires a compatible glibc distribution and native runtime dependencies; musl remains unsupported. Repository development commands run in the [canonical container](docs/container-development.md). An explicit advisory/off profile opts out of default strict verification.

The standard setup requires .NET SDK 10.0.100 or newer and its .NET 10 runtime, independently of the application's target framework. Explicit portable-only advisory analysis retains its separate SDK 9.0.300+/Roslyn 4.14 minimum; that older host cannot run the default verifier. The canonical image supplies the pinned SDK.

The consumer application can retain a compatible older target framework. For setup diagnostics, run `pwsh -File scripts/Invoke-SharpProof.ps1 doctor -Target path/to/Consumer.csproj -Format Json` from this checkout; it evaluates a trusted project without restore/build and identifies missing assets or configuration. A doctor pass does not execute a proof.

See [older frameworks, Windows, and Visual Studio](docs/legacy-compatibility.md) for the modern-host application-target matrix, the Windows MSBuild 18+ task-host integration, explicit advisory-only MSBuild 17.14 behavior, and deployment/interactive-IDE qualification limits.

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

Start with the [single-reference setup](docs/getting-started.md#first-use-in-three-steps) and [your first proof](docs/getting-started.md#your-first-proof). [Project templates](templates/README.md) generate the standard setup or an explicit portable-only alternative using this checkout's package version. Follow [incremental adoption](docs/adoption.md) for an existing repository and [upgrades](docs/upgrading.md) when changing versions. See [public API](docs/public-api.md) for the full contract surface.

The [configuration reference](docs/configuration.md) covers selection, IDE behavior, report paths, specification packs, budgets, and caching. Use [verification evidence](docs/verification-results.md) to interpret a run and the [language reference](docs/language-reference.md) to investigate particular C# constructs.

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

The [development workflows](docs/development-workflows.md) guide connects repository changes to the relevant container commands and CI evidence.

Releases use the three-package graph and tag-gated consumer checks described in [the release process](eng/release/README.md).
