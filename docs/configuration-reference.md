# Analyzer configuration

SharpProof keeps configuration focused on proof budgets, SMT execution, diagnostic reporting, attribute stub namespaces, and generic effect contracts.

External effect contracts use a dynamic global option:

```text
sharpproof_effect_contract.<sha256-of-canonical-method-key>
```

The value is a JSON object:

```json
{
  "key": "spm1|...",
  "complete": true,
  "effects": "ReadsAmbientState, Throws",
  "capabilities": "FileSystem",
  "deterministic": true,
  "exceptions": ["System.IO.IOException"]
}
```

The embedded key must match both the option suffix and the requested method. A partial contract adds positive facts and leaves omitted effects unknown. Conflicting contracts are unioned conservatively and produce unknown evidence.

Supported ordinary options are listed by `SharpProof.Analyzer/Configuration/AnalyzerConfigurationOptions.json`. Purity profiles, known-pure/known-impure member lists, BCL fallback reporting, missing-purity suggestions, and effect-summary JSON configuration are intentionally unsupported.
