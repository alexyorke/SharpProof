### New Rules

Rule ID | Category | Severity | Notes
-------|--------|--------|-----
SP0024 | Usage | Error | Reports malformed SharpProof contract arguments.
SP0025 | Configuration | Warning | Reports invalid supported analyzer options.
SP0027 | Contracts | Disabled | Reports calls that do not prove a `[Requires]` precondition.
SP0030 | ExceptionFlow | Disabled | Reports escaping exceptions that violate exception contracts.
SP0045 | Allocation | Disabled | Reports `[ZeroAllocations]` contracts that could not be verified.
SP0046 | ExceptionFlow | Disabled | Reports exception contracts that could not be verified.

### Removed Rules

Rule ID | Category | Severity | Notes
-------|--------|--------|-----
SP0018 | Contracts | Warning | Deep analyzer-side `[Ensures]` refutation was removed; opt-in worker verification owns supported postconditions.
SP0019 | Contracts | Warning | Deep analyzer-side `[Ensures]` unknown reporting was removed with the legacy string-contract pipeline.
SP0021 | Complexity | Warning | Statistical complexity contracts were removed because they do not have a crisp soundness oracle.
SP0022 | Complexity | Warning | Statistical complexity-contract unknown reporting was removed with the complexity analyzer.

### Changed Rules

Rule ID | New Category | New Severity | Old Category | Old Severity | Notes
-------|--------------|--------------|--------------|--------------|-----
SP0002 | Purity | Disabled | Purity | Error | Demoted to opt-in informational reporting for the narrowed analyzer.
SP0013 | Allocation | Disabled | Allocation | Warning | Demoted to opt-in informational reporting for the narrowed analyzer.
SP0015 | Capabilities | Disabled | Capabilities | Warning | Demoted to opt-in informational reporting for the narrowed analyzer.
SP0016 | Capabilities | Disabled | Capabilities | Warning | Demoted to opt-in informational reporting for the narrowed analyzer.
