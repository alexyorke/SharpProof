# Getting started

Run `dotnet add package SharpProof` in an SDK-style C# project using your configured feed, then build. The package brings the contract API and analyzer transitively and enables strict verification automatically; no SharpProof properties or separate Attributes/analyzer references are required. `SharpProof.Verifier` remains a compatibility entrypoint.

The
[workspace template](developer-experience.md#install-and-start) supplies the matching
tool manifest and feed configuration.

Full verification requires .NET SDK 10.0.100+ with the .NET 10 runtime and a host/filesystem in the [support matrix](preview-support.md). Application target frameworks are separate from the compiler/worker host. Repository build/test/pack commands always run in the canonical Linux amd64 container.

Targeting an older application framework does not require running the verifier on an old OS or SDK. [Legacy compatibility](legacy-compatibility.md) covers SDK-style Framework 4.6.1+, Standard/Core and modern .NET targets, the Windows MSBuild 18+ task-host path, and the limits of Visual Studio and old-Windows qualification.

## First use in three steps

1. Obtain the current packages from your configured feed; this checkout does not imply public NuGet availability. See [obtaining packages](#obtain-this-checkouts-packages).
2. Add this single reference to your project, or use the [verified project template](../templates/README.md):

   ```xml
   <ItemGroup>
     <PackageReference Include="SharpProof" Version="1.0.0-preview.1" />
   </ItemGroup>
   ```

3. Save this complete compilation unit as `Counter.cs`, then run `dotnet build -c Release` in your consumer directory:

```csharp
using SharpProof.Attributes;

public static class Counter
{
    public static int Next(int value)
    {
        Contract.Requires(value >= 0 && value < int.MaxValue);
        Contract.Ensures(Contract.Result<int>() == Contract.Old(value) + 1);
        return checked(value + 1);
    }
}
```

The default is full verification with `strict`, `require-proven`, and assumption policy `error`. The build automatically selects the packaged worker and architecture-matched solver. Keep the single standard reference without `PrivateAssets="all"` so the contract API's application/runtime dependency can propagate when your project is consumed or packed.

## Your first proof

Inspect the build summary and its published evidence paths. Require a completed run, the expected selected claim, `Proven`, no incomplete callable coverage, and reviewed assumptions/vacuity. A successful build with no selected claims does not establish a proof. [Reading results](verification-results.md) explains these checks.

Change the postcondition's `+ 1` to `+ 2` and rebuild: the incorrect claim must fail strict verification. Restore the correct claim and rebuild. Contracts describe static obligations; they do not insert runtime guards. Keep normal input validation where unverified callers can supply invalid values. Do not define `SHARPPROOF_CONTRACTS`.

`Requires` states the entry domain and caller obligation. `Ensures` describes normal return; it does not prove termination. Put clauses in a contiguous method prologue. `Old` refers to entry state and `Result` to the normal return value.

## Obtain this checkout's packages

From a clean committed SharpProof checkout with Docker Compose v2 and Linux containers:

```text
docker compose build tooling
pwsh -File eng/scripts/Invoke-SharpProof.ps1 pack -Configuration Release
```

The host wrapper supplies read-only external Git metadata mounts for worktrees. Packed artifacts are exported under `artifacts/container-packages`. Configure your consumer's NuGet sources to include that local feed using a path visible inside its build environment. The standard SharpProof reference restores the exact-version `SharpProof -> SharpProof.Verifier -> SharpProof.Attributes` dependency graph automatically. [eng/Versions.props](../eng/Versions.props) owns the package version.

`pack` requires clean exact-commit source. For development validation with uncommitted changes, use `docker compose run --rm tooling samples -Configuration Release`; its harness builds an isolated feed and validates actual consumer diagnostics and proof records.

## Existing projects and advanced controls

Start with one project, review its selected obligations and results, then expand using [incremental adoption](adoption.md). The standard reference needs no activation properties. Settings in [configuration](configuration.md) are optional overrides for selection, diagnostics, policies, budgets, caching, and report paths. An unsupported host or unavailable worker is an actionable failure, not silent proof success.

Default evidence is published under `obj/<Configuration>/<TargetFramework>/SharpProof` as request, result and compiler manifest files. Finite repository task workspaces are disposable; use a persistent container workspace or retained artifacts when investigating intermediate evidence. Optional SARIF requires an explicit output setting.

## Portable-only alternative

For deliberate analysis without native proofs, opt out explicitly on the same single package:

```xml
<ItemGroup>
  <PackageReference Include="SharpProof" Version="1.0.0-preview.1" />
</ItemGroup>
<PropertyGroup>
  <SharpProofProfile>advisory</SharpProofProfile>
  <SharpProofVerify>false</SharpProofVerify>
</PropertyGroup>
```

This explicit portable-only configuration requires SDK 9.0.300+/Roslyn 4.14+ and does not run native proofs. The default configuration still requires SDK 10.0.100+ and runs full verification. `SharpProofProfile=off` disables analysis as well. The [portable template](../templates/README.md) and [diagnostic tutorial](tutorials/02-diagnostics.md) demonstrate the advisory alternative, including the SP0027 precondition warning. Editor design-time feedback remains portable; normal builds run the verifier by default.

## Next steps

Use [Unknown reasons](unknown-reasons.md) to understand unsupported semantics or exhausted budgets before changing limits. `Assume` and trusted boundaries are assumptions, not fixes for a failing claim. Follow the [tutorial series](tutorials/README.md) for supported examples and [upgrades](upgrading.md) when changing versions.

The optional [developer tools](developer-experience.md) add packaged project templates, configuration checks, evidence comparison, NUnit regression export, and companion scaffolding. They use matching preview packages from your configured feed; the standard single-reference setup above remains sufficient for build verification.
