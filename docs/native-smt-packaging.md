# Native SMT packaging

SharpProof ships an exact-version three-package graph:

`SharpProof.Verifier -> SharpProof -> SharpProof.Attributes`

Attributes supplies the compile-time API. SharpProof supplies the portable analyzer, companion loading hook, compiler collector, and managed implementation closure. Verifier supplies build tooling and native verification in the canonical Linux container and on macOS ARM64/x64.

## Layout and isolation

The portable package has analyzer entry assemblies under `tools/analyzers/dotnet/cs`, shared implementations under `tools/shared/netstandard2.0`, and the collector under `tools/collector`. It does not contain the native worker/Z3 payload.

The verifier package puts build/worker dependencies, including `Microsoft.Z3.dll`, under `tools/net9`. It puts architecture-specific native libraries under `tools/native/linux-x64`, `tools/native/osx-arm64`, and `tools/native/osx-x64`. The authoritative layouts are [SharpProof.nuspec](../SharpProof.Package/SharpProof.nuspec) and [SharpProof.Verifier.nuspec](../SharpProof.Verifier/SharpProof.Verifier.nuspec). It does not duplicate application Attributes compile assets or portable analyzer discovery entries.

Implementation assemblies are tool payloads, not supported application compile APIs.

## Pinned Z3 closure

[eng/container/toolchain.json](../eng/container/toolchain.json) owns Z3 version 4.12.2, official Linux and macOS archive URLs/hashes, extracted managed/native hashes, and byte sizes. The Dockerfile verifies pinned inputs; binaries are not checked into source.

Before a native context is created, Linux container loading validates the expected environment and resolves the required absolute library through the installed resolver. macOS loading checks the package-owned managed assembly and selected native library against their pinned hashes. Ambient system libraries do not substitute for the pinned payload.

The archive pins, managed `Microsoft.Z3.dll`, and native libraries are distinct checks. Keep them synchronized when changing the payload.

## Execution boundary

Full verification requires Core MSBuild in the canonical Linux amd64 container or on native macOS ARM64/x64. Build tasks and launcher bound execution and termination; Docker supplies the outer CPU/memory boundary only for container verification. Worker query, method, project, and construction budgets remain separate.

The worker consumes the sealed compiler artifact, not a source reparse. Current protocol/schema ownership is listed in [release constants](release-constants.md). See [SMT lifecycle](smt-lifecycle.md) for evidence, replay, and cancellation.

## Package validation and release

`tooling pack -Configuration Release` requires clean exact-commit source, builds the package graph, and validates packed layouts and metadata. The release set contains three main packages and their matching symbol packages at one version.

Package and consumer checks cover entrypoint/layout separation, dependency ranges, source/repository metadata, native closure, and actual analyzer/verifier behavior. Portable consumers run on Linux, Windows, macOS ARM64, and macOS Intel for branches, pull requests, and tags. Real verifier consumers run in the Linux container and on both native macOS architectures.

The tag workflow downloads the package job's artifacts for consumers and publication and uses `dotnet nuget push --skip-duplicate`. Duplicate skipping does not establish that an existing remote package has identical bytes. See [release process](../eng/release/README.md) for security dependencies and private-preview routing.
