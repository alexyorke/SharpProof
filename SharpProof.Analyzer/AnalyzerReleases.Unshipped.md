### New Rules

Rule ID | Category | Severity | Notes
-------|--------|--------|-----
SP0024 | Usage | Error | Reports malformed SharpProof contract arguments such as empty `[Ensures]` conditions, undefined `[ExpectedComplexity]` values, and unknown `[AllowedCapabilities]` bits.
SP0025 | Configuration | Warning | Reports invalid `sharpproof_*` analyzer option values that would otherwise fall back to defaults silently.
SP0026 | Usage | Warning | Reports SharpProof-looking attribute names whose type identity is not in `SharpProof.Attributes` or an opt-in source-stub namespace.
SP0027 | Contracts | Warning | Reports calls that do not prove a callee `[Requires]` precondition.
SP0028 | Contracts | Warning | Reports `[Requires]` preconditions that could not be parsed, lowered, or proven within the supported bounded proof surface.
SP0029 | Usage | Error | Reports `[Requires]` attributes applied to non-method-like declarations; its code fix moves property/indexer attributes to the getter.
SP0030 | ExceptionFlow | Warning | Reports escaping exceptions that violate `[DoesNotThrow]` or `[AllowedExceptions]` contracts.
SP0031 | Usage | Error | Reports `[DoesNotThrow]` and `[AllowedExceptions]` outside method-like declarations and getter-bearing property/indexer aliases.
SP0032 | Configuration | Warning | Reports malformed, empty, unsupported, partially ignored, or stale SharpProof analyzer AdditionalFiles, including exact effect-summary identity and artifact-source mismatches.
SP0033 | ExceptionFlow | Info | Opt-in unknown runtime-hazard candidate with stable proof, reason, trigger, explain, and baseline evidence; enabled by the `unknowns` runtime-hazard modes.
SP0034 | Suggestions | Info | Opt-in high-confidence `[ZeroAllocations]` suggestion with stable evidence and a code fix.
SP0035 | Suggestions | Info | Opt-in high-confidence `[AllowedCapabilities]` suggestion with an inferred exact capability set and a code fix.
SP0036 | Suggestions | Info | Opt-in high-confidence `[ExpectedComplexity]` suggestion with an inferred bounded complexity class and a code fix.
SP0037 | Suggestions | Info | Opt-in inferred `[DoesNotThrow]` or `[AllowedExceptions]` suggestion with confidence metadata and a code fix.
SP0038 | Suggestions | Info | Opt-in high-confidence simple `[Ensures]` suggestion with a code fix.
SP0039 | Suggestions | Info | Opt-in high-confidence guard-derived `[Requires]` suggestion with a code fix.
SP0040 | Review | Info | Opt-in structured report for applied and overridden purity trust shortcuts, including exact symbol, source, value, and override disposition.
SP0041 | Nullability | Warning | Reports a reachable normal return that violates a non-null nullable return contract.
SP0042 | Nullability | Warning | Reports a reachable normal completion that violates a nullable parameter postcondition.
SP0043 | Nullability | Warning | Reports a reachable normal completion that violates a member-not-null contract.
SP0044 | Nullability | Warning | Reports a null-forgiving operator whose operand is proven null.
SP0045 | Nullability | Info | Reports a null-forgiving operator whose operand is already proven non-null.
SP0046 | Nullability | Info | Reports a nullable contract proved by every relevant completion path.
SP0047 | Nullability | Disabled | Opt-in report for nullable verification that ended unsupported or unknown.

### Changed Rules

Rule ID | New Category | New Severity | Old Category | Old Severity | Notes
-------|------------|------------|------------|------------|-----
SP0015 | Capabilities | Warning | Capability | Warning | Normalize capability diagnostics to the public `Capabilities` taxonomy.
SP0016 | Capabilities | Warning | Capability | Warning | Normalize capability diagnostics to the public `Capabilities` taxonomy.
