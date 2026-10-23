# SharpProofStarter

This project uses SharpProof __SHARPPROOF_VERSION__ and the qualified compiler SDK.
The starter targets .NET 10 and pins C# 13 for compatibility with the packaged
Roslyn exporter. Its single SharpProof reference supplies the contract API,
analyzers, and verifier.
Configure your NuGet feed if the preview packages are private.

The portable variant explicitly disables native verification; the container
variant enables strict verification.
For the portable variant, run `dotnet restore`, then `dotnet build`.
Portable analyzer silence is not a native proof.

For the container variant, run `docker compose pull verify`,
`docker compose run --rm verify restore SharpProofStarter.csproj`, then
`docker compose run --rm verify build SharpProofStarter.csproj`.
The native report should prove the postcondition on `Counter.Next`.
Its reports are retained under `artifacts/sharpproof/`. Host and container
compiler intermediates are isolated.

`Counter.DemonstrateDiagnostic` intentionally produces SP0027 in both variants;
change its argument from -1 to 0 to remove the warning.

Install `SharpProof.Tool` version `__SHARPPROOF_VERSION__` as an optional local
.NET tool. For the portable variant, run
`dotnet sharpproof doctor --project SharpProofStarter.csproj --verification portable`.
Run native setup checks with `--verification container` from the canonical
container where the tool is installed. Stored native reports can be explained
on the host with
`dotnet sharpproof explain --result artifacts/sharpproof/result.json`.
The explain command requires the matching request and compiler manifest.
Export regression tests to a sibling directory outside this project. The
generated Compose service mounts this project only; running a sibling test
project in that service requires an additional bind mount.

Contracts are static declarations. They do not perform runtime input validation.
Keep runtime checks at application boundaries where required.
