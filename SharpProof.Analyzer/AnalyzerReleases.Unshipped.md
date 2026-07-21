### New Rules

Rule ID | Category | Severity | Notes
-------|--------|--------|-----
SP0024 | Usage | Error | Reports malformed SharpProof contract arguments.
SP0025 | Configuration | Warning | Reports invalid supported analyzer options.
SP0027 | Contracts | Warning | Reports calls that do not prove a `[Requires]` precondition.
SP0028 | Contracts | Warning | Reports `[Requires]` preconditions that cannot be translated or proven.
SP0030 | ExceptionFlow | Warning | Reports escaping exceptions that violate exception contracts.
SP0041 | Nullability | Warning | Reports nullable return contract violations.
SP0042 | Nullability | Warning | Reports nullable parameter postcondition violations.
SP0043 | Nullability | Warning | Reports nullable member contract violations.
SP0044 | Nullability | Warning | Reports null-forgiving operators that can suppress a feasible null value.
