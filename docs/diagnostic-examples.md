# SharpProof diagnostics

The authoritative descriptors are generated as static fields in
`SharpProof.Analyzer/GeneratedDiagnosticDescriptors.cs`. Feature diagnostics
are Info and disabled by default. SP0024 and SP0025 are enabled because they
report invalid usage or configuration.

<a id="sp0002"></a>
## SP0002 - purity not proven

`[EnforcePure]` cannot be established because the method has observable or
unknown effects.

<a id="sp0013"></a>
## SP0013 - allocation in a zero-allocation method

Reserved for a future replay-validated allocation witness. The v2 analyzer's
path-insensitive may-effect summary does not emit this definitive diagnostic.

<a id="sp0015"></a>
## SP0015 - disallowed capability

Reserved for a future replay-validated capability witness. The v2 analyzer's
path-insensitive may-effect summary does not emit this definitive diagnostic.

<a id="sp0016"></a>
## SP0016 - capability contract not proven

Capability effects are incomplete or include a possibly disallowed
capability. Unknown never counts as success.

<a id="sp0024"></a>
## SP0024 - invalid contract argument

A control attribute has a missing or blank reason, or another closed contract
argument is malformed.

<a id="sp0025"></a>
## SP0025 - invalid analyzer configuration

`sharpproof_mode` is not `off`, `effects`, `contracts`, or
`all-experimental`. Analysis fails closed.

<a id="sp0027"></a>
## SP0027 - precondition violated

A compiler-bound `Contract.Requires(...)` expression evaluates to false for
the concrete call arguments. Unknown arguments and unsupported contracts are
silent.

<a id="sp0030"></a>
## SP0030 - exception contract violated

Reserved for a future replay-validated escaping-exception witness. The v2
analyzer's path-insensitive may-effect summary does not emit this definitive
diagnostic.

<a id="sp0045"></a>
## SP0045 - zero-allocation contract not proven

Allocation behavior is incomplete or includes a possible allocation, so
SharpProof refuses to claim success.

<a id="sp0046"></a>
## SP0046 - exception contract not proven

Exception behavior is incomplete or includes a possibly disallowed exception,
so SharpProof refuses to claim success.
