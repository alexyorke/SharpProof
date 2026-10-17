# Native Windows verifier plan

The goal is to run the packaged verifier with Core MSBuild and `dotnet build` on
Windows x64 while retaining the qualified Linux amd64 container path. Portable
analysis already has a Windows package consumer; it is not evidence of a native
proof run. Until the Windows acceptance gate below passes, the Windows verifier
remains unsupported as described in [preview support](preview-support.md).

## Current boundary

- `SharpProof.Verifier` now stages `tools/native/win-x64/libz3.dll` alongside
  `tools/native/linux-x64/libz3.so`. The toolchain catalog, payload preparation,
  package layout, and package checks pin both binaries. The worker still loads
  only the Linux payload.
- `ContainerContract` requires Linux x64 and a canonical container marker. Its
  Z3 loader verifies a file and loads it through `/proc/self/fd`.
- Verifier MSBuild props/targets require the container marker and point to the
  Linux Z3 file. The repository's own restore/build/test gate also requires
  the canonical container.
- Child runtime validation assumes a direct `dotnet` muxer. Windows uses
  `dotnet.exe`; path identity and publication containment must account for
  Windows case folding and filesystem aliases.
- Linux Docker provides the outer CPU and memory boundary. A native Windows
  run needs an explicit, tested budget and failure policy; worker query,
  method, project, and wall-clock budgets alone do not impose an OS memory
  limit. Do not introduce host Job Objects or process-memory budgets into the
  existing container workflow.

## Implementation sequence

1. **Keep the Linux baseline green.** Preserve its pinned Z3 archive, exact
   package layout, container contract, and full verifier consumer test. Run
   targeted tests after each shared-code change and the canonical package
   consumer before declaring a Windows milestone complete.
2. **Prepare platform-neutral launch code.** Accept only the current direct
   `dotnet.exe` muxer on Windows and `dotnet` on Linux. Audit the child
   environment for Windows runtime needs while retaining startup-hook and
   ambient native-library rejection. Add case-aware path identity without
   relaxing Linux's exact comparisons.
3. **Pin and package Windows Z3.** The official Windows x64 archive for Z3
   4.12.2, its native DLL, and its managed assembly are now pinned by hash and
   length. The DLL is staged and packed alongside the unchanged Linux payload.
   A Windows loader must still reject an unpinned or ambient DLL and establish
   compatibility between the Windows native DLL and the common managed
   assembly currently packaged under `tools/net9`.
4. **Add a Windows host contract and loader.** Select the host contract by
   actual OS and process architecture, not by a caller-controlled flag. Check
   the pinned package inputs before loading Z3 by an absolute path. Preserve
   the Linux descriptor-based loader. Keep Core MSBuild and the minimum SDK
   requirement for both hosts.
5. **Qualify Windows files and processes.** Exercise real NTFS paths with
   spaces, Unicode, case aliases, junctions/reparse points, and attempted
   publication into the worker runtime. Verify atomic result publication,
   locking, concurrent builds, stale-result rejection, cancellation, process
   tree termination, and bounded failure reporting. Define the supported
   Windows filesystem and resource boundary from those results.
6. **Add package-backed Windows proof CI.** Build or download the same exact
   three-package graph, then run a real proven/refuted/unknown consumer on
   `windows-latest`, including timeout and cancellation cases. Run the
   existing Linux container verifier job in parallel. Only then update the
   support docs to claim native Windows verification.

Native repository builds on Windows are a separate follow-on: the repository
currently requires its canonical container for restore, build, tests, and
packaging. The Windows proof milestone can use packages produced in that
container and consumed by a native Windows project.

## Acceptance gate

Windows support is complete only when an exact packed release candidate runs
full verification natively on Windows x64, produces and validates structured
claim evidence, refuses missing or modified Z3, meets the file/process cases
above, and passes its Windows CI job while the canonical Linux verifier and
portable consumer jobs still pass.
