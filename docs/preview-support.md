# Preview support boundary

SharpProof 1.0.0-preview is a bounded verifier for a local, trusted Windows
build host. This document is the normative host and filesystem qualification
boundary for the preview.

## Supported

- Windows x64 `dotnet build` and command-line MSBuild using the pinned SDK.
- Visual Studio 2022 Build Tools 17.14 x64 MSBuild with Roslyn 4.14 or newer.
- The packaged analyzer plus bounded out-of-process worker, cache, and SARIF.
- Local request, result, manifest, cache, and SARIF paths, including spaces,
  percent characters, and paths longer than 260 characters.
- Project directories up to 239 characters. Longer project directories fail
  before compiler launch because supported Windows compiler hosts cannot
  reliably launch child tools from a longer working directory.
- Cooperative concurrent builds when publication sets are disjoint or exactly
  equal. Request, result, manifest, and optional SARIF paths are locked as one
  canonical set; partial overlap is rejected.
- A direct trusted `dotnet.exe` muxer. Forwarding wrapper hosts are rejected.

## Trusted-host assumptions

The user account, build process, project directory, package cache, and local
filesystem namespace are trusted while a build is running. SharpProof rejects
device paths, alternate data streams, reparse traversal, protected-file
aliases, and UNC publication, but it is not a defense against a malicious
local process that repeatedly swaps directories, junctions, or files during a
publication transaction.

The package payload is unsigned. Its release trust boundary is the exact
package hash, embedded payload SHA-256, assembly name/version identity, and
the repository's tested-byte promotion evidence. A public-key token is not an
authenticity claim.

## Unsupported in this preview

- Rider and other IDE hosts not explicitly qualified above.
- Windows ARM64 and non-Windows worker execution.
- UNC, mapped-network, or cross-host publication.
- Hostile concurrent local filesystem mutation.
- Arbitrary `SharpProofDotNetHost` wrappers.
- Loops in worker verification, mutable-heap reasoning, and general
  source-callee assume/guarantee verification.

These are roadmap items, not ambiguous supported-surface defects. Portable
analyzer use on the separately tested consumer matrix remains distinct from
worker execution.
