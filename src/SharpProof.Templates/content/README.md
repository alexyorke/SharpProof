# SharpProofStarter

This project uses SharpProof __SHARPPROOF_VERSION__ and the qualified compiler SDK.
The starter targets .NET 10 and pins C# 13 for compatibility with the packaged
Roslyn exporter. Its single SharpProof reference supplies the contract API,
analyzers, and verifier. The matching local tool is pinned in
`.config/dotnet-tools.json`; run `dotnet tool restore` before `dotnet sharpproof`.
The optional editor package is included by default; use `--editor false` when
generating a project to omit it.

`NuGet.config` restores every package, SharpProof included, from nuget.org and
needs no credentials for that source. If the matching preview packages are not
published there, configure your qualified local or private feed before restore. Authenticate Docker to `ghcr.io/alexyorke/sharpproof-preview` using your
credential store before pulling the private container image.

The portable variant explicitly disables native verification; the container
variant enables strict verification.
For the portable variant, run `dotnet restore`, then `dotnet build`.
Portable analyzer silence is not a native proof.

For the container variant, run `docker compose pull verify`,
`docker compose run --rm verify restore SharpProofStarter.csproj`, then
`docker compose run --rm verify build SharpProofStarter.csproj`.
Run `docker compose run --rm verify tool restore` to install the pinned tool in
the container. Its package and resolver caches use private named volumes.
The native report should prove the postcondition on `Counter.Next`.
Its reports are retained under `artifacts/sharpproof/`. Host and container
compiler intermediates are isolated.

`Counter.DemonstrateDiagnostic` intentionally produces SP0027 in both variants.
In the strict container variant, its call-precondition claim is also Refuted
with SP0051 and fails the build. Change its argument from -1 to 0 before
expecting a successful strict build; `Counter.Next` itself has a valid proof.

After restoring the local tool, for the portable variant run
`dotnet sharpproof doctor --project SharpProofStarter.csproj --verification portable`.
For the native variant, run `docker compose run --rm verify tool run dotnet-sharpproof -- doctor --project SharpProofStarter.csproj --verification container`.
Stored native reports can be explained
on the host with
`dotnet sharpproof explain --result artifacts/sharpproof/result.json`.
The explain command validates the matching request and compiler manifest when
both sibling files exist or a companion path is explicitly supplied. Otherwise
it can report the standalone stored result. Comparison and regression export
require the complete set.
Export regression tests to a sibling directory outside this project. The
generated Compose service mounts this project only; running a sibling test
project in that service requires an additional bind mount.
Generate `sharpproof-workspace` for a root that already mounts both `src/app`
and `tests`, with regressions exported below `tests/`.

The container CI caller pins the reusable baseline workflow to source revision
`__SHARPPROOF_SOURCE_REVISION__`. Set read-only `SHARPPROOF_PREVIEW_TOKEN` in your
repository for private image reads. Packages default to nuget.org; configure the
optional package-read token only if you choose the GitHub package feed. Comparison starts in `report`
mode; set `mode: enforce` in the caller when ready to gate regressions. Baseline
selection requires the exact PR base commit. Use the explicit default-branch
manual `initialize-baseline` input to establish the first baseline.

Contracts are static declarations. They do not perform runtime input validation.
Keep runtime checks at application boundaries where required.
