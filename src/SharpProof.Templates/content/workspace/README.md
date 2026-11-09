# SharpProofStarter

This workspace pins SharpProof __SHARPPROOF_VERSION__, its matching local tool,
the qualified .NET 10 SDK, and C# 13 for the packaged Roslyn exporter.

Application source lives in `src/app/`; generated NUnit regressions belong under
`tests/`. The root stays mounted at `/workspace/consumer` in the container, so
both projects and the pinned local tool remain visible. Compiler outputs, NuGet
packages, and the local tool resolver cache use private named volumes.

`NuGet.config` restores every package, SharpProof included, from nuget.org and
needs no credentials for that source. If the matching preview packages are not
published there, configure your qualified local or private feed before restore. Authenticate Docker to the private
`ghcr.io/alexyorke/sharpproof-preview` image using your credential store.

For portable analysis:

```text
dotnet tool restore
dotnet build src/app/SharpProofStarter.csproj
dotnet sharpproof doctor --project src/app/SharpProofStarter.csproj --verification portable
```

For strict native verification in the container:

```text
docker compose pull verify
docker compose run --rm verify tool restore
docker compose run --rm verify restore src/app/SharpProofStarter.csproj
docker compose run --rm verify build src/app/SharpProofStarter.csproj
docker compose run --rm verify tool run dotnet-sharpproof -- doctor --project src/app/SharpProofStarter.csproj --verification container
docker compose run --rm verify tool run dotnet-sharpproof -- explain --result artifacts/sharpproof/result.json
```

Use the commands for the variant selected with `--verification`. Portable
analysis uses the advisory profile and disables native verification; the
container variant uses the strict profile. `DemonstrateDiagnostic` intentionally
emits SP0027; strict verification also refutes that call with SP0051. Change its
argument from -1 to 0 before expecting the container build to pass.

The optional `SharpProof.CodeFixes` reference is included by default. Generate
with `--editor false` to omit it. The core `SharpProof` facade remains the only
required application package.

After a supported scalar postcondition is refuted, export into
`tests/SharpProof.Regression` with the matching claim ID:

```text
docker compose run --rm verify tool run dotnet-sharpproof -- export-test --project src/app/SharpProofStarter.csproj --result artifacts/sharpproof/result.json --claim <claim-id> --workspace . --output tests/SharpProof.Regression
docker compose run --rm verify test tests/SharpProof.Regression/SharpProof.Regression.csproj
```

The container variant's CI caller pins the reusable workflow to source revision
`__SHARPPROOF_SOURCE_REVISION__`. Configure read-only
`SHARPPROOF_PREVIEW_TOKEN` for private image reads. Packages default to nuget.org;
the optional package-read token is needed only with the GitHub package feed. Comparison defaults to
`report`; change the caller's `mode` to `enforce` to gate regressions. PR
baselines come from the exact base commit. An explicit default-branch manual
`initialize-baseline` run establishes the first baseline.

Contracts are static declarations, not runtime input validation.
