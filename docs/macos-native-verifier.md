# Native macOS verifier qualification

Add one `SharpProof` package reference and build with .NET SDK 10.0.100+ and the .NET 10 runtime on a supported macOS ARM64/x64 host. Strict full verification is automatic; no SharpProof activation properties or separate API/analyzer references are needed. Keep the standard reference without `PrivateAssets="all"` so application contracts propagate.

The verifier package includes pinned Z3 4.12.2 libraries for `osx-arm64` and `osx-x64`. The managed worker is under `tools/net10`; MSBuild selects the native library for the host and process architecture. The worker validates the package-owned managed assembly and native library hashes before creating a Z3 context. Ambient Z3 installations are not used.

The [release workflow](../.github/workflows/release.yml) packs the artifacts in the Linux container. Its `native-smoke` matrix has one `macos-latest` job, which runs [Test-SharpProofNativeSmoke.ps1](../eng/scripts/Test-SharpProofNativeSmoke.ps1) against those packages and requires a Proven postcondition and a Refuted broken counterpart. It uses the hosted runner's architecture; the workflow does not define separate Intel/ARM64 jobs or a native payload-corruption test. Both macOS payloads are packaged, but publication is gated only on the defined smoke jobs, not a six-platform native qualification matrix.

GitHub Actions [requires Linux runners for job, service, and Docker action containers](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#jobsjob_idcontainer). Native macOS verifier qualification therefore runs directly on macOS runners. The canonical Linux container verifier job remains independent.

Native macOS runs use worker query, method, and project budgets plus launcher wall-clock termination. Docker's outer CPU and memory isolation applies only to the Linux container path. See [preview support](preview-support.md) for the current host and filesystem boundary.
