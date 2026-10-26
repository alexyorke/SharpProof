# Native Windows verifier

The packaged verifier supports native Windows x64 and ARM64 with Core MSBuild and
.NET SDK 10.0.100 or later, including the .NET 10 runtime. The architecture-matched
Z3 payload links the C++ runtime statically; consumers do not install a separate
Visual C++ redistributable.
The canonical Linux amd64 container remains supported.
Repository builds, tests, and packaging still use that container; native Windows
projects consume its exact NuGet artifacts.

## Run a proof on Windows

Add the single standard reference to the consuming SDK project. Full strict verification is automatic; no SharpProof activation properties or separate API/analyzer references are required:

```xml
<ItemGroup>
  <PackageReference Include="SharpProof" Version="1.0.0-preview.1" />
</ItemGroup>
```

Use the package version from your configured feed and keep the standard reference without `PrivateAssets="all"` so transitive application contract dependencies propagate. The required SDK/runtime above remains a host prerequisite; adding a NuGet reference does not install it. Use matching native OS and .NET process architectures; emulation does not replace native ARM64 qualification.

Run `dotnet build` from a native Windows x64 or ARM64 .NET installation. No container
marker, separately installed Z3, PATH changes, or native-root environment override
is required. The worker uses the common packaged managed Z3 binding and the
Windows DLL in `tools/native/win-x64/libz3.dll` or `tools/native/win-arm64/libz3.dll`. Its length and SHA-256 are checked
against the embedded toolchain before loading. Missing or modified DLLs fail.
The pinned Windows payloads are compiled from source with static C++ runtime
linkage. Architecture/import audits check that they do not require the separate
MSVC runtime DLLs. Remaining Windows system dependencies resolve beside the pinned
DLL and in System32.

Earlier Windows payloads used the upstream x64 DLL with dynamic Visual C++ runtime
dependencies. The current x64/ARM64 payloads remove that installation step. This
build/import change is distinct from native ABI qualification: the exact packed
artifacts must still run the native proof and payload-integrity checks on both
architectures before a release is qualified.

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

Native Windows proofs are not exercised in CI; the canonical Linux container runs the
package tests.
Native Windows repository build/test/pack remains unsupported outside the canonical
Linux container.
