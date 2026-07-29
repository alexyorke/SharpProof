# Supported public API

SharpProof's supported compile-time API is the `SharpProof.Attributes`
package. The `SharpProof` package contributes portable analyzers, the
`ContractFor` generator, and build-transitive configuration, but intentionally
adds no compile-time assembly reference. `SharpProof.Verifier.Win-x64`
contributes build tooling and a Windows x64 worker, not an application API.

## Contract clauses and expressions

`Contract.Requires`, `Contract.Ensures`, and `Contract.Assume` are direct,
contiguous prologue clauses. They are compiler-elided unless
`SHARPPROOF_CONTRACTS` is defined. `Contract.Result<T>()` and
`Contract.Old<T>(T)` are expressions for use inside postconditions; executing
either placeholder directly throws.

`ContractForAttribute` associates a static companion class with a target
interface or class. The generator validates the association and member
matching by compiler symbol identity.

Direct and companion clauses are alternative sources, not additive ones. Any
valid direct clause on a target member makes that member the source for all of
its clauses; companion clauses are used only when the target has no valid
direct clause. Misplaced direct clauses still produce SP0024. They do not
displace a valid companion, and their complete compiler-elided invocation,
including argument evaluation, is omitted from verifier body execution.

## Closed parameter and return contracts

`NotNullAttribute`, `PositiveAttribute`, and `InRangeAttribute` apply only to
parameters and return values. Their constructors and properties are part of
the supported API. Invalid target or argument shapes produce diagnostics
instead of being treated as evidence.

## Effect contracts

The supported effect attributes are:

- `EnforcePureAttribute`
- `ZeroAllocationsAttribute`
- `DoesNotThrowAttribute`
- `AllowedCapabilitiesAttribute`
- `AllowedExceptionsAttribute`
- `EffectContractAttribute`

`SharpProofEffect` and `SharpProofCapability` are closed flag enums. Unknown
bits are invalid contract data, and each declared effect flag is independent:
for example, `Throws` does not imply `Allocates`. A complete
`EffectContractAttribute` can describe a reviewed external boundary;
`SharpProofTrustedAttribute` alone does not supply an effect fact. The
attribute defaults are conservative:
`Capabilities=None`, `ThrownExceptions=[]`, `IsDeterministic=false`, and
`Complete=false`. Every stronger boundary fact must be written explicitly.

## Reporting and trust controls

`SharpProofSuppressAttribute` changes diagnostic reporting only.
`SharpProofTrustedAttribute` records reviewed evidence and is visible to the
worker's assumption policy. Both require a nonempty reason.

## Documentation guarantee

`SharpProof.Attributes.xml` ships beside the package DLL for IntelliSense.
The Attributes test suite reflects the assembly and requires an exact XML
member-set match, a nonempty summary for every exported type and declared
public member, and parameter, type-parameter, return, and property-value text
where applicable. This makes an undocumented addition or stale entry a test
failure.

Analyzer, generator, protocol, and worker implementation assemblies are
package payloads rather than supported compile assets. Their public CLR
visibility is not a compatibility promise for consumers.
