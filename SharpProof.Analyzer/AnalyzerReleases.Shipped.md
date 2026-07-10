## Release 1.0.0

### New Rules

Rule ID | Category | Severity | Notes
-------|--------|--------|-----
SP0002 | Purity | Error | Purity Not Proven: method marked [EnforcePure]/[Pure] contains operations the analyzer cannot prove pure.
SP0003 | Usage | Error | Misplaced [EnforcePure]/[Pure] attribute applied to a non-method declaration.
SP0004 | Purity | Warning | Missing [EnforcePure] on a method/accessor/ctor that appears pure.

## Release 0.0.4

### New Rules

Rule ID | Category | Severity | Notes
-------|--------|--------|-----
SP0005 | Usage | Warning | Conflicting purity attributes: a method marked with both [EnforcePure] and [Pure].
SP0006 | Usage | Warning | [AllowSynchronization] used without [EnforcePure]/[Pure] on the method.
SP0007 | Usage | Error | Misplaced [AllowSynchronization] attribute applied to a non-method declaration.
SP0008 | Usage | Info | Redundant [AllowSynchronization] on method with no synchronization constructs.

## Release 0.1.0

### New Rules

Rule ID | Category | Severity | Notes
-------|--------|--------|-----
SP0009 | Purity | Info | Optional purity diagnostic explanation emitted when `sharpproof_emit_explanations` is enabled.
SP0010 | ExceptionFlow | Info | Optional thrown-exception summary emitted when `sharpproof_report_exceptions` or `sharpproof_runtime_hazard_mode = summaries/all` is enabled.
SP0011 | ExceptionFlow | Warning | Optional uncaught call-site and runtime-hazard warning emitted when `sharpproof_checked_exceptions` or `sharpproof_runtime_hazard_mode = sites/all` is enabled.
SP0012 | Purity | Info | Optional non-authoritative BCL purity fallback guess emitted when `sharpproof_emit_explanations` or `sharpproof_report_bcl_fallback_guesses` is enabled.
SP0013 | Allocation | Warning | Reports direct source-visible allocation sites inside methods marked with `[ZeroAllocations]`.
SP0014 | Usage | Error | Reports `[ZeroAllocations]` when it is applied to a non-method declaration.
SP0015 | Capability | Warning | Reports source-visible operations or proven transitive source callees that exceed a method's `[AllowedCapabilities]` contract.
SP0016 | Capability | Warning | Reports capability-contract operations whose required capabilities could not be fully verified conservatively.
SP0017 | Usage | Error | Reports `[AllowedCapabilities]` when it is applied to a non-method declaration.
SP0018 | Contracts | Warning | Reports `[Ensures]` postconditions contradicted by a return site.
SP0019 | Contracts | Warning | Reports `[Ensures]` postconditions that cannot be parsed, lowered, or proven within the bounded proof surface.
SP0020 | Usage | Error | Reports `[Ensures]` attributes applied to non-method-like declarations.
SP0021 | Complexity | Warning | Reports methods whose inferred complexity exceeds their `[ExpectedComplexity]` contract.
SP0022 | Complexity | Warning | Reports `[ExpectedComplexity]` contracts that cannot be verified conservatively.
SP0023 | Usage | Error | Reports `[ExpectedComplexity]` attributes applied to non-method-like declarations.
