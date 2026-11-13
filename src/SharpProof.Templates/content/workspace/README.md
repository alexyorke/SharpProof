# SharpProofStarter

This workspace uses SharpProof __SHARPPROOF_VERSION__. It targets .NET 10 and pins
C# 13. The one `SharpProof` package reference supplies the contract API, the
analyzers and the verifier. The matching `dotnet sharpproof` tool is pinned in
`.config/dotnet-tools.json`; run `dotnet tool restore` to install it.

The application is in `src/app/`. Exported NUnit regression projects belong
under `tests/`, outside the application project.

You need the .NET 10 SDK (10.0.100 or later). Packages restore from nuget.org
without credentials.

`Counter.Next` has a precondition and a postcondition, and its proof succeeds.
To see a failure, uncomment `Counter.Broken` in `Counter.cs`: it calls
`Next(-1)`, which breaks the precondition.

How contracts are checked depends on the `--verification` option used when the
workspace was generated. Follow the one section that applies.

## native (the default)

Every build verifies the contracts on this machine and fails if one is refuted
or cannot be proven. Supported hosts are Windows, macOS and glibc Linux on x64
or ARM64.

```text
dotnet build src/app/SharpProofStarter.csproj
dotnet tool restore
dotnet sharpproof doctor --project src/app/SharpProofStarter.csproj
dotnet sharpproof explain --result src/app/obj/Debug/net10.0/SharpProof/result.json
```

`.github/workflows/build.yml` runs the same build on every push and pull
request.

## container

Verification runs in a Linux amd64 container image pinned by digest in
`compose.yaml`, so every machine and CI run uses the same toolchain. Reports are
kept under `artifacts/sharpproof/`.

```text
docker compose pull verify
docker compose run --rm verify tool restore
docker compose run --rm verify restore src/app/SharpProofStarter.csproj
docker compose run --rm verify build src/app/SharpProofStarter.csproj
docker compose run --rm verify tool run dotnet-sharpproof -- doctor --project src/app/SharpProofStarter.csproj --verification container
docker compose run --rm verify tool run dotnet-sharpproof -- explain --result artifacts/sharpproof/result.json
```

`.github/workflows/sharpproof.yml` compares each run with the run for its base
commit. It calls the SharpProof reusable workflow at source revision
`__SHARPPROOF_SOURCE_REVISION__`. It starts with only a manual trigger, because
the first comparison needs a baseline:

1. On the default branch, run the workflow by hand with `initialize-baseline`
   ticked.
2. When that run succeeds, uncomment the `push` and `pull_request` triggers in
   the workflow file.

Comparison starts in `report` mode, which records regressions without failing.
Set `mode: enforce` in the workflow to fail on them. A refuted contract fails
the build in either mode.

After a postcondition is refuted, export a regression test for it into `tests/`:

```text
docker compose run --rm verify tool run dotnet-sharpproof -- export-test --project src/app/SharpProofStarter.csproj --result artifacts/sharpproof/result.json --claim <claim-id> --workspace . --output tests/SharpProof.Regression
docker compose run --rm verify test tests/SharpProof.Regression/SharpProof.Regression.csproj
```

## portable

The analyzers run in the editor and the build, but no proofs are run
(`SharpProofProfile=advisory`, `SharpProofVerify=false`). A build with no
diagnostics is not a proof.

```text
dotnet build src/app/SharpProofStarter.csproj
dotnet tool restore
dotnet sharpproof doctor --project src/app/SharpProofStarter.csproj --verification portable
```

## Notes

The optional `SharpProof.CodeFixes` editor package is referenced by default.
Generate with `--editor false` to leave it out.

Contracts are static declarations. They do not validate input at run time; keep
runtime checks where your application needs them.
