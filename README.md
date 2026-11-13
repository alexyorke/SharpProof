# SharpProof

SharpProof checks C# contracts at build time with an SMT solver. Add one package to an SDK-style project, write contracts as ordinary C# calls and attributes, and every build reports which of them hold.

**This is an early preview and it has many bugs.** Treat a reported issue as a lead: turn it into a test case (your AI tool of choice can help) that demonstrates the specific failure. `Proven` is conditional on the modeled language subset and recorded assumptions. `Refuted` requires a replayable violation. `Unknown` is an accountable result, not success. See [semantics](SEMANTICS.md) and [coverage and limits](docs/coverage-and-limits.md).

**Status:** version `1.0.0-preview.2` is not on nuget.org yet. Until it is, [pack this checkout](docs/getting-started.md#obtain-this-checkouts-packages) to try it.

## What it checks

- **Postconditions:** `Contract.Ensures`, and return attributes such as `NonNull`, `Positive` and `InRange`.
- **Call preconditions:** every call must satisfy the callee's `Contract.Requires` and parameter attributes.
- **Purity and effects:** a method declared pure, or with a stated set of effects, does nothing else.
- **Allocations:** a method declared allocation-free does not allocate.
- **Exceptions:** a method throws only what its contract allows.
- **Capabilities:** code uses only the capabilities it declares, such as file or network access.
- **Resource bounds:** declared upper bounds on loop iterations and array allocation.

Each claim ends as `Proven`, `Refuted` (with a replayable violation) or `Unknown` (with a reason). Code SharpProof cannot prove is reported as incomplete, never as proven. See the [public API](docs/public-api.md) for the contract surface and [verification evidence](docs/verification-results.md) for how to read a run.

## Requirements

- **Build machine:** .NET SDK 10.0.100 or newer with its .NET 10 runtime. The application itself can target an older framework.
- **Operating system:** Windows, macOS or glibc Linux, on x64 or ARM64. musl Linux (Alpine) is not supported.
- **Windows paths:** the project must be on a fixed local NTFS volume.

The [support matrix](docs/preview-support.md) has the details.

## Quick start

```xml
<ItemGroup>
  <PackageReference Include="SharpProof" Version="1.0.0-preview.2" />
</ItemGroup>
```

That one reference is the whole setup: it restores the contract API, the analyzers, the verifier and the pinned solver. Do not add `PrivateAssets="all"`, because projects that consume yours need the contract API too.

Verification is strict by default. The build fails when a selected claim is refuted, when it cannot be proven, or when it depends on a declared assumption. To adopt gradually, start with analyzer diagnostics only and no proofs:

```xml
<PropertyGroup>
  <SharpProofProfile>advisory</SharpProofProfile>
  <SharpProofVerify>false</SharpProofVerify>
</PropertyGroup>
```

`dotnet new sharpproof` generates a starter project with either setup; see the [developer tools guide](docs/developer-experience.md#install-and-start).

## Packages

| Package | Purpose |
| --- | --- |
| `SharpProof.Attributes` | Application contract API and IntelliSense XML; targets netstandard2.0 |
| `SharpProof` | Standard complete entrypoint: automatically restores the API, analyzer, build tasks, worker and pinned native solver |
| `SharpProof.Verifier` | Compatibility entrypoint and implementation payload for analysis and verification |

The consumer application can retain a compatible older target framework. For setup diagnostics, run `dotnet sharpproof doctor --project path/to/Consumer.csproj --format json` with the optional [SharpProof.Tool](src/SharpProof.Tool/README.md); it evaluates a trusted project without restore/build and identifies missing assets or configuration. A doctor pass does not execute a proof.

See [older frameworks, Windows, and Visual Studio](docs/legacy-compatibility.md) for the modern-host application-target matrix, the Windows MSBuild 18+ task-host integration, explicit advisory-only MSBuild 17.14 behavior, and deployment/interactive-IDE qualification limits.

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

Start with the [single-reference setup](docs/getting-started.md#first-use-in-three-steps) and [your first proof](docs/getting-started.md#your-first-proof). The [`sharpproof` project templates](docs/developer-experience.md#install-and-start) generate the standard setup, or a portable-only alternative with `--verification portable`. Follow [incremental adoption](docs/adoption.md) for an existing repository and [upgrades](docs/upgrading.md) when changing versions. See [public API](docs/public-api.md) for the full contract surface.

The [configuration reference](docs/configuration.md) covers selection, IDE behavior, report paths, specification packs, budgets, and caching. Use [verification evidence](docs/verification-results.md) to interpret a run and the [language reference](docs/language-reference.md) to investigate particular C# constructs.

The [developer tools guide](docs/developer-experience.md) covers workspace templates, preview-first project initialization, setup/evidence reports in text/JSON/Markdown, exact-base CI comparison, relocatable NUnit regression export, and optional editor scaffolding.

## Understanding results

- Read claim outcomes, reasons, assumptions, and vacuity in worker records. Diagnostic silence alone does not prove anything.
- Compare portable diagnostics with [diagnostic examples](docs/diagnostic-examples.md). Analyzer and worker results have different scopes.
- Inspect [typed Unknown reasons](docs/unknown-reasons.md) before adjusting a budget or changing a contract.
- Review trusted boundaries and API specifications as assumptions. Suppression affects reporting, not evidence.
- Loops, calls, exceptions, and heap operations have modeled cases and conservative limits; language admission does not guarantee a proof.

## Documentation

The [documentation index](docs/README.md) links the current guides, implementation references, and validation fixtures. [Contributing](CONTRIBUTING.md) describes development standards. Report vulnerabilities through the [security policy](SECURITY.md).

To work on SharpProof itself, start with [development workflows](docs/development-workflows.md) and the [container command reference](docs/container-development.md).

Releases use the three-package graph and tag-gated consumer checks described in [the release process](eng/release/README.md).
