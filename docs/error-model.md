# Typed Query Error Model

SharpProof exposes one typed error contract across the symbolic .NET API and
CLI. Automation can branch on a stable `SPQ` code and category instead of
matching exception or console text.

## Error Codes And Exit Mapping

| Code | Category | Meaning | Recommended CLI exit |
| --- | --- | --- | --- |
| `SPQ1000` | `Usage` | Invalid request or option combination | 64 |
| `SPQ1001` | `Input` | Target line, column, position, or span is outside the source | 65 |
| `SPQ1002` | `Unsupported` | The source/target combination is not supported | 65 |
| `SPQ1100` | `Input` | Source input was not found | 66 |
| `SPQ1101` | `Input` | Metadata reference was not found | 66 |
| `SPQ1200` | `Parse` | A required request, metadata, or project document could not be parsed | 65 |
| `SPQ1300` | `Project` | MSBuild project or solution loading failed | 65, or 66 when the project path is missing |
| `SPQ2000` | `Solver` | The native SMT solver could not be loaded | 69 |
| `SPQ2001` | `Solver` | An escaping SMT solver operation failed | 75 |
| `SPQ2100` | `Timeout` | The query timed out | 75 |
| `SPQ3000` | `Cancellation` | The query was canceled | 130 |
| `SPQ9000` | `Internal` | An unexpected non-fatal failure escaped the query boundary | 70 |

`SymbolicErrorCodes` and `SymbolicErrorExitCodes` expose these values in the
public library. `SymbolicError` also carries a human-readable message,
`IsRetryable`, and bounded string details such as `path` and `exceptionType`.
Details do not include a stack trace.

Native solver failures that bounded analysis already converts into
`SymbolicSmtDiagnostics` or a conservative unknown remain normal query
results. `SPQ2000`, `SPQ2001`, and `SPQ2100` apply when a failure escapes the
query boundary and no typed result can be returned.

Similarly, recoverable C# syntax diagnostics can still produce conservative
analysis. `SPQ1200` represents a failed request/document/metadata parse, such
as malformed JSON request input, rather than every Roslyn syntax diagnostic.

## JSON Error Envelopes

`--json`, `--sarif`, `--request-json`, and
`--request-json-stdin` automatically emit failures as a lower-camel JSON
envelope on stdout. Use `--error-json` to request the same behavior for a
text-mode query.

A failed `explain --sarif` request returns this typed JSON error envelope, not
a partial SARIF log. Successful SARIF and Markdown explain reports are
documented in [machine-readable explain reports](explain-reports.md).

```json
{
  "kind": "error",
  "schemaVersion": 1,
  "error": {
    "code": "SPQ1101",
    "category": "Input",
    "message": "--reference does not exist: Missing.dll",
    "recommendedExitCode": 66,
    "isRetryable": false,
    "details": {
      "path": "Missing.dll"
    }
  }
}
```

The process returns `error.recommendedExitCode`. JSON error requests leave
stderr empty, so stdout contains exactly one machine-readable document. Text
errors use stderr in this form:

```text
SPQ1000 [Usage]: Unknown option '--compact-json'.
```

Usage failures also print CLI help after that line.

CI gate failures are different: the query succeeded and produced a result,
but a configured policy failed. They return exit code 1, retain the requested
result on stdout, and report gate reasons on stderr. See [CI exit-code
gates](ci-exit-gates.md).

## .NET API

Existing methods such as `Query(...)`, `QueryRuntimeHazards(...)`,
`QueryCapabilities(...)`, and `QueryComplexity(...)` retain their throwing
behavior. Additive `Try*` methods return `SymbolicOperationResult<T>`:

```csharp
var outcome = new SymbolicQueryService().TryQuery(
    new SymbolicQueryContext(
        SymbolicSourceInput.FromText(sourceText, "virtual/Buffer.cs"),
        SymbolicQueryTarget.Point(line: 42)));

if (!outcome.IsSuccess)
{
    SymbolicError error = outcome.Error!;
    Console.Error.WriteLine($"{error.Code}: {error.Message}");
    return error.RecommendedExitCode;
}

SymbolicQueryResult result = outcome.Value!;
```

The supported methods are `TryQuery`, `TryProve`,
`TryQueryRuntimeHazards`, `TryQueryCapabilities`, and `TryQueryComplexity`.
They catch non-fatal exceptions, classify them through
`SymbolicErrorClassifier`, and retain cancellation as `SPQ3000` instead of
throwing it. Call `SymbolicErrorClassifier.FromException(...)` directly when a
host needs to normalize an exception from its own workspace or transport
boundary. `SymbolicQueryException` carries an already-classified
`SymbolicError` through intermediate layers without losing its code.

`SymbolicErrorEnvelope` is the public schema object used by the CLI and is safe
to serialize with lower-camel property names and string enums.
