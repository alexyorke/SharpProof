# Open-source corpus provenance

The source corpus includes 200 selected methods from a real buildable library, separate from synthetic metamorphic cases. Transformed seeds do not count toward the OSS floor.

## Pinned source and license

| Field | Value |
| --- | --- |
| Repository identity | `https://github.com/aalhour/C-Sharp-Algorithms` |
| Commit | `b82432474a916ac784cd1446eabcba615c333463` |
| License | MIT |
| Included roots | `Algorithms/`, `DataStructures/` |
| Selected declarations | 200 across 87 source files |

[oss-methods.json](oss-methods.json) stores upstream source, file hashes, method hashes, source paths/line ranges, expected verdicts, and independent support classifications. The copied [MIT notice](third-party/aalhour-C-Sharp-Algorithms-LICENSE.txt) is checked in.

The importer requires a clean upstream checkout with the expected origin. Selection is round-robin across source files. Gate validation checks distinct locations/hashes, full commit provenance, license identity, a 200-500 method range, and at least 25 source files.

## Instrumentation and observations

Source text is pinned with normalized LF line endings. At test time the runner adds `[SharpProof.Attributes.EnforcePure]` to selected declarations without rewriting bodies, signatures, or dependencies, then compiles the pinned bundle together.

Portable diagnostics and native semantic outcomes are observed separately. Native verification consumes the compiler-produced artifact and runs the worker. Unsupported targets remain explicit; silence is not proof.

The [ratchet](unknown-reason-ratchet.json) requires at least 240 supported cases overall and 14 supported OSS methods, with at most 222 total Unknown and independent reason-bucket caps. Support labels are reviewed evidence, not derived from the expected verdict.

## Reproduce or update

Use a persistent canonical container workspace so edits survive. Inside its shell, clone and pin the upstream checkout, then invoke the importer:

```text
git clone https://github.com/aalhour/C-Sharp-Algorithms /tmp/sharpproof-upstream
git -C /tmp/sharpproof-upstream checkout b82432474a916ac784cd1446eabcba615c333463
pwsh -NoLogo -NoProfile -File SharpProof.Gates/Corpus/Import-OssCorpus.ps1 -UpstreamRoot /tmp/sharpproof-upstream
```

The target clone path must be unused. The commands above run inside the canonical container, not a host PowerShell session.

Review regenerated provenance, copied license, semantic expectations, and canonical snapshot. The importer preserves support by declaration hash and rejects unclassified new methods. Changing upstream commit requires explicit review and an updated pin here.

For observation-only updates, run `sp corpus-update -Configuration Release` without the importer. It does not silently replace the upstream source lock. See the [gate guide](../README.md) for classifications and invariance checks.
