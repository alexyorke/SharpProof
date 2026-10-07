# Release constants

Use the owning files below when changing compatibility or packaging. Do not copy stale numbers from documentation.

| Value | Current value | Owner |
| --- | --- | --- |
| Package version | `1.0.0-preview.1` | [SharpProof.Release.props](../SharpProof.Release.props) |
| Assembly version | `1.0.0.0` | [SharpProof.Release.props](../SharpProof.Release.props) |
| Worker protocol | `13` | [ProtocolModel.generated.cs](../SharpProof.Worker.Protocol/ProtocolModel.generated.cs) |
| Worker manifest schema | `5` | Same protocol model |
| Response cache schema | `15` | Same protocol model |
| Compiler artifact schema | `31` | [CompilerArtifactModel.generated.cs](../SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs) |
| Relational summary schema | `2` | Same compiler artifact model |
| Specification pack schema | `1` | Same compiler artifact model |
| Specification pack catalog version | `2` | Same compiler artifact model |
| Default API table version | `6` | [DefaultApiSpecCatalog.generated.cs](../SharpProof.Specs/DefaultApiSpecCatalog.generated.cs) |
| Canonical container platform | `linux/amd64` | [toolchain.json](../eng/container/toolchain.json) and [compose.yaml](../compose.yaml) |

## Coordinated changes

A semantic or wire-shape change needs matching producer, consumer, validation, tests, and version updates. Cache identity must invalidate evidence that is no longer compatible. The compiler artifact is not the same schema as the worker request's claim manifest.

Files whose header says hand-maintained remain hand-maintained despite a `.generated.cs` suffix. Keep declarative mappings and their validators/tests synchronized; do not invoke a retired generator.

Worker defaults are mirrored between protocol and verifier props; see [analysis limits](analysis-limits.md). Pinned toolchain hashes belong in `toolchain.json`, not duplicated prose.

## Qualification and publication

Acceptance inventories and numeric floors belong to [contract.json](../eng/acceptance/contract.json), with corpus support independently ratcheted. Updating a snapshot or schema cannot silently relax those criteria.

The [release workflow](../.github/workflows/package-consumers.yml) checks tag/version agreement, packed package consumers, and tag-only security/portable checks before publication. See [release process](../eng/release/README.md) for feed routing. Passing branch CI does not establish that tag-only checks ran or that packages were published.
