# Native SMT packaging

SharpProof ships three exact-version packages:

- `SharpProof.Attributes`: the compiler-visible contract API;
- `SharpProof`: the portable analyzer, generator, and managed analyzer closure;
- `SharpProof.Verifier`: container-only Linux amd64 MSBuild integration,
  launcher, worker, and native SMT closure.

The dependency graph is
`SharpProof.Verifier -> SharpProof -> SharpProof.Attributes`, with an exact
current-version range at each edge. The portable package contains no worker,
`tools/net9`, Z3, or native payload. The verifier package contains no analyzer
directory or Attributes DLL.

The `SharpProof` package has two small Roslyn entry assemblies under
`tools/analyzers/dotnet/cs`: `SharpProof.Analyzer.dll` and
`SharpProof.ContractForGenerator.dll`. Their non-entrypoint implementation and
dependency closure is stored once under `tools/shared/netstandard2.0`; the
compiler collector remains the only entry under `tools/collector`. This avoids
the former linked-source analyzer monolith and prevents duplicate analyzer
discovery without duplicating the implementation closure.

## Pinned Z3 closure

`eng/container/toolchain.json` is the authority for the Z3 version, official
archive URL, archive SHA-256, extracted `libz3.so` SHA-256 and size, and the
managed `Microsoft.Z3.dll` SHA-256 and size. The Docker build downloads the
official archive and fails unless every pin matches. The binary is not stored
in Git.

The verifier package places the native library at
`runtimes/linux-x64/native/libz3.so` and the managed assembly under
`tools/net9`. Before constructing a Z3 context, the worker resolves and hashes
the canonical container library, installs a `NativeLibrary` resolver, and
loads only that absolute file. `LD_LIBRARY_PATH` and ambient system libraries
are not trust inputs.

## Container process boundary

The full verifier runs only in the pinned Linux amd64 container. Core MSBuild
starts the launcher, which validates the container contract, runtime closure,
paths, and publication ownership before releasing its direct child worker via
an exact stdin startup message. Cancellation terminates the child gracefully
and then forcibly within one monotonic deadline. The worker installs a Linux
parent-death signal so launcher loss cannot leave it running.

Docker is the hard CPU and memory boundary. SharpProof does not implement a
second cgroup or RSS controller. Its own protocol retains wall-clock,
solver, and semantic-work budgets.

## Package and release validation

Package validation creates one isolated three-package feed and checks exact
layouts, SourceLink symbol packages, repository commits, dependency ranges,
native hashes, analyzer entry points, and packaged verification. Consumer
restore is isolated from public feeds except for explicitly prepared framework
reference packages.

Each `.nupkg` is PDB-free and has one matching `.snupkg`; together the release
set is exactly three main packages and three symbol packages at one version.
Release evidence includes SPDX 2.3, `SHA256SUMS`, the release manifest,
container-toolchain identity, and exact source commit. Publication promotes
the tested bytes in dependency order:
`SharpProof.Attributes -> SharpProof -> SharpProof.Verifier`.
Every main package must be absent before publication, and duplicate skipping is
never used. A main or symbol collision fails closed; any partial publication
requires a new version rather than reusing remote bytes.

The worker protocol is 11, the cache schema is 13, and compiler artifacts use
schema 17. The worker consumes sealed compiler artifacts rather than parsing
source or rereading references. The admitted semantic subset and typed
`Unknown` behavior are documented separately in `SEMANTICS.md` and
`docs/analysis-limits.md`.
