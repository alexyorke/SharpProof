# Native SMT Packaging And Platform Support

SharpProof uses Microsoft Z3 4.12.2 for bounded SMT proofs. Native support is
explicitly platform-dependent; missing or incompatible native code must reduce
proof strength, not crash an analyzer host or turn an unknown result into a
proof.

## Packaging Decision

| Consumer surface | Platform | Native asset policy | Expected behavior |
| --- | --- | --- | --- |
| `SharpProof.Symbolic` | Windows x64 | `Microsoft.Z3` selects `runtimes/win-x64/native/libz3.dll` | Native SMT required by the package-consumer probe |
| `SharpProof.Symbolic` | macOS x64 | `Microsoft.Z3` selects `runtimes/osx-x64/native/libz3.dylib` | Native SMT required by the package-consumer probe |
| `SharpProof.Symbolic` | Linux x64 | Microsoft.Z3 4.12.2 has no Linux native asset | Use a compatible host-provided Z3 library or fall back conservatively |
| `SharpProof` analyzer | Windows x64 | Official `libz3.dll` is embedded beside analyzer dependencies | Native SMT available to Roslyn analyzer hosts |
| `SharpProof` analyzer | macOS x64 | Official `libz3.dylib` is embedded beside analyzer dependencies | Native SMT available to Roslyn analyzer hosts |
| Either surface | arm64 or another unsupported RID | No bundled 4.12.2 native asset | Use a compatible host-provided library or fall back conservatively |

The normal library package follows NuGet's
[`runtimes/{rid}/native/` convention](https://learn.microsoft.com/en-us/nuget/create-packages/native-files-in-net-packages)
through its `Microsoft.Z3` dependency. The analyzer package is different:
Roslyn loads analyzer dependencies from the analyzer directory, and the
[Roslyn packaging guidance](https://github.com/dotnet/roslyn/blob/main/docs/features/source-generators.cookbook.md#use-functionality-from-nuget-packages)
places private dependencies beside the analyzer assembly.

Native Windows DLLs under `analyzers/dotnet/cs` are also discovered by NuGet as
candidate managed analyzers. `buildTransitive/SharpProof.targets` removes only
`libz3.dll` from the compiler's `Analyzer` item list before `CoreCompile`, so it
does not produce `CS8034` or hide a real analyzer load failure.

Roslyn shadow-copies managed analyzer dependencies but does not copy arbitrary
native neighbors. The package therefore ships
`SharpProof.NativeSmtLocator.txt` beside Z3 and passes that tiny marker as an
`AdditionalFiles` item from the original package directory. SharpProof never
reads the marker contents. It accepts only that exact locator name, chooses the
matching x64 native file for the current OS, and preloads the native library
before the first SMT query. This keeps native resolution working in compiler
server and non-shared compiler hosts without treating a native binary as text.

The official
[`Microsoft.Z3` 4.12.2 package](https://www.nuget.org/packages/Microsoft.Z3/4.12.2)
does not contain `linux-x64` or arm64 native assets. SharpProof does not copy an
untracked Linux binary from a separate release archive: doing so would create
an independent ABI, `libgomp`, provenance, and patching obligation outside the
pinned managed binding package. Linux and unsupported-architecture behavior is
therefore an intentional graceful fallback for this preview.

## Graceful Fallback Contract

The first native initialization failure is caught inside `SmtAnalysisService`.
The service becomes `PermanentlyUnavailable`, disposes any partial thread-local
solver context, and returns conservative unknown proof results. Later queries
do not repeatedly attempt the same failed native initialization.

`SmtAnalysisHealth.LastFailureCode` uses these stable values:

| Failure code | Meaning |
| --- | --- |
| `smt_native_library_missing` | The managed binding or native library could not be found |
| `smt_native_library_incompatible` | The binary format or native entry point is incompatible |
| `smt_platform_unsupported` | The host explicitly rejected the platform |
| `smt_initialization_failure` | Another permanent type/native initialization failure occurred |

Nested `TypeInitializationException` and aggregate wrappers are unwrapped so a
native missing/incompatible cause retains its specific code. Query results use
the existing `smt_unavailable` reason and stable
`proof.native_solver_failure` unknown taxonomy. Analyzer callbacks continue
without `AD0001`, `CS8032`, `CS8034`, or a process crash. Users who do not want
any native attempt can configure SMT mode `off`.

## Provenance

The analyzer package takes `Microsoft.Z3.dll`, `libz3.dll`, and
`libz3.dylib` directly from the restored, pinned Microsoft.Z3 4.12.2 package by
using its generated NuGet path property. `THIRD-PARTY-NOTICES.txt` records the
upstream tag, package URL, embedded files, copyright, and MIT license. Packaging
tests verify those entries and reject an undocumented `libz3.so`.

## Consumer Verification

Run the same package-consumer probe used by CI:

```powershell
.\scripts\Test-SharpProofPackageConsumers.ps1 -Configuration Release -ExpectedSmt Required
```

The script:

1. restores and builds the shipping package graph;
2. packs `SharpProof` and `SharpProof.Symbolic` into a temporary local feed;
3. restores a clean symbolic consumer and executes a real SMT-backed pair of
   implication proofs;
4. restores a clean analyzer consumer, requires `SP0004` to prove analyzer
   execution, and reads the compiler SARIF log to reject analyzer-load failures;
5. removes the bounded temporary consumer directory.

`.github/workflows/package-consumers.yml` runs this probe on
`windows-latest`, `ubuntu-latest`, and the x64 `macos-15-intel` runner. Windows
and macOS require native proofs. Linux accepts either a compatible
host-provided solver or the documented permanent conservative fallback. The
fixture always requires at least one attempted SMT query, so a test cannot pass
by silently disabling SMT before reaching native initialization.
