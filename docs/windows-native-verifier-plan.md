# Native Windows verifier

The packaged verifier supports native Windows x64 with Core MSBuild and the
.NET SDK 10.0.100 or later (including the .NET 10 runtime) and the Microsoft Visual C++ v14 x64 runtime.
The canonical Linux amd64 container remains supported.
Repository builds, tests, and packaging still use that container; native Windows
projects consume its exact NuGet artifacts.

## Run a proof on Windows

Reference `SharpProof.Verifier` at the current package version and enable
verification in the consuming SDK project:

```xml
<PropertyGroup>
  <SharpProofProfile>advisory</SharpProofProfile>
  <SharpProofVerify>true</SharpProofVerify>
  <SharpProofVerifyPolicy>require-proven</SharpProofVerifyPolicy>
</PropertyGroup>
```

Run `dotnet build` from a native Windows x64 .NET installation. No container
marker, separately installed Z3, PATH changes, or native-root environment override
is required. The worker uses the common packaged managed Z3 binding and the
Windows DLL in `tools/native/win-x64/libz3.dll`. Its length and SHA-256 are checked
against the embedded toolchain before loading. Missing or modified DLLs fail.
The pinned DLL imports `MSVCP140.dll`, `VCRUNTIME140.dll`, and
`VCRUNTIME140_1.dll`; install the Microsoft Visual C++ v14 x64 runtime if those
system dependencies are absent. Dependencies are resolved beside the pinned DLL
and in System32.

The default structured evidence is under
`obj/<configuration>/<target-framework>/SharpProof/result.json`; it includes
claim outcomes, proof cores or counterexample models, and input/request hashes.
`require-proven` fails the build for refuted, unknown, or incomplete claims.

## Qualified boundary and CI

Use ordinary paths on fixed local NTFS volumes. Spaces and Unicode are covered
by the native consumer. Case aliases share publication identity and lock names.
Reparse points (including junctions), UNC/device paths, alternate data streams,
short-name aliases, and ambiguous trailing dots/spaces are rejected. Paths with
`~` are conservatively rejected to exclude short-name aliases. Do not use mapped
drives or shared/network package caches and output directories.

The source, package cache, SDK installation, and filesystem namespace are trusted
throughout the build. The DLL stays open without write/delete sharing while it
is verified and loaded. Hostile concurrent host mutation is outside this boundary.

Windows uses the existing solver query/method limits, project wall deadline,
cancellation, and process-tree termination. Native runs do not have Docker's
CPU/memory isolation or an OS memory quota. The caller must provision sufficient
resources or supply an external execution boundary for untrusted workloads.

Every PR runs `scripts/Test-SharpProofWindowsProofs.ps1` against packages built
by the canonical container. It checks actual proven/refuted/unknown results,
structured claim/input identity, stale-evidence rejection after source changes,
project deadline failure, Windows path guards, and missing/modified DLL rejection.
The canonical Linux proof consumer and portable Linux/Windows/macOS consumers run
alongside it. Native Windows repository build/pack support and ARM64 qualification
remain separate work.
