# Symbolic query preview API migration

The preview .NET query API now uses one `SymbolicQueryContext` for the source,
target, and common options shared by every operation. The five operation-specific
request wrappers were removed.

Use these replacements:

```csharp
var context = new SymbolicQueryContext(source, target, options);

var query = service.Query(context);
var proof = service.Prove(context, conditionText);
var hazards = service.QueryRuntimeHazards(context, hazardOptions);
var complexity = service.QueryComplexity(context);
var capabilities = service.QueryCapabilities(context);
```

The corresponding `Try*` methods accept the same context and operation-specific
arguments. `SymbolicQueryOptions` remains the place for references, SMT ownership,
analysis limits, implied conditions, expression-point selection, and filters.

Query operations now return one `SymbolicQueryResult` for point, line, span, and
file scopes. Inspect `result.Scope.Kind` and `result.Scope` for typed scope metadata,
then consume `result.ProgramPoints`, whose entries are `SymbolicProgramPointResult`.
The former `SymbolicSourceQueryResult` name and the public line/span/file result
DTOs were removed; they were artifacts of the internal source-query engine.

Capability and complexity results now inherit their shared method scope from
`SymbolicMethodResult`. Code that accepts either result can use that base type to
read `FilePath`, method identity, declaration kind, span, and line/column bounds.
The properties retain their names and values, so CLI and JSON projections are
unchanged.

Capability results now use the canonical
`SharpProof.Attributes.SharpProofCapability` flags. The duplicate preview
`SharpProof.Symbolic.SymbolicCapability` enum was removed. Member names and
numeric values are identical, so serialized CLI values remain unchanged; .NET
callers should reference the attributes package and update the enum type name.

Compact and invariant projection DTOs were removed from
`SharpProof.Symbolic.dll` and moved into the Symbolic CLI adapter. This retires
the `SymbolicCompact*`, `ISymbolicCompactResult`, `SymbolicInvariantQueryResult`,
and `ToCompactResult`/`ToInvariantQueryResult` preview surface. Library callers
should consume `SymbolicQueryResult`, `SymbolicProgramPointResult`,
`SymbolicCapabilityResult`, `SymbolicComplexityResult`, and
`SymbolicRuntimeHazardQueryResult` directly. Processes that need the existing
machine-readable schema should invoke the CLI with `--compact-json`; its JSON
property names and shapes are unchanged.

The compact query schema is now the sole invariant JSON result schema; the
former invariant-only envelope and its duplicate focus/query-summary fields
were removed.

The standalone JSON request envelope now uses schema version 2 with a single
`arguments` string array. The former nested source, target, query, output, and
gate objects were unreleased compatibility DTOs that duplicated the CLI
grammar. Pass each canonical CLI token as one array entry; nested request
selectors are rejected.
