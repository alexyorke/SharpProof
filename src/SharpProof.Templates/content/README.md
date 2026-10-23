# SharpProofStarter

This project uses SharpProof __SHARPPROOF_VERSION__ and the qualified compiler SDK.
The starter targets .NET 10 and pins C# 13 for compatibility with the packaged
Roslyn exporter. Its single SharpProof reference supplies the contract API,
analyzers, and verifier.
Configure your NuGet feed if the preview packages are private.

Run `dotnet restore`, then `dotnet build`. `Counter.DemonstrateDiagnostic` intentionally
produces SP0027; change its argument from -1 to 0 to remove the warning.
Portable analyzer silence is not a native proof.
The portable variant explicitly disables native verification; the container
variant enables strict verification.

For the container variant, run `docker compose pull verify` and
`docker compose run --rm verify build SharpProofStarter.csproj`.
The native report should prove the postcondition on `Counter.Next`.
Its reports are retained under `artifacts/sharpproof/`. Host and container
compiler intermediates are isolated.

Install the matching optional `SharpProof.Tool` as a local .NET tool, then run
`dotnet sharpproof doctor --project SharpProofStarter.csproj` and
`dotnet sharpproof explain --result artifacts/sharpproof/result.json`.
The explain command requires the matching request and compiler manifest.

Contracts are static declarations. They do not perform runtime input validation.
Keep runtime checks at application boundaries where required.
