# Preview support boundary

This document defines the current verifier's qualified host/filesystem boundary. Language limits are described separately in [coverage and limits](coverage-and-limits.md).

## Supported

- Docker Engine or Docker Desktop with Compose v2, running the pinned Linux amd64 container.
- Core MSBuild and `dotnet build` inside that canonical container.
- Packaged portable analyzer and bounded out-of-process verifier, cache, and optional SARIF.
- Local publication paths, including spaces, percent characters, Unicode, and long paths.
- Cooperative concurrent builds with disjoint or exactly equal publication sets. Partial overlap is rejected.
- Independent clones and Compose projects without cross-host sharing of build outputs or qualification evidence.

The portable Attributes/analyzer package consumer surface has separate cross-platform tests. Full native verification remains container-only.

## Trusted-container assumptions

The canonical image, build process, source workspace, package cache, and local filesystem namespace are trusted while a build runs.

Publication validates path ownership and identity, symlink/alias restrictions, regular files, and local filesystem capabilities. It locks the canonical request/result/manifest/optional SARIF set and uses atomic publication. Recognized network filesystems and unsupported locking/rename behavior are rejected.

Docker supplies CPU and memory isolation. Worker protocol budgets and launcher wall-clock enforcement operate within that boundary.

Hostile concurrent host mutation after validation is outside this preview threat model: for example, swapping a bind mount or directory during publication. This is not a sandbox for arbitrary malicious host processes.

The package payload is unsigned. Exact package/assembly identity, payload evidence, and pinned inputs establish the implemented compatibility boundary; no public-key authenticity claim is made.

## Unsupported hosts and workflows

- Native verifier execution on Windows, macOS, or a directly installed Linux toolchain.
- Full-framework MSBuild verifier execution or a separate IDE integration guarantee.
- ARM64 verifier qualification.
- UNC, NFS, CIFS/SMB, SSHFS, mapped-network, or cross-host publication.
- Hostile concurrent mutation of the trusted host filesystem.

Do not interpret a successful portable analyzer run as full verifier qualification. Check the actual worker run, claim records, and package-consumer evidence.
