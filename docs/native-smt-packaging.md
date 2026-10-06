# Native SMT packaging

SharpProof ships an exact-version three-package graph:

`SharpProof.Verifier -> SharpProof -> SharpProof.Attributes`

Attributes supplies the compile-time API. SharpProof supplies the portable analyzer, companion loading hook, compiler collector, and managed implementation closure. Verifier supplies canonical-container build tooling and native verification.

## Layout and isolation

The portable package has analyzer entry assemblies under `tools/analyzers/dotnet/cs`, shared implementations under `tools/shared/netstandard2.0`, and the collector under `tools/collector`. It does not contain the native worker/Z3 payload.

The verifier package puts build/worker dependencies under `tools/net9` and the native library at `runtimes/linux-x64/native/libz3.so`. It does not duplicate application Attributes compile assets or portable analyzer discovery entries.

Implementation assemblies are tool payloads, not supported application compile APIs.

## Pinned Z3 closure

[eng/container/toolchain.json](../eng/container/toolchain.json) owns Z3 version 4.12.2, the official archive URL/hash, extracted managed/native hashes, and byte sizes. The Dockerfile verifies pinned inputs; binaries are not checked into source.

Before a native context is created, container loading validates the expected environment and resolves the required absolute library through the installed resolver. Ambient system libraries and `LD_LIBRARY_PATH` do not substitute for the pinned payload.

The archive pin, managed `Microsoft.Z3.dll`, and native `libz3.so` are distinct checks. Keep them synchronized when changing the payload.

## Execution boundary

Full verification requires the canonical Linux amd64 container and Core MSBuild. Build tasks and launcher bound execution and termination; Docker supplies the outer CPU/memory boundary. Worker query, method, project, and construction budgets remain separate.

The worker consumes the sealed compiler artifact, not a source reparse. Current protocol/schema ownership is listed in [release constants](release-constants.md). See [SMT lifecycle](smt-lifecycle.md) for evidence, replay, and cancellation.

## Package validation and release

`tooling pack -Configuration Release` requires clean exact-commit source, builds the package graph, and validates packed layouts and metadata. The release set contains three main packages and their matching symbol packages at one version.

Package and consumer checks cover entrypoint/layout separation, dependency ranges, source/repository metadata, native closure, and actual analyzer/verifier behavior. Portable tag consumers run on Linux, Windows, and macOS; full verification runs in the canonical container.

The tag workflow downloads the package job's artifacts for consumers and publication and uses `dotnet nuget push --skip-duplicate`. Duplicate skipping does not establish that an existing remote package has identical bytes. See [release process](../eng/release/README.md) for security dependencies and private-preview routing.
