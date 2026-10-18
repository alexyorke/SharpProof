# .NET API coverage inventory

The automatic inventory covers every declared public, protected, and protected-internal
type/member in **Microsoft.NETCore.App.Ref 10.0.13 / net10.0**, supplied by the canonical
.NET SDK 10.0.402 container. The additional pinned .NET 9 reference-pack stage remains
available for compatibility checks; it does not select the active inventory universe.
This is a framework API inventory, not an inventory of
SDK command-line tools. It does not load implementation assemblies or execute API bodies.

The unit is `(defining assembly name, XML documentation declaration ID)`. Overloads
and generic arity have distinct identities. Nested types are included only when their
containing types are externally visible. Inherited declarations and type forwarders
are counted at their definitions; properties/events own their accessors. Private,
internal, and private-protected declarations are outside the public universe.
Type declarations are explicitly classified as `excluded-container`; they contribute
no behavioral support. Methods, constructors, operators, properties, fields, and events
are classified independently. No API is declared fully modeled.

`partially-modeled` records evidence from the production `ApiSpecResolver`, including
each facet and postcondition count, or the audited production frontend
intrinsic predicates. A getter spec does not establish setter support. `unmodeled`
means no evidence from those sources, not that every possible use must fail verification.
Contextual language lowering (arrays, operators, conversions, field reads, etc.) is not
a blanket API specification. Opt-in relational packs, ASP.NET Core, Windows Desktop,
workload packs, NuGet libraries, and other target frameworks are outside this initial
inventory. These exclusions must remain visible when interpreting the report.

## Reports and CI

Run the same required gate as CI:

```sh
docker compose run --rm tooling test \
  -Target src/SharpProof.Effects.Tests/SharpProof.Effects.Tests.csproj \
  -TestFilter FullyQualifiedName~DotnetApiInventoryTests
```

CI publishes an assembly summary, modeled facets, and bounded change lists in the job
summary. The `dotnet-api-inventory-*` artifact contains the complete `inventory.tsv`,
`manifest.json`, `delta.json`, and `report.md`, even when the baseline comparison fails.
TSV has no header: assembly, declaration ID, symbol kind, status, evidence (`-` when absent).

The reviewed `net10.0.tsv` baseline gates additions, removals, and every changed evidence
row. Equal totals cannot conceal churn. Loss of a catalog row, known facet, or recognized
intrinsic fails the gate. The manifest additionally pins SDK identity, all reference
assembly SHA-256 checksums, and the catalog semantic digest, so semantic changes with
unchanged facet counts also require review. Resolver failures fail closed. Missing
packs, duplicate identities, and missing baselines are errors.

## Which modeled behaviors have tests?

The same required CI job also runs `ApiSpecRuntimeOracleTests`. Its
`ModeledFacetsHaveExecutedTestEvidence` gate derives required rows from the current
default catalog, not from a manually maintained list of tests. Each known effect,
allocation, exception, nullness, cardinality, termination facet and each postcondition
must have exactly one runtime witness, named edge inputs, a passing declared-behavior
check, and a check that rejects a deliberately wrong claim. Unknown and not-applicable
facets do not claim behavior and therefore do not count as tested facets.

`catalog-test-evidence.json` and `.md` map each API declaration and required facet to
the fully qualified test method that actually executed those checks, its edge inputs,
and `passed`, `missing`, or `failed` status. These reports are written before the gate
asserts success, so a missing witness or failed behavior remains visible in failed CI
artifacts. The registry's existing exact-coverage test additionally rejects stale or
duplicate registrations. Mutation controls verify that removing an API/facet or
postcondition witness, accepting a wrong claim, or omitting edge inputs is detected.

`intrinsic-test-evidence.json` and `.md` cover the audited string, integer comparison,
and concrete Count invariants. The inventory test compiles comparison expressions through the
production frontend, and interprets their result and receiver-safety conditions against
runtime expectations. Inputs cover equal/unequal strings, empty strings, null receivers
and arguments, embedded nulls, and ordinal Unicode behavior; an inverted-result claim
must be rejected. Integer and Count edges are described below. Every inventoried
intrinsic requires a registered behavioral probe.

All four evidence files are included in the API inventory artifact, and both Markdown
matrices appear in the CI job summary. To reproduce the catalog gate separately:

```sh
docker compose run --rm tooling test \
  -Target src/SharpProof.Specs.Tests/SharpProof.Specs.Tests.csproj \
  -TestFilter FullyQualifiedName~ApiSpecRuntimeOracleTests
```

This mapping is bounded runtime/semantic evidence, not line coverage or a proof that
every possible input is exercised. A passing allocation or termination witness records
the observed inputs and does not establish those properties universally. Opt-in packs
and other contextual intrinsics remain outside this mapping, as outside the inventory.

## Reviewing an intentional update

Generate a candidate; this does **not** change the reviewed baseline:

```sh
docker compose run --rm -e SHARPPROOF_API_INVENTORY_CANDIDATE=1 tooling test \
  -Target src/SharpProof.Effects.Tests/SharpProof.Effects.Tests.csproj \
  -TestFilter FullyQualifiedName~DotnetApiInventoryTests
```

Inspect the complete delta and manifest diff. Investigate support losses rather than
approving a lower count. After reviewing intentional changes, copy the candidate:

```sh
docker compose run --rm tooling dev -lc \
  'cp artifacts/api-coverage/inventory.tsv eng/api-coverage/net10.0.tsv && cp artifacts/api-coverage/manifest.json eng/api-coverage/net10.0.manifest.json'
```

Run the gate again without candidate mode and commit both baselines with an explanation.
For an SDK/reference-pack upgrade, change the inventory's explicit pack pin as well;
never select the highest installed pack or silently fall back to runtime assemblies.
New support evidence still needs semantic tests; this inventory measures registrations
and drift, not behavioral correctness for every runtime input or proof pipeline stage.

## Act on observed API usage

[The checked-in usage snapshot](usage-ranking.generated.md) ranks exact API overloads
in 200 original OSS targets, 28 synthetic baseline targets, and the 10 checked-in sample
source files. CI regenerates `usage-ranking.md` and `usage-ranking.json`, publishing
both with the inventory artifact and the Markdown report in its job summary. Reproduce:

```sh
docker compose run --rm tooling test \
  -Target src/SharpProof.Gates.Tests/SharpProof.Gates.Tests.csproj \
  -TestFilter FullyQualifiedName~ApiUsageRankingTests
```

The unit is a static source use site, not runtime execution frequency. The scanner
binds against the pinned reference pack, normalizes constructed generics and reduced
extension methods, and separates property reads, writes, and read/write uses. It scans
selected OSS declarations only, original synthetic Focus methods only, and sample
sources together with their implicit usings. Dependency bodies, synthetic helper
scaffolding, metamorphic duplicates, compile-time nameof, delegate construction, and
implicit compiler calls are excluded. Source content hashes and every resolved site
are retained in the JSON report. Compilation errors or unresolved sites fail the gate.
Getter evidence never credits a setter. Unknown result relations remain unknown.

### First implementation batch

The scan found 641 framework uses with zero unresolved sites. The following five
previously unmodeled getters account for 41 read sites in the fixed universe:

| API | Read sites | Added evidence |
| --- | ---: | --- |
| `List<T>.Count` | 18 | Reads receiver; no allocation; no throw on non-null receiver; terminates |
| `List<T>.get_Item(int)` | 16 | May allocate; may throw ArgumentOutOfRangeException |
| `Queue<T>.Count` | 5 | Same Count facets |
| `Dictionary<TKey,TValue>.Count` | 1 | Same Count facets; comparer callbacks are not invoked |
| `Stack<T>.Count` | 1 | Same Count facets |

Runtime witnesses cover empty, singleton, grown-and-reduced collections; dictionary
comparer callback observation; null/non-null indexed elements; and negative, Count,
empty-list, and extreme integer indexes. Count fixtures check receiver fields remain
unchanged. Indexer effects remain Unknown because exception helpers can initialize
ambient resources. Native worker tests prove the Count allocation/exception claims
and retain conservative results for abstract getters, comparator-driven dictionary
lookups, invalid indexes, setters, and unmodeled collection-content relationships. These are
partial API models, not collection-content or full heap models.

### Integer comparisons and Count result guarantees

The frontend models typed comparisons for all nine supported integer types.
`SByte/Byte/Int16/UInt16/Char.CompareTo` return the exact Int32-promoted difference;
`Int32/Int64/UInt32/UInt64.CompareTo` return -1/0/1. Proven default comparers use
the corresponding result, retaining non-unit differences for smaller types.
The runtime/IR witnesses cover each overload and default comparer across ordered
boundary pairs, including signed extrema and UTF-16 surrogate code units.
Default identity flows through aliases, compatible joins, CFG captures and
immutable source parameters. Generic helpers use declaration-owned scalar type
arguments or proven array element types, including local-function symbol clones.
Receivers and arguments retain source order, including named arguments. The
Default getter remains an opaque call with initialization, allocation and exception
possibilities; wrong-contract replay after that call remains a separate gap.
Custom comparers, unbound generic types, Boolean and object overloads do not inherit
integer semantics. The promoted-difference behavior is also visible in the
[CoreLib implementation](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/SByte.cs).

The four concrete Count getters above now additionally constrain normal results to
`Count >= 0` in native lowering. This does not imply strict positivity, collection
contents, or an invariant for arbitrary `ICollection<T>` implementations. The
catalog remains an effect specification; this result guarantee is separately
inventoried as a frontend intrinsic.

CI runs runtime/IR comparison checks across ordered pairs of extreme, negative,
zero, equal, and positive values, as well as native proof and conservative
regression tests. The usage report splits rows by contextual support status:
`unmodeled-dispatch` means the API declaration has a guarded intrinsic but this
call does not meet its guards. Generic comparer calls in the original corpus
therefore remain visible as gaps; this batch does not claim all 26 are covered.

### Array-backed IList and ICollection

The frontend models `ICollection<T>.Count` and `IList<T>.Item` when the receiver is
a single-dimensional scalar array, reached through a direct cast or proved
local, CFG-capture, or inlined source-parameter provenance, to the matching
framework interface. Count is the array length; index reads return its elements;
simple index stores update the same array storage and retain mutation effects.
The model also works for inherited Count access through `IList<T>` and for
getter expressions in contract clauses.

For example, with non-null and valid-index preconditions, this call has the same
result as `values[index]` and proves no-throw/no-allocation claims:

```csharp
return ((IList<int>)values)[index]; // values is int[]
```

Receiver, index, and setter value evaluation retain C# ordering. Native tests cover
scalar reads/stores, saved reads across aliasing stores, receiver reassignment by
the setter value, value evaluation before a null receiver fault, and an opaque
callback invalidating content knowledge. Compiled CFG/heap execution is checked
against runtime Count/get/set behavior on null, empty, singleton, and extreme
element values and negative, valid, Count, and extreme indexes. Deliberately wrong
result claims must fail.

This is guarded implementation support. Arbitrary interface parameters, local
interface aliases with unknown or mixed bindings, custom derived interfaces,
reference array covariance, `as` casts, and compound index assignments remain
outside this slice. Provenance requires every definition to have the same scalar
array representation; cycles, ref escapes, and closure-captured mutable storage
abstain. Array identities are stored at assignment time, so later reassignment of
the original array variable does not change an existing interface alias.
Source helpers receive this representation only for immutable by-value interface
parameters. Generic interface elements can specialize scalar temporary storage
when the concrete array element is proved.
Invalid-index branches retain possible exception allocation, framework cache writes,
and ambient resource/capability effects. Valid-index preconditions exclude those
branches. Known non-writing calls preserve array contents; unspecified or writing
calls still invalidate that knowledge.
Array's interface indexer throws `ArgumentOutOfRangeException`, whereas direct
array indexing throws `IndexOutOfRangeException`. Native IR currently retains an
Unknown bounds fault for the interface case; exact exception-type claims on those
paths remain Unknown.

The usage report applies this guard to each interface call, including writes.
A supported simple store is partial evidence; an arbitrary receiver is
`unmodeled-dispatch`; a compound assignment is `unmodeled-access`. The 109 original
IList index/collection Count sites do not gain blanket support from the new two API
rows: their standalone interface parameters still admit arbitrary implementations.
Concrete callers can now specialize those receivers through source helpers. The
worker regression loads the exact OSS0002 `Swap<T>` declaration from the corpus and
compares its `int[]` specialization in native IR against the compiled .NET method
on null, empty, singleton and multi-element arrays, including equal and invalid
indices. Normal results use no approximation; fault kinds retain the documented
interface bounds abstraction. The original declaration is not rewritten, and
arbitrary callers receive no implementation assumptions. Alias and smaller source
helper postconditions also run through the worker. The full swap postcondition
currently reaches the worker verification-condition construction budget; runtime
model evidence does not turn that Unknown into a proof.

### Highest remaining priorities

| Gap | Source sites | Required work before adding stronger models |
| --- | ---: | --- |
| `IList<T>.Item` | 56 reads, 13 writes | Array receivers tracked through aliases/captures/source helpers; model other concrete implementations |
| `Dictionary<TKey,TValue>.Item` | 40 reads, 1 write | Key/value state plus arbitrary comparer callback effects and exceptions |
| `ICollection<T>.Count` | 40 reads | Array receiver provenance implemented; resolve other concrete implementations |
| `Array.Length` | 32 reads | Existing contextual array lowering; the absence of a blanket catalog row is not absence of all support |
| `Comparer<T>.Compare` | 26 calls | Resolve comparer implementation and its callback behavior |
| `Dictionary.Add / ContainsKey` | 22 calls each | Comparer callbacks, dictionary state, allocation, and exception conditions |
| `HashSet<T>.Contains` | 16 calls | Comparer callbacks and set membership state |

Prioritize by real call sites, then choose a sound semantic slice. Add or extend the
model, runtime and native regression tests, then update the reviewed inventory. Do
not assign interface or comparer-driven calls the effects of a convenient concrete
implementation. Refresh the snapshot only after reviewing source/baseline changes.

The List indexer's named exception set reaches the portable effect summary even
while its other effects remain Unknown. Native opaque-call IR currently retains only
a may-throw bit, so a native AllowedExceptions claim for this getter remains Unknown;
a regression test records that boundary. Importing exact exception types into native
opaque calls is separate follow-up work, not a guarantee supplied by this batch.

See [the comprehensive gap backlog](gaps.md) for every ranked API/access row, all partial declaration evidence, cross-cutting limitations and completion criteria.
