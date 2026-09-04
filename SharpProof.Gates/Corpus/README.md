# Open-source analyzer corpus

The release corpus contains 200 methods copied from a real, buildable
open-source C# library. These are separate from the small synthetic suite used
for metamorphic invariance checks; transformed synthetic cases never count
toward the 200-method release floor.

## Pinned source and license

- Repository: `https://github.com/aalhour/C-Sharp-Algorithms`
- Commit: `b82432474a916ac784cd1446eabcba615c333463`
- License: MIT
- Included source roots: `Algorithms/` and `DataStructures/`

`oss-methods.json` contains the exact upstream text of every C# file needed to
compile those two projects together. Each file has a SHA-256 hash. Each selected
method records its upstream path, one-based line range, name, a SHA-256 hash of
the declaration, expected verdict, and explicit reviewed support
classification. Support is not derived from the expected verdict. The importer
selects 200 distinct declarations round-robin across source files; the
checked-in selection currently spans 87 files. The gate requires 200-500
methods, at least 25 source files, unique source locations, unique declaration
hashes, a full Git commit, and a matching checked-in license hash.

The source bundle is intentionally plain JSON rather than a binary archive so
reviewers can inspect and diff the vendored code. It is under 1 MiB. The
upstream MIT notice is preserved in
`third-party/aalhour-C-Sharp-Algorithms-LICENSE.txt`.

## Analyzer instrumentation

The stored source is byte-for-byte stable after line endings are normalized to
LF. At test time the runner adds only
`[SharpProof.Attributes.EnforcePure]` to each selected declaration. It does not
rewrite the method body, signature, containing type, or dependencies. All 200
targets are analyzed in one compilation of the pinned upstream source.

The snapshot records each target's internal semantic outcome independently
from its canonical diagnostics. Because corpus targets are explicitly selected
with `[EnforcePure]`, unsupported methods can now carry SP0047 while remaining
explicit `Abstained` semantic entries; they are never omitted or counted as
proofs. The separate silent-Unknown metric still covers unannotated/internal
cases. Gate output reports explicit Unknown, silent Unknown, and their combined
semantic Unknown count and rate. The checked-in ratchet requires at least 163
supported cases overall and one supported OSS method, while capping total and
per-reason Unknown counts. These starting floors expose the current narrow OSS
coverage and can only move upward as support expands.

Corpus compilation uses the current advisory profile with effect features.
Strict worker claim-accountability is covered by worker/package integration
tests rather than by this analyzer-only source corpus.

## Reproducible update

The importer accepts only a clean checkout whose `origin` is the repository
above. To reproduce the current source lock:

```powershell
git clone https://github.com/aalhour/C-Sharp-Algorithms `
    C:\work\C-Sharp-Algorithms
git -C C:\work\C-Sharp-Algorithms checkout `
    b82432474a916ac784cd1446eabcba615c333463
docker compose run --rm `
    -v C:\work\C-Sharp-Algorithms:/upstream:ro tooling `
    pwsh SharpProof.Gates/Corpus/Import-OssCorpus.ps1 `
    -UpstreamRoot /upstream
```

Run the importer through the canonical Linux tooling container; the upstream
checkout is mounted read-only at `/upstream` so the generated files and
validation use the same environment as the release gates.

The command regenerates the source/provenance manifest, copied license, reviewed
semantic expectations, and canonical analyzer snapshot using LF without a BOM.
Updating to another upstream commit is deliberate: check out that commit, run
the same importer, review the manifest/snapshot diff, explicitly classify every
new declaration whose support is `Unspecified`, and update this document's pin.
The importer preserves support by declaration hash and refuses to complete
while any new method remains unclassified. A normal `corpus-update` without the
importer updates only observations; it does not silently replace the upstream
source lock.
