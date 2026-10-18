# Preview support boundary

This document defines the current verifier's qualified host/filesystem boundary. Language limits are described separately in [coverage and limits](coverage-and-limits.md).

## Compatibility matrix

| Workflow | Qualified environment | Boundary/evidence |
| --- | --- | --- |
| Attributes API | netstandard2.0-compatible application targets | Application target is separate from compiler SDK |
| Portable analyzer consumers | Linux x64, Windows x64, macOS ARM64/x64; SDK 9.0.300+ / Roslyn 4.14+ | Exact packed consumer matrix in [package CI](../.github/workflows/package-consumers.yml); no native solver |
| Full verification | Canonical Linux amd64 container | Pinned .NET 10 SDK/runtime, payload, Docker resource isolation |
| Full verification | Native Windows x64, fixed local NTFS; SDK 10.0.100+ | .NET 10 runtime; real package worker CI; restrictions and resource caveats below |
| Full verification | Native macOS ARM64/x64; SDK 10.0.100+ | .NET 10 runtime; architecture-matched packaged solver; native package consumer CI |
| Full verification | Native Linux x64/ARM64; SDK 10.0.100+ | .NET 10 runtime and supported distribution prerequisites; solver payload requires glibc 2.31+ on x64 or glibc 2.17+ on ARM64, plus compatible native runtime dependencies |
| Repository build/test/pack/acceptance | Canonical Linux amd64 container only | Includes repository PowerShell commands |
| IDE typing/navigation | Portable diagnostics and design-time configuration | No separate IDE/version qualification promise; design-time builds do not run proofs |
| Windows ARM64, musl Linux, full-framework MSBuild verifier | Unqualified | No universal OS/architecture guarantee; a successful portable build does not extend qualification |

The consumer matrix is maintained in [package CI](../.github/workflows/package-consumers.yml). Compiler-host minimums are checked separately from the current repository SDK in [global.json](../global.json). Installing a newer SDK does not retarget an application's framework. IDE support also depends on its embedded Roslyn host; installing a command-line SDK alone does not establish that host's compatibility.

The standard `SharpProof.Verifier` reference automatically enables strict full verification on a supported host; no activation properties or separate API/analyzer references are required. Package defaults cannot install a compatible compiler/IDE host, supply an unsupported OS ABI, or remove local filesystem requirements. An unavailable verifier must report a failure rather than silently treating portable diagnostics as full proof.

## Supported

- Docker Engine or Docker Desktop with Compose v2, running the pinned Linux amd64 container.
- Core MSBuild and `dotnet build` inside that canonical container.
- Native Linux x64 or ARM64 Core MSBuild and `dotnet build` on a .NET 10-supported glibc distribution with SDK 10.0.100+ and the native runtime dependencies below.
- Native Windows x64 Core MSBuild and `dotnet build` with SDK 10.0.100 or later, consuming the packaged verifier on fixed local NTFS volumes. See [native Windows verification](windows-native-verifier-plan.md).
- Native Core MSBuild and `dotnet build` on macOS ARM64 or x64, using the architecture-matched pinned Z3 payload in the verifier package.
- Packaged portable analyzer and bounded out-of-process verifier, cache, and optional SARIF.
- Local publication paths, including spaces, percent characters, Unicode, and long paths.
- Cooperative concurrent builds with disjoint or exactly equal publication sets. Partial overlap is rejected.
- Independent clones and Compose projects without cross-host sharing of build outputs or qualification evidence.

The portable Attributes/analyzer package consumer surface has separate cross-platform tests. Native Windows proof CI also runs the real packaged worker and validates its claim evidence. Native macOS verifier consumers use those same exact artifacts. Native Linux x64/ARM64 consumer CI restores the exact packed artifacts through the single Verifier reference and checks proven/refuted claims and a packed library's downstream contract dependency. Review the actual source SHA and completed consumer job evidence when qualifying a release; the matrix describes the tested workflow, not the outcome of any particular CI run.

The Linux x64 solver payload requires glibc 2.31+. The ARM64 solver payload requires glibc 2.17+ and C++ runtime symbols through GLIBCXX_3.4.19, with the architecture-matched system loader and standard C/C++ runtime libraries available. These are native payload baselines, not a promise that an old distribution supports .NET 10: the complete host must also satisfy .NET 10's supported OS and runtime dependencies. musl distributions are unsupported. Windows x64 requires the Microsoft Visual C++ v14 x64 runtime; macOS uses its architecture-matched packaged library.

## Trusted host assumptions

The canonical image, build process, source workspace, package cache, and local filesystem namespace are trusted while a build runs.

Publication validates path ownership and identity, symlink/alias restrictions, regular files, and local filesystem capabilities. It locks the canonical request/result/manifest/optional SARIF set and uses atomic publication. Recognized network filesystems and unsupported locking/rename behavior are rejected.

Docker supplies CPU and memory isolation for Linux container verification. Native Linux and macOS verification use worker protocol budgets and launcher wall-clock enforcement; they do not have Docker's outer CPU and memory isolation. The trusted native caller must provision adequate resources or provide external isolation.

Native Windows uses the same solver and wall-clock budgets and process-tree termination, but has no Docker CPU/memory boundary or built-in OS memory quota. Its trusted caller must provision resources or provide external isolation. Windows publication rejects reparse points, UNC/device paths, alternate streams, short-name aliases, and ambiguous paths; case aliases share identity and locks.

Hostile concurrent host mutation after validation is outside this preview threat model: for example, swapping a bind mount or directory during publication. This is not a sandbox for arbitrary malicious host processes.

The package payload is unsigned. Exact package/assembly identity, payload evidence, and pinned inputs establish the implemented compatibility boundary; no public-key authenticity claim is made.

## Unsupported hosts and workflows

- Native Windows/Linux/macOS repository build/test/pack workflows outside the canonical container.
- Full-framework MSBuild verifier execution or a separate IDE integration guarantee.
- Windows ARM64 and musl Linux verifier execution.
- UNC, NFS, CIFS/SMB, SSHFS, mapped-network, or cross-host publication.
- Hostile concurrent mutation of the trusted host filesystem.

Do not interpret a successful portable analyzer run as full verifier qualification. Check the actual worker run, claim records, and package-consumer evidence.
