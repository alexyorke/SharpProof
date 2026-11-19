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

### Reviewed classification for closed type initializers

When type initializers that are closed computations began to be run at analysis time (SEMANTICS.md, "Effects", closed computations), calls into NodaTime's `Duration`, `Instant`, `Offset`, `LocalTime` and the Hebrew calendar types stopped being opaque. Nine NodaTime methods became Proven and were reviewed against their bodies and marked Supported/Proven: `HebrewMonthConverter.CivilToScriptural` (OSS0310), `HebrewScripturalCalculator.IsLeapYear` (OSS0311), `HebrewYearMonthDayCalculator.CalendarToCivilMonth` (OSS0312), `WeekYearRules.ForMinDaysInFirstWeek` (OSS0321), `TimeSpanExtensions.ToDuration` (OSS0338), `Instant.FromTrustedDuration` (OSS0346), `Interval.Contains` (OSS0347), `Offset.Max` (OSS0352) and `PeriodBuilder.Build` (OSS0357). Nine methods that abstained now lower and stop at SP0002 instead: MoreLINQ `Random` (OSS0268), `RandomSubset` (OSS0269) and `Shuffle` (OSS0282), and NodaTime `GetStartOfYearInDays` (OSS0322), `ToLocalTime` (OSS0336), `TimePeriodField.Add` (OSS0341), `LocalInstant.Minus` (OSS0350), `OffsetDateTime.ToInstant` (OSS0354) and `GetPositiveHours` (OSS0379). No verdict became Refuted. The supported floors rose by nine (271 to 280, 45 to 54); the total-Unknown ceiling fell by nine (491 to 482), the SP0002 ceiling rose by seven (235 to 242) and the SP0047 ceiling fell by sixteen (220 to 204).

### Reviewed classification for runtime types

When allocations and boxes began to record their objects' runtime types and type tests began to read them (SEMANTICS.md, "Typed execution model"), NodaTime `Duration64.Equals(object)` (OSS0344, `obj is Duration64 other && Equals(other)` on a readonly struct, with `Equals(Duration64)` being `this == other`) became Proven: its purity holds whatever `obj` is. It was reviewed against the body and marked Supported/Proven. Three methods changed reason and stay Unknown: `aalhour-c-sharp-algorithms` `BellmanFordShortestPaths._bellmanFord` (OSS0003) and NodaTime `CachedDateTimeZone.ForZone` (OSS0400) now lower and stop at SP0002, and `OpenAddressingHashTable._rehash` (OSS0127) now lowers further into its callee and stops at SP0047 `Advisory:Lowering:UnsupportedControlFlow`. The supported floors rose by one (270 to 271, 44 to 45); the total-Unknown ceiling fell by one (492 to 491), the SP0002 ceiling rose by one (234 to 235) and the SP0047 ceiling fell by two (222 to 220).

The allocation's runtime-type fact is emitted only for programs that contain a type test. Emitted for every program, it changed which models the solver returned, so some purity counterexamples that had replayed stopped replaying and others started; omitting an unused fact keeps those programs' queries exactly as before.

### Reviewed classification for nullable values

When nullable value types over scalars began to lower (SEMANTICS.md, "Typed execution model"), two selected methods changed reason and both stay Unknown. MoreLINQ `Repeat(sequence, int count)` (OSS0273) passes `count` to `RepeatImpl(IEnumerable<T>, int?)`; that `int` to `int?` conversion was SP0047 `Advisory:Lowering:UnsupportedType`, and the method now lowers and stops at SP0002 because the iterator callee is not proven pure. Humanizer `DateHumanizeExtensions.Humanize` (OSS0410) assigns `utcDate ??= ...` on a `bool?`, which is now modeled, and stops at SP0047 `Advisory:Lowering:UnsupportedOperationKind` on its `DateTime?` argument, which is not. No verdict changed. The SP0002 ceiling rose by one (233 to 234) and the SP0047 ceiling fell by one (223 to 222); the total-Unknown ceiling is unchanged.

### Reviewed classification for enum values

When enum values began to lower as their underlying integer (SEMANTICS.md, "Typed execution model"), three NodaTime methods left Unknown. `PatternFields.HasAny` (OSS0386, `(fields & target) != 0`) is Proven. `DayOfWeekExtensions.ToIsoDayOfWeek` (OSS0333) and `IsoDayOfWeekExtensions.ToDayOfWeek` (OSS0334) are Refuted: each calls a `BclConversions` method that, for an out-of-range day, throws `new ArgumentOutOfRangeException(nameof(...))`, whose message lookup enters the runtime resource lock, a synchronization effect the worker replays. All three were reviewed against their bodies and marked Supported. The supported floors rose by three (267 to 270, 41 to 44); the total-Unknown ceiling fell by three (495 to 492), the SP0002 ceiling by two (235 to 233) and the SP0047 ceiling by one (224 to 223).

### Reviewed classification for exact dispatch targets

When a dispatched call whose target the program text fixes began to be inlined (SEMANTICS.md, "Calls and loops"), NodaTime `CopticYearMonthDayCalculator.CalculateStartOfYearDays` (OSS0304) became Proven. It calls `IsLeapYear` on `this` in a sealed calculator, which inherits the override `(year & 3) == 3`; the rest is `int` arithmetic on its argument. It was reviewed against the body and marked Supported/Proven. The supported floors rose by one (266 to 267, 40 to 41); the total-Unknown ceiling (496 to 495) and the SP0002 ceiling (236 to 235) fell by one.

### Reviewed classification for recursive purity

When a recursive call into the method being verified began to be assumed pure by the purity check alone, by induction on recursion depth (SEMANTICS.md, "Calls and loops"), `aalhour-c-sharp-algorithms` `GreatestCommonDivisor.Stein` (OSS0180) became Proven. Its body only compares, shifts and subtracts its two `int` arguments and calls itself; it reads and writes no state. It was reviewed against the body and marked Supported/Proven. The supported floors rose by one (265 to 266, 39 to 40); the total-Unknown ceiling (497 to 496) and the SP0002 ceiling (237 to 236) fell by one.

### Reviewed classification for ref and out source callees

When source callees with `ref` and `out` parameters began to be inlined (SEMANTICS.md, "Calls and loops"), six selected methods left SP0047 `Advisory:Lowering:UnsupportedOperationKind`. `aalhour-c-sharp-algorithms` `ChainedHashTable._contractCapacity` (OSS0125) became Refuted: after its `_rehash(ref newHashTableStore, ...)` call it assigns `_hashTableStore`, an instance field of `this`. It was reviewed against the body and marked Supported/Refuted. The other five now stop at SP0047 `Advisory:Lowering:UnsupportedControlFlow`. The supported floors rose by one (264 to 265, 38 to 39); the total-Unknown ceiling (498 to 497) and the SP0047 ceiling (225 to 224) fell by one.

### Reviewed classification for readonly struct values

When source `readonly struct` values were modeled (SEMANTICS.md, "Heap"), ten NodaTime methods that had stopped at SP0047 `Advisory:Lowering:UnsupportedOperationKind` lowered completely. Two became Proven and were reviewed against their bodies: `Instant64.CompareTo` (OSS0345, `=> nanoseconds.CompareTo(other.nanoseconds)`, two `long` field reads and a pure framework comparison) and `OffsetDate.WithOffset` (OSS0353, `=> new OffsetDate(date, offset)`, whose constructor only stores its two arguments). Both are marked Supported/Proven. Of the other eight, six are Unknown with SP0002 (their effects are not proved) and two with SP0047 `Advisory:EntryInitialization:TypeInitializer`. The supported floors rose by two (262 to 264, 36 to 38); the total-Unknown ceiling fell by two (500 to 498), the SP0047 ceiling fell by eight (233 to 225) and the SP0002 ceiling rose by six (231 to 237).

### Reviewed classification for constructor readonly stores

Once a constructor's stores to its own readonly fields and get-only auto-properties were modeled, NodaTime `Period.FromYears` (OSS0356, `=> new Period(years, 0, 0, 0)`) became Proven. Its private constructor only assigns four get-only auto-properties, and `Period`'s static initializer only constructs fresh `Period` values, which purity admits. It was reviewed against the body and marked Supported/Proven. The supported floors rose by one (261 to 262, 35 to 36), and the SP0002 ceiling (232 to 231) and total-Unknown ceiling (501 to 500) fell by one.

### Reviewed bucket move for same-type callees

When a source callee declared in the caller's own type began to be inlined under that type's initializer (SEMANTICS.md, "Calls and loops"), 48 selected methods that had stopped at SP0047 `Advisory:Lowering:UnsupportedOperationKind` lowered completely. None of them became Proven or Refuted: each is still Unknown, now with SP0002 because its effects are not proved (most enumerate a caller's sequence or invoke a caller's delegate). The ratchet therefore moved 48 from the SP0047 ceiling (281 to 233) to the SP0002 ceiling (184 to 232). Total Unknown, the supported floors and every support label are unchanged. [verdicts-by-source.md](verdicts-by-source.md) shows the per-source split.

### Reviewed classification for chained constructors

Once a constructor chained with `: this(...)` was inlined (SEMANTICS.md, "Heap"), NodaTime `OffsetTime.WithOffset` (OSS0355, `=> new OffsetTime(TimeOfDay, offset)`) became Proven. That public constructor chains to `: this(time.NanosecondOfDay, offset.Seconds)`, whose body packs both values into one readonly field. It was reviewed against the body and marked Supported/Proven. The supported floors rose by one (280 to 281, 54 to 55), and the SP0047 ceiling (204 to 203) and total-Unknown ceiling (482 to 481) fell by one.

### Reviewed classification for initialized statics

Once a readonly static, or a static get-only auto-property, took the value its type's closed initialization leaves (SEMANTICS.md, "Type initialization"), NodaTime `LocalDate.AtMidnight` (OSS0348, `=> new LocalDateTime(this, LocalTime.Midnight)`) became Proven. `LocalTime.Midnight` is `{ get; } = new LocalTime(0, 0, 0)`, whose initializer runs to completion at analysis time, so the read is the struct value it made; the method only builds a new value from it. It was reviewed against the body and marked Supported/Proven. The supported floors rose by one (281 to 282, 55 to 56), the total-Unknown ceiling fell by one (481 to 480), and the SP0047 ceiling fell by two (203 to 201) while the SP0002 ceiling rose by one (242 to 243): NodaTime `YearStartCacheEntry.CreateCache` (OSS0323), which fills a new array with the initialized static `Invalid`, now lowers and stays Unknown, because a store into an array that is returned cannot be shown local. Humanizer `MalteseNumberToWordsConvertor.Convert` (OSS0487) still abstains, now on its control flow rather than on an operation.

### Reviewed classification for null patterns on type parameters

Once `x is null` over a type-parameter value became an exact null test, NodaTime `Preconditions.CheckNotNull<T>` lowers, and three methods that call it became Refuted: `StopwatchExtensions.ElapsedDuration` (OSS0335), `TimeProviderExtensions.ToClock` (OSS0337) and `CachedDateTimeZone.ForZone` (OSS0400). Each passes its argument to `CheckNotNull`, which throws `ArgumentNullException` for `null`, and building that exception reads the ambient culture's resources, so none of them is observably pure. They were reviewed against the bodies and marked Supported/Refuted. The supported floors rose by three (282 to 285, 56 to 59), and the SP0002 ceiling (243 to 240) and total-Unknown ceiling (480 to 477) fell by three.

### Reviewed classification for iterator calls

Once a call to a synchronous iterator lowered as a fresh allocation of its enumerator (SEMANTICS.md, "Effects"), 23 selected methods changed verdict.

- **Proven (7, all from the C-Sharp-Algorithms source):** `TrieMapNode.GetByPrefix` (OSS0086), `TrieNode.GetByPrefix` (OSS0087), `DirectedSparseGraph.IncomingEdges` (OSS0131), `UndirectedSparseGraph.IncomingEdges` (OSS0135), `UndirectedWeightedSparseGraph.IncomingEdges` (OSS0137), `TrieMapNode.GetTerminalChildren` (OSS0167) and `TrieNode.GetTerminalChildren` (OSS0168). Each is an iterator itself. Calling one runs none of its body, so the claim, which describes the call, holds. They were SP0047 `UnsupportedStatement`.
- **Refuted (16, all MoreLINQ):** Cartesian (OSS0209), Choose (OSS0210), Exclude (OSS0223), Generate (OSS0240), Insert (OSS0246), Pairwise (OSS0261), Permutations (OSS0265), Pipe (OSS0266), PreScan (OSS0267), Repeat (OSS0273), SkipUntil (OSS0284), Subsets (OSS0289), TagFirstLast (OSS0290), TakeUntil (OSS0292), TraverseBreadthFirst (OSS0299) and Unfold (OSS0300). Each checks its arguments eagerly and then returns an iterator. The iterator call is no longer an unknown call, so what remains is the throw path: `new ArgumentNullException(nameof(x))` and the other parameter-name constructors build their message through a resource lookup that reads the ambient culture. They were SP0002.

All 23 were reviewed against the bodies and marked Supported. The supported floors rose by 23 (285 to 308, 59 to 82). The total-Unknown ceiling fell by 23 (477 to 454), the SP0047 ceiling by 7 (201 to 194) and the SP0002 ceiling by 16 (240 to 224).

### Reviewed classification for interpolated strings

Once an interpolated string over `string`, `char`, `bool`, integer and reference holes lowered (SEMANTICS.md, "Effects"), Humanizer `ArticlePrefixSort.AppendArticlePrefix` (OSS0401) stopped abstaining at its interpolation and now stops at SP0002: its body lowers, but it is not proven observably pure, since an integer hole formats through the current culture. The verdict stays Unknown. The SP0047 ceiling fell by one (194 to 193) and the SP0002 ceiling rose by one (224 to 225); the total-Unknown ceiling is unchanged.

### Reviewed classification for collection initializers

Once a static initializer that only builds framework collections, such as `new Dictionary<uint, BigInteger>()`, counted as making fresh values (SEMANTICS.md, "Type initialization"), `CatalanNumbers.GetNumberByBinomialCoefficients` (OSS0179) began to inline `BinomialCoefficients`, whose type has that cache. It now stops at SP0002 instead of SP0047: `Factorial` stores into the cache to memoize, a real write to shared state, so the verdict stays Unknown. The SP0047 ceiling fell by one (193 to 192) and the SP0002 ceiling rose by one (225 to 226); the total-Unknown ceiling is unchanged.

### Reviewed classification for lambda creation

Once creating a delegate from a lambda lowered without running its body (SEMANTICS.md, "Effects"), eight selected methods stopped abstaining at SP0047.

- **Proven:** NodaTime `CreateYearOfEraHandler` (OSS0383) and `CreatePeriodHandler` (OSS0388). Each only returns a lambda over parameters that are never written, so the call is pure.
- **Refuted:** NodaTime `GetAllZones` (OSS0332). It checks its argument with `Preconditions.CheckNotNull`, which throws for `null` and builds that exception from culture resources, as reviewed for OSS0335.
- **Unknown, now SP0002:** `_tryGetEdge` in the directed and undirected weighted sparse graphs (OSS0049, OSS0053), MoreLINQ `AssertCount` (OSS0205) and `TakeEvery` (OSS0291), and Humanizer `DefaultCollectionFormatter.Humanize<T>` (OSS0430). Each now lowers past its lambda and stops on unknown library calls.

All eight were reviewed against the bodies. The supported floors rose by three, the total-Unknown ceiling fell by three, the SP0047 ceiling fell by eight and the SP0002 ceiling rose by five.

### Reviewed lowering reasons for opaque floating-point conversions

Five C-Sharp-Algorithms methods stay Unknown with SP0047 after their integer-to-floating conversions lower. `SieveOfAtkin` (OSS0016) and `PrimesList.IsPrime` (OSS0124) stop at the conversion of `Math.Sqrt` back to `int`. The Sieve of Eratosthenes (OSS0017) and both comb-sort methods (OSS0107 and OSS0185) reach unsupported floating comparisons. Their verdicts, support classifications, and diagnostic ID counts are unchanged.

## Contracts on real code

The corpus above checks one purity claim per method. [oss-contracts.json](oss-contracts.json) adds hand-written postconditions and preconditions for 15 of those methods, so contracts are also tested on real code. Each entry is a `[ContractFor]` companion class, compiled beside the unchanged pinned source with no effect attribute (`OpenSourceCorpusRunner.PrepareContracts`). A type has at most one companion, so a second method of the same type names the first entry in `sharedWith`. Each expected verdict was checked against the method's body, and its `review` says why it holds or why it is not decided. Two contracts are deliberately false.

`SharpProof.Gates contract-oracle` runs them, and `OssContractGateTests` fails when any verdict changes. Today two are Proven: NodaTime `JulianYearMonthDayCalculator.IsLeapYear` (OSS0315), a sealed override, and Humanizer `RussianGrammaticalNumberDetector.Detect` (OSS0453), whose result is an enum. One is Refuted: the deliberately false NodaTime `HebrewMonthConverter.CivilToScriptural` contract (OSS0310), whose witness runs through `HebrewScripturalCalculator`, a type whose initializer is a closed computation, and so replays. The others are Unknown for reasons each review names:

- Humanizer's formatter methods are overrides in unsealed classes. A postcondition there is a contract for every override, so it is not verified, and the two false contracts among them are not refuted either.
- NodaTime's checked 32-bit multiply with a remainder (OSS0311) exhausts the query budget.
- A witness through a loop and `Math.Sqrt` (OSS0124) cannot be replayed.
- A base-class property read (OSS0393), `Math.Abs` with a loop (OSS0015) and recursion (OSS0180) are not modeled for postconditions.

A change that turns one of them into Proven or Refuted updates its entry and this list.

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
