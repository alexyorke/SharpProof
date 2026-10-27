# Default API specification catalog

This is a hand-maintained reference to [DefaultApiSpecCatalog.cs](../src/SharpProof.Specs/DefaultApiSpecCatalog.cs). The C# declarations are the runtime authority.

- Table identity: `SharpProof.ApiSpec.Default`
- Table version: `10`
- Declarations: 31

## Matching and evidence

The catalog matches exact member shape, containing type, generic arity, parameter/result kinds, and approved assembly evidence. The source defines separate framework and SharpProof Attributes assembly sets. An arbitrary same-named member does not inherit a specification.

Each declaration has independently reviewed effect, allocation, exception, nullness, cardinality, optional termination, and postcondition facets. An Unknown facet stays Unknown. A result relation does not follow from exception freedom, and a Throws permission does not imply allocation permission.

Evidence distinguishes documented contracts, supported-runtime observations, type-initialization boundaries, and compiler-bound ghost contract semantics. These are assumptions with provenance, not universal implementation proofs. Consult each declaration in the source for the complete current facets and domain conditions.

## Declarations

| Catalog ID | Exact member target |
| --- | --- |
| `bcl.dictionary.count` | <code>P:System.Collections.Generic.Dictionary&#96;2.Count</code> |
| `bcl.list.count` | <code>P:System.Collections.Generic.List&#96;1.Count</code> |
| `bcl.list.item.int32` | <code>M:System.Collections.Generic.List&#96;1.get_Item(System.Int32)</code> |
| `bcl.queue.count` | <code>P:System.Collections.Generic.Queue&#96;1.Count</code> |
| `bcl.stack.count` | <code>P:System.Collections.Generic.Stack&#96;1.Count</code> |
| `bcl.array.empty` | <code>M:System.Array.Empty&#96;&#96;1</code> |
| `bcl.enumerable.empty` | <code>M:System.Linq.Enumerable.Empty&#96;&#96;1</code> |
| `bcl.exception.ctor` | <code>M:System.Exception.#ctor</code> |
| `bcl.exception.ctor.string` | <code>M:System.Exception.#ctor(System.String)</code> |
| `bcl.invalid-operation-exception.ctor` | <code>M:System.InvalidOperationException.#ctor</code> |
| `bcl.invalid-operation-exception.ctor.string` | <code>M:System.InvalidOperationException.#ctor(System.String)</code> |
| `bcl.key-value-pair.key` | <code>P:System.Collections.Generic.KeyValuePair&#96;2.Key</code> |
| `bcl.key-value-pair.value` | <code>P:System.Collections.Generic.KeyValuePair&#96;2.Value</code> |
| `bcl.list.add` | <code>M:System.Collections.Generic.List&#96;1.Add(&#96;0)</code> |
| `bcl.math.abs.int32` | <code>M:System.Math.Abs(System.Int32)</code> |
| `bcl.math.max.int32-int32` | <code>M:System.Math.Max(System.Int32,System.Int32)</code> |
| `bcl.math.min.int32-int32` | <code>M:System.Math.Min(System.Int32,System.Int32)</code> |
| `bcl.nullable.get-value-or-default` | <code>M:System.Nullable&#96;1.GetValueOrDefault</code> |
| `bcl.nullable.get-value-or-default.value` | <code>M:System.Nullable&#96;1.GetValueOrDefault(&#96;0)</code> |
| `bcl.nullable.has-value` | <code>P:System.Nullable&#96;1.HasValue</code> |
| `bcl.nullable.value` | <code>P:System.Nullable&#96;1.Value</code> |
| `bcl.object.ctor` | <code>M:System.Object.#ctor</code> |
| `bcl.string.concat.string-string` | <code>M:System.String.Concat(System.String,System.String)</code> |
| `bcl.string.is-null-or-empty` | <code>M:System.String.IsNullOrEmpty(System.String)</code> |
| `bcl.string.item.int32` | <code>M:System.String.get_Chars(System.Int32)</code> |
| `bcl.string.length` | <code>P:System.String.Length</code> |
| `contract.assume` | <code>M:SharpProof.Attributes.Contract.Assume(System.Boolean)</code> |
| `contract.ensures` | <code>M:SharpProof.Attributes.Contract.Ensures(System.Boolean)</code> |
| `contract.old` | <code>M:SharpProof.Attributes.Contract.Old&#96;&#96;1(&#96;&#96;0)</code> |
| `contract.requires` | <code>M:SharpProof.Attributes.Contract.Requires(System.Boolean)</code> |
| `contract.result` | <code>M:SharpProof.Attributes.Contract.Result&#96;&#96;1</code> |

The KeyValuePair getters copy immutable fields with evidence for purity, no allocations, no exceptions and termination. Their result relationships and constructors remain unmodeled. Receiver evaluation and surrounding effects retain their own obligations.

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
