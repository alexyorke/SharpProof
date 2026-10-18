# SharpProof

SharpProof checks C# contracts and effects. Add `SharpProof.Verifier` to an SDK-style project and build to run the portable analyzer and full verification automatically.

**This is a preview and very, very likely (read: does) have many bugs.** The way I would use this project is, if SharpProof claims there is an issue, then get your AI tool of choice to use it as a guide for a test case to prove that that specific test case fails. `Proven` is conditional on the modeled language subset and recorded assumptions. `Refuted` requires a replayable violation. `Unknown` is an accountable result, not success. See [semantics](SEMANTICS.md) and [coverage and limits](docs/coverage-and-limits.md).

## Repository checks

Run all repository lint, spelling, package compatibility, and link checks with one
Docker command:

```sh
docker compose run --rm --build lint
```

It builds the pinned toolchain and lint tools automatically, writes reports under
`artifacts/lint`, and exits nonzero if a check fails. For host Git worktrees, use
`pwsh -File scripts/Invoke-SharpProof.ps1 lint`. See [quality gates](eng/lint/README.md)
for scope and individual checks.

## Packages

The standard setup is one reference with no SharpProof configuration properties:

```xml
<ItemGroup>
  <PackageReference Include="SharpProof.Verifier" Version="1.0.0-preview.1" />
</ItemGroup>
```

It restores the contract API, analyzer, worker, and pinned native solver automatically and defaults to strict verification. Keep the standard reference without `PrivateAssets="all"` so application contract dependencies propagate. The internal dependency graph remains version-matched; no separate installation is needed.

| Package | Purpose |
| --- | --- |
| `SharpProof.Attributes` | Application contract API and IntelliSense XML; targets netstandard2.0 |
| `SharpProof` | Portable analyzer, companion validation hook, compiler collector, and build configuration |
| `SharpProof.Verifier` | Standard complete entrypoint: transitive API/analyzer plus build tasks, worker and pinned native solver |

Full verification runs on native Linux x64/ARM64, Windows x64, macOS ARM64/x64, and the canonical Linux container within the [host and filesystem support matrix](docs/preview-support.md). Linux requires a compatible glibc distribution and native runtime dependencies; Windows ARM64 and musl remain unsupported. Repository development commands run in the [canonical container](docs/container-development.md). `SharpProof` remains available as a deliberate portable-only alternative with advisory defaults and no native solver.

Active analyzer builds require .NET SDK 9.0.300 or newer (Roslyn 4.14 or newer). This is a compiler-host requirement, separate from the application's target framework. The canonical image supplies the pinned SDK.

Full verification additionally requires .NET SDK 10.0.100 or newer and its .NET 10 runtime. The consumer application can retain a compatible older target framework. For setup diagnostics, run `pwsh -File scripts/Invoke-SharpProof.ps1 doctor -Target path/to/Consumer.csproj -Format Json` from this checkout; it evaluates a trusted project without restore/build and identifies missing assets or configuration. A doctor pass does not execute a proof.

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
