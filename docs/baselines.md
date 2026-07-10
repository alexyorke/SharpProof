# SharpProof Diagnostic Baselines

SharpProof can suppress known analyzer diagnostics through an additional file
named `SharpProof.Baseline.json`. The baseline workflow tool creates and
maintains that file from current diagnostics.

Generated documents and entries carry the shared
[`evidenceSchemaVersion` compatibility contract](evidence-schema.md). Legacy
unversioned entries remain readable during the preview and are upgraded when
the baseline tool writes a new file.

Generate a baseline from a SARIF log:

```powershell
dotnet run --project Tools/SharpProof.Baseline -- generate --output SharpProof.Baseline.json artifacts/sharpproof.sarif
```

Generate a baseline directly from a project or solution by letting the tool run
`dotnet build` with a temporary SARIF error log:

```powershell
dotnet run --project Tools/SharpProof.Baseline -- generate --output SharpProof.Baseline.json SharpProof.sln
```

Explain which baseline entries still match current diagnostics:

```powershell
dotnet run --project Tools/SharpProof.Baseline -- explain --baseline SharpProof.Baseline.json artifacts/current.sarif
```

Prune entries that no longer match current diagnostics:

```powershell
dotnet run --project Tools/SharpProof.Baseline -- prune --baseline SharpProof.Baseline.json --output SharpProof.Baseline.json artifacts/current.sarif
```

Each generated entry includes the diagnostic id, the stable owner symbol, and
the normalized source path. Entries can also include line, column, contract,
operation kind, and evidence key fields. Those optional fields narrow a match,
so suppressing one allocation site, capability site, postcondition return site,
exception site, BCL fallback explanation, or usage diagnostic does not hide
unrelated diagnostics in the same method.

Older three-field entries still work. When an entry contains only `id`,
`symbol`, and `path`, SharpProof treats the missing optional fields as
wildcards.
