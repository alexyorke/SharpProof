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

The lock also embeds 100 source files needed to compile the pinned bundle; 87 contain selected declarations. Included dependency files do not count as extra selected methods.

[oss-methods.json](oss-methods.json) stores upstream source, file hashes, method hashes, source paths/line ranges, expected verdicts, and independent support classifications. The copied [MIT notice](third-party/aalhour-C-Sharp-Algorithms-LICENSE.txt) is checked in.

The importer requires a clean upstream checkout with the expected origin. Selection is round-robin across source files. Gate validation checks distinct locations/hashes, full commit provenance, license identity, a 200-500 method range, and at least 25 source files.

## Instrumentation and observations

Source text is pinned with normalized LF line endings. At test time the runner adds `[SharpProof.Attributes.EnforcePure]` to selected declarations without rewriting bodies, signatures, or dependencies, then compiles the pinned bundle together.

Portable diagnostics and native semantic outcomes are observed separately. Native verification consumes the compiler-produced artifact and runs the worker. Unsupported targets remain explicit; silence is not proof.

The [ratchet](unknown-reason-ratchet.json) requires at least 243 supported cases overall and 17 supported OSS methods, with at most 219 total Unknown and independent reason-bucket caps. Support labels are reviewed evidence, not derived from the expected verdict.

### Reviewed exceptional-resource correction (BUG-007)

`OSS0015.baseline` (`FindGCDEuclidean`) was previously classified Supported/Proven. Its unchanged upstream body calls `Math.Abs(a)` and `Math.Abs(b)` before checking either input. For `a == int.MinValue`, runtime exception-message lookup invokes the ambient culture's virtual `Name` getter, which can mutate observable state or throw another exception. The previous unrestricted purity claim was therefore unsound.

Only this declaration is reclassified IntentionallyUnsupported/Unknown until exceptional resource callbacks have an exact purity model. Its source file hash `fd34466c9843b6b88c3f73cb71a18f6a2e9236393d4ceafaf1f1b3c8afce7f0f`, declaration hash `905e65ea85f97ec8985ab0a655079eb25464267fb4e8fc6e05027eec3ddec554`, signature, and body remain unchanged. `ResourceCallbackCorrectionRetainsPinnedGreatestCommonDivisorSource` executes that exact pinned source with a culture callback; `ExceptionalFrameworkCultureTests` also covers resource callback effects and exception substitution.

The reviewed correction changes the supported-case floor 244→243, supported-OSS floor 18→17, total-Unknown ceiling 218→219, and SP0002 ceiling 121→122. All other classifications, reason buckets, snapshot entries, and support rules remain unchanged. This corrects one invalid historical proof; it is not a general allowance for precision regressions.

## Reproduce or update

Use a persistent canonical container workspace so edits survive. Inside its shell, clone and pin the upstream checkout, then invoke the importer:

```text
git clone https://github.com/aalhour/C-Sharp-Algorithms /tmp/sharpproof-upstream
git -C /tmp/sharpproof-upstream checkout b82432474a916ac784cd1446eabcba615c333463
pwsh -NoLogo -NoProfile -File tests/SharpProof.Gates/Corpus/Import-OssCorpus.ps1 -UpstreamRoot /tmp/sharpproof-upstream
```

The target clone path must be unused. The commands above run inside the canonical container, not a host PowerShell session.

Review regenerated provenance, copied license, semantic expectations, and canonical snapshot. The importer preserves support by declaration hash and rejects unclassified new methods. Changing upstream commit requires explicit review and an updated pin here.

For observation-only updates, run `sp corpus-update -Configuration Release` without the importer. It does not silently replace the upstream source lock. See the [gate guide](../README.md) for classifications and invariance checks.
