# NuGet debugging and reproducibility

`SharpProof` is the consumer entrypoint. Its package is a metadata-only facade
depending on `SharpProof.Verifier`, which carries the analyzer, generator,
MSBuild tasks, worker, and native payloads. `SharpProof.Attributes` supplies the
public runtime contract API. A release has three `.nupkg` files and two
`.snupkg` files: Attributes and Verifier. The facade has no assembly or PDB to
publish.

## Symbols and sources

First-party tooling assemblies carry embedded portable PDBs. Their symbols
travel with the DLL when Roslyn loads an analyzer from a shadow-copy directory
or MSBuild loads a task. Tracked and generated C# source documents are embedded
in those PDBs, so inspecting source and resolving file/line stack frames does
not require a symbol server or source download. This is the compiler's
[embedded debug format](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-options/code-generation#debugtype).
The runtime still needs to retain a useful stack frame: optimization, inlining,
or a host that suppresses exception detail can limit the displayed trace.

Attributes retains an external portable PDB in its symbol package, with
embedded source documents. For interactive debugging of that runtime assembly,
enable the debugger's NuGet symbol server, or provide the matching PDB from
`SharpProof.Attributes.<version>.snupkg` beside the matching DLL. A debugger
must support portable PDBs; see the [.NET symbol and source debugging
design](https://github.com/dotnet/designs/blob/main/accepted/2020/diagnostics/debugging-with-symbols-and-sources.md).

The build exports each tooling PDB's exact embedded bytes before testing and
packing. Verifier's `.snupkg` therefore supplies matching portable PDBs for
debuggers that use external symbols as well. Source Link maps repository source
to the exact published commit; it complements the embedded source rather than
requiring a network connection for every debugging session. See Microsoft's
[Source Link documentation](https://learn.microsoft.com/en-us/dotnet/standard/library-guidance/sourcelink).

Package validation checks the exact package identities, common version and
repository commit, managed PE/PDB identity, Source Link commit, source document
checksums, embedded source inventory, and equality between the tooling DLL's
embedded PDB and its `.snupkg` counterpart. The facade must contain no DLL,
executable, or PDB and must depend on the exact matching Verifier version.
Native worker integrity checks remain independent of symbol validation.

## Verify packed artifacts

Run repository commands in the canonical Linux amd64 container. The host wrapper
also supports Git worktrees by mounting their Git metadata. Packing requires a
clean committed checkout so the source receipt describes the shipped code.

```powershell
pwsh -File eng/scripts/Invoke-SharpProof.ps1 pack -Configuration Release
pwsh -File eng/scripts/Invoke-SharpProof.ps1 package-consumers -Configuration Release -PackageSource artifacts/container-packages
pwsh -File eng/scripts/Invoke-SharpProof.ps1 dev -lc 'cd /workspace/SharpProof && pwsh -NoProfile -File eng/scripts/Test-SharpProofOfflineStackTraces.ps1 -PackageSource artifacts/container-packages'
```

The last check copies actual packaged DLLs into an isolated directory without
sidecar PDBs and requires file names and positive line numbers in exceptions
from the analyzer, generator, build task, and worker. It tests managed loading
and stack traces, not an interactive IDE session. The separate embedded-symbol
regression exercises corrupt debug records, mismatched PDBs, missing sources,
and source checksum failures.

## Reproduce managed bytes across paths

The repository uses deterministic compilation, CI source-path normalization,
locked restore, a pinned SDK/container toolchain, and exact-commit package
provenance. These settings provide the relevant compiler controls without
adding another build package. The
[DotNet.ReproducibleBuilds project](https://github.com/dotnet/reproducible-builds)
documents the same kinds of compiler, source-path, and build-isolation concerns.
Reproducibility remains a measured property of the actual outputs.

The [reviewed API inventory](../eng/api-coverage/README.md) uses the pinned
`Microsoft.NETCore.App.Ref` 10.0.13 / `net10.0` surface. The additional .NET 9
reference pack supports compatibility checks; it does not define the active
API inventory or the default verifier's SDK requirement.

After committing all source and harness changes, run the smaller experiment:

```powershell
pwsh -File eng/scripts/Invoke-SharpProof.ps1 dev -lc 'cd /workspace/SharpProof && pwsh -NoProfile -File eng/scripts/Test-SharpProofReproduciblePackages.ps1 -Scope Representative'
```

It sequentially creates two independent exact-commit Git clones at different
absolute paths, including a path containing spaces. It builds Attributes,
Worker.Protocol, and Host in Release using their declared target frameworks,
exports embedded symbols, and requires identical SHA-256 hashes for every
first-party DLL/PDB in those outputs. This covers external and embedded PDBs
and the current `netstandard2.0` and `net10.0` framework families. It does not
claim that the whole package graph was reproduced.

For the complete shipped managed payload set, use:

```powershell
pwsh -File eng/scripts/Invoke-SharpProof.ps1 dev -lc 'cd /workspace/SharpProof && pwsh -NoProfile -File eng/scripts/Test-SharpProofReproduciblePackages.ps1 -Scope Full'
```

Full mode runs the existing pack and package-source qualification flow twice,
sequentially in private clones. It checks every first-party DLL/PDB entry across
the three packages and two symbol packages, including duplicate shipped copies
under distinct package paths. Both modes verify the selected SDK, record the
toolchain catalog and native input hashes, and reject a changing native input
set. They reuse the container's NuGet download cache; each clone has fresh
source, `bin`, `obj`, and packing outputs. Temporary clones are removed after
the experiment; logs and a machine-readable `report.json` remain under
`artifacts/reproducibility`. Use `-OutputDirectory` to select another child of
the checkout's `artifacts` directory.

The `package-reproducibility` job in [ci.yml](../.github/workflows/ci.yml) requires Full mode and uploads its `report.json` as an exact-run/source artifact. Managed payload equality is required; raw ZIP equality remains a separately reported result.

Full mode also records raw `.nupkg` and `.snupkg` SHA-256 comparisons separately.
Equal managed payloads do not establish equal ZIP containers. Microsoft's
[deterministic package documentation](https://learn.microsoft.com/en-us/nuget/create-packages/deterministic-packages)
describes deterministic archive behavior introduced with .NET 11;
this repository pins SDK 10.0.402. Do not report byte-identical package archives
unless `packageBytesEqual` is true for the measured run. Add
`-RequirePackageBytes` to Full mode when the experiment should also fail on a
raw archive difference. The harness does not rewrite archives to make this
check pass.
