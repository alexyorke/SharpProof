# SharpProofStarter

This workspace pins SharpProof __SHARPPROOF_VERSION__, its matching local tool,
the qualified .NET 10 SDK, and C# 13 for the packaged Roslyn exporter.

Application source lives in `src/app/`; generated NUnit regressions belong under
`tests/`. The root stays mounted at `/workspace/consumer` in the container, so
both projects and the pinned local tool remain visible. Compiler outputs, NuGet
packages, and the local tool resolver cache use private named volumes.

`NuGet.config` sends only `SharpProof*` to the private `SharpProofPreview` feed.
Supply read-only credentials through your credential provider or the process
environment variable `NuGetPackageSourceCredentials_SharpProofPreview`.
Credentials are not stored in this workspace. Authenticate Docker to the private
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
emits SP0027; change its argument from -1 to 0 to remove that warning.

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
`SHARPPROOF_PREVIEW_TOKEN` for image and package reads. Comparison defaults to
`report`; change the caller's `mode` to `enforce` to gate regressions. PR
baselines come from the exact base commit. An explicit default-branch manual
`initialize-baseline` run establishes the first baseline.

Contracts are static declarations, not runtime input validation.
