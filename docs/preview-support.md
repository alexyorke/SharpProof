# Preview support boundary

SharpProof 1.0.0-preview is a bounded verifier for the canonical SharpProof
Linux amd64 container. This document is the normative host and filesystem
qualification boundary for the preview.

## Supported

- Docker Engine or Docker Desktop with Compose v2 on the host.
- The repository's pinned `linux/amd64` image and container contract.
- `dotnet build` and Core MSBuild inside that container.
- The packaged analyzer plus bounded out-of-process worker, cache, and SARIF.
- Local request, result, manifest, cache, and SARIF paths, including spaces,
  percent characters, Unicode, and long paths.
- Cooperative concurrent builds when publication sets are disjoint or exactly
  equal. Request, result, manifest, and optional SARIF paths are locked as one
  canonical set; partial overlap is rejected.
- Independent local clones and Compose projects on multiple computers. No
  build output, cache, volume, or release evidence is shared between them.

The portable `SharpProof.Attributes` and `SharpProof` analyzer packages retain
their separately tested cross-platform consumer surface. Only full verifier
execution is container-only.

## Trusted-container assumptions

The canonical container, build process, bind-mounted worktree, package cache,
and local filesystem namespace are trusted while a build is running.
SharpProof rejects symlink traversal, non-regular publication targets,
protected-file aliases, unowned existing outputs, and recognized network
filesystems. It probes the active workspace for local locking, atomic rename,
durable file creation, and stable file identity before publication.

Docker supplies the hard CPU and memory boundary. SharpProof does not inspect
cgroups or duplicate Docker's resource controller. Compose CPU and memory
limits are ordinary operator-configurable outer limits; verifier wall-clock
and semantic budgets retain their protocol-defined behavior.

Hostile concurrent mutation of paths after validation is outside this preview
threat model. This includes a trusted host process deliberately swapping bind
mounts, directories, symlinks, or files during a publication transaction.

The package payload is unsigned. Its release trust boundary is the exact
package and assembly name/version identity, pinned container inputs, semantic
payload evidence, and the repository's tested-byte promotion evidence. A
public-key token is not an authenticity claim.

## Unsupported in this preview

- Native verifier execution on Windows, macOS, or a directly installed Linux
  toolchain.
- Visual Studio/full-framework MSBuild verifier execution and Rider
  integration.
- ARM64 verifier containers, including emulation as qualification evidence.
- UNC, NFS, CIFS/SMB, SSHFS, mapped-network, or cross-host publication.
- Hostile concurrent host filesystem mutation.
- Loops or recursion in worker verification, mutable-heap reasoning, virtual
  dispatch, and general source-callee verification beyond the direct acyclic
  scalar relational-summary boundary.

These are explicit roadmap items, not ambiguous supported-surface defects.
