## Release 1.0.0

### New Rules

Rule ID | Category | Severity | Notes
-------|--------|--------|-----
SP0002 | Purity | Error | Reports `[EnforcePure]` methods whose observable purity is not proven by `MethodEffects`.
SP0013 | Allocation | Warning | Reports allocations that violate `[ZeroAllocations]`.
SP0015 | Capabilities | Warning | Reports proven capabilities outside `[AllowedCapabilities]`.
SP0016 | Capabilities | Warning | Reports capability contracts that remain unknown.
SP0018 | Contracts | Warning | Reports contradicted `[Ensures]` postconditions.
SP0019 | Contracts | Warning | Reports `[Ensures]` postconditions that cannot be translated or proven.
SP0021 | Complexity | Warning | Reports inferred complexity above `[ExpectedComplexity]`.
SP0022 | Complexity | Warning | Reports complexity contracts that cannot be verified conservatively.
