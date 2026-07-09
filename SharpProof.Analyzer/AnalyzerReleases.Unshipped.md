## Unshipped Release

Post-`0.1.0` analyzer diagnostics and release-note entries should be recorded
here after the preview release is cut.

### New Rules

| Rule ID | Category | Severity | Notes |
| ------- | -------- | -------- | ----- |
| SP0024 | Usage | Error | Reports malformed SharpProof contract arguments such as empty `[Ensures]` conditions, undefined `[ExpectedComplexity]` values, and unknown `[AllowedCapabilities]` bits. |
| SP0025 | Configuration | Warning | Reports invalid `sharpproof_*` analyzer option values that would otherwise fall back to defaults silently. |
| SP0026 | Usage | Warning | Reports SharpProof-looking attribute names whose type identity is not in `SharpProof.Attributes` or an opt-in source-stub namespace. |
| SP0027 | Contracts | Warning | Reports calls that do not prove a callee `[Requires]` precondition. |
| SP0028 | Contracts | Warning | Reports `[Requires]` preconditions that could not be parsed, lowered, or proven within the supported bounded proof surface. |
| SP0029 | Usage | Error | Reports `[Requires]` attributes applied to non-method-like declarations. |
| SP0030 | ExceptionFlow | Warning | Reports escaping exceptions that violate `[DoesNotThrow]` or `[AllowedExceptions]` contracts. |
| SP0031 | Usage | Error | Reports `[DoesNotThrow]` and `[AllowedExceptions]` attributes applied to non-method-like declarations. |
