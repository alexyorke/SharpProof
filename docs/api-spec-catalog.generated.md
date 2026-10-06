# Default API specification catalog

This is a hand-maintained reference to [DefaultApiSpecCatalog.generated.cs](../SharpProof.Specs/DefaultApiSpecCatalog.generated.cs). The C# declarations are the runtime authority. The filename is retained for existing references; there is no active Markdown generator or JSON review source.

- Table identity: `SharpProof.ApiSpec.Default`
- Table version: `6`
- Declarations: 24

## Matching and evidence

The catalog matches exact member shape, containing type, generic arity, parameter/result kinds, and approved assembly evidence. The source defines separate framework and SharpProof Attributes assembly sets. An arbitrary same-named member does not inherit a specification.

Each declaration has independently reviewed effect, allocation, exception, nullness, cardinality, optional termination, and postcondition facets. An Unknown facet stays Unknown. A result relation does not follow from exception freedom, and a Throws permission does not imply allocation permission.

Evidence distinguishes documented contracts, supported-runtime observations, type-initialization boundaries, and compiler-bound ghost contract semantics. These are assumptions with provenance, not universal implementation proofs. Consult each declaration in the source for the complete current facets and domain conditions.

## Declarations

| Catalog ID | Exact member target |
| --- | --- |
| `bcl.array.empty` | <code>M:System.Array.Empty&#96;&#96;1</code> |
| `bcl.enumerable.empty` | <code>M:System.Linq.Enumerable.Empty&#96;&#96;1</code> |
| `bcl.exception.ctor` | <code>M:System.Exception.#ctor</code> |
| `bcl.exception.ctor.string` | <code>M:System.Exception.#ctor(System.String)</code> |
| `bcl.invalid-operation-exception.ctor` | <code>M:System.InvalidOperationException.#ctor</code> |
| `bcl.invalid-operation-exception.ctor.string` | <code>M:System.InvalidOperationException.#ctor(System.String)</code> |
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
