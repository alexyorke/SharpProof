# Machine-Readable Explain Reports

`explain` can compose the proof surfaces for one source point into a bounded
JSON, SARIF 2.1.0, or Markdown report. These formats are intended for IDEs, CI
bots, and issue attachments that need more context than a focused query result.

```powershell
dotnet run --project .\Tools\SharpProof.SymbolicCli\SharpProof.SymbolicCli.csproj -- explain --file Example.cs --line 42 --json
dotnet run --project .\Tools\SharpProof.SymbolicCli\SharpProof.SymbolicCli.csproj -- explain --file Example.cs --line 42 --sarif
dotnet run --project .\Tools\SharpProof.SymbolicCli\SharpProof.SymbolicCli.csproj -- explain --file Example.cs --line 42 --markdown
```

The three output selectors are mutually exclusive. Without one, `explain`
keeps its concise text output.

## Composed JSON Schema

`explain --json` emits one lower-camel JSON document with `kind: "explain"`
and `schemaVersion: 3`. It also carries the shared
`evidenceSchemaVersion` and `evidenceSchemaCompatibility` fields.

The document contains:

- `source`: virtual or physical file identity, source-input kind, and optional
  snippet source-map metadata
- `target`: requested line/column or position plus the resolved program point,
  method, and program-point kind
- `project`: project/solution identity, analyzer configs, AdditionalFiles,
  baseline/effect-summary counts, workspace diagnostics, and configuration
  issues when MSBuild context was loaded
- `invariant`: the canonical point-query result, including reachability, proof
  outcomes, SMT diagnostics, facts, and conditions
- `runtimeHazards`: a bounded runtime-hazard view for the
  resolved source line
- `capabilities`: the canonical containing-method capability result
- `complexity`: the canonical containing-method complexity result
- `diagnostics`: relevant bounded project analyzer diagnostics, with
  target-first ordering
- `truncation`: one aggregate view of report bounds and analysis-limit events

Standalone inputs do not run the analyzer, so their `diagnostics` section has
zero counts. Add `--project` or `--solution` to include diagnostics after the
same analyzer configuration, baseline suppression, and AdditionalFiles used by
the build. For a position target, runtime hazards are queried on the resolved
program point's line.

## Output Bounds

Machine-readable reports default to 50 items per bounded collection:

| Option | Bounded content |
| --- | --- |
| `--report-max-diagnostics <n>` | Analyzer diagnostic items |
| `--report-max-hazards <n>` | Runtime-hazard items |
| `--report-max-items <n>` | Project metadata paths, workspace messages, and configuration issues |

All limits accept zero. Canonical invariant, capability, and complexity results
are no longer copied into separate bounded wrapper graphs. Root `truncation`
tells a consumer whether a report projection or underlying bounded analysis was
truncated. Increase report limits only when the attachment or consumer can
accept the additional data. Analysis-limit truncation is separate and must be
addressed through the bounded-analysis settings described in
[bounded analysis limits](analysis-limits.md).

## SARIF 2.1.0

`explain --sarif` emits a SARIF 2.1.0 log with the standard schema URL and one
SharpProof run. Results include:

- relevant Roslyn analyzer diagnostics with their original IDs and severities
- bounded runtime hazards with stable `SPQ-HZ-*` rule IDs
- `SPQ-REPORT-TRUNCATED` when report or analysis bounds affected the result

Locations use SARIF physical locations. Run properties retain the explain and
evidence schema versions plus report truncation status.

## Markdown

`explain --markdown` renders the same composed result as an issue-ready report.
It includes summary metadata, invariant/reachability status, hazards,
capability, complexity, diagnostics, and an explicit truncation notice.
Markdown is a presentation format; use JSON when a consumer
needs stable field names or exact counts.

## JSON Request Envelope

The strict schema-versioned request envelope supports all explain formats and
limits without a temporary source file:

```json
{
  "schemaVersion": 2,
  "arguments": [
    "explain",
    "--source-text", "class C { static void M() { throw new System.Exception(); } }",
    "--source-file-name", "virtual/Buffer.cs",
    "--line", "1",
    "--column", "35",
    "--sarif",
    "--report-max-diagnostics", "25",
    "--report-max-hazards", "25",
    "--report-max-items", "25"
  ]
}
```

For explain reports, `maxHazards`, `maxDiagnostics`, and `maxItems` bound only
the composed report sections. Focused query JSON is not output-truncated.

Successful explain reports return exit code 0 even when they contain analyzer
diagnostics or hazards. CI exit gates require a focused query mode and cannot
be combined with `explain`. Failed JSON or SARIF-oriented requests emit the
typed `kind: "error"` JSON envelope instead of a partial report. See the
[typed query error model](error-model.md).
