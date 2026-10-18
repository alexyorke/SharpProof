# Preview support boundary

This document defines the current verifier's qualified host/filesystem boundary. Language limits are described separately in [coverage and limits](coverage-and-limits.md).

## Compatibility matrix

| Workflow | Qualified environment | Boundary/evidence |
| --- | --- | --- |
| Attributes API | netstandard2.0-compatible application targets | Application target is separate from compiler SDK |
| Older application targets | SDK-style Framework 4.6.1/4.6.2/4.7.2/4.8/4.8.1, Standard 2.0, Core 2.0/3.1, .NET 5-10 | Actual default proof/refutation tests on a modern build host; see [legacy compatibility and qualification limits](legacy-compatibility.md) |
| Explicit advisory-only analyzer consumers | Linux x64, Windows x64, macOS ARM64/x64; SDK 9.0.300+ / Roslyn 4.14+ | Separate portable consumer matrix in [package CI](../.github/workflows/package-consumers.yml); the default full verifier still requires SDK 10 |
| Full verification | Canonical Linux amd64 container | Pinned .NET 10 SDK/runtime, payload, Docker resource isolation |
| Full verification | Native Windows x64/ARM64, fixed local NTFS; SDK 10.0.100+ | .NET 10 runtime; architecture-matched solver with static C++ runtime; native package worker CI and restrictions below |
| Full verification | Native macOS ARM64/x64; SDK 10.0.100+ | .NET 10 runtime; architecture-matched packaged solver; native package consumer CI |
| Full verification | Native Linux x64/ARM64; SDK 10.0.100+ | .NET 10 runtime and supported distribution prerequisites; solver payload requires glibc 2.31+ on x64 or glibc 2.17+ on ARM64, plus compatible native runtime dependencies |
| Visual Studio command-line build host | Windows x64 full-framework MSBuild 18+ with .NET 10 SDK/runtime | Verifier tasks use the out-of-process .NET task host; require a successful completed exact-artifact MSBuild.exe proof/refutation/downstream/clean qualification run |
| Older Visual Studio command-line build host | MSBuild 17.14 with SDK 9.0.300 and Roslyn 4.14 | Explicit advisory-only operation; default strict verification is not supported on this host |
| Repository build/test/pack/acceptance | Canonical Linux amd64 container only | Includes repository PowerShell commands |
| IDE typing/navigation | Portable diagnostics and design-time configuration | No separate IDE/version qualification promise; design-time builds do not run proofs |
| musl Linux, other OS/architecture combinations, older full-framework MSBuild verifier hosts | Unqualified | No universal OS/architecture guarantee; a successful portable build does not extend qualification |

The consumer matrix is maintained in [package CI](../.github/workflows/package-consumers.yml). Compiler-host minimums are checked separately from the current repository SDK in [global.json](../global.json). Installing a newer SDK does not retarget an application's framework. IDE support also depends on its embedded Roslyn host; installing a command-line SDK alone does not establish that host's compatibility.

[Legacy compatibility](legacy-compatibility.md) separates application targets and deployment operating systems from build hosts. SDK-style targets from Framework 4.6.1 through modern .NET have package proof/refutation checks on a modern host. Windows `net48` downstream execution checks CLR 4 on the current hosted image; it does not qualify original Framework installations or old Windows VMs. The x64 MSBuild 18+ bridge and MSBuild 17.14 advisory-only matrix cover command-line hosts. Review successful completed exact-artifact CI runs for the source SHA and host version; job definitions alone are not outcomes. Full-framework ARM64 task hosting is implemented but is not separately qualified by these x64 Visual Studio jobs; native ARM64 Core MSBuild coverage is distinct. Interactive IDE behavior, classic non-SDK projects, `packages.config`, and Visual Studio 2019 or older remain unqualified.

The standard `SharpProof` reference automatically enables strict full verification on a supported host; no activation properties or separate API/analyzer references are required. `SharpProof.Verifier` remains a compatibility entrypoint. Package defaults cannot install a compatible compiler/IDE host, supply an unsupported OS ABI, or remove local filesystem requirements. An unavailable verifier must report a failure rather than silently treating portable diagnostics as full proof.

## Supported

- Docker Engine or Docker Desktop with Compose v2, running the pinned Linux amd64 container.
- Core MSBuild and `dotnet build` inside that canonical container.
- Native Linux x64 or ARM64 Core MSBuild and `dotnet build` on a .NET 10-supported glibc distribution with SDK 10.0.100+ and the native runtime dependencies below.
- Native Windows x64 or ARM64 Core MSBuild and `dotnet build` with SDK 10.0.100 or later, consuming the architecture-matched packaged verifier on fixed local NTFS volumes. See [native Windows verification](windows-native-verifier-plan.md).
- Native Core MSBuild and `dotnet build` on macOS ARM64 or x64, using the architecture-matched pinned Z3 payload in the verifier package.
- Packaged portable analyzer and bounded out-of-process verifier, cache, and optional SARIF.
- Local publication paths, including spaces, percent characters, Unicode, and long paths.
- Cooperative concurrent builds with disjoint or exactly equal publication sets. Partial overlap is rejected.
- Independent clones and Compose projects without cross-host sharing of build outputs or qualification evidence.

Explicit portable-only advisory configurations have separate cross-platform tests. Native Windows proof CI also runs the real packaged worker and validates its claim evidence. Native macOS verifier consumers use those same exact artifacts. Native Linux x64/ARM64 consumer CI restores the exact packed artifacts through the single SharpProof reference and checks proven/refuted claims and a packed library's downstream contract dependency. Review the actual source SHA and completed consumer job evidence when qualifying a release; the matrix describes the tested workflow, not the outcome of any particular CI run.

The Linux x64 solver payload requires glibc 2.31+. The ARM64 solver payload requires glibc 2.17+ and C++ runtime symbols through GLIBCXX_3.4.19, with the architecture-matched system loader and standard C/C++ runtime libraries available. These are native payload baselines, not a promise that an old distribution supports .NET 10: the complete host must also satisfy .NET 10's supported OS and runtime dependencies. musl distributions are unsupported. Windows x64 and ARM64 solver payloads link their C++ runtime statically and require no separate Visual C++ redistributable installation; compatible Windows and .NET 10 host prerequisites still apply. macOS uses its architecture-matched packaged library.

## Trusted host assumptions

The canonical image, build process, source workspace, package cache, and local filesystem namespace are trusted while a build runs.

Publication validates path ownership and identity, symlink/alias restrictions, regular files, and local filesystem capabilities. It locks the canonical request/result/manifest/optional SARIF set and uses atomic publication. Recognized network filesystems and unsupported locking/rename behavior are rejected.

Docker supplies CPU and memory isolation for Linux container verification. Native Linux and macOS verification use worker protocol budgets and launcher wall-clock enforcement; they do not have Docker's outer CPU and memory isolation. The trusted native caller must provision adequate resources or provide external isolation.

Native Windows uses the same solver and wall-clock budgets and process-tree termination, but has no Docker CPU/memory boundary or built-in OS memory quota. Its trusted caller must provision resources or provide external isolation. Windows publication rejects reparse points, UNC/device paths, alternate streams, short-name aliases, and ambiguous paths; case aliases share identity and locks.

Hostile concurrent host mutation after validation is outside this preview threat model: for example, swapping a bind mount or directory during publication. This is not a sandbox for arbitrary malicious host processes.

Attributes is fully strong-named with a deliberately public, stable open-source identity key for Framework compatibility. This is not publisher authentication or proof authority; exact assembly/payload admission and pinned inputs remain required. Public-release workflow provenance and SPDX attestations have separate subject/workflow/source checks in the [release process](../eng/release/README.md). Packages are not author-signed merely because the assembly has a strong name or the workflow uses OIDC; NuGet repository signatures and original GitHub release-asset digests are separate verification domains.

## Unsupported hosts and workflows

- Native Windows/Linux/macOS repository build/test/pack workflows outside the canonical container.
- Default verification in full-framework MSBuild older than 18, or a separate interactive IDE integration guarantee. MSBuild 18+ uses the .NET task-host path described above and requires its own exact-artifact qualification.
- Classic non-SDK projects, `packages.config`, Visual Studio 2019 or older, and local verifier execution on Windows XP/7/8/8.1. Modern-host builds targeting an older framework do not establish old-OS deployment execution.
- musl Linux and native OS/architecture combinations beyond Windows, macOS, and glibc Linux on x64/ARM64.
- UNC, NFS, CIFS/SMB, SSHFS, mapped-network, or cross-host publication.
- Hostile concurrent mutation of the trusted host filesystem.

Do not interpret a successful portable analyzer run as full verifier qualification. Check the actual worker run, claim records, and package-consumer evidence.
