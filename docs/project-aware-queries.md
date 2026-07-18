# Project-Aware Proof Queries

Standalone source queries deliberately build a small synthetic compilation.
Use project-aware mode when the answer must match the user's real build:

```powershell
dotnet run --project .\Tools\SharpProof.SymbolicCli\SharpProof.SymbolicCli.csproj -- explain `
  --project .\src\Example\Example.csproj `
  --file .\src\Example\Worker.cs `
  --line 42
```

The project should be restored before it is queried. SharpProof uses Roslyn's
MSBuild workspace to load:

- project and framework references
- C# parse options, language version, preprocessor symbols, and nullable mode
- compilation options such as unsafe code, platform, optimization, and warning
  severity
- `.editorconfig` and global analyzer configuration
- `SharpProof.Baseline.json` and effect-summary `AdditionalFiles`

`explain` also runs `SharpProofAnalyzer` against that loaded compilation. Its
`Build diagnostics` section therefore reflects analyzer severity configuration,
baseline suppression, and configured effect summaries instead of reconstructing
diagnostics from symbolic results.

## Projects And Solutions

Use `--project` when the project is known. Use `--solution` when project
selection is part of the query:

```powershell
dotnet run --project .\Tools\SharpProof.SymbolicCli\SharpProof.SymbolicCli.csproj -- explain `
  --solution .\Example.sln `
  --project-name Example.Core `
  --file .\src\Example.Core\Worker.cs `
  --line 42
```

`--project-name` matches the Roslyn project name, assembly name, or project
file name. It is required when multiple solution projects compile the same
linked source file.

For multi-targeting or non-default configurations, pass MSBuild properties:

```powershell
dotnet run --project .\Tools\SharpProof.SymbolicCli\SharpProof.SymbolicCli.csproj -- --project .\Example.csproj --file Worker.cs --line 42 `
  --configuration Release `
  --framework net8.0 `
  --msbuild-property RuntimeIdentifier=win-x64
```

`--msbuild-property` can be repeated. Project mode rejects standalone compiler
switches such as `--reference`, `--language-version`, `--define`, and
`--nullable`; allowing both sources of truth would make the query different
from the build.

Explicit SMT and bounded-analysis CLI overrides remain valid. Without an
override, project mode uses the global `sharpproof_smt_*` and
`sharpproof_analysis_*` values from the loaded analyzer configuration.

Workspace load warnings are printed by `explain`. A missing project, missing
document, ambiguous linked document, or failed compilation is returned as a
usage error instead of silently falling back to standalone mode.

## .NET API

The public .NET API intentionally accepts source text or a source file through
`SharpProofAnalysisSession`. Roslyn compilation, workspace, analyzer-option,
and project-loading adapters are internal implementation details so raw Roslyn
objects cannot leak into the supported API.

Use the CLI for project- and solution-aware analysis. Hosts that need an
in-process project adapter should invoke the CLI JSON protocol until a focused,
Roslyn-independent project request contract is added.
