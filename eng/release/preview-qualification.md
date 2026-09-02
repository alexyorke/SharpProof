# SharpProof preview qualification matrix

The full verifier candidate is qualified only in the canonical SharpProof
Linux amd64 container. Release evidence must bind every required row to the
exact source commit, Dockerfile, container-toolchain catalog, pinned base
images, verified Z3 payload layout, and package identities.

| Behavior | Required container evidence |
|---|---|
| Clean Docker-only build | Build the pinned image and run locked restore with no host .NET, PowerShell, Z3, or MSBuild dependency |
| Ordinary packaged verification | Isolated-feed `SharpProof.Verifier` consumer produces a real proof |
| Portable consumers | Isolated-feed netstandard2.0, net8.0, and net472 builds pass |
| Percent, Unicode, spaces, and long local paths | Linux package integration matrix passes |
| Cache and SARIF publication | Packaged verification publishes and validates both outputs |
| Cooperative concurrent publication | Disjoint/equal sets complete coherently; each partial overlap is rejected; reverse lock order cannot deadlock |
| Verifier cancellation | Before-start, startup, active verification, and publication cancellation tests pass with no surviving worker |
| Native SMT closure | Packaged `libz3.so` path and size match the toolchain catalog and hostile ambient library paths cannot redirect loading |
| Package graph | Exactly `SharpProof.Attributes`, `SharpProof`, and `SharpProof.Verifier`, with one analyzer and one collector entry point |
| Release gates | Debug, Release, acceptance, coverage, mutation, fuzz, corpus, performance, package, pilots, and publication dry run pass in-container |

Portable analyzer packages remain operating-system-neutral, but their declared
framework consumers are restored and built only inside the canonical
container. No native-host SDK or MSBuild job participates in qualification.

Windows/Visual Studio verifier execution, native host installs, ARM64 verifier
containers, Rider, hostile host mutation, and shared/network publication are
outside this preview. The normative boundary is `docs/preview-support.md`.
