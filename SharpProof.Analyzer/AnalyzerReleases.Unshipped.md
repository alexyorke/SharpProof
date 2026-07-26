### New Rules

Rule ID | Category | Severity | Notes
-------|--------|--------|-----
SP0002 | Purity | Disabled | Reports `[EnforcePure]` methods whose observable purity is not proven.
SP0013 | Allocation | Disabled | Reserved for known allocations in `[ZeroAllocations]` methods; currently not emitted.
SP0015 | Capabilities | Disabled | Reserved for known capabilities outside `[AllowedCapabilities]`; currently not emitted.
SP0016 | Capabilities | Disabled | Reports capability contracts whose rich effect summary remains unknown.
SP0024 | Usage | Error | Reports invalid capability flags, exception types, and blank suppression or trust reasons.
SP0025 | Configuration | Warning | Reports invalid supported analyzer options.
SP0027 | Contracts | Disabled | Reports only compiler-bound call-site `[Requires]` preconditions that concretely replay as false; unknown or throwing evaluation is silent.
SP0030 | ExceptionFlow | Disabled | Reserved for escaping-exception violations once effect-trace replay exists; currently not emitted.
SP0045 | Allocation | Disabled | Reports `[ZeroAllocations]` contracts that could not be verified.
SP0046 | ExceptionFlow | Disabled | Reports exception contracts that could not be verified.
