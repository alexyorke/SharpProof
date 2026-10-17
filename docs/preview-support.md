# Preview support boundary

This document defines the current verifier's qualified host/filesystem boundary. Language limits are described separately in [coverage and limits](coverage-and-limits.md).

## Supported

- Docker Engine or Docker Desktop with Compose v2, running the pinned Linux amd64 container.
- Core MSBuild and `dotnet build` inside that canonical container.
- Native Windows x64 Core MSBuild and `dotnet build` with SDK 9.0.300 or later, consuming the packaged verifier on fixed local NTFS volumes. See [native Windows verification](windows-native-verifier-plan.md).
- Native Core MSBuild and `dotnet build` on macOS ARM64 or x64, using the architecture-matched pinned Z3 payload in the verifier package.
- Packaged portable analyzer and bounded out-of-process verifier, cache, and optional SARIF.
- Local publication paths, including spaces, percent characters, Unicode, and long paths.
- Cooperative concurrent builds with disjoint or exactly equal publication sets. Partial overlap is rejected.
- Independent clones and Compose projects without cross-host sharing of build outputs or qualification evidence.

The portable Attributes/analyzer package consumer surface has separate cross-platform tests. Native Windows proof CI also runs the real packaged worker and validates its claim evidence. Native macOS verifier consumers use those same exact artifacts.

## Trusted-container assumptions

The canonical image, build process, source workspace, package cache, and local filesystem namespace are trusted while a build runs.

Publication validates path ownership and identity, symlink/alias restrictions, regular files, and local filesystem capabilities. It locks the canonical request/result/manifest/optional SARIF set and uses atomic publication. Recognized network filesystems and unsupported locking/rename behavior are rejected.

Docker supplies CPU and memory isolation for Linux container verification. Native macOS verification uses worker protocol budgets and launcher wall-clock enforcement; it does not have Docker's outer CPU and memory isolation.

Native Windows uses the same solver and wall-clock budgets and process-tree termination, but has no Docker CPU/memory boundary or built-in OS memory quota. Its trusted caller must provision resources or provide external isolation. Windows publication rejects reparse points, UNC/device paths, alternate streams, short-name aliases, and ambiguous paths; case aliases share identity and locks.

Hostile concurrent host mutation after validation is outside this preview threat model: for example, swapping a bind mount or directory during publication. This is not a sandbox for arbitrary malicious host processes.

The package payload is unsigned. Exact package/assembly identity, payload evidence, and pinned inputs establish the implemented compatibility boundary; no public-key authenticity claim is made.

## Unsupported hosts and workflows

- Native verifier execution on a directly installed Linux toolchain; native Windows repository build/test/pack workflows.
- Full-framework MSBuild verifier execution or a separate IDE integration guarantee.
- Linux or Windows ARM64 verifier qualification.
- UNC, NFS, CIFS/SMB, SSHFS, mapped-network, or cross-host publication.
- Hostile concurrent mutation of the trusted host filesystem.

Do not interpret a successful portable analyzer run as full verifier qualification. Check the actual worker run, claim records, and package-consumer evidence.
