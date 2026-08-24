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
SP0050 | Infrastructure | Error | Reports a referenced contract API assembly that could not be read to verify its payload.
SPCF0001 | SharpProof.ContractFor.Usage | Error | Reports an invalid ContractFor target.
SPCF0002 | SharpProof.ContractFor.Usage | Error | Reports duplicate ContractFor companions.
SPCF0003 | SharpProof.ContractFor.Usage | Error | Reports an invalid ContractFor companion type.
SPCF0004 | SharpProof.ContractFor.Usage | Error | Reports a missing ContractFor member.
SPCF0005 | SharpProof.ContractFor.Usage | Error | Reports a ContractFor member signature mismatch.
SPCF0006 | SharpProof.ContractFor.Usage | Error | Reports an ambiguous ContractFor member.
SPCF0007 | SharpProof.ContractFor.Usage | Error | Reports a ContractFor member without a required body.
SPCF0008 | SharpProof.ContractFor.Usage | Error | Reports invalid ContractFor clause placement.
