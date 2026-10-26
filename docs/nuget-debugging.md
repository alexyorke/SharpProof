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
```

## Reproduce managed bytes across paths

The repository uses deterministic compilation, CI source-path normalization,
locked restore, a pinned SDK/container toolchain, and exact-commit package
provenance. The
[DotNet.ReproducibleBuilds project](https://github.com/dotnet/reproducible-builds)
documents the same kinds of compiler, source-path, and build-isolation concerns.
Reproducibility is not continuously measured in CI.
