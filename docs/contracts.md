# Contracts

SharpProof derives contracts from one `MethodEffects` analysis.

- `[EnforcePure]` requires a proven observable-purity verdict.
- `[ZeroAllocations]` rejects allocation effect sites.
- `[AllowedCapabilities]` compares inferred capability facts with the declared mask.
- `[DoesNotThrow]` and `[AllowedExceptions]` project thrown-exception facts.
- `[EffectContract]` declares effects at a trusted boundary.
- `[Requires]`, `[Ensures]`, nullable contracts, and complexity contracts retain their symbolic proof logic and are reported alongside the method result.

Fresh allocations and deterministic exceptions are compatible with observable purity. Mutation of fresh owned state is distinct from mutation of receiver, argument, captured, static, or ambient state.
