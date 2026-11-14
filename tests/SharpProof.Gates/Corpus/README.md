# Open-source corpus provenance

The corpus includes 500 selected methods from four real, buildable libraries, separate from synthetic metamorphic cases. Transformed seeds do not count toward the OSS floor. Measuring several unrelated codebases keeps the reported coverage honest: one algorithms library alone overstates how much ordinary code the verifier handles.

## Pinned sources and licenses

[sources.json](sources.json) is the reviewed importer configuration. Each entry pins a full commit and lists the license and notice files with the SHA-256 of their reviewed text, the source roots and excludes, a method quota, and the compile settings that change meaning (nullable context, checked arithmetic, global usings, preprocessor symbols).

| Source | Repository | Commit | License | Roots | Methods (files) | Supported | Proven / Refuted / Unknown |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `aalhour-c-sharp-algorithms` | `https://github.com/aalhour/C-Sharp-Algorithms` | `b82432474a916ac784cd1446eabcba615c333463` | MIT | `Algorithms/`, `DataStructures/` | 200 (87) | 18 | 9 / 9 / 182 |
| `morelinq` | `https://github.com/morelinq/MoreLINQ` | `d217ab1e8eac68247650c27e977b767dd66bcab0` | Apache-2.0 | `MoreLinq/` | 100 (100) | 4 | 3 / 1 / 96 |
| `nodatime` | `https://github.com/nodatime/nodatime` | `6e024ddefed3ea21169f0eca5ab5c32312adb9cf` | Apache-2.0 | `src/NodaTime/` | 100 (100) | 8 | 7 / 1 / 92 |
| `humanizer` | `https://github.com/Humanizr/Humanizer` | `3ebc38de585fc641a04b0e78ed69468453b0f8a1` (v2.14.1) | MIT | `src/Humanizer/` | 100 (100) | 9 | 9 / 0 / 91 |

No Unknown is silent: each carries a diagnostic that names its reason, and the ratchet allows no `silent-unclassified` bucket. Two trivially pure methods, NodaTime `HebrewScripturalCalculator.IsLeapYear` and Humanizer `IsAndSplitNeeded`, remain Unknown with SP0047 `Advisory:EntryInitialization:TypeInitializer`. Each declaring type's static initializer calls code (`YearStartCacheEntry.CreateCache()`, and a `Dictionary` collection initializer), which is an entry effect. The gate prints this table's last three columns per source on every run. [verdicts-by-source.md](verdicts-by-source.md) is generated with the snapshot and breaks the Unknowns down by diagnostic and reason for each source.

Source notes:

- MoreLINQ compiles with its net9.0 symbols. Its generated `Extensions.g.cs` and `Extensions.ToDataTable.g.cs` wrappers are excluded; nothing else depends on them. `Lookup.cs` and `UnreachableException.cs` are MIT-licensed by their own headers, which stay embedded in the pinned text; `COPYING.txt` repeats that notice.
- NodaTime compiles with its net8.0 symbols, nullable enabled, and checked arithmetic, as its build does. Its Apache `NOTICE.txt` is copied with the license.
- Humanizer is pinned at release v2.14.1 because later commits generate locale code with a source generator, which a BCL-only build cannot run. The T4-generated `FluentDate` files are excluded. `Bytes/` and `Inflections/` carry their own MIT licenses, copied as notices.

The lock embeds every C# file under each source's roots (less excludes): 604 files. Included dependency files do not count as extra selected methods. [oss-methods.json](oss-methods.json) stores each source's pin, license and notice hashes and compile settings, file hashes, method hashes, source paths and line ranges, expected verdicts, and independent support classifications. License and notice copies live under [third-party/](third-party/) as `third-party/<source-id>/<upstream path>`.

Gate validation accepts MIT and Apache-2.0 only, checks full commit pins, license and notice hashes, distinct locations and declaration hashes, a 200-500 method range, and at least 25 source files. The importer also requires each source's selection to span at least 25 files.

## Instrumentation and observations

Source text is pinned with normalized LF line endings and no byte-order mark. At test time the runner adds `[SharpProof.Attributes.EnforcePure]` to selected declarations without rewriting bodies, signatures, or dependencies. Each source compiles as its own assembly (`SharpProofOssCorpus.<source-id>`) against the runtime's BCL and SharpProof's attributes only; a source that needs a NuGet package fails to compile and cannot be added. Syntax trees carry source-qualified paths (`<source-id>/<path>`), so diagnostic locations stay distinct across sources.

Portable diagnostics and native semantic outcomes are observed separately. Native verification consumes the compiler-produced artifact and runs the worker. Unsupported targets remain explicit; silence is not proof.

The [ratchet](unknown-reason-ratchet.json) sets floors for supported cases overall and supported OSS methods, a cap on total Unknown, and independent reason-bucket caps; the file holds the current values. Support labels are reviewed evidence, not derived from the expected verdict. The 2026-11 review marked a new method Supported only when its observed Proven verdict was checked against the body (constant or concatenation results, `[Conditional("DEBUG")]` calls removed in release builds, and modular arithmetic); every other new method is IntentionallyUnsupported. When the exact module-initializer search replaced a 4,096-symbol scan, three NodaTime methods were reviewed against their bodies and marked Supported. `JulianYearMonthDayCalculator.IsLeapYear` (`(year & 3) == 0`) and `RegularYearMonthDayCalculator.GetMonthsInYear` (a field read) are Proven. `ValueCursor.Match` is Refuted, because it calls `MoveNext`, which writes the cursor's `Index` and `Current`. When purity claims began admitting type initializers that only construct fresh values (SEMANTICS.md), four more methods were reviewed against their bodies and marked Supported. MoreLINQ `IdFn` (`x => x`), NodaTime `Era.ToString` (an auto-property read) and `DateAdjusters.DayOfMonth` (returns a new lambda) are Proven. MoreLINQ `Aggregate` (OSS0202) is Refuted, because it enumerates a caller's sequence and invokes the caller's delegates, either of which may write state. `MoreEnumerable`'s string-array initializer had masked that verdict; `TypeInitializerEffectTests` checks that an admitted initializer leaves a body's verdict unchanged.

### Reviewed exceptional-resource correction (OSS0015)

`OSS0015.baseline` (`FindGCDEuclidean`) was previously classified Supported/Proven. Its unchanged upstream body calls `Math.Abs(a)` and `Math.Abs(b)` before checking either input. For `a == int.MinValue`, runtime exception-message lookup invokes the ambient culture's virtual `Name` getter, which can mutate observable state or throw another exception. The previous unrestricted purity claim was therefore unsound.

Only this declaration is reclassified IntentionallyUnsupported/Unknown until exceptional resource callbacks have an exact purity model. Its source file hash `fd34466c9843b6b88c3f73cb71a18f6a2e9236393d4ceafaf1f1b3c8afce7f0f`, declaration hash `905e65ea85f97ec8985ab0a655079eb25464267fb4e8fc6e05027eec3ddec554`, signature, and body remain unchanged. `ResourceCallbackCorrectionRetainsPinnedGreatestCommonDivisorSource` executes that exact pinned source with a culture callback; `ExceptionalFrameworkCultureTests` also covers resource callback effects and exception substitution.

The reviewed correction lowered the supported-case and supported-OSS floors by one and raised the total-Unknown and SP0002 ceilings by one. All other classifications, reason buckets, snapshot entries, and support rules remain unchanged. This corrects one invalid historical proof; it is not a general allowance for precision regressions.

### Reviewed classification for ref and out source callees

When source callees with `ref` and `out` parameters began to be inlined (SEMANTICS.md, "Calls and loops"), six selected methods left SP0047 `Advisory:Lowering:UnsupportedOperationKind`. aalhour `ChainedHashTable._contractCapacity` (OSS0125) became Refuted: after its `_rehash(ref newHashTableStore, ...)` call it assigns `_hashTableStore`, an instance field of `this`. It was reviewed against the body and marked Supported/Refuted. The other five now stop at SP0047 `Advisory:Lowering:UnsupportedControlFlow`. The supported floors rose by one (264 to 265, 38 to 39); the total-Unknown ceiling (498 to 497) and the SP0047 ceiling (225 to 224) fell by one.

### Reviewed classification for readonly struct values

When source `readonly struct` values were modeled (SEMANTICS.md, "Heap"), ten NodaTime methods that had stopped at SP0047 `Advisory:Lowering:UnsupportedOperationKind` lowered completely. Two became Proven and were reviewed against their bodies: `Instant64.CompareTo` (OSS0345, `=> nanoseconds.CompareTo(other.nanoseconds)`, two `long` field reads and a pure framework comparison) and `OffsetDate.WithOffset` (OSS0353, `=> new OffsetDate(date, offset)`, whose constructor only stores its two arguments). Both are marked Supported/Proven. Of the other eight, six are Unknown with SP0002 (their effects are not proved) and two with SP0047 `Advisory:EntryInitialization:TypeInitializer`. The supported floors rose by two (262 to 264, 36 to 38); the total-Unknown ceiling fell by two (500 to 498), the SP0047 ceiling fell by eight (233 to 225) and the SP0002 ceiling rose by six (231 to 237).

### Reviewed classification for constructor readonly stores

Once a constructor's stores to its own readonly fields and get-only auto-properties were modeled, NodaTime `Period.FromYears` (OSS0356, `=> new Period(years, 0, 0, 0)`) became Proven. Its private constructor only assigns four get-only auto-properties, and `Period`'s static initializer only constructs fresh `Period` values, which purity admits. It was reviewed against the body and marked Supported/Proven. The supported floors rose by one (261 to 262, 35 to 36), and the SP0002 ceiling (232 to 231) and total-Unknown ceiling (501 to 500) fell by one.

### Reviewed bucket move for same-type callees

When a source callee declared in the caller's own type began to be inlined under that type's initializer (SEMANTICS.md, "Calls and loops"), 48 selected methods that had stopped at SP0047 `Advisory:Lowering:UnsupportedOperationKind` lowered completely. None of them became Proven or Refuted: each is still Unknown, now with SP0002 because its effects are not proved (most enumerate a caller's sequence or invoke a caller's delegate). The ratchet therefore moved 48 from the SP0047 ceiling (281 to 233) to the SP0002 ceiling (184 to 232). Total Unknown, the supported floors and every support label are unchanged. [verdicts-by-source.md](verdicts-by-source.md) shows the per-source split.

## Reproduce, update, or add a source

Use a persistent canonical container workspace so edits survive (`docker compose run --rm tooling dev`, or the Dev Container). Inside its shell, clone the upstream repository, check out the commit pinned in `sources.json`, and import it:

```text
git clone https://github.com/morelinq/MoreLINQ /tmp/upstream/morelinq
git -C /tmp/upstream/morelinq checkout d217ab1e8eac68247650c27e977b767dd66bcab0
pwsh -NoLogo -NoProfile -File tests/SharpProof.Gates/Corpus/Import-OssCorpus.ps1 -UpstreamRoot /tmp/upstream/morelinq
```

The importer finds the source by the checkout's `origin`, requires a clean checkout whose HEAD is the pinned commit, checks that the commit exists on the approved repository, and re-reads the license and notices against their reviewed hashes. It reads only committed blobs. It then merges that one source into the manifest: other sources are untouched, methods still present keep their IDs, support labels, and order, and the snapshot is regenerated. Re-importing at the same pins is byte-identical.

To add a source:

1. Check the license is MIT or Apache-2.0 for every file under the roots, including vendored components, and that the code builds without NuGet packages (polyfill and analyzer packages are fine when the BCL already provides the types).
2. Append an entry to `sources.json`: a lowercase `id`, the repository URL, the full commit, the SPDX license, `license`/`notices` with the SHA-256 of each file's LF-normalized text (ending in one newline), `roots`, `excludes` (a file path, or a directory ending in `/`), `methodQuota`, and the upstream build's compile settings. Keep the quota total at 500 or less.
3. Run the import with `-Draft`. New methods are appended from the next free `OSSnnnn` ID with `"support": "Unspecified"` and their observed verdicts, and the catalog rejects the manifest until each is labeled.
4. Review every new method: mark it `Supported` only after checking its Proven or Refuted verdict against the body, and `IntentionallyUnsupported` otherwise. Then import again without `-Draft` and confirm nothing else changes.
5. Update the ratchet with the measured gate numbers, the table above, the [gate guide](../README.md), and `THIRD-PARTY-NOTICES.txt`.

Changing a pinned commit is a reviewed change too: methods that disappear are dropped (later IDs shift down), and replacements are appended. For observation-only updates, run `sp corpus-update -Configuration Release` without the importer; it does not replace the source lock. See the [gate guide](../README.md) for classifications and invariance checks.
