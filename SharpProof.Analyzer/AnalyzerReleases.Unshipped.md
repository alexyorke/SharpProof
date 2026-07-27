### New Rules

Rule ID | Category | Severity | Notes
-------|--------|--------|-----
SP0002 | Purity | Info | Reports `[EnforcePure]` methods whose observable purity is not proven.
SP0013 | Allocation | Info | Reserved for known allocations in `[ZeroAllocations]` methods; currently not emitted.
SP0015 | Capabilities | Info | Reserved for known capabilities outside `[AllowedCapabilities]`; currently not emitted.
SP0016 | Capabilities | Info | Reports capability contracts whose rich effect summary remains unknown.
SP0024 | Usage | Error | Reports invalid capability flags, exception types, and blank suppression or trust reasons.
SP0025 | Configuration | Warning | Reports invalid supported analyzer options.
SP0027 | Contracts | Warning | Reports only compiler-bound call-site `[Requires]` preconditions that concretely replay as false; unknown or throwing evaluation is silent.
SP0030 | ExceptionFlow | Info | Reserved for escaping-exception violations once effect-trace replay exists; currently not emitted.
SP0045 | Allocation | Info | Reports `[ZeroAllocations]` contracts that could not be verified.
SP0046 | ExceptionFlow | Info | Reports exception contracts that could not be verified.
SP0047 | Verification | Info | Reports selected methods outside the supported analyzer subset.
SP0049 | Infrastructure | Error | Reports failure to emit the selected final compiler manifest.
