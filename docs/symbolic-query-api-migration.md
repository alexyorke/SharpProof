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

This change affects only the preview .NET surface. Existing CLI forms and JSON
property names remain compatible.
