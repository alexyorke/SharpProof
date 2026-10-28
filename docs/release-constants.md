# Release constants

Use the owning files below when changing compatibility or packaging. Do not copy stale numbers from documentation.

| Value | Current value | Owner |
| --- | --- | --- |
| Package version | `1.0.0-preview.1` | [eng/Versions.props](../eng/Versions.props) |
| Assembly version | `1.0.0.0` | [eng/Versions.props](../eng/Versions.props) |
| Worker protocol | `45` | [ProtocolModel.cs](../src/SharpProof.Worker.Protocol/ProtocolModel.cs) |
| Worker manifest schema | `6` | Same protocol model |
| Response cache schema | `68` | Same protocol model |
| Compiler artifact schema | `56` | [CompilerArtifactModel.cs](../src/SharpProof.CompilerArtifact/CompilerArtifactModel.cs) |
| Relational summary schema | `2` | Same compiler artifact model |
| Specification pack schema | `1` | Same compiler artifact model |
| Specification pack catalog version | `2` | Same compiler artifact model |
| Default API table version | `11` | [DefaultApiSpecCatalog.cs](../src/SharpProof.Specs/DefaultApiSpecCatalog.cs) |
| Canonical container platform | `linux/amd64` | [toolchain.json](../eng/container/toolchain.json) and [compose.yaml](../compose.yaml) |

## Coordinated changes

A semantic or wire-shape change needs matching producer, consumer, validation, tests, and version updates. Cache identity must invalidate evidence that is no longer compatible. The compiler artifact is not the same schema as the worker request's claim manifest.

Declarative mappings are ordinary hand-maintained C#. Keep them and their validators and tests synchronized.

Worker defaults are mirrored between protocol and verifier props; see [analysis limits](analysis-limits.md). Pinned toolchain hashes belong in `toolchain.json`, not duplicated prose.

The acceptance contract also mirrors protocol, artifact, summary, pack, cache, and container versions. Update its matching entries with their owning declarations, retaining the compatibility checks that validate agreement. Source pins and package layout are separate from these wire versions; see [native SMT packaging](native-smt-packaging.md).

## Qualification and publication

Corpus support is independently ratcheted. Updating a snapshot or schema cannot silently relax those criteria. Public CI uses distinct `0.0.0-ci.<run>.<attempt>` validation versions; [release.yml](../.github/workflows/release.yml) publishes tagged versions as described in the [release process](../eng/release/README.md).
