# .NET API gap backlog

This is the committed work list for API support and its evidence. Baseline: latest
master `dafb6d376f298de00df91f01f0cc0a8739a3c208`, followed by this branch's
.NET 10 inventory and comparer-provenance work. Counts are static source sites,
not runtime frequency. A passing test, a modeled effect, and a proved caller
postcondition are separate claims.

## Universe and completeness

The complete declaration universe is the 59,639 rows of
[net10.0.tsv](net10.0.tsv), pinned to Microsoft.NETCore.App.Ref 10.0.13 / net10.0
and SDK 10.0.402; [the manifest](net10.0.manifest.json) records assembly checksums.
Of these, 3,966 type containers are explicitly excluded from behavioral support,
55,640 declarations have no catalog/audited-intrinsic evidence, and 33 have partial
evidence. No declaration is labeled fully modeled. The TSV is the exhaustive list
of declaration gaps; duplicating its 55,640 unmodeled rows here would create a
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

## Priorities and acceptance criteria

Work from the largest source counts, but split a family into sound semantic slices.
Do not grant arbitrary interface receivers or comparer callbacks a convenient
concrete implementation's behavior. A site stays open until its actual receiver,
access kind and value domains satisfy the production guard.

| Work item | Priority and present boundary | Concrete completion criteria |
| --- | --- | --- |
| A: IList / ICollection dispatch | 56 index reads, 13 writes, 40 Count reads; arbitrary standalone interface parameters remain open | Prove concrete receiver identity, add the concrete implementation's heap/effect model, and test aliases, joins, mutation, ref/closure escape and wrong implementations. Existing scalar-array provenance remains supported. |
| D: Dictionary | 40 index reads, 22 Add, 22 ContainsKey, one index write and smaller constructor/view uses | Track key/value membership, aliasing and Count; model default versus custom comparers, GetHashCode/Equals callbacks, callback mutation/exceptions, missing/null/duplicate keys, allocation and mutation. Start with proven default-comparer scalar keys. Do not assume dictionary-typed parameters use default comparers. |
| C: Comparers and comparable values | 26 Comparer.Compare calls and nine IComparable.CompareTo calls; standalone generic/custom implementations remain open | Resolve concrete type and implementation, preserve initialization and callback effects, and validate signed/unsigned limits, nulls and argument order. This MR supports tracked Default integer aliases and immutable source parameters; generic array-element specialization is separately tested. Arbitrary generic receivers do not gain those assumptions. |
| L: Lists and collection contents | Count/getter/Add have partial effects; constructors, stores, range operations and count/content relationships remain open | Model shared storage, capacity/growth and allocation, Count changes, bounds/argument faults, setter mutation and alias invalidation. List.GetItem's exception set alone does not supply content semantics. |
| S: HashSet / Queue / Stack | Contains/Add/Remove, enqueue/dequeue/push/pop and constructors remain open; concrete Counts only nonnegative | Add membership or sequence state, comparer callback behavior, empty/duplicate cases, allocation and mutation effects. Count nonnegativity does not prove contents or strict positivity. |
| R: Arrays | SZ-array Length already has contextual support; general System.Array, multidimensional and array helper methods need distinct models | Keep contextual guards in ranking. Model Clear/Copy/Resize state, overlapping ranges, allocation/ref replacement and precise faults. Reference covariance and arbitrary interface implementations remain open. |
| T: Strings | Length/index/equality/basic predicates partially modeled; construction, split/replace/format/join/substring and others remain open | Preserve UTF-16/ordinal domains, nulls, bounds, overload/culture distinctions, allocations and effects. Check native proof support separately from catalog facets. |
| N: Numeric / conversion | A few numeric helpers are partial; conversions, floating operations and formatting need separate work | Specify checked/unchecked overflow, NaN/infinity/rounding and culture/formatting as applicable; compare IR with runtime across boundary values and refute wrong claims. |
| E: Exceptions | Selected constructors are partial; other constructors and exact native opaque-call exception sets remain open | Preserve constructor arguments, allocation/cache effects, null/bounds behavior and exact exception identity. Portable named exception evidence must be carried into native IR before claiming AllowedExceptions proofs. |
| F: Delegates / enumeration / LINQ / parallelism | Invoke, iterator members, LINQ and Parallel.For remain open | Model callback effects, captured mutable state, deferred execution, iteration faults, allocation and parallel behavior; preserve conservative fallback until covered. |
| O: Ambient state and other APIs | Date/time, randomness, IO, volatile reads, diagnostics, reflection and object behavior need individual contracts | Define nondeterminism, ambient capabilities, observable reads/writes, exceptions and allocation per overload. Do not force deterministic scalar semantics. |

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
  Unrelated or unbound type parameters, generic defaults/operators without scalar
  semantics, ref/out signatures, reference/non-SZ arrays and mutable/captured or
  otherwise unproven interface receiver representations remain open. Closed scalar
  type arguments without validated parameter evidence do not supply those models.
  Generic local IList/ICollection Count branches
  and full Swap postconditions retain source declaration/ordinal/assembly ownership,
  with native proofs/refutations and compiled CLR/IR normal/fault agreement.
- Count >= 0 is weaker than a collection representation invariant. List/dictionary/
  queue/stack count/content relations and repeated getter consistency remain open.
- Default comparer getter initialization can allocate, throw and affect ambient
  state. Proving a comparison result does not erase these effects. Parameters that
  may be custom comparers, generic types without concrete evidence and mutable
  helper parameters still need dispatch/provenance work. Null-coalescing comparer
  selection in generic corpus sorts is a concrete next dataflow task.
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
| P:System.Collections.Generic.Dictionary`2.Item(`0) | read | 40 | unmodeled | D |
| P:System.Collections.Generic.ICollection`1.Count | read | 40 | unmodeled-dispatch | A |
| P:System.Array.Length | read | 32 | partially-modeled | R |
| M:System.Collections.Generic.Comparer`1.Compare(`0,`0) | call | 26 | unmodeled-dispatch | C |
| M:System.Collections.Generic.Dictionary`2.Add(`0,`1) | call | 22 | unmodeled | D |
| M:System.Collections.Generic.Dictionary`2.ContainsKey(`0) | call | 22 | unmodeled | D |
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
| M:System.Collections.Generic.List`1.#ctor | call | 11 | unmodeled | L |
| M:System.Collections.Generic.Queue`1.Enqueue(`0) | call | 10 | unmodeled | S |
| M:System.Collections.Generic.HashSet`1.#ctor | call | 9 | unmodeled | S |
| M:System.IComparable`1.CompareTo(`0) | call | 9 | unmodeled | C |
| M:System.Collections.Generic.Dictionary`2.#ctor | call | 8 | unmodeled | D |
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
| M:System.Collections.Generic.List`1.Insert(System.Int32,`0) | call | 4 | unmodeled | L |
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
| M:System.Collections.Generic.List`1.GetRange(System.Int32,System.Int32) | call | 2 | unmodeled | L |
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
| P:System.Collections.Generic.Dictionary`2.Item(`0) | write | 1 | unmodeled-setter | D |
| P:System.Collections.Generic.Dictionary`2.Keys | read | 1 | unmodeled | D |
| P:System.Collections.Generic.IEnumerator`1.Current | read | 1 | unmodeled | F |
| P:System.Collections.Generic.Stack`1.Count | read | 1 | partially-modeled | S |
| P:System.DateTime.Ticks | read | 1 | unmodeled | O |
| P:System.DateTime.UtcNow | read | 1 | unmodeled | O |

## Every declaration with partial evidence

These 33 rows record the supplied facets; Unknown facets, zero postconditions and guarded contexts are still gaps. The complete TSV remains authoritative.

| Declaration | Current evidence |
| --- | --- |
| M:System.Collections.Generic.Comparer`1.Compare(`0,`0) | intrinsic=default-integer-comparer-only |
| M:System.Collections.Generic.List`1.Add(`0) | catalog=bcl.list.add;effects=WritesReceiverState;allocation=MayAllocate;throws=Unknown;exceptions=;nullness=NotApplicable;cardinality=NotApplicable;termination=Unknown;postconditions=0 |
| P:System.Collections.Generic.Dictionary`2.Count | catalog=bcl.dictionary.count;effects=ReadsReceiverState;allocation=None;throws=DoesNotThrow;exceptions=;nullness=NotApplicable;cardinality=NotApplicable;termination=Terminates;postconditions=0;intrinsic=concrete-count-nonnegative |
| P:System.Collections.Generic.List`1.Count | catalog=bcl.list.count;effects=ReadsReceiverState;allocation=None;throws=DoesNotThrow;exceptions=;nullness=NotApplicable;cardinality=NotApplicable;termination=Terminates;postconditions=0;intrinsic=concrete-count-nonnegative |
| P:System.Collections.Generic.List`1.Item(System.Int32) | catalog=bcl.list.item.int32;effects=Unknown;allocation=MayAllocate;throws=MayThrow;exceptions=System.ArgumentOutOfRangeException;nullness=Unknown;cardinality=NotApplicable;termination=Unknown;postconditions=0 |
| P:System.Collections.Generic.Queue`1.Count | catalog=bcl.queue.count;effects=ReadsReceiverState;allocation=None;throws=DoesNotThrow;exceptions=;nullness=NotApplicable;cardinality=NotApplicable;termination=Terminates;postconditions=0;intrinsic=concrete-count-nonnegative |
| P:System.Collections.Generic.Stack`1.Count | catalog=bcl.stack.count;effects=ReadsReceiverState;allocation=None;throws=DoesNotThrow;exceptions=;nullness=NotApplicable;cardinality=NotApplicable;termination=Terminates;postconditions=0;intrinsic=concrete-count-nonnegative |
| M:System.Linq.Enumerable.Empty``1 | catalog=bcl.enumerable.empty;effects=Unknown;allocation=Unknown;throws=DoesNotThrow;exceptions=;nullness=NonNull;cardinality=Empty;termination=Unknown;postconditions=0 |
| M:System.Array.Empty``1 | catalog=bcl.array.empty;effects=Unknown;allocation=Unknown;throws=DoesNotThrow;exceptions=;nullness=NonNull;cardinality=Empty;termination=Unknown;postconditions=0 |
| M:System.Exception.#ctor | catalog=bcl.exception.ctor;effects=WritesReceiverState;allocation=None;throws=DoesNotThrow;exceptions=;nullness=NotApplicable;cardinality=NotApplicable;termination=Terminates;postconditions=0 |
| M:System.Exception.#ctor(System.String) | catalog=bcl.exception.ctor.string;effects=WritesReceiverState;allocation=None;throws=DoesNotThrow;exceptions=;nullness=NotApplicable;cardinality=NotApplicable;termination=Terminates;postconditions=0 |
| M:System.Int32.CompareTo(System.Int32) | intrinsic=integer-comparison |
| M:System.Int64.CompareTo(System.Int64) | intrinsic=integer-comparison |
| M:System.InvalidOperationException.#ctor | catalog=bcl.invalid-operation-exception.ctor;effects=WritesReceiverState, ReadsAmbientState, Synchronization;allocation=MayAllocate;throws=DoesNotThrow;exceptions=;nullness=NotApplicable;cardinality=NotApplicable;termination=Terminates;postconditions=0 |
| M:System.InvalidOperationException.#ctor(System.String) | catalog=bcl.invalid-operation-exception.ctor.string;effects=WritesReceiverState;allocation=None;throws=DoesNotThrow;exceptions=;nullness=NotApplicable;cardinality=NotApplicable;termination=Terminates;postconditions=0 |
| M:System.Math.Abs(System.Int32) | catalog=bcl.math.abs.int32;effects=None;allocation=None;throws=MayThrow;exceptions=System.OverflowException;nullness=NotApplicable;cardinality=NotApplicable;termination=Unknown;postconditions=1 |
| M:System.Math.Max(System.Int32,System.Int32) | catalog=bcl.math.max.int32-int32;effects=None;allocation=None;throws=DoesNotThrow;exceptions=;nullness=NotApplicable;cardinality=NotApplicable;termination=Terminates;postconditions=0 |
| M:System.Math.Min(System.Int32,System.Int32) | catalog=bcl.math.min.int32-int32;effects=None;allocation=None;throws=DoesNotThrow;exceptions=;nullness=NotApplicable;cardinality=NotApplicable;termination=Terminates;postconditions=0 |
| M:System.Nullable`1.GetValueOrDefault | catalog=bcl.nullable.get-value-or-default;effects=ReadsReceiverState;allocation=None;throws=DoesNotThrow;exceptions=;nullness=NotApplicable;cardinality=NotApplicable;termination=Unknown;postconditions=0 |
| M:System.Nullable`1.GetValueOrDefault(`0) | catalog=bcl.nullable.get-value-or-default.value;effects=ReadsReceiverState;allocation=None;throws=DoesNotThrow;exceptions=;nullness=NotApplicable;cardinality=NotApplicable;termination=Unknown;postconditions=0 |
| M:System.Object.#ctor | catalog=bcl.object.ctor;effects=None;allocation=None;throws=DoesNotThrow;exceptions=;nullness=NotApplicable;cardinality=NotApplicable;termination=Terminates;postconditions=0 |
| M:System.String.Concat(System.String,System.String) | catalog=bcl.string.concat.string-string;effects=None;allocation=MayAllocate;throws=DoesNotThrow;exceptions=;nullness=NonNull;cardinality=NotApplicable;termination=Terminates;postconditions=0 |
| M:System.String.Equals(System.String) | intrinsic=string-instance-equality |
| M:System.String.Equals(System.String,System.String) | intrinsic=string-static-equality |
| M:System.String.IsNullOrEmpty(System.String) | catalog=bcl.string.is-null-or-empty;effects=None;allocation=None;throws=DoesNotThrow;exceptions=;nullness=NotApplicable;cardinality=NotApplicable;termination=Terminates;postconditions=0 |
| M:System.UInt32.CompareTo(System.UInt32) | intrinsic=integer-comparison |
| M:System.UInt64.CompareTo(System.UInt64) | intrinsic=integer-comparison |
| P:System.Collections.Generic.ICollection`1.Count | intrinsic=scalar-array-backed-interface |
| P:System.Collections.Generic.IList`1.Item(System.Int32) | intrinsic=scalar-array-backed-interface |
| P:System.Nullable`1.HasValue | catalog=bcl.nullable.has-value;effects=ReadsReceiverState;allocation=None;throws=DoesNotThrow;exceptions=;nullness=NotApplicable;cardinality=NotApplicable;termination=Unknown;postconditions=0 |
| P:System.Nullable`1.Value | catalog=bcl.nullable.value;effects=ReadsReceiverState;allocation=None;throws=MayThrow;exceptions=System.InvalidOperationException;nullness=NotApplicable;cardinality=NotApplicable;termination=Unknown;postconditions=0 |
| P:System.String.Chars(System.Int32) | catalog=bcl.string.item.int32;effects=ReadsReceiverState;allocation=None;throws=MayThrow;exceptions=System.IndexOutOfRangeException;nullness=NotApplicable;cardinality=NotApplicable;termination=Unknown;postconditions=0 |
| P:System.String.Length | catalog=bcl.string.length;effects=ReadsReceiverState;allocation=None;throws=DoesNotThrow;exceptions=;nullness=NotApplicable;cardinality=NotApplicable;termination=Unknown;postconditions=1 |
