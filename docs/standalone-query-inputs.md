# Standalone Query Inputs

Use standalone inputs for editor buffers, generated snippets, and automation
that does not need an MSBuild project. These modes create the same synthetic
compilation used by `--file`; no temporary source file is required.

## Source Text Transports

Pass source directly on the command line:

```powershell
dotnet run --project .\Tools\SharpProof.SymbolicCli\SharpProof.SymbolicCli.csproj -- `
  --source-text 'class C { static int M(int value) => value; }' `
  --source-file-name virtual/Buffer.cs `
  --line 1 `
  --json
```

Or read only the C# source from standard input:

```powershell
Get-Content .\Buffer.cs -Raw | dotnet run --project .\Tools\SharpProof.SymbolicCli\SharpProof.SymbolicCli.csproj -- `
  --stdin `
  --source-file-name virtual/Buffer.cs `
  --line 12
```

`--file`, `--stdin`, and `--source-text` are mutually exclusive.
`--source-file-name` is a virtual Roslyn syntax-tree path and is reported in
query results; it is not opened from disk. Standalone compiler switches such
as `--reference`, `--language-version`, `--define`, and `--nullable` work with
all three transports.

For a snippet extracted from a larger document, retain its origin metadata:

```powershell
dotnet run --project .\Tools\SharpProof.SymbolicCli\SharpProof.SymbolicCli.csproj -- explain `
  --source-text $snippet `
  --source-file-name generated/Selection.cs `
  --source-map-uri file:///workspace/Original.cs `
  --source-map-original-line 80 `
  --source-map-original-column 5 `
  --line 1
```

The map says that snippet line 1, column 1 begins at the supplied original
location. Analysis targets and result coordinates remain snippet-local. An
editor adapter can use the retained URI and origin to translate them back to
the containing document. `explain` prints this metadata when present.

## JSON Request Envelope

Automation can provide a strict, versioned request inline with
`--request-json <json>` or stream it through `--request-json-stdin`. A request
selector is the only CLI argument. The envelope carries the canonical CLI
argument vector, so command-line and JSON requests cannot drift into separate
grammars.

```json
{
  "schemaVersion": 2,
  "arguments": [
    "--source-text", "#if FEATURE\nclass C { int M(int value) => value; }\n#endif",
    "--source-file-name", "virtual/Buffer.cs",
    "--source-map-uri", "file:///workspace/Original.cs",
    "--source-map-original-line", "80",
    "--source-map-original-column", "5",
    "--line", "2",
    "--column", "11",
    "--define", "FEATURE",
    "--nullable", "enable",
    "--implies", "value >= 0",
    "--smt-mode", "bounded",
    "--smt-timeout-ms", "300",
    "--analysis-limit", "merged-if-else-facts=24",
    "--check-reachability",
    "--json",
    "--fail-on-unproven-implies"
  ]
}
```

Every entry is exactly one CLI token. Use the same mode, target, analysis,
output, and gate arguments documented by `--help`; put the `explain` command
first when requesting an explain report. Nested `--request-json` selectors are
rejected.

For the composed JSON schema, SARIF projection, Markdown layout, and exact
limit semantics, see [machine-readable explain reports](explain-reports.md).

Schema version 2 rejects unknown envelope properties, blank arguments, nested
request selectors, invalid budgets, and incompatible query combinations. This
makes misspelled automation settings visible instead of silently accepting a
different query.

Gate arguments return exit code 1 on failure and are written to stderr while
the requested JSON result remains on stdout. See [CI exit-code gates](ci-exit-gates.md).

Malformed envelopes and other failed JSON-oriented requests return a typed
`kind: "error"` document on stdout with a stable `SPQ` code. See the [typed
query error model](error-model.md).

## .NET API

The public API accepts in-memory source, SMT enablement, implied conditions,
and bounded-analysis limits directly:

```csharp
using var session = SharpProofAnalysisSession.FromText(
    sourceText,
    "virtual/Buffer.cs",
    new SharpProofAnalysisOptions(
        enableSmt: true,
        impliedConditions: new[] { "value >= 0" }));
var response = session.Analyze(
    SharpProofQuery.Invariant(SharpProofTarget.Point(line: 2, column: 11)));
var result = (SourceQueryPayload)response.Payload!;
```

The CLI remains the public surface for standalone compilation profiles and
source-map metadata. Session query coordinates remain local to the supplied
source text.
