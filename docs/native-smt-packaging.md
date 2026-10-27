# Native SMT packaging

SharpProof ships an exact-version three-package graph:

`SharpProof -> SharpProof.Verifier -> SharpProof.Attributes`

Consumers install only `SharpProof`. NuGet restores the exact-version dependencies automatically, and the package enables strict full verification without SharpProof configuration properties. Keep this standard reference without `PrivateAssets="all"` so the transitive application contract dependency propagates. `SharpProof.Verifier` remains a compatibility entrypoint with the same defaults. Explicit advisory/off settings opt out of the default strict verifier.

Attributes supplies the application API. SharpProof is the standard entrypoint; Verifier owns the analyzer, companion loading hook, compiler collector, managed implementation closure, build tooling and worker/native payloads. The graph is cycle-free and supports Windows, macOS, and glibc Linux on x64/ARM64, as well as the canonical Linux container.

## Layout and isolation

The Verifier implementation package has analyzer entry assemblies under `tools/analyzers/dotnet/cs`, shared implementations under `tools/shared/netstandard2.0`, and the collector under `tools/collector`, alongside its native verification payload. Main SharpProof restores that complete implementation transitively. Advisory/off opt-outs change execution rather than installing a separate portable package graph.

The verifier package puts build/worker dependencies, including `Microsoft.Z3.dll`, under `tools/net10`. It puts architecture-specific native libraries under `tools/native/linux-x64`, `tools/native/linux-arm64`, `tools/native/win-x64`, `tools/native/win-arm64`, `tools/native/osx-arm64`, and `tools/native/osx-x64`. The authoritative layouts are [SharpProof.nuspec](../src/SharpProof.Package/SharpProof.nuspec) and [SharpProof.Verifier.nuspec](../src/SharpProof.Verifier/SharpProof.Verifier.nuspec). It does not duplicate application Attributes compile assets or portable analyzer discovery entries.

Implementation assemblies are tool payloads, not supported application compile APIs.

## Pinned Z3 closure

[eng/container/toolchain.json](../eng/container/toolchain.json) owns Z3 version 4.12.2, native build/archive provenance, managed/native hashes, and byte sizes. Linux ARM64 uses its pinned wheel payload. Windows x64/ARM64 payloads are built from pinned source with the C++ runtime linked statically and audited for architecture and imports, so consumers need no separate Visual C++ redistributable installation. The Dockerfile verifies pinned inputs; binaries are not checked into source. Native ABI qualification runs against the exact packed artifacts; compilation and import audits alone do not establish that qualification.

Before a native context is created, Linux container loading validates the expected environment and resolves the required absolute library through the installed resolver. macOS loading checks the package-owned managed assembly and selected native library against their pinned hashes. Ambient system libraries do not substitute for the pinned payload.

The archive pins, managed `Microsoft.Z3.dll` files, and native `libz3.so`/`libz3.dll`/`libz3.dylib` files are distinct checks. Keep them synchronized when changing the payload. The package uses the Linux archive's managed assembly for its common worker closure; the native Windows proof consumer exercises its compatibility with the Windows DLL.

The worker is framework-dependent and uses the installed .NET 10 runtime's framework libraries, including JSON serialization and encoding. Its dependency manifest and runtime configuration are packaged together under `tools/net10`; preserve that complete managed/native closure when validating package layout and native consumers.

## Execution boundary

Full verification requires SDK 10.0.100+ with the .NET 10 runtime in the canonical Linux amd64 container or on native Windows, macOS, or glibc Linux on x64/ARM64. It uses Core MSBuild, or Windows x64 full-framework MSBuild 18+ with the out-of-process .NET task-host bridge for SDK-style consumers. Full-host qualification requires successful completed actual MSBuild.exe CI runs against the exact source/artifacts. The ARM64 full-framework bridge is implemented but is not separately qualified by these x64 Visual Studio jobs; its native Core MSBuild path has separate coverage. MSBuild 17.14 supports explicit advisory-only operation; older full-framework hosts do not run the default verifier. See [legacy compatibility](legacy-compatibility.md) and the [support matrix](preview-support.md) for compiler, filesystem, glibc, and native runtime restrictions. musl is unsupported. Build tasks and launcher bound execution and termination; Docker supplies the outer CPU/memory boundary only for container verification. Worker query, method, project, and construction budgets remain separate.

The worker consumes the sealed compiler artifact, not a source reparse. Current protocol/schema ownership is listed in [release constants](release-constants.md). See [SMT lifecycle](smt-lifecycle.md) for evidence, replay, and cancellation.

## Package validation and release

`tooling pack -Configuration Release` requires clean exact-commit source, builds the package graph, and validates packed layouts and metadata. The release set contains three main packages and their matching symbol packages at one version.

Package tests cover entrypoint/layout separation, dependency ranges, source/repository metadata, native closure, and actual analyzer/verifier behavior in the canonical container.

The tag workflow publishes the package job's artifacts with `dotnet nuget push --skip-duplicate`. Duplicate skipping does not establish that an existing remote package has identical bytes. See [release process](../eng/release/README.md).
