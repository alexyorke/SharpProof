# Release constants

Use the owning files below when changing compatibility or packaging. Do not copy stale numbers from documentation.

| Value | Current value | Owner |
| --- | --- | --- |
| Package version | `1.0.0-preview.1` | [eng/Versions.props](../eng/Versions.props) |
| Assembly version | `1.0.0.0` | [eng/Versions.props](../eng/Versions.props) |
| Worker protocol | `39` | [ProtocolModel.generated.cs](../src/SharpProof.Worker.Protocol/ProtocolModel.generated.cs) |
| Worker manifest schema | `6` | Same protocol model |
| Response cache schema | `47` | Same protocol model |
| Compiler artifact schema | `52` | [CompilerArtifactModel.generated.cs](../src/SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs) |
| Relational summary schema | `2` | Same compiler artifact model |
| Specification pack schema | `1` | Same compiler artifact model |
| Specification pack catalog version | `2` | Same compiler artifact model |
| Default API table version | `6` | [DefaultApiSpecCatalog.generated.cs](../src/SharpProof.Specs/DefaultApiSpecCatalog.generated.cs) |
| Canonical container platform | `linux/amd64` | [toolchain.json](../eng/container/toolchain.json) and [compose.yaml](../compose.yaml) |

## Coordinated changes

A semantic or wire-shape change needs matching producer, consumer, validation, tests, and version updates. Cache identity must invalidate evidence that is no longer compatible. The compiler artifact is not the same schema as the worker request's claim manifest.

Files whose header says hand-maintained remain hand-maintained despite a `.generated.cs` suffix. Keep declarative mappings and their validators/tests synchronized; do not invoke a retired generator.

Worker defaults are mirrored between protocol and verifier props; see [analysis limits](analysis-limits.md). Pinned toolchain hashes belong in `toolchain.json`, not duplicated prose.

The acceptance contract also mirrors protocol, artifact, summary, pack, cache, and container versions. Update its matching entries with their owning declarations, retaining the compatibility checks that validate agreement. Source pins and package layout are separate from these wire versions; see [native SMT packaging](native-smt-packaging.md).

## Qualification and publication

Trusted pipeline classifications belong to [contract.json](../eng/acceptance/contract.json); numeric coverage floors belong to [baseline.json](../eng/coverage/baseline.json), with corpus support independently ratcheted. The coverage universe comes from evaluated solution/MSBuild inventory. Updating a snapshot or schema cannot silently relax those criteria.

The [release workflow](../.github/workflows/package-consumers.yml) checks tag/version agreement, packed package consumers, and tag-only security/portable checks before publication. See [release process](../eng/release/README.md) for feed routing. Passing branch CI does not establish that tag-only checks ran or that packages were published.
