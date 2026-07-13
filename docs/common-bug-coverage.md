# Common C# bug coverage

SharpProof treats the three common-bug catalogs as a coverage inventory, not as an
instruction to duplicate every compiler or .NET SDK diagnostic. The dispositions
below are terminal:

- `SharpProof`: implemented by a SharpProof diagnostic.
- `Existing`: already covered by SharpProof's contract or symbolic engine.
- `Platform`: enabled in the shipped profiles and owned by the C# compiler or .NET SDK analyzers.
- `Rejected`: the proposed diagnostic would be incorrect or duplicative.
- `Enhancement`: useful framework-specific analysis that cannot be made reliable from the general C# semantics alone.

## Nullability and API contracts

| Target | Disposition | Coverage |
|---|---|---|
| Nullable return, parameter postcondition, and member initialization contracts | Existing | SP0041-SP0043 use path-sensitive verification and understand compiler null-state attributes. |
| Unsafe, redundant, or inconclusive null-forgiving operators | Existing | SP0044, SP0045, and opt-in SP0047 distinguish proved-null, proved-non-null, and unknown cases. |
| Immediate dereference of known default-returning lookup and LINQ APIs | SharpProof | SP0064. |
| Public API argument validation | Platform | CA1062, with compiler nullable warnings for flow-state mismatches. |
| Nullable adoption and unreviewed suppression debt | SharpProof | SP0072 and SP0073. |
| `Count()` used for existence or when a property is available | Platform | CA1827, CA1829, and CA1860. |
| Iteration after a successful null check should fail for an empty collection | Rejected | Enumerating a non-null empty collection is safe and performs zero iterations. |

## Async and task lifecycle

| Target | Disposition | Coverage |
|---|---|---|
| Awaiting a nullable null-conditional result | SharpProof | SP0048. |
| Converting an unawaited `Task` to text | SharpProof | SP0049. |
| `TaskCompletionSource` without `RunContinuationsAsynchronously` | SharpProof | SP0050. |
| Non-event `async void` | SharpProof | SP0051. |
| `.Result`, `.Wait()`, or `GetAwaiter().GetResult()` inside async code | SharpProof | SP0052; known-completed tasks are excluded. |
| Returning null from a non-async task-returning method | SharpProof | SP0053. |
| Using a task as the disposable resource instead of awaiting it | SharpProof | SP0054. |
| Public async argument validation whose exception is task-captured | SharpProof | SP0055. |
| Dropped task or invalid `ValueTask` consumption | Platform | CS4014 and CA2012. |
| Missing `ConfigureAwait(false)` in library policy | Platform | CA2007; disabled in CI because application code often intentionally captures context. |
| Synchronous API use in async code | Platform | CA1849. |
| Cancellation token not forwarded | Platform | CA2016. |

## Collections, closures, and concurrency

| Target | Disposition | Coverage |
|---|---|---|
| Mutating the same ordinary collection during `foreach` | SharpProof | SP0056 uses receiver-symbol identity and ignores nested callables. |
| Escaping lambda that captures a `for` iteration variable | SharpProof | SP0057. |
| Unsynchronized captured or field mutation in known parallel callbacks | SharpProof | SP0061, with lock and `Interlocked`-style safe cases excluded. |
| LINQ/interface enumeration over a concurrent collection | SharpProof | SP0062. |
| Locking weak-identity objects such as `this`, strings, or `Type` | Platform | CA2002. |
| `[ThreadStatic]` on an instance field | Platform | CA2259. |
| `await` inside `lock` | Rejected | The C# compiler rejects this construct. |
| UI-thread affinity violations | Enhancement | Requires framework-specific dispatcher and control models; a general rule would report legitimate background access. |

## Resources and ownership

| Target | Disposition | Coverage |
|---|---|---|
| Locally created disposable not deterministically cleaned up | Platform | CA2000. |
| Definitely owned disposable field on a type with no disposal contract | SharpProof | SP0059, complemented by CA1001 and CA2213. |
| Container-resolved service disposed by consuming code | SharpProof | SP0075. |
| `HttpClient` constructed inside a loop | SharpProof | SP0060. |
| Per-request `HttpClient` lifetime inferred from framework registration | Enhancement | Requires host/DI registration knowledge; loop construction remains the reliable general-language boundary. |

## LINQ and query providers

| Target | Disposition | Coverage |
|---|---|---|
| Multiple enumeration of a deferred sequence | Platform | CA1851. |
| Side effects in a deferred query lambda | SharpProof | SP0066. |
| Source-only helper call inside an `IQueryable` expression | SharpProof | SP0067 reports a provider-translation risk, not a guaranteed failure. |
| Materializing `IQueryable` and immediately continuing in-memory LINQ | SharpProof | SP0065. |
| Deferred query constructed and never consumed | SharpProof | SP0076. |
| Exact ORM/provider translation support | Enhancement | Translation varies by provider and version; SP0067 deliberately uses a conservative boundary. |

## Serialization, attributes, and deployment

| Target | Disposition | Coverage |
|---|---|---|
| Source-declared structural cycle serialized without an explicit JSON policy | SharpProof | SP0068. |
| Newtonsoft.Json/System.Text.Json ignore-attribute mismatch | SharpProof | SP0069. |
| `[Required]` on a non-nullable value member | SharpProof | SP0070. |
| `BinaryFormatter` use | Platform | SYSLIB0011. |
| Reflection-heavy APIs and JSON under trimming or Native AOT | Platform | IL2026, IL2104, and IL3050; publish-mode testing remains required. |
| Invalid attribute target, omitted constructor argument, or wrong argument type | Rejected | These are C# compiler errors. |
| Blanket warning that base attributes must be repeated on derived members | Rejected | Inheritance is controlled by each attribute's `AttributeUsage` and the reflection API used. |
| `[Obsolete]` combined with a purity annotation | Rejected | The contracts are independent and can legitimately coexist. |

## Equality, text, security, and performance

| Target | Disposition | Coverage |
|---|---|---|
| Identical stable operands on suspicious built-in binary operations | SharpProof | SP0074 excludes floating-point and user-defined operators because they need not be reflexive. |
| `Equals`, `GetHashCode`, equality operators, and `IEquatable<T>` consistency | Platform | CS0659, CS0660, CS0661, CA1066, CA1067, and CA1815. |
| `ReferenceEquals` with value types | Platform | CA2013. |
| Culture-sensitive identifier comparison and casing | Platform | CA1307, CA1308, CA1309, and CA1310. |
| Nonconstant SQL command text | Platform | CA2100. |
| Mutable structs | SharpProof | SP0058. |
| Boxing in loops | SharpProof | SP0063; SP0013 already proves boxing allocations inside `[ZeroAllocations]` methods. |
| `stackalloc` inside loops | Platform | CA2014. |
| Unchecked multiplication used as an allocation size | SharpProof | SP0071. |
| String interpolation in structured logging | Platform | CA2254 and CA1848. |
| `ref readonly` should be underlined despite correct compiler enforcement | Rejected | This is editor presentation, not a correctness diagnostic. |

## Existing formal verification coverage

SharpProof already provides SMT-backed path-sensitive checks for division by zero,
array bounds, nullable contracts, preconditions, postconditions, exception contracts,
purity, allocation, capabilities, and expected complexity. Bounded loops, solver
timeouts, or unsupported operations remain explicit `unknown` results; they are not
reported as successful proofs. Exception rethrow style remains owned by CA2200.

The shipped profiles in `config/profiles` configure both SharpProof and delegated
platform rules. Migration favors suggestions, audit exposes review signals, CI fails
high-confidence correctness defects, and strict promotes the broadest reliable set.
SP0048-SP0076 are disabled at the bare descriptor default and enabled explicitly
by every shipped profile so existing consumers can choose when to adopt the new surface.
