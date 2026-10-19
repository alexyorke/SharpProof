# .NET API gap backlog

This is the committed work list for API support and its evidence. Baseline: merged
API inventory/provenance work at master `52cc38fea`, followed by the verified
collection and scalar slices below. Counts are static source sites,
not runtime frequency. A passing test, a modeled effect, and a proved caller
postcondition are separate claims.

The verified sections record chronological checkpoints. Later slices supersede
older descriptions of remaining boundaries; the universe, priorities and complete
tables reflect the current inventory and report.

## Universe and completeness

The complete declaration universe is the 59,639 rows of
[net10.0.tsv](net10.0.tsv), pinned to Microsoft.NETCore.App.Ref 10.0.13 / net10.0
and SDK 10.0.402; [the manifest](net10.0.manifest.json) records assembly checksums.
Of these, 3,966 type containers are explicitly excluded from behavioral support,
55,625 declarations have no catalog/audited-intrinsic evidence, and 48 have partial
evidence. No declaration is labeled fully modeled. The TSV is the exhaustive list
of declaration gaps; duplicating its 55,625 unmodeled rows here would create a
second authority. Fields, events, constructors, overloads and generic arity remain
separate entries. Absence of catalog evidence does not establish that contextual
language lowering is missing.

The source-use universe is all 200 selected original OSS targets, 28 original
synthetic targets and 10 sample files: 641 resolved sites, zero unresolved sites,
114 API/access/status rows. Every one is listed below, including partial models
that still have gaps. Dependency-only methods, metamorphic duplicates, repository
implementation code, dynamic/reflection calls and unexecuted workloads do not
contribute to this ranking. The CI JSON has source hashes and exact locations;
[the checked-in ranking](usage-ranking.generated.md) has origin/caller counts.

The inventory does not cover other framework/workload reference packs, SDK tools,
opt-in relational packs or all possible behavioral facets. Such areas are outside
this universe, rather than verified or absent. Runtime/IR differential evidence
also does not imply that every caller can be proved by the SMT verifier.

## Verified reference identity boundary

Compiler-owned closed class types now establish impossible entry aliases when
neither type belongs to the other's inheritance chain. Runtime identity erases
nested tuple element names, dynamic/object spelling and nullable annotations;
these spellings do not create disjoint CLR types. Exact constructed source-helper
receiver and parameter types preserve the caller's nominal identity.

Native counterexample replay checks compatible class ancestry and compiler-owned
interface/implementation evidence before sharing an identity across distinct
nominal views. Impossible closed class field aliases cannot authorize a refutation.
Base/derived, supported interface aliases and both-null values retain their tested
behavior. Missing legacy metadata and uncertified open generic views can still
yield Unknown, including legitimate aliases when generic arguments coincide.
Candidate search prefers separate non-null identities for uncertified typed views
only after decoding rejects an otherwise satisfiable model, over at most 64 nominal
reference types and every requested view of those types. This type-group limit
is separate from the unchanged small string/array/count witness size. Required
uncertified aliases can still yield Unknown; this preference adds no proof premise.
Unobserved entry fields remain unavailable to replay. Source helper substitution
preserves constructed owners, tuple and array types, and concrete array provenance.
This boundary does not add complete field disjointness proofs or arbitrary CLR
subtype materialization. Portable metadata is part of the canonical artifact and
its request-bound digest; malformed ancestry and interface metadata fail closed.

## Priorities and acceptance criteria

Work from the largest source counts, but split a family into sound semantic slices.
Do not grant arbitrary interface receivers or comparer callbacks a convenient
concrete implementation's behavior. A site stays open until its actual receiver,
access kind and value domains satisfy the production guard.

| Work item | Priority and present boundary | Concrete completion criteria |
| --- | --- | --- |
| A: IList / ICollection dispatch | 56 index reads, 13 writes, 40 Count reads; arbitrary standalone interface parameters remain open | Prove concrete receiver identity, add the concrete implementation's heap/effect model, and test aliases, joins, mutation, ref/closure escape and wrong implementations. Existing scalar-array provenance and exact List allocations/aliases are supported, including immutable generic source-helper parameters with caller implementation evidence; arbitrary incoming interface parameters remain open. |
| D: Dictionary | Fresh default construction and Count support all key/value domains; integer-key scalar/reference/opaque storage also flows through proven immutable source-helper parameters; the 40 reads, 22 Add, 22 ContainsKey and one write in the ranked corpus still lack receiver/domain evidence | Remaining: establish actual corpus receiver/comparer and generic/reference-key semantics; model incoming/custom comparers and callbacks, missing/null/duplicate keys, precise initialization/capacity/growth faults and partial mutation, views, removal and other constructors. Do not assume dictionary-typed parameters use default comparers. |
| C: Comparers and comparable values | 26 Comparer.Compare calls and nine IComparable.CompareTo calls; standalone generic/custom implementations remain open | Resolve concrete type and implementation, preserve initialization and callback effects, and validate signed/unsigned limits, nulls and argument order. Tracked Default integer aliases are supported and proven null/default source parameters; generic array-element specialization is separately tested. Arbitrary generic receivers do not gain those assumptions. |
| L: Lists and collection contents | Fresh List construction and concrete List Count, Add, Insert, GetRange and scalar/reference/opaque get/set share state; arbitrary interface receivers, unsupported operations on values and other List operations remain open | Model shared storage, capacity/growth and allocation, Count changes, bounds/argument faults, setter mutation and alias invalidation. List.GetItem's exception set alone does not supply content semantics. |
| S: HashSet / Queue / Stack | Contains/Add/Remove, enqueue/dequeue/push/pop and constructors remain open; concrete Counts only nonnegative | Add membership or sequence state, comparer callback behavior, empty/duplicate cases, allocation and mutation effects. Count nonnegativity does not prove contents or strict positivity. |
| R: Arrays | SZ-array Length already has contextual support; general System.Array, multidimensional and array helper methods need distinct models | Keep contextual guards in ranking. Model Clear/Copy/Resize state, overlapping ranges, allocation/ref replacement and precise faults. Reference covariance and arbitrary interface implementations remain open. |
| T: Strings | Length/index/equality/basic predicates partially modeled; construction, split/replace/format/join/substring and others remain open | Preserve UTF-16/ordinal domains, nulls, bounds, overload/culture distinctions, allocations and effects. Check native proof support separately from catalog facets. |
| N: Numeric / conversion | A few numeric helpers are partial; conversions, floating operations and formatting need separate work | Specify checked/unchecked overflow, NaN/infinity/rounding and culture/formatting as applicable; compare IR with runtime across boundary values and refute wrong claims. |
| E: Exceptions | Selected constructors are partial; other constructors and exact native opaque-call exception sets remain open | Preserve constructor arguments, allocation/cache effects, null/bounds behavior and exact exception identity. Portable named exception evidence must be carried into native IR before claiming AllowedExceptions proofs. |
| F: Delegates / enumeration / LINQ / parallelism | Invoke, iterator members, LINQ and Parallel.For remain open | Model callback effects, captured mutable state, deferred execution, iteration faults, allocation and parallel behavior; preserve conservative fallback until covered. |
| O: Ambient state and other APIs | Date/time, randomness, IO, volatile reads, diagnostics, reflection and object behavior need individual contracts | Define nondeterminism, ambient capabilities, observable reads/writes, exceptions and allocation per overload. Do not force deterministic scalar semantics. |

## Verified dictionary state slice

Implementation/test commit `dca85cfd9` closes successful local-state reasoning for
fresh parameterless framework `Dictionary<TKey,TValue>` instances whose keys are
the nine primitive integer types and whose values are supported scalars. Shared
keyed heap storage carries membership, Count and values through aliases, normal
Add, present-key reads, ContainsKey and simple index assignments. Existing-key
assignment keeps Count unchanged. Typed symbolic keys retain their entire bit
width, including Int64/UInt64; argument values are captured before later writes.

The tests include 18 compiled CLR/IR oracle cases, proof/refutation pairs for all
nine key types, colliding long hash codes, separate receiver identities, fresh and
mixed joins, unknown incoming/custom comparers, ref/closure escape, unknown writers
and a key computed before RHS mutation. Runtime initialization retains allocation,
static-cache/capability and fault uncertainty. Native replay observes only a fixed
set of real primitive CoreLib dictionary operations before choosing an already
initialized normal path. Failed observations cannot certify that path.

Verification on commit `a80a22ad2`: all 314 changed trusted-code lines are covered
(100%, no uncovered lines), with comparison master `4d456246a`. The canonical
container run passes 1,979 frontend tests, 998 selected worker tests, 477 IR tests,
357 SMT tests, 56 verification tests, 28 inventory/evidence tests and six ranking
tests. This filtered cohort certifies changed-code coverage; the full aggregate
coverage and exact-head CI gates still determine merge readiness. No coverage
floor changed. The SDK, reference assembly bytes and catalog digest are unchanged.
Exactly five evidence rows changed; four previously unmodeled declarations now
have partial evidence.

Follow-up commit `5a794ca6c` closes alias escape propagation for this slice.
Escape exclusions follow every alias definition and CFG capture/join through a
deduplicated work list; publishing an alias also removes the original receiver's
ownership fact. Three direct provenance controls reproduced the previous gap;
all nine frontend domain/depth controls now pass. Native alias publication stays
Unknown, with a CLR probe showing that an escaped alias can change comparer state.
All 177 selected worker/loop/artifact/replay controls, eight keyed IR controls and
the mathematical-integer-key SMT rejection test pass. Keyed loop cuts forget
memory and replay preserves 32/64-bit keys. Framework observations that disagree
with the expected state now decline replay authority instead of throwing.
MR #317 auto-merged as `3809aaf3b` after all 25 CI checks passed, including aggregate coverage and platform consumers.

This closes a semantic slice, not the Dictionary work item or its corpus sites.
The 40 ranked index reads span 13 source files. Representative declarations in
those files use `TVertex/int`, `int/TVertex`, `T/int`, `T/long`, nested graph/trie
types, and `uint/BigInteger`; these need type, value-storage and comparer/callback
models beyond the verified integer-key state and supported scalar/reference values. All ranked dictionary index reads,
Add/ContainsKey calls and the index write still show unmodeled dispatch. Four parameterless
corpus constructions gained empty-state support first; the four generic-key constructions
now also have partial construction/Count support. Generic-key content operations remain open. Missing/duplicate-key paths retain opaque fallback
and have explicit Unknown controls; their original proof probes remain preserved
under `artifacts/api-coverage/open-gap-probes/`. Incoming or escaped references,
custom comparers, reference/unbound-key operations, capacities and constructor overloads,
views, removal/clear, and precise growth/exception behavior remain actionable gaps.

## Verified reference dictionary values

The integer-key state model now stores reference identities as well as scalars.
Supported reference values include object, string, class, interface and delegate
values, admitted arrays whose elements are not arrays, and type parameters with
reference-type evidence such as a class constraint. Native tests prove returned
identity and alias updates and refute claims about a distinct reference. A null
value retains membership and remains different from an absent key. Symbolic Int64
keys retain distinct values across their full width.

A stored value can leave through a getter without publishing its owning dictionary.
Receiver publication, self-storage, closure capture and aliases of those escapes
still remove the ownership fact. Direct frontend controls cover these distinctions.
Compiled CLR/IR cases cover snapshots, replacement through aliases, stored nulls,
array identity and shared mutation. A CLR/IR probe also verifies that dictionary
state operations never call the stored values' virtual equality or hashing methods.

Pure clauses preserve compiler-established CLR reference conversions to object,
including arrays and reference-constrained type parameters. This permits native
ReferenceEquals result contracts and generic reference-identity proofs, with
wrong-claim controls. It does not admit boxing or arbitrary generic operators.
Keyed entry cells have no complete replay reconstruction: their values stay
unconstrained until ordered stores supply evidence. Removing mandatory freshness
reads of unknown entry cells lets valid stored references replay without inventing
entry contents. Existing freshness controls for fields without keys remain in place.

The five dictionary registrations use the more accurate evidence label
`fresh-default-integer-key-dictionary-state`. The declaration counts remain 43
partial and 55,630 unmodeled; no declaration is fully modeled. SDK, reference pack,
assembly checksums and catalog digest are unchanged. The usage ranking still has
all 40 dictionary reads, 22 Add calls, 22 ContainsKey calls and one write awaiting
actual receiver/domain evidence. At the reference-value snapshot, all eight parameterless
corpus constructions lacked supported domains; the empty-state extension below closes
the value-domain gap for four of them. The generic-key construction extension below
covers the paired four; their content/comparer operations remain open.

This closes reference-value storage/identity within the owned integer-key slice.
Reference or unsupported keys, unconstrained generic/structure values, arrays of
arrays, arbitrary incoming/custom-comparer receivers, missing/duplicate keys,
constructors with capacities/comparers, growth faults, views and removal stay open.
Reference-value contents and invoked callbacks need their own models; storing a
reference does not model every operation on the referenced object.

Verification on implementation commit `38f110971`: all 24 coverage-counted changed
trusted-code lines are covered (100%, no uncovered lines), compared with master
`0c52c313d`. The report retains two source-mapping diagnostics for a local function
declaration and a continued property pattern. The canonical container passes 2,003
frontend tests, 640 contract tests, 659 selected worker tests, 80 selected IR tests,
74 selected SMT tests, 35 selected effects tests including the strict inventory
checks, and 30 selected Gates tests including usage ranking. The filtered collection
certifies changed-code coverage; it does not certify full aggregate coverage.
Complete exact-head CI remains required before merging. No coverage floor changed.

## Verified empty dictionary state across value domains

Parameterless framework construction with primitive integer keys establishes empty
Count and membership even when the value type is an opaque structure or an open
type parameter. These operations do not interpret, compare, hash or construct a
stored value. Native controls prove zero Count and absent-key membership and refute
an incorrect Count claim. CLR/IR comparisons cover decimal, DateTime, Guid and
BigInteger values; an additional CLR probe uses a value constructor that throws
and verifies that empty dictionary creation and membership never invoke it.

Content operations retain their value-domain guard. Add, index reads and index
writes with opaque values still take conservative fallback, and their unknown
writes remove empty-state facts. Capacity/comparer constructors remain outside
this state slice. An empty collection initializer is removed by the compiler CFG;
its default-comparer receiver fact does not grant empty contents after a nonempty,
unsupported initializer write. Receiver publication and alias/closure exclusions
remain unchanged.

This moves four actual corpus constructions into partial support:
BellmanFordShortestPaths line 134, BipartiteColoring line 102,
BreadthFirstShortestPaths line 90 and DijkstraShortestPaths line 93, in the pinned
`Algorithms/Graphs` sources embedded in the corpus manifest. Each constructs
`Dictionary<int, TVertex>` (or `Dictionary<int, T>`), whose value parameter has only
an IComparable constraint. At that snapshot the paired four generic-key constructions
remained unsupported; the following extension covers their construction and Count. All 40 dictionary reads, 22 Add calls, 22 ContainsKey calls and one
write still need receiver/content or comparer evidence. Constructor support does
not prove the surrounding graph algorithms or every operation on their fields.

That intermediate ranking had 641 sites with zero unresolved sites in 115
API/access/status rows; the following construction extension combines the two constructor rows. At that stage, the complete declaration universe and all 43 partial facet
registrations were unchanged. Constructor, Count and ContainsKey evidence now also
executes the generic-value empty-state probe; Add/indexer evidence retains its
scalar/reference scope. No SDK, reference assembly, catalog or inventory pin changed.

## Verified default construction and Count for generic keys

Parameterless framework dictionary construction and owned Count no longer require
primitive keys. On normal constructor return the result is fresh and has zero
Count, regardless of key or value type. For a non-primitive or open key type,
default-comparer initialization remains an opaque operation with allocation,
exception and arbitrary existing-state effects. Fresh dictionary state is
established only after its normal continuation, preserving those effects without
inventing access to the unpublished result.

Native controls prove empty Count for unconstrained, comparable-constrained,
class-constrained and string keys. CLR probes verify construction without invoking
the key's constructor, static initializer, virtual equality or hashing methods.
CLR/IR normal-state comparisons cover string, object and Guid keys. These IR
comparisons explicitly consume approximation for initialization; they provide no
concrete native replay authority. A wrong Count claim remains Unknown when the
initializer cannot be replayed concretely. Unrelated heap facts and zero Count
after an opaque mutation are not preserved.

This moves the paired four generic-key corpus constructors into partial support:
BellmanFordShortestPaths line 133, BipartiteColoring line 101,
BreadthFirstShortestPaths line 89 and DijkstraShortestPaths line 92. All eight
parameterless corpus constructions now have partial construction support. The
ranking has 114 API/access/status rows at the same 641 sites, with zero unresolved
sites. Constructor and Count gain executed construction-state evidence; inventory
counts and SDK/reference/catalog pins remain unchanged.

This does not model generic/reference-key hashing, equality, null-key behavior or
membership/content operations. The 40 index reads, 22 Add calls, 22 ContainsKey
calls and one index write still need actual receiver/comparer/content evidence.
Incoming or escaped receivers, explicit comparer/capacity constructors, precise
initialization faults and replay, growth, removal and views remain open.

## Verified owned List state and interface aliases

Fresh parameterless framework List construction now establishes a zero Count.
Count, Add and simple indexed reads/writes share keyed heap state across concrete
List and proven IList/ICollection aliases. Contents support the scalar/reference
domains, including class-constrained generic values; empty construction and Count
do not require interpreting the element type. Receiver and index snapshots retain
C# evaluation order when an assignment RHS rebinds a local or mutates the same cell.
CFG collection initializers use the same constructor and Add paths.

Normal initialization retains possible empty-array allocation and static cache
writes. Growth retains possible allocation and arbitrary faults; its failure path
keeps unknown effects and partial mutation. A full Count cannot wrap on successful
Add. Invalid indexes retain resource/culture callback effects and unknown ancillary
faults before the intended ArgumentOutOfRangeException. The audited framework
[List implementation](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/Collections/Generic/List.cs)
initializes an empty array and copies stored values without element constructors,
equality or hashing callbacks. CLR probes check that boundary on the pinned runtime.

Native proof controls cover all nine integer types, Boolean, reference identity,
nulls, generic Count, class-constrained values, aliases, distinct lists, initialized
lists, bounds and receiver/index/RHS order. Wrong claims retain Unknown without
concrete initialization/growth replay evidence. CLR/IR normal-state and bounds
comparisons explicitly consume approximation and do not grant native replay
authority. Unknown writers, publication, ref/closure escape, mixed incoming joins,
cycles and subclasses that replace interface members do not acquire List content assumptions.

All 11 parameterless corpus constructor sites gain partial construction support,
including the two `SieveOfAtkin` collection initializers. Three of 13 explicit Add
sites now have receiver/domain evidence. The remaining ten Add calls, 56 IList
reads, 13 IList writes, 40 ICollection Count reads and 16 concrete List reads still
lack the required owned receiver or value domain. Concrete incoming List Count
keeps its separate nonnegative-only model. This ranking has 115 rows at the same
641 sites, with zero unresolved sites. Seven evidence rows gain executed List
probes; two declarations move from unmodeled to partial evidence. SDK, reference
assembly and catalog pins are unchanged.

Verification at integrated implementation head `38c3eb639` against master
`e34429555`: 2,036 frontend tests, 958 selected worker tests, 29 inventory tests
and seven ranking tests pass in the canonical container. Full CI covers all
30,274 aggregate executable points and all 211 changed executable trusted-code
lines (100%). The List probes select bounded normal initialization/growth choices;
these coverage results do not upgrade approximate replay or close other contexts.

Incoming/escaped List content state, arbitrary interfaces and subclasses,
capacity/enumerable constructors, compound
updates, range/removal/sort operations, capacity/version/enumeration relationships,
precise growth failure state and concrete replay remain open. No complete API or
work item is closed by this slice.

## Verified incoming concrete List state

Framework `List<T>` Count, Add and simple indexed get/set bound to base members use
shared Count and keyed content state for incoming concrete receivers as well as
fresh instances. Count and indexed reads can appear in contracts, including entry
preconditions and Old snapshots. The value domain remains supported scalar and
reference types; Count does not interpret the element type. A subclass hiding or
reimplementing a member cannot change an explicitly bound base List operation.
Arbitrary incoming IList/ICollection calls still require implementation evidence.

Replay reconstructs only queried owner/key entry cells, preserving typed keys,
reference/null identity and cycles. Other cells remain unavailable. Unknown writes
invalidate entry fallback; ordered stores and Old snapshots retain their distinct
semantics. Oversized List witnesses are rejected under the query resource budget;
this does not constrain the universal SMT proof domain. A solver choosing such a
witness can still yield Unknown, so this slice does not promise every unbounded
List claim can be decided. Growth, allocation and fault uncertainty remain.
Reviewed effect facets bound growth-failure calls just as they bound ordinary
calls; missing evidence still uses the opaque fallback. Known capabilities do not
imply no faults or no allocations.

Six more corpus sites gain partial support: indexed reads in PrimesList lines 148
and 149 and BTree line 53, plus Add in BucketSorter lines 32 and 72 and BTree line
44. At this checkpoint, 13 concrete List reads and seven Add calls still lacked
value-domain evidence; the opaque storage slice below closes that boundary. The ranking has 116 API/access/status rows at
641 sites, with zero unresolved sites. The three direct List declarations gain
`intrinsic=concrete-list-state`; all 45 partial declarations retain their other
facets, and the SDK, reference bytes and catalog pins are unchanged.

Native tests cover shared Count/content proofs, wrong-claim refutations, incoming
scalar boundaries, Boolean/reference/null values, source helpers, aliases, Old
snapshots and unknown writers. Keyed IR and SMT tests cover owner/key separation,
full-width keys, missing cells, cycles, invalidation and oversized witnesses.
Executed inventory probes compare incoming Count, set, Add and get against CLR
behavior; their selected normal growth path consumes approximation and grants no
concrete growth replay authority. Remaining interfaces, operations on opaque values,
compound updates, other List operations and precise growth behavior stay open.

Canonical container validation passes 2,041 frontend tests, 983 selected worker
tests, 481 IR tests, 363 SMT tests, 640 contract tests, 29 inventory/evidence tests
and eight usage-ranking tests. All 63 analyzer mode/effect controls pass, including capability evidence alongside
unknown growth faults and allocation. Layout passes for all 59 projects. CI still
requires aggregate and changed-code coverage plus platform/package consumers.

Worker verification includes the complete 5,505-test suite plus the receiver-assignment
proof, and the complete analyzer
suite passes all 1,134 tests. Effect-contract diagnostics
retain the first source location and prefer a mutation/call summary at that same
operation over its broad internal read site. A compiled read-only List control
still reports the read violation; the existing mutation assertion is unchanged.

Assignment-receiver controls distinguish assigning a fresh List to a local
interface receiver from assigning an unknown incoming implementation. The native
Count proof and both provenance controls retain the existing freshness path.

## Verified exact List receivers in source helpers

An exact framework List allocation now preserves its runtime implementation through
by-value publication and immutable source-helper IList/ICollection parameters.
Generic Count, Add and simple indexed get/set share the caller's storage through
nested calls and extension-method syntax. Scalar and admitted reference element
representations follow their own declaration symbols into the helper, including
class-constrained caller types. Reference specialization is scoped to helpers
declaring a supported List/IList/ICollection parameter; unrelated generic source
calls retain their existing scalar domain and corpus diagnostics. Knowing an element representation alone never
establishes an interface implementation. An incoming List-typed parameter can hold
a subclass that reimplements interfaces, so it supplies no interface dispatch proof.

Allocation identity is separate from content knowledge. Unknown writers invalidate
contents even after by-value publication. Rebinding, ref escape, closure capture,
mixed definitions and unknown interface implementations retain conservative fallback.
Helper preconditions use caller state; helper Ensures/Old expressions are not
verified at specialized source call sites. A standalone arbitrary
interface claim remains Unknown even when a proven call context can model its body.

Native tests include scalar limits, Boolean/reference/null identity, shared mutation,
nested/extension calls, contracts and negative dispatch/escape controls. Six compiled
CLR/IR oracle cases execute the original OSS0002 generic Swap from the pinned corpus,
including early returns, normal swapping and invalid indexes. The inventory's List
probes additionally execute scalar and reference source-helper Count/Add/get/set and
compare results with CLR behavior; these remain approximation evidence for normal
growth, not concrete replay authority for growth or initialization.

A feasibility query could previously choose an oversized irrelevant entry List size
and hit the replay budget even when its postcondition was provable. SAT witness
selection now prefers small observed List sizes, like existing string/array witnesses.
Failed preferences retry the unbounded query. Tests retain large counterexamples,
full-domain proofs and oversized-witness rejection under the unchanged resource limit.

Canonical container validation passes 5,548 worker tests, 2,049 frontend tests,
368 SMT tests, 481 IR tests, 640 contract tests, 1,128 analyzer tests,
29 inventory/evidence tests and eight usage-ranking tests. The full 471-test Gates
suite also passes, preserving the canonical corpus snapshots and Unknown-reason
ratchets without baseline changes.

This closes a contextual source-helper slice. The static report still has 116 rows
at 641 sites and zero unresolved sites: the original generic interface declarations
have no caller allocation evidence. Their 56 IList reads, 13 writes and 40 ICollection
Count reads therefore remain actionable; they must not all be credited as modeled.
The declaration universe, support statuses, SDK/reference pins and coverage floors
are unchanged. Other List operations, operations on opaque values and precise growth/fault
semantics remain open.

## Verified List insertion and range storage

Framework `List<T>.Insert`, proven exact `IList<T>.Insert` and framework
`List<T>.GetRange` now use the shared List Count/content fields. Insert shifts
slots backward before storing the new value and increasing Count. GetRange
allocates a distinct List and copies the selected slots. Later assignments to
source and result remain independent while reference elements retain identity.
The existing scalar, reference, null and opaque value domains are copied without
interpreting unsupported value operations. Immutable generic source helpers use
the same storage; arbitrary incoming interface receivers remain open.

Index/count arguments retain source evaluation order, including named arguments,
receiver rebinding and null receivers. Negative index/count and invalid ranges
retain the runtime's argument-fault distinction. Resource lookup still has opaque
callbacks, writes, allocation, ambient capabilities and ancillary exceptions;
initialization and growth uncertainty remain. The model does not promise precise
capacity, allocation counts, fault messages, enumerator versioning or reflection
access to private List fields. Those operations continue through their existing
opaque fallback and invalidate contents where appropriate.

The copy/shift lowering peels four iterations, then executes an unrestricted
residual loop. This preserves all lengths. Native content proofs cover small
concrete ranges; arbitrary symbolic or longer content proofs can still need heap
loop invariants and remain Unknown. CLR/IR differential controls cover sizes
0, 1, 4, 5 and 12 across the prefix boundary, signed/unsigned scalar limits,
Boolean/reference values, opaque decimal/NaN/enum/Guid/nullable/struct payloads,
copy independence, aliases, mutation, evaluation order and bounds. The selected
normal growth and fault paths consume approximation and confer no replay authority.
Native positive/wrong-claim, effect and unknown-writer/interface controls accompany
these runtime comparisons. Open generic entry roots observed through aliased
`List<T>`/`List<U>` or `List<object>` views currently report
Unknown/UnsupportedExpression. Their CLR mutation contradictions and native
boundary controls do not establish modeled cross-generic alias semantics.

Three declarations gain partial intrinsic evidence: List.Insert, IList.Insert
and List.GetRange. The four ranked Insert sites and two GetRange sites gain
receiver/domain evidence; this does not prove their callers or close work item L.
Clear, Remove/RemoveAt, Contains/IndexOf, AddRange/InsertRange, other constructors,
capacity and enumerator semantics remain separate gaps. SDK/reference/catalog
pins and the 59,639-row universe are unchanged.

## Verified opaque List value storage

List Add and indexed get/set now store and copy the existing opaque value domain,
including unbound type parameters, enums and non-scalar structs. Concrete List
receivers and proven exact IList aliases share these values through mutation and
nested source helpers. Element specialization preserves each declaration's own
opaque representation at the helper boundary. Arbitrary interface implementations
still receive no List dispatch assumptions.

This closes the remaining value-domain gap at 13 concrete indexed reads and seven
Add sites in the ranked corpus. All 16 concrete List reads and 13 Add sites now have
partial receiver/domain evidence. The current ranking has 114 API/access/status
rows at 641 sites, with zero unresolved sites. Partial evidence does not establish
that every expression, caller body or behavioral facet is modeled. The four Add
and indexer declarations gain `intrinsic=opaque-list-value-storage`; declaration
statuses, SDK/reference pins and the catalog digest are unchanged.

Native probes reproduced nine failures before the change and now prove shared
Count and nested helper behavior for unbound/struct/class/comparable type parameters
and decimal, floating, Guid, DateTime and nullable inputs. Thirteen CLR/IR differential
cases compile the original OSS0002 Swap and verify the copied value, including
full decimal bits, NaN payloads, nullable values, large integers and structs with
managed references and enum bits. Executed inventory evidence also checks opaque helper storage
against CLR decimal bits and rejects a deliberately wrong returned-value claim.

Canonical container verification on `48a27b949` passes 5,618 worker tests,
2,049 frontend tests, all 471 Gates tests, 1,134 analyzer tests, 640 contract tests,
481 IR tests, 368 SMT tests and 29 inventory/evidence tests. Both changed executable
trusted-code lines are covered, with no uncovered or unmapped changed lines. The
filtered run does not certify the full aggregate floor; CI enforces that floor.

Opaque means stored, passed and returned without interpretation. Struct field
access, numeric operators, conversions, equality/comparer callbacks and defaults
still need independent models. Those gaps stay open, with negative controls.
Boxing controls retain allocation uncertainty for opaque and nullable elements.
Existing alias invalidation, dispatch and growth/fault uncertainty remain. Normal
CLR/IR growth choices consume approximation and grant no native replay authority.

## Verified constructor-origin dictionary source parameters

Parameterless framework dictionary allocation identity now flows through aliases,
by-value publication and immutable source-helper parameters. Generic helpers
specialize key/value storage from the caller's known domains and share Count,
membership and values. Primitive-integer-key operations copy opaque values as
atoms without interpreting their operators, fields, equality or conversions.
Specialized source preconditions can read Count, ContainsKey and present values.
Caller proofs use shared body state. Helper postconditions and Old heap
expressions are not verified at specialized call sites; standalone incoming
dictionaries remain unsupported. Their postcondition verification remains open.

Allocation lineage does not prove the current comparer. A constructor-written
heap predicate guards the primitive model after publication. Unknown writers
invalidate that predicate and the contents, preserving opaque fallback. Incoming
parameters, custom/explicit-comparer construction, ref escape, captured mutable
parameters and mixed unproven receivers acquire no constructor-origin fact.
Source preconditions remain obligations and cannot restore comparer knowledge.

All 196 Dictionary worker tests pass, including 25 CLR/IR comparisons, source
preconditions, key/receiver evaluation order and comparer mutation controls.
Opaque insertion proves Count one and refutes Count zero without invoking value
comparison/equality/hash callbacks. Failed initialization observations still
cannot certify replay. Separate snapshot tests validate Old lowering with an
explicitly supplied origin; these are not specialized postcondition proofs.
False helper Ensures controls prove the actual return and refute the claimed
wrong return, confirming those helper postconditions are not imported assumptions.
Nine reproduced regressions also verify that generic construction specialized to
primitive integer keys preserves caller memory. Non-primitive-key initialization
retains arbitrary existing-state effects.
The inventory's 29 tests pass with executed positive/wrong-claim probes for shared
helper state and opaque atom storage. Exactly five existing partial declaration
rows gain evidence; the reference manifest and declaration totals are unchanged.
All 40 Dictionary frontend domain tests and nine ranking tests also pass. The
pre-integration core run passes 2,057 frontend and 472 Gates tests. Integrated-head
coverage and exact-commit CI remain gates.

The original corpus still has 85 unmodeled Dictionary content sites: 23 use
primitive keys but obtain receivers from static caches (9), virtual Trie Children
properties (10), or graph fields (4). The remaining 62 also require generic-key
equality/hash/comparer semantics. Capacity constructors and ref parameters remain
separate barriers. This removes the source-helper and opaque-storage gaps only
within the proven domain; it does not close those corpus sites or work item D.
Precise initialization/growth/faults, missing/duplicate/null keys, views, removal,
other constructors and arbitrary incoming/custom comparer behavior remain open.

## Cross-cutting gaps previously identified

- Array-backed IList bounds failure can invoke user CultureInfo callbacks during
  resource lookup. Those callbacks may throw arbitrary exceptions or mutate
  reachable state. The intended ArgumentOutOfRangeException now has an exact
  native/portable fault kind and tested catch hierarchy, but narrowing ancillary
  callback effects requires culture/callback provenance. Invalid paths retain
  exception allocation, framework cache writes and capability uncertainty.
- Scalar SZ-array aliases/captures/immutable source parameters, matching `as`
  conversions to framework IList/ICollection receivers, compound index assignments
  and pre/post updates are supported. Unknown or mixed bindings, cycles, ref
  escapes, closure-captured mutable state, custom derived interfaces and reference
  covariance need their own sound models. Rebinding must preserve identity
  snapshots; opaque writers must invalidate content knowledge.
- Immutable scalar-array-backed IList<T> helpers now specialize returns of the
  same T, including method, local-function, containing-type and nested generic
  contexts. Native proofs/refutations and compiled CLR/IR boundary checks cover
  the supported scalar types. Direct scalar SZ-array T[] and by-value scalar T
  parameters now preserve typed storage through nested calls and reassignment;
  array reads, full-content Swap, effects and CLR/IR faults are verified. This
  signature gap was closed by implementation/test commit `8a3dd8a39` (4,976 worker,
  1,843 frontend and 468 Gates tests pass; changed trusted code is 304/304 covered).
  Defaults of these parameter-specialized scalar types now produce exact zero/false
  values, including stores to generic arrays. Commit `b087332db` verifies 80 native
  proof/refutation cases, ten CLR/IR scalar-boundary cases and effect/fallback
  controls; 1,287 selected worker, 1,853 frontend and 468 Gates tests pass, with
  changed trusted code 308/308 covered. Closed scalar method and containing-type
  arguments now establish each declaration's own representation without value
  parameter evidence, including distinct result parameters and nested generic
  owners. Commit `56d140aa9` adds 120 native proof/refutation cases, eleven CLR/IR
  boundary cases and method/owner budget sweeps; 1,406 selected worker, 1,866
  frontend and 468 Gates tests pass, with changed trusted code 317/317 covered.
  Unbound or unsupported non-scalar type arguments, other generic operators, ref/out
  signatures, reference/non-SZ arrays and mutable/captured or otherwise unproven
  interface receiver representations remain open. Type specialization alone does
  not establish collection contents, default/custom dispatch or callback effects.
  Generic local IList/ICollection Count branches
  and full Swap postconditions retain source declaration/ordinal/assembly ownership,
  with native proofs/refutations and compiled CLR/IR normal/fault agreement.
- Count >= 0 is weaker than a collection representation invariant. Fresh integer-key
  dictionary Count/content consistency is verified within the documented domain.
  Fresh owned Lists and incoming concrete Lists now also relate Count to contents. Arbitrary list interfaces, interpreting opaque values, dictionary receivers, queues and stacks remain open.
- Default comparer getter initialization can allocate, throw and affect ambient
  state. Proving a comparison result does not erase these effects. Parameters that
  may be custom comparers and generic types without concrete evidence still need
  dispatch/provenance work. Commit `eb91e74aa` closes source-parameter normalization
  for independently proven null/default inputs: `??`, `??=` and explicit null
  branches retain an all-write domain invariant, including mutable parameters,
  nested helpers, omitted optional arguments and all nine primitive integer types.
  Null receivers fault after argument evaluation, and pure clauses retain their
  non-null safety condition. All 57 new native cases pass, with 1,884 frontend
  tests and five focused domain/entry/depth controls. Both array-interface and
  comparer aliases now reject unknown parameter entry values even when a later
  assignment has a known representation. Arbitrary incoming/custom comparers,
  ref/closure escapes, unbound/non-scalar types and other collection implementations
  remain open; normalization does not manufacture a default receiver for them.
  Closed scalar generic default comparers now resolve local-function type parameters
  through the same declaration-owned type resolver. Typed SByte/Byte/Int16/UInt16/Char
  CompareTo and proven default-comparer calls retain the exact promoted Int32
  difference; the larger integer types retain -1/0/1. Implementation/test commit
  `38e35988b` verifies 33 native cases and ordered CLR/IR boundary pairs for all nine
  integer types; 1,439 selected worker, 1,868 frontend, 468 Gates and 27 inventory
  tests pass, with changed trusted code 319/319 covered. The five new intrinsic
  registrations add partial evidence without claiming universal API coverage.
  Commit `b898a640f` sweeps the entire 0-4096 construction allowance for real
  precondition and exception-filter imports, compares successful IR results with CLR
  execution, and covers both budget exits exposed by aggregate CI coverage.
  Commit `40cd4a402` closes the 18 preserved wrong-contract replay cases for
  the nine primitive integer default comparers. Replay observes only the fixed
  framework getters, confirms a successful return and retains their actual
  singleton identities. Explicit object upcasts preserve that identity. All 39
  targeted worker controls pass, including distinct char/ushort singleton values,
  unavailable observations and unrelated opaque reads that must remain Unknown;
  469 IR, 1,868 frontend and 56 verification tests pass, with two additional
  framework ownership controls rejecting source-defined lookalikes. Symbolic
  initialization, allocation, cache writes and faults remain conservative.
  Commit `390ec0933` closes the two preserved Object.ReferenceEquals replay
  probes and adds framework-only identity lowering for bodies and clauses.
  Nulls, aliases, distinct objects, overridden equality and named-argument effects
  have native controls and ten CLR/IR oracle cases. All 1,880 frontend tests and
  27 inventory tests pass. The inventory gains one partial declaration, with a
  passing semantic probe and wrong-claim rejection. Argument conversion, boxing,
  allocation and fault models still determine which complete callers are supported;
  this does not close arbitrary object behavior or every API facet.
- Catalog and audited-intrinsic evidence gates require executed behavioral probes
  and wrong-claim controls for modeled requirements. They do not automatically map
  every repository test to every possible supported behavior. New facets need an
  explicit API/facet/test mapping, runtime boundary cases and native proof/fallback
  tests. Unmapped behavior remains unchecked, rather than counted as tested.
- The .NET 10 refresh adds 3,219 declaration identities and removes 51 relative to
  the prior .NET 9 baseline. Thirteen removals are same-ID assembly moves; 38 are
  ARM SVE intrinsics absent under their previous identities. Existing same-identity
  evidence has zero changes. New/unmodeled declarations remain explicit backlog,
  not newly supported APIs. Future SDK changes require the same reviewed refresh.

## Closing a work item

1. Select exact API/access/receiver sites from CI's usage JSON. Record the semantic
   slice and all fallback conditions before changing the model.
2. Add implementation, compiled .NET versus IR boundary/alias/fault probes, a
   deliberate wrong claim, and native proof plus conservative fallback tests.
3. Add API/facet/test evidence for each new modeled facet. Register any trusted
   production source in the acceptance manifest. Preserve all coverage floors.
4. Run the inventory, evidence and ranking tests in the canonical Linux container;
   review identities, dispatch changes, assembly bytes and semantic digest before
   refreshing checked-in baselines. Never use candidate mode as a CI bypass.
5. Update this table and remaining boundaries only after verification. Require
   unchanged corpus ratchets, full trusted-code coverage and exact-head CI before
   calling an MR ready. A partial slice closes only that slice.

## Every ranked API/access gap

The action letter refers to the work items above. `partially-modeled` still needs
review of unsupported facets/contexts; it is not a fully covered API.

| API | Access | Sites | Status | Work item |
| --- | --- | ---: | --- | --- |
| P:System.Collections.Generic.IList`1.Item(System.Int32) | read | 56 | unmodeled-dispatch | A |
| P:System.Collections.Generic.Dictionary`2.Item(`0) | read | 40 | unmodeled-dispatch | D |
| P:System.Collections.Generic.ICollection`1.Count | read | 40 | unmodeled-dispatch | A |
| P:System.Array.Length | read | 32 | partially-modeled | R |
| M:System.Collections.Generic.Comparer`1.Compare(`0,`0) | call | 26 | unmodeled-dispatch | C |
| M:System.Collections.Generic.Dictionary`2.Add(`0,`1) | call | 22 | unmodeled-dispatch | D |
| M:System.Collections.Generic.Dictionary`2.ContainsKey(`0) | call | 22 | unmodeled-dispatch | D |
| P:System.String.Length | read | 22 | partially-modeled | T |
| M:System.Exception.#ctor(System.String) | call | 18 | partially-modeled | T |
| P:System.Collections.Generic.List`1.Count | read | 18 | partially-modeled | L |
| P:System.String.Chars(System.Int32) | read | 18 | partially-modeled | T |
| M:System.Collections.Generic.HashSet`1.Contains(`0) | call | 16 | unmodeled | S |
| P:System.Collections.Generic.List`1.Item(System.Int32) | read | 16 | partially-modeled | L |
| M:System.Collections.Generic.List`1.Add(`0) | call | 13 | partially-modeled | L |
| P:System.Collections.Generic.Comparer`1.Default | read | 13 | unmodeled | C |
| P:System.Collections.Generic.IList`1.Item(System.Int32) | write | 13 | unmodeled-dispatch | A |
| M:System.Collections.Generic.HashSet`1.Add(`0) | call | 12 | unmodeled | S |
| M:System.Collections.Generic.List`1.#ctor | call | 11 | partially-modeled | L |
| M:System.Collections.Generic.Queue`1.Enqueue(`0) | call | 10 | unmodeled | S |
| M:System.Collections.Generic.HashSet`1.#ctor | call | 9 | unmodeled | S |
| M:System.IComparable`1.CompareTo(`0) | call | 9 | unmodeled | C |
| M:System.Collections.Generic.Dictionary`2.#ctor | call | 8 | partially-modeled | D |
| M:System.Action`1.Invoke(`0) | call | 7 | unmodeled | F |
| M:System.Collections.Generic.KeyNotFoundException.#ctor(System.String) | call | 7 | unmodeled | T |
| M:System.String.IsNullOrEmpty(System.String) | call | 7 | partially-modeled | T |
| M:System.ArgumentException.#ctor(System.String) | call | 5 | unmodeled | T |
| M:System.Collections.Generic.Dictionary`2.#ctor(System.Int32) | call | 5 | unmodeled | D |
| M:System.Collections.Generic.Queue`1.Dequeue | call | 5 | unmodeled | S |
| M:System.Linq.Enumerable.ToList``1(System.Collections.Generic.IEnumerable{``0}) | call | 5 | unmodeled | F |
| M:System.Random.Next(System.Int32,System.Int32) | call | 5 | unmodeled | N |
| P:System.Collections.Generic.KeyValuePair`2.Value | read | 5 | unmodeled | O |
| P:System.Collections.Generic.Queue`1.Count | read | 5 | partially-modeled | S |
| M:System.ArgumentOutOfRangeException.#ctor(System.String) | call | 4 | unmodeled | T |
| M:System.Collections.Generic.List`1.Insert(System.Int32,`0) | call | 4 | partially-modeled | L |
| M:System.Console.WriteLine(System.String) | call | 4 | unmodeled | T |
| M:System.InvalidOperationException.#ctor(System.String) | call | 4 | partially-modeled | T |
| M:System.Math.Abs(System.Int32) | call | 4 | partially-modeled | N |
| M:System.Math.Max(System.Int32,System.Int32) | call | 4 | partially-modeled | N |
| M:System.ArgumentNullException.#ctor | call | 3 | unmodeled | E |
| M:System.ArgumentNullException.#ctor(System.String) | call | 3 | unmodeled | T |
| M:System.Array.Clear(System.Array,System.Int32,System.Int32) | call | 3 | unmodeled | R |
| M:System.Collections.Generic.HashSet`1.Remove(`0) | call | 3 | unmodeled | S |
| M:System.Collections.Generic.Queue`1.#ctor(System.Int32) | call | 3 | unmodeled | S |
| M:System.Convert.ToInt64(System.Int16) | call | 3 | unmodeled | N |
| M:System.Convert.ToInt64(System.Int32) | call | 3 | unmodeled | N |
| M:System.Guid.NewGuid | call | 3 | unmodeled | O |
| M:System.Linq.Enumerable.Max(System.Collections.Generic.IEnumerable{System.Int32}) | call | 3 | unmodeled | N |
| M:System.Linq.Enumerable.Min(System.Collections.Generic.IEnumerable{System.Int32}) | call | 3 | unmodeled | N |
| M:System.Math.Sqrt(System.Double) | call | 3 | unmodeled | N |
| M:System.Object.#ctor | call | 3 | partially-modeled | O |
| M:System.Object.Equals(System.Object) | call | 3 | unmodeled | O |
| P:System.Collections.Generic.KeyValuePair`2.Key | read | 3 | unmodeled | O |
| M:System.Collections.Generic.List`1.GetRange(System.Int32,System.Int32) | call | 2 | partially-modeled | L |
| M:System.Collections.Generic.Queue`1.#ctor | call | 2 | unmodeled | S |
| M:System.Collections.Generic.Stack`1.Push(`0) | call | 2 | unmodeled | S |
| M:System.Console.Write(System.String) | call | 2 | unmodeled | T |
| M:System.IndexOutOfRangeException.#ctor(System.String) | call | 2 | unmodeled | T |
| M:System.Math.Min(System.Int64,System.Int64) | call | 2 | unmodeled | N |
| M:System.Predicate`1.Invoke(`0) | call | 2 | unmodeled | F |
| M:System.String.#ctor(System.Char,System.Int32) | call | 2 | unmodeled | T |
| M:System.String.Format(System.String,System.Object) | call | 2 | unmodeled | T |
| M:System.String.Join(System.String,System.Collections.Generic.IEnumerable{System.String}) | call | 2 | unmodeled | T |
| M:System.String.Replace(System.String,System.String) | call | 2 | unmodeled | T |
| M:System.String.ToCharArray | call | 2 | unmodeled | T |
| P:System.Collections.Generic.Dictionary`2.Values | read | 2 | unmodeled | D |
| M:System.ArgumentException.#ctor(System.String,System.String) | call | 1 | unmodeled | T |
| M:System.ArgumentNullException.ThrowIfNull(System.Object,System.String) | call | 1 | unmodeled | T |
| M:System.ArgumentOutOfRangeException.ThrowIfGreaterThan``1(``0,``0,System.String) | call | 1 | unmodeled | T |
| M:System.ArgumentOutOfRangeException.ThrowIfNegativeOrZero``1(``0,System.String) | call | 1 | unmodeled | T |
| M:System.ArgumentOutOfRangeException.ThrowIfNegative``1(``0,System.String) | call | 1 | unmodeled | T |
| M:System.Array.Copy(System.Array,System.Int32,System.Array,System.Int32,System.Int32) | call | 1 | unmodeled | R |
| M:System.Array.Resize``1(``0[]@,System.Int32) | call | 1 | unmodeled | R |
| M:System.Collections.Generic.EqualityComparer`1.Equals(`0,`0) | call | 1 | unmodeled | C |
| M:System.Collections.Generic.EqualityComparer`1.GetHashCode(`0) | call | 1 | unmodeled | C |
| M:System.Collections.Generic.HashSet`1.#ctor(System.Collections.Generic.IEnumerable{`0}) | call | 1 | unmodeled | S |
| M:System.Collections.Generic.HashSet`1.IsSupersetOf(System.Collections.Generic.IEnumerable{`0}) | call | 1 | unmodeled | S |
| M:System.Collections.Generic.ICollection`1.Remove(`0) | call | 1 | unmodeled | A |
| M:System.Collections.Generic.KeyValuePair`2.#ctor(`0,`1) | call | 1 | unmodeled | O |
| M:System.Collections.Generic.List`1.#ctor(System.Int32) | call | 1 | unmodeled | L |
| M:System.Collections.Generic.List`1.AddRange(System.Collections.Generic.IEnumerable{`0}) | call | 1 | unmodeled | L |
| M:System.Collections.Generic.List`1.Clear | call | 1 | unmodeled | L |
| M:System.Collections.Generic.List`1.RemoveAll(System.Predicate{`0}) | call | 1 | unmodeled | L |
| M:System.Collections.Generic.Stack`1.#ctor(System.Int32) | call | 1 | unmodeled | S |
| M:System.Collections.Generic.Stack`1.Pop | call | 1 | unmodeled | S |
| M:System.Collections.IEnumerator.MoveNext | call | 1 | unmodeled | F |
| M:System.Convert.ToInt16(System.Int64) | call | 1 | unmodeled | N |
| M:System.Convert.ToInt32(System.Int32) | call | 1 | unmodeled | N |
| M:System.Convert.ToInt32(System.Int64) | call | 1 | unmodeled | N |
| M:System.Convert.ToInt32(System.String) | call | 1 | unmodeled | T |
| M:System.Convert.ToString(System.Char) | call | 1 | unmodeled | N |
| M:System.Diagnostics.Debug.Assert(System.Boolean,System.String) | call | 1 | unmodeled | T |
| M:System.Exception.#ctor(System.String,System.Exception) | call | 1 | unmodeled | T |
| M:System.Func`1.Invoke | call | 1 | unmodeled | F |
| M:System.IndexOutOfRangeException.#ctor | call | 1 | unmodeled | E |
| M:System.Int32.ToString | call | 1 | unmodeled | N |
| M:System.Linq.Enumerable.Select``2(System.Collections.Generic.IEnumerable{``0},System.Func{``0,``1}) | call | 1 | unmodeled | F |
| M:System.NotSupportedException.#ctor(System.String) | call | 1 | unmodeled | T |
| M:System.NullReferenceException.#ctor(System.String) | call | 1 | unmodeled | T |
| M:System.Random.NextDouble | call | 1 | unmodeled | O |
| M:System.String.#ctor(System.Char[]) | call | 1 | unmodeled | T |
| M:System.String.Insert(System.Int32,System.String) | call | 1 | unmodeled | T |
| M:System.String.IsNullOrWhiteSpace(System.String) | call | 1 | unmodeled | T |
| M:System.String.Split(System.Char,System.StringSplitOptions) | call | 1 | unmodeled | T |
| M:System.String.Substring(System.Int32,System.Int32) | call | 1 | unmodeled | T |
| M:System.Threading.Tasks.Parallel.For(System.Int32,System.Int32,System.Action{System.Int32}) | call | 1 | unmodeled | N |
| M:System.Threading.Volatile.Read(System.Int64@) | call | 1 | unmodeled | O |
| M:System.Type.GetTypeCode(System.Type) | call | 1 | unmodeled | O |
| P:System.Collections.Generic.Dictionary`2.Count | read | 1 | partially-modeled | D |
| P:System.Collections.Generic.Dictionary`2.Item(`0) | write | 1 | unmodeled-dispatch | D |
| P:System.Collections.Generic.Dictionary`2.Keys | read | 1 | unmodeled | D |
| P:System.Collections.Generic.IEnumerator`1.Current | read | 1 | unmodeled | F |
| P:System.Collections.Generic.Stack`1.Count | read | 1 | partially-modeled | S |
| P:System.DateTime.Ticks | read | 1 | unmodeled | O |
| P:System.DateTime.UtcNow | read | 1 | unmodeled | O |

## Every declaration with partial evidence

These 48 rows record the supplied facets; Unknown facets, zero postconditions and guarded contexts are still gaps. The complete TSV remains authoritative.

| Declaration | Current evidence |
| --- | --- |
| M:System.Array.Empty``1 | catalog=bcl.array.empty;effects=Unknown;allocation=Unknown;throws=DoesNotThrow;exceptions=;nullness=NonNull;cardinality=Empty;termination=Unknown;postconditions=0 |
| M:System.Byte.CompareTo(System.Byte) | intrinsic=integer-comparison |
| M:System.Char.CompareTo(System.Char) | intrinsic=integer-comparison |
| M:System.Collections.Generic.Comparer`1.Compare(`0,`0) | intrinsic=default-integer-comparer-only |
| M:System.Collections.Generic.Dictionary`2.#ctor | intrinsic=fresh-default-integer-key-dictionary-state;intrinsic=constructor-origin-dictionary-state;intrinsic=fresh-default-dictionary-construction-state |
| M:System.Collections.Generic.Dictionary`2.Add(`0,`1) | intrinsic=fresh-default-integer-key-dictionary-state;intrinsic=constructor-origin-dictionary-state;intrinsic=opaque-dictionary-value-storage |
| M:System.Collections.Generic.Dictionary`2.ContainsKey(`0) | intrinsic=fresh-default-integer-key-dictionary-state;intrinsic=constructor-origin-dictionary-state |
| M:System.Collections.Generic.ICollection`1.Add(`0) | intrinsic=fresh-list-state;intrinsic=opaque-list-value-storage |
| M:System.Collections.Generic.IList`1.Insert(System.Int32,`0) | intrinsic=fresh-list-state;intrinsic=opaque-list-value-storage;intrinsic=list-insertion-range-state |
| M:System.Collections.Generic.List`1.#ctor | intrinsic=fresh-list-state |
| M:System.Collections.Generic.List`1.Add(`0) | catalog=bcl.list.add;effects=WritesReceiverState;allocation=MayAllocate;throws=Unknown;exceptions=;nullness=NotApplicable;cardinality=NotApplicable;termination=Unknown;postconditions=0;intrinsic=fresh-list-state;intrinsic=opaque-list-value-storage;intrinsic=concrete-list-state |
| M:System.Collections.Generic.List`1.GetRange(System.Int32,System.Int32) | intrinsic=fresh-list-state;intrinsic=opaque-list-value-storage;intrinsic=list-insertion-range-state |
| M:System.Collections.Generic.List`1.Insert(System.Int32,`0) | intrinsic=fresh-list-state;intrinsic=opaque-list-value-storage;intrinsic=list-insertion-range-state |
| M:System.Exception.#ctor | catalog=bcl.exception.ctor;effects=WritesReceiverState;allocation=None;throws=DoesNotThrow;exceptions=;nullness=NotApplicable;cardinality=NotApplicable;termination=Terminates;postconditions=0 |
| M:System.Exception.#ctor(System.String) | catalog=bcl.exception.ctor.string;effects=WritesReceiverState;allocation=None;throws=DoesNotThrow;exceptions=;nullness=NotApplicable;cardinality=NotApplicable;termination=Terminates;postconditions=0 |
| M:System.Int16.CompareTo(System.Int16) | intrinsic=integer-comparison |
| M:System.Int32.CompareTo(System.Int32) | intrinsic=integer-comparison |
| M:System.Int64.CompareTo(System.Int64) | intrinsic=integer-comparison |
| M:System.InvalidOperationException.#ctor | catalog=bcl.invalid-operation-exception.ctor;effects=WritesReceiverState, ReadsAmbientState, Synchronization;allocation=MayAllocate;throws=DoesNotThrow;exceptions=;nullness=NotApplicable;cardinality=NotApplicable;termination=Terminates;postconditions=0 |
| M:System.InvalidOperationException.#ctor(System.String) | catalog=bcl.invalid-operation-exception.ctor.string;effects=WritesReceiverState;allocation=None;throws=DoesNotThrow;exceptions=;nullness=NotApplicable;cardinality=NotApplicable;termination=Terminates;postconditions=0 |
| M:System.Linq.Enumerable.Empty``1 | catalog=bcl.enumerable.empty;effects=Unknown;allocation=Unknown;throws=DoesNotThrow;exceptions=;nullness=NonNull;cardinality=Empty;termination=Unknown;postconditions=0 |
| M:System.Math.Abs(System.Int32) | catalog=bcl.math.abs.int32;effects=None;allocation=None;throws=MayThrow;exceptions=System.OverflowException;nullness=NotApplicable;cardinality=NotApplicable;termination=Unknown;postconditions=1 |
| M:System.Math.Max(System.Int32,System.Int32) | catalog=bcl.math.max.int32-int32;effects=None;allocation=None;throws=DoesNotThrow;exceptions=;nullness=NotApplicable;cardinality=NotApplicable;termination=Terminates;postconditions=0 |
| M:System.Math.Min(System.Int32,System.Int32) | catalog=bcl.math.min.int32-int32;effects=None;allocation=None;throws=DoesNotThrow;exceptions=;nullness=NotApplicable;cardinality=NotApplicable;termination=Terminates;postconditions=0 |
| M:System.Nullable`1.GetValueOrDefault | catalog=bcl.nullable.get-value-or-default;effects=ReadsReceiverState;allocation=None;throws=DoesNotThrow;exceptions=;nullness=NotApplicable;cardinality=NotApplicable;termination=Unknown;postconditions=0 |
| M:System.Nullable`1.GetValueOrDefault(`0) | catalog=bcl.nullable.get-value-or-default.value;effects=ReadsReceiverState;allocation=None;throws=DoesNotThrow;exceptions=;nullness=NotApplicable;cardinality=NotApplicable;termination=Unknown;postconditions=0 |
| M:System.Object.#ctor | catalog=bcl.object.ctor;effects=None;allocation=None;throws=DoesNotThrow;exceptions=;nullness=NotApplicable;cardinality=NotApplicable;termination=Terminates;postconditions=0 |
| M:System.Object.ReferenceEquals(System.Object,System.Object) | intrinsic=object-reference-identity |
| M:System.SByte.CompareTo(System.SByte) | intrinsic=integer-comparison |
| M:System.String.Concat(System.String,System.String) | catalog=bcl.string.concat.string-string;effects=None;allocation=MayAllocate;throws=DoesNotThrow;exceptions=;nullness=NonNull;cardinality=NotApplicable;termination=Terminates;postconditions=0 |
| M:System.String.Equals(System.String) | intrinsic=string-instance-equality |
| M:System.String.Equals(System.String,System.String) | intrinsic=string-static-equality |
| M:System.String.IsNullOrEmpty(System.String) | catalog=bcl.string.is-null-or-empty;effects=None;allocation=None;throws=DoesNotThrow;exceptions=;nullness=NotApplicable;cardinality=NotApplicable;termination=Terminates;postconditions=0 |
| M:System.UInt16.CompareTo(System.UInt16) | intrinsic=integer-comparison |
| M:System.UInt32.CompareTo(System.UInt32) | intrinsic=integer-comparison |
| M:System.UInt64.CompareTo(System.UInt64) | intrinsic=integer-comparison |
| P:System.Collections.Generic.Dictionary`2.Count | catalog=bcl.dictionary.count;effects=ReadsReceiverState;allocation=None;throws=DoesNotThrow;exceptions=;nullness=NotApplicable;cardinality=NotApplicable;termination=Terminates;postconditions=0;intrinsic=concrete-count-nonnegative;intrinsic=fresh-default-integer-key-dictionary-state;intrinsic=constructor-origin-dictionary-state;intrinsic=fresh-default-dictionary-construction-state |
| P:System.Collections.Generic.Dictionary`2.Item(`0) | intrinsic=fresh-default-integer-key-dictionary-state;intrinsic=constructor-origin-dictionary-state;intrinsic=opaque-dictionary-value-storage |
| P:System.Collections.Generic.ICollection`1.Count | intrinsic=scalar-array-backed-interface;intrinsic=fresh-list-state |
| P:System.Collections.Generic.IList`1.Item(System.Int32) | intrinsic=scalar-array-backed-interface;intrinsic=fresh-list-state;intrinsic=opaque-list-value-storage |
| P:System.Collections.Generic.List`1.Count | catalog=bcl.list.count;effects=ReadsReceiverState;allocation=None;throws=DoesNotThrow;exceptions=;nullness=NotApplicable;cardinality=NotApplicable;termination=Terminates;postconditions=0;intrinsic=concrete-count-nonnegative;intrinsic=fresh-list-state;intrinsic=concrete-list-state |
| P:System.Collections.Generic.List`1.Item(System.Int32) | catalog=bcl.list.item.int32;effects=Unknown;allocation=MayAllocate;throws=MayThrow;exceptions=System.ArgumentOutOfRangeException;nullness=Unknown;cardinality=NotApplicable;termination=Unknown;postconditions=0;intrinsic=fresh-list-state;intrinsic=opaque-list-value-storage;intrinsic=concrete-list-state |
| P:System.Collections.Generic.Queue`1.Count | catalog=bcl.queue.count;effects=ReadsReceiverState;allocation=None;throws=DoesNotThrow;exceptions=;nullness=NotApplicable;cardinality=NotApplicable;termination=Terminates;postconditions=0;intrinsic=concrete-count-nonnegative |
| P:System.Collections.Generic.Stack`1.Count | catalog=bcl.stack.count;effects=ReadsReceiverState;allocation=None;throws=DoesNotThrow;exceptions=;nullness=NotApplicable;cardinality=NotApplicable;termination=Terminates;postconditions=0;intrinsic=concrete-count-nonnegative |
| P:System.Nullable`1.HasValue | catalog=bcl.nullable.has-value;effects=ReadsReceiverState;allocation=None;throws=DoesNotThrow;exceptions=;nullness=NotApplicable;cardinality=NotApplicable;termination=Unknown;postconditions=0 |
| P:System.Nullable`1.Value | catalog=bcl.nullable.value;effects=ReadsReceiverState;allocation=None;throws=MayThrow;exceptions=System.InvalidOperationException;nullness=NotApplicable;cardinality=NotApplicable;termination=Unknown;postconditions=0 |
| P:System.String.Chars(System.Int32) | catalog=bcl.string.item.int32;effects=ReadsReceiverState;allocation=None;throws=MayThrow;exceptions=System.IndexOutOfRangeException;nullness=NotApplicable;cardinality=NotApplicable;termination=Unknown;postconditions=0 |
| P:System.String.Length | catalog=bcl.string.length;effects=ReadsReceiverState;allocation=None;throws=DoesNotThrow;exceptions=;nullness=NotApplicable;cardinality=NotApplicable;termination=Unknown;postconditions=1 |
