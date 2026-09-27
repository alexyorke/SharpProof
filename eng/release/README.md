# Release process

Releases are driven by `.github/workflows/package-consumers.yml`:

1. `pack` builds the three-package graph in the canonical Linux amd64
   container and validates the package graph.
2. `package-consumers` runs the analyzer and the real verifier against the
   packed packages in the container, and `Test-SharpProofPortableConsumer.ps1`
   runs framework consumers on Linux, Windows, and macOS for tags.
3. A `v*` tag whose name matches `SharpProofPackageVersion` in
   `SharpProof.Release.props` pushes the packed `.nupkg` files (and their
   symbol packages) with `dotnet nuget push`. `v1.0.0-preview.1` goes to the
   private feed; later tags go to nuget.org through NuGet OIDC login.

`third-party-components.json` lists the third-party components summarized in
`THIRD-PARTY-NOTICES.txt`.
