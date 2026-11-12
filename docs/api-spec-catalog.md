# Default API specification catalog

This is a hand-maintained reference to [DefaultApiSpecCatalog.cs](../src/SharpProof.Specs/DefaultApiSpecCatalog.cs). The C# declarations are the runtime authority.

- Table identity: `SharpProof.ApiSpec.Default`
- Table version: `12`
- Declarations: 87

## Matching and evidence

The catalog matches exact member shape, containing type, generic arity, parameter/result kinds, and approved assembly evidence. The source defines separate framework and SharpProof Attributes assembly sets. An arbitrary same-named member does not inherit a specification.

Each declaration has independently reviewed effect, allocation, exception, nullness, cardinality, optional termination, and postcondition facets. An Unknown facet stays Unknown. A result relation does not follow from exception freedom, and a Throws permission does not imply allocation permission.

Exceptional paths through `Math.Abs(int)` and `Nullable<T>.Value` can consult runtime resources and invoke ambient culture callbacks. Their catalog effects, exception behavior, and termination are therefore conservative. Scalar lowering keeps the normal `Math.Abs(int)` arithmetic path precise and places the resource boundary only on overflow. `Nullable<T>.Value` remains opaque, including after a `HasValue` check; its purity may remain unproved. `GetValueOrDefault` retains its separate specification.

Evidence distinguishes documented contracts, supported-runtime observations, type-initialization boundaries, and compiler-bound ghost contract semantics. These are assumptions with provenance, not universal implementation proofs. Consult each declaration in the source for the complete current facets and domain conditions.

## Declarations

| Catalog ID | Exact member target |
| --- | --- |
| `bcl.array.empty` | <code>M:System.Array.Empty&#96;&#96;1</code> |
| `bcl.date-only.day` | <code>P:System.DateOnly.Day</code> |
| `bcl.date-only.day-number` | <code>P:System.DateOnly.DayNumber</code> |
| `bcl.date-only.day-of-week` | <code>P:System.DateOnly.DayOfWeek</code> |
| `bcl.date-only.day-of-year` | <code>P:System.DateOnly.DayOfYear</code> |
| `bcl.date-only.month` | <code>P:System.DateOnly.Month</code> |
| `bcl.date-only.op-equality` | <code>M:System.DateOnly.op_Equality(System.DateOnly,System.DateOnly)</code> |
| `bcl.date-only.op-greater-than` | <code>M:System.DateOnly.op_GreaterThan(System.DateOnly,System.DateOnly)</code> |
| `bcl.date-only.op-greater-than-or-equal` | <code>M:System.DateOnly.op_GreaterThanOrEqual(System.DateOnly,System.DateOnly)</code> |
| `bcl.date-only.op-inequality` | <code>M:System.DateOnly.op_Inequality(System.DateOnly,System.DateOnly)</code> |
| `bcl.date-only.op-less-than` | <code>M:System.DateOnly.op_LessThan(System.DateOnly,System.DateOnly)</code> |
| `bcl.date-only.op-less-than-or-equal` | <code>M:System.DateOnly.op_LessThanOrEqual(System.DateOnly,System.DateOnly)</code> |
| `bcl.date-only.year` | <code>P:System.DateOnly.Year</code> |
| `bcl.date-time-offset.utc-date-time` | <code>P:System.DateTimeOffset.UtcDateTime</code> |
| `bcl.date-time-offset.utc-ticks` | <code>P:System.DateTimeOffset.UtcTicks</code> |
| `bcl.date-time.date` | <code>P:System.DateTime.Date</code> |
| `bcl.date-time.day` | <code>P:System.DateTime.Day</code> |
| `bcl.date-time.day-of-week` | <code>P:System.DateTime.DayOfWeek</code> |
| `bcl.date-time.day-of-year` | <code>P:System.DateTime.DayOfYear</code> |
| `bcl.date-time.hour` | <code>P:System.DateTime.Hour</code> |
| `bcl.date-time.kind` | <code>P:System.DateTime.Kind</code> |
| `bcl.date-time.millisecond` | <code>P:System.DateTime.Millisecond</code> |
| `bcl.date-time.minute` | <code>P:System.DateTime.Minute</code> |
| `bcl.date-time.month` | <code>P:System.DateTime.Month</code> |
| `bcl.date-time.op-equality` | <code>M:System.DateTime.op_Equality(System.DateTime,System.DateTime)</code> |
| `bcl.date-time.op-greater-than` | <code>M:System.DateTime.op_GreaterThan(System.DateTime,System.DateTime)</code> |
| `bcl.date-time.op-greater-than-or-equal` | <code>M:System.DateTime.op_GreaterThanOrEqual(System.DateTime,System.DateTime)</code> |
| `bcl.date-time.op-inequality` | <code>M:System.DateTime.op_Inequality(System.DateTime,System.DateTime)</code> |
| `bcl.date-time.op-less-than` | <code>M:System.DateTime.op_LessThan(System.DateTime,System.DateTime)</code> |
| `bcl.date-time.op-less-than-or-equal` | <code>M:System.DateTime.op_LessThanOrEqual(System.DateTime,System.DateTime)</code> |
| `bcl.date-time.second` | <code>P:System.DateTime.Second</code> |
| `bcl.date-time.ticks` | <code>P:System.DateTime.Ticks</code> |
| `bcl.date-time.time-of-day` | <code>P:System.DateTime.TimeOfDay</code> |
| `bcl.date-time.year` | <code>P:System.DateTime.Year</code> |
| `bcl.dictionary.count` | <code>P:System.Collections.Generic.Dictionary&#96;2.Count</code> |
| `bcl.enumerable.empty` | <code>M:System.Linq.Enumerable.Empty&#96;&#96;1</code> |
| `bcl.exception.ctor` | <code>M:System.Exception.#ctor</code> |
| `bcl.exception.ctor.string` | <code>M:System.Exception.#ctor(System.String)</code> |
| `bcl.invalid-operation-exception.ctor` | <code>M:System.InvalidOperationException.#ctor</code> |
| `bcl.invalid-operation-exception.ctor.string` | <code>M:System.InvalidOperationException.#ctor(System.String)</code> |
| `bcl.key-value-pair.key` | <code>P:System.Collections.Generic.KeyValuePair&#96;2.Key</code> |
| `bcl.key-value-pair.value` | <code>P:System.Collections.Generic.KeyValuePair&#96;2.Value</code> |
| `bcl.list.add` | <code>M:System.Collections.Generic.List&#96;1.Add(&#96;0)</code> |
| `bcl.list.count` | <code>P:System.Collections.Generic.List&#96;1.Count</code> |
| `bcl.list.item.int32` | <code>M:System.Collections.Generic.List&#96;1.get_Item(System.Int32)</code> |
| `bcl.math.abs.int32` | <code>M:System.Math.Abs(System.Int32)</code> |
| `bcl.math.max.int32-int32` | <code>M:System.Math.Max(System.Int32,System.Int32)</code> |
| `bcl.math.min.int32-int32` | <code>M:System.Math.Min(System.Int32,System.Int32)</code> |
| `bcl.nullable.get-value-or-default` | <code>M:System.Nullable&#96;1.GetValueOrDefault</code> |
| `bcl.nullable.get-value-or-default.value` | <code>M:System.Nullable&#96;1.GetValueOrDefault(&#96;0)</code> |
| `bcl.nullable.has-value` | <code>P:System.Nullable&#96;1.HasValue</code> |
| `bcl.nullable.value` | <code>P:System.Nullable&#96;1.Value</code> |
| `bcl.object.ctor` | <code>M:System.Object.#ctor</code> |
| `bcl.queue.count` | <code>P:System.Collections.Generic.Queue&#96;1.Count</code> |
| `bcl.stack.count` | <code>P:System.Collections.Generic.Stack&#96;1.Count</code> |
| `bcl.string.concat.string-string` | <code>M:System.String.Concat(System.String,System.String)</code> |
| `bcl.string.is-null-or-empty` | <code>M:System.String.IsNullOrEmpty(System.String)</code> |
| `bcl.string.item.int32` | <code>M:System.String.get_Chars(System.Int32)</code> |
| `bcl.string.length` | <code>P:System.String.Length</code> |
| `bcl.time-only.hour` | <code>P:System.TimeOnly.Hour</code> |
| `bcl.time-only.millisecond` | <code>P:System.TimeOnly.Millisecond</code> |
| `bcl.time-only.minute` | <code>P:System.TimeOnly.Minute</code> |
| `bcl.time-only.op-equality` | <code>M:System.TimeOnly.op_Equality(System.TimeOnly,System.TimeOnly)</code> |
| `bcl.time-only.op-greater-than` | <code>M:System.TimeOnly.op_GreaterThan(System.TimeOnly,System.TimeOnly)</code> |
| `bcl.time-only.op-greater-than-or-equal` | <code>M:System.TimeOnly.op_GreaterThanOrEqual(System.TimeOnly,System.TimeOnly)</code> |
| `bcl.time-only.op-inequality` | <code>M:System.TimeOnly.op_Inequality(System.TimeOnly,System.TimeOnly)</code> |
| `bcl.time-only.op-less-than` | <code>M:System.TimeOnly.op_LessThan(System.TimeOnly,System.TimeOnly)</code> |
| `bcl.time-only.op-less-than-or-equal` | <code>M:System.TimeOnly.op_LessThanOrEqual(System.TimeOnly,System.TimeOnly)</code> |
| `bcl.time-only.second` | <code>P:System.TimeOnly.Second</code> |
| `bcl.time-only.ticks` | <code>P:System.TimeOnly.Ticks</code> |
| `bcl.time-span.days` | <code>P:System.TimeSpan.Days</code> |
| `bcl.time-span.hours` | <code>P:System.TimeSpan.Hours</code> |
| `bcl.time-span.milliseconds` | <code>P:System.TimeSpan.Milliseconds</code> |
| `bcl.time-span.minutes` | <code>P:System.TimeSpan.Minutes</code> |
| `bcl.time-span.op-equality` | <code>M:System.TimeSpan.op_Equality(System.TimeSpan,System.TimeSpan)</code> |
| `bcl.time-span.op-greater-than` | <code>M:System.TimeSpan.op_GreaterThan(System.TimeSpan,System.TimeSpan)</code> |
| `bcl.time-span.op-greater-than-or-equal` | <code>M:System.TimeSpan.op_GreaterThanOrEqual(System.TimeSpan,System.TimeSpan)</code> |
| `bcl.time-span.op-inequality` | <code>M:System.TimeSpan.op_Inequality(System.TimeSpan,System.TimeSpan)</code> |
| `bcl.time-span.op-less-than` | <code>M:System.TimeSpan.op_LessThan(System.TimeSpan,System.TimeSpan)</code> |
| `bcl.time-span.op-less-than-or-equal` | <code>M:System.TimeSpan.op_LessThanOrEqual(System.TimeSpan,System.TimeSpan)</code> |
| `bcl.time-span.seconds` | <code>P:System.TimeSpan.Seconds</code> |
| `bcl.time-span.ticks` | <code>P:System.TimeSpan.Ticks</code> |
| `contract.assume` | <code>M:SharpProof.Attributes.Contract.Assume(System.Boolean)</code> |
| `contract.ensures` | <code>M:SharpProof.Attributes.Contract.Ensures(System.Boolean)</code> |
| `contract.old` | <code>M:SharpProof.Attributes.Contract.Old&#96;&#96;1(&#96;&#96;0)</code> |
| `contract.requires` | <code>M:SharpProof.Attributes.Contract.Requires(System.Boolean)</code> |
| `contract.result` | <code>M:SharpProof.Attributes.Contract.Result&#96;&#96;1</code> |

The KeyValuePair getters copy immutable fields with evidence for purity, no allocations, no exceptions and termination. Their result relationships and constructors remain unmodeled. Receiver evaluation and surrounding effects retain their own obligations.

## Calendar and clock values

The `bcl.date-time.*`, `bcl.date-only.*`, `bcl.time-only.*`, `bcl.time-span.*` and `bcl.date-time-offset.*` rows cover the calendar and time-of-day getters and the six comparison operators of `DateTime`, `DateOnly`, `TimeOnly` and `TimeSpan`, plus `DateTimeOffset.UtcDateTime` and `UtcTicks`.

- **Facets.** Each row reads only the value it is given, allocates nothing, does not throw and terminates. A getter projects a component of the stored ticks; for `DateTimeOffset`, it reads the UTC instant. An operator compares the stored ticks, or for `DateOnly` the day number.
- **Evidence.** The rows list no exception and consult no culture, time zone or clock. Runtime witnesses check each row: it allocates nothing on boundary values, and it returns the same results under a culture that records every calendar, format, comparison and text read.
- **Operators.** An operator row applies because the frontend lowers a non-lifted user-defined binary operator declared in another assembly as a call to its operator method.
- **Older frameworks.** `DateOnly` and `TimeOnly` first ship in .NET 6, so on .NET Framework and .NET Standard reference surfaces their rows stay unresolved and those members stay unmodeled.
- **Not covered.** Constructors, `Now`/`UtcNow`/`Today`, date arithmetic that can overflow, `DateTimeOffset` operators and formatting have no row. They remain conservative.

## Optional relational specification packs

The default table above is distinct from the opt-in [relational pack catalog](../src/SharpProof.Specs/RelationalSpecPackCatalog.cs). A default effect or exception facet does not supply a relational postcondition automatically.

| Pack ID | Pack version | Catalog version | Member relation |
| --- | --- | --- | --- |
| `dotnet.scalar` | `1` | `2` | Int32 `Math.Max(left, right)` returns `left` when `left >= right`, otherwise `right` |

Enable the pack with the consumer property `SharpProofSpecificationPacks` set to `dotnet.scalar`; the version is catalog evidence rather than part of the property value. Multiple enabled IDs are separated by semicolons. Unknown IDs, duplicate IDs, and empty entries in a nonempty list are rejected. The compiler binds exact member/assembly shapes and carries the resolved evidence into the closed artifact; a same-named source method cannot acquire the relation.

See [configuration](configuration.md), [compiler pack parsing](../src/SharpProof.CompilerCollector/FinalCompilationCollector.cs), [pack-provider tests](../src/SharpProof.Worker.Tests/CompilerSpecificationPackProviderTests.cs), and [coverage and limits](coverage-and-limits.md). The catalog's member, pack, and catalog versions are separate from the compiler artifact's specification-pack wire schema; use [release constants](release-constants.md) when changing them.

## Concrete collection getters

The Count getters on List, Dictionary, Queue, and Stack read receiver state,
allocate nothing, terminate, and do not throw for a non-null receiver in the
supported runtime model. They do not invoke collection comparers. The catalog
also approves the signed System.Collections runtime family and the .NET Framework System reference family required by these getters.

List.get_Item may throw ArgumentOutOfRangeException and may allocate an exception.
Its effects remain Unknown because exceptional helpers may initialize ambient
resources. Setter behavior, element identity/content relations, and numeric Count
relations are not supplied by these entries. Interface dispatch and comparer-driven
collection methods retain conservative behavior.
