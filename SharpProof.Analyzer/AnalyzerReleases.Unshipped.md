### New Rules

Rule ID | Category | Severity | Notes
-------|--------|--------|-----
SP0024 | Usage | Error | Reports malformed SharpProof contract arguments such as empty `[Ensures]` conditions, undefined `[ExpectedComplexity]` values, and unknown `[AllowedCapabilities]` bits.
SP0025 | Configuration | Warning | Reports invalid `sharpproof_*` analyzer option values that would otherwise fall back to defaults silently.
SP0026 | Usage | Warning | Reports SharpProof-looking attribute names whose type identity is not in `SharpProof.Attributes` or an opt-in source-stub namespace.
SP0027 | Contracts | Warning | Reports calls that do not prove a callee `[Requires]` precondition.
SP0028 | Contracts | Warning | Reports `[Requires]` preconditions that could not be parsed, lowered, or proven within the supported bounded proof surface.
SP0029 | Usage | Error | Reports `[Requires]` attributes applied to non-method-like declarations.
SP0030 | ExceptionFlow | Warning | Reports escaping exceptions that violate `[DoesNotThrow]` or `[AllowedExceptions]` contracts.
SP0031 | Usage | Error | Reports `[DoesNotThrow]` and `[AllowedExceptions]` attributes applied to non-method-like declarations.
SP0032 | Configuration | Warning | Reports malformed, empty, unsupported, partially ignored, or stale SharpProof analyzer AdditionalFiles, including exact effect-summary identity and artifact-source mismatches.
SP0033 | ExceptionFlow | Info | Opt-in unknown runtime-hazard candidate with stable proof, reason, trigger, explain, and baseline evidence; enabled by the `unknowns` runtime-hazard modes.

### Changed Rules

Rule ID | New Category | New Severity | Old Category | Old Severity | Notes
-------|------------|------------|------------|------------|-----
SP0015 | Capabilities | Warning | Capability | Warning | Normalize capability diagnostics to the public `Capabilities` taxonomy.
SP0016 | Capabilities | Warning | Capability | Warning | Normalize capability diagnostics to the public `Capabilities` taxonomy.
