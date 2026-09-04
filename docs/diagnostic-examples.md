# SharpProof diagnostics

The authoritative descriptor catalog is
`eng/diagnostics/diagnostic-descriptors.v1.json`. The checked-in
`*DiagnosticDescriptors.generated.cs` files in the analyzer, `ContractFor`
generator, and soundness meta-analyzer are compiled projections.
Run `.\scripts\Generate-DiagnosticDescriptors.ps1` after catalog changes; CI
uses `-Verify` to reject stale projections.

The portable `SharpProof` package defaults to advisory analysis with both
feature groups:

```xml
<PropertyGroup>
  <SharpProofProfile>advisory</SharpProofProfile>
  <SharpProofFeatures>all</SharpProofFeatures>
</PropertyGroup>
```

`SharpProofProfile` is `advisory`, `strict`, or `off`.
`SharpProofFeatures` is `effects`, `contracts`, or `all`. Main feature
diagnostics are enabled `Info` diagnostics by default, except SP0027 is a
Warning. Configure their effective severities with normal Roslyn settings:

The selected feature value is compiler-visible, enters the closed verifier
artifact, and filters its manifest. Contract-only artifacts ignore effect-only
annotations; effect-only artifacts do not create postcondition claims.

```ini
dotnet_diagnostic.SP0002.severity = suggestion
dotnet_diagnostic.SP0016.severity = suggestion
dotnet_diagnostic.SP0045.severity = suggestion
dotnet_diagnostic.SP0046.severity = suggestion
dotnet_diagnostic.SP0027.severity = warning
```

`SP0024` is an error and `SP0025` is a warning. The `SPCF` rules are errors once
the generator is loaded. Unsupported unannotated methods are quiet; an
unsupported explicitly selected method produces SP0047. SP0049 is a fatal
compiler-collection infrastructure error during container verification.

## Main analyzer summary

| ID | Feature/profile | Descriptor default | Emitted now? |
|---|---|---|---|
| `SP0002` | `effects` | Info, on | Yes |
| `SP0013` | `effects` | Info, on | Reserved |
| `SP0015` | `effects` | Info, on | Reserved |
| `SP0016` | `effects` | Info, on | Yes |
| `SP0024` | Any non-`off` profile | Error, on | Yes |
| `SP0025` | Invalid configuration | Warning, on | Yes |
| `SP0027` | `contracts` | Warning, on | Yes |
| `SP0030` | `effects` | Info, on | Reserved |
| `SP0045` | `effects` | Info, on | Yes |
| `SP0046` | `effects` | Info, on | Yes |
| `SP0047` | Explicitly selected unsupported method | Info, on | Yes |
| `SP0049` | Container verification compiler manifest | Error, on | On artifact failure |
| `SP0050` | Referenced contract API assembly | Error, on | On unreadable payload |

`SharpProofFeatures=all` enables both feature pipelines. The former
`SharpProofMode` and `all-experimental` compatibility inputs are removed.

<a id="sp0002"></a>
## SP0002 - purity not proven

`[EnforcePure]` cannot be established because the method summary is incomplete
or includes observable reads, writes, capabilities, or other effects forbidden
by observable purity.

This is a not-proven diagnostic. It does not claim a replayed impure trace.

<a id="sp0013"></a>
## SP0013 - allocation in a zero-allocation method

Reserved as a live-analyzer diagnostic. The current path-insensitive
may-effect analyzer never emits SP0013. Separately, the opt-in container worker
can publish a typed effect `Refuted` result after independently replaying the
schema-10 event for an unconditional definite managed object/array allocation.

A possible allocation is reported as SP0045 instead.

<a id="sp0015"></a>
## SP0015 - disallowed capability

Reserved for a future replay-validated capability witness. The current
path-insensitive may-effect analyzer never emits SP0015.

A possibly disallowed capability is reported as SP0016 instead.

<a id="sp0016"></a>
## SP0016 - capability contract not proven

`[AllowedCapabilities(...)]` cannot be established because the capability
summary is incomplete, unknown, or includes a capability outside the declared
set. This is conservative may-analysis output, not a definitive capability
trace.

<a id="sp0024"></a>
## SP0024 - invalid contract argument

A SharpProof contract or control attribute has a malformed argument. The
current analyzer reports SP0024 for:

- a missing or blank `[SharpProofTrusted]` or `[SharpProofSuppress]` reason;
- an undefined `[AllowedCapabilities]` flag value;
- malformed or non-exception `[AllowedExceptions]` types;
- `[NotNull]` on a value that cannot be null, `[Positive]` on an unsupported
  type, or `[InRange]` with an unsupported type or unordered bounds;
- a `Requires`, `Ensures`, or `Assume` clause that is conditional, nested,
  unreachable, late, or otherwise not a direct contiguous prologue statement.

SP0024 is an enabled-by-default error because invalid control data cannot be
silently interpreted.

<a id="sp0025"></a>
## SP0025 - invalid analyzer configuration

The compilation-global `sharpproof_profile`/`SharpProofProfile` or
`sharpproof_features`/`SharpProofFeatures` value is invalid, or the removed
`sharpproof_mode`/`SharpProofMode` alias was supplied. Valid profile values are
`advisory`, `strict`, and `off`; feature values are `effects`, `contracts`, and
`all`. SharpProof reports a warning and analyzes an invalid configuration as
`off`.

Tree-local attempts to set this compilation-global option are also invalid
unless they exactly match the global value.

SharpProof also reports SP0025 and disables analysis when the reserved
`SHARPPROOF_CONTRACTS` preprocessor symbol is active. That symbol changes ghost
contract calls into runtime calls, so continuing analysis would make the
verified body differ from the emitted program. Package builds reject an exact
`DefineConstants` entry before compilation; compiler validation also covers
source-local directives and generated trees.

<a id="sp0027"></a>
## SP0027 - precondition violated

A compiler-bound `Contract.Requires(...)` clause or closed parameter
precondition evaluates to false for an exact ordinary invocation or object
creation. SharpProof reports only after exact receiver/argument substitution
and concrete IR replay.

Unknown arguments, unsupported expressions, possible receiver/argument/prefix
throws, and non-definitely-executed calls remain silent.

Direct top-level expression statements, returns, throws, single local
initializers, simple assignments with definitely non-throwing targets,
expression-bodied members, and constructor initializers are replayable shapes.

Example:

```csharp
using SharpProof.Attributes;

static class Example {
    private static void Positive(int value) {
        Contract.Requires(value > 0);
    }

    internal static void Call() {
        Positive(0); // SP0027 when contract features are enabled.
    }
}
```

<a id="sp0030"></a>
## SP0030 - exception contract violated

Reserved for a future replay-validated escaping-exception witness. The current
path-insensitive may-effect analyzer never emits SP0030.

A possible or unknown disallowed exception is reported as SP0046 instead.

<a id="sp0045"></a>
## SP0045 - zero-allocation contract not proven

`[ZeroAllocations]` cannot be established because allocation behavior is
incomplete or the may summary includes possible allocation.

```csharp
using SharpProof.Attributes;

static class Example {
    [ZeroAllocations]
    internal static object Create() => new object(); // SP0045, not SP0013.
}
```

<a id="sp0046"></a>
## SP0046 - exception contract not proven

`[DoesNotThrow]` or `[AllowedExceptions(...)]` cannot be established because
the exception summary is incomplete, contains unknown exceptions, or includes
a possibly disallowed exception.

This is a not-proven result. The analyzer reserves definitive SP0030 reporting
until it has concrete exception-effect replay.

<a id="sp0047"></a>
## SP0047 - selected analysis incomplete

The analyzer emits SP0047 when a contract or SharpProof annotation explicitly
selects a method but the method is outside the supported analyzer subset.
This includes selected abstract, interface, and `extern` declarations that have
no operation body. Unannotated or explicitly suppressed unsupported methods
remain silent.

SP0047 also reports `ContractApiIdentityRejected` when a clause or annotation
binds to a source/project lookalike, a mismatched `SharpProof.Attributes`
assembly, or a malformed non-elided contract API. The rejected symbol supplies
no proof fact.

The verifier launcher also emits SP0047 when one or more selected callables
have incomplete coverage or an `Unknown` claim. Its severity comes from
`SharpProofVerifyPolicy`: `advisory` is information,
`warn-on-unknown` is a warning, and `require-proven` is an error that fails the
build. SP0047 never means the method was proven.

<a id="sp0048"></a>
## SP0048 - user assumption or trusted evidence

SP0048 is a verifier-launcher diagnostic, not a Roslyn analyzer descriptor. It
reports declared `Contract.Assume` or `[SharpProofTrusted]` evidence recorded
by the manifest. `SharpProofAssumptionPolicy=allow` reports information,
`warn` reports a warning, and `error` fails the build. Advisory defaults to
`allow`; strict defaults to `error`.

<a id="sp0049"></a>
## SP0049 - final compiler manifest emission failed

The production analyzer emits SP0049 when container verification requested a
post-generator compiler-manifest artifact but could not collect or write it.
This includes invalid compiler-visible expression depth; resolver-dependent
`#r`/`#load` or missing-assembly resolution; reference supersession; a custom
assembly-identity comparer; a non-file or unreadable metadata reference; and
artifact lowering, serialization, or write failure. The diagnostic is an error
because the required closed compiler evidence is missing. It is an
infrastructure failure, never a contract or proof outcome.

Compiler artifact schema version 18 includes the sealed selected-claim manifest,
compiler diagnostics, source/generated-tree hashes and parse evidence, and,
for each supported selected callable, bound contract/spec metadata plus
portable whole-body lowered CFG/IR. It contains no source text. The worker
hydrates this artifact without constructing a Roslyn compilation or rereading
references. Exact manifest/lowered-callable equality and the expression-depth
match are required before cache lookup or backend creation.

<a id="sp0050"></a>
## SP0050 - contract API could not be verified

SharpProof pins the exact payload of the `SharpProof.Attributes` assembly it
was built against. SP0050 is emitted when that assembly is referenced and
located but cannot be read to check the pin -- a sharing violation, an
antivirus scanner, a permission failure, or an unreadable network share.

The diagnostic exists because the failure would otherwise be invisible. An
unverifiable contract API leaves every `Contract.Requires`, `Contract.Ensures`,
and closed contract attribute unresolvable. SP0050 makes that infrastructure
failure explicit; it is never a contract or proof outcome.

A readable payload whose hash does not match the pin is rejected and every
attempted use reports SP0047 `ContractApiIdentityRejected`; the rejected symbol
supplies no proof fact. SP0050 is reserved for a payload that cannot be read.

<a id="contractfor-generator-diagnostics"></a>
## ContractFor generator diagnostics

The incremental `ContractFor` generator validates companions and emits no
source. All ten rules are enabled-by-default errors once the generator is
loaded.

A valid instance-member companion uses a static class and an explicit receiver
parameter:

```csharp
#nullable enable
using SharpProof.Attributes;

public interface IService {
    string? Find(string key);
}

[ContractFor(typeof(IService))]
public static class IServiceContracts {
    public static string? Find(IService receiver, string key) {
        Contract.Requires(receiver is not null);
        Contract.Requires(key.Length > 0);
        Contract.Ensures(Contract.Result<string?>() == null);
        return null;
    }
}
```

The companion method is contract source, not an implementation and not
generated code. Its generic arity/constraints, receiver, parameters, ref and
scoped kinds, nullability, defaults, and return shape must match exactly.

<a id="spcf0001"></a>
### SPCF0001 - invalid ContractFor target

The companion's `[ContractFor(...)]` argument does not identify one resolvable
named target type. Missing, error, ambiguous, and non-named targets are
rejected.

<a id="spcf0002"></a>
### SPCF0002 - duplicate ContractFor companion

More than one companion targets the same type. Exactly one companion is
allowed, so each duplicate declaration is diagnosed.

<a id="spcf0003"></a>
### SPCF0003 - invalid ContractFor companion type

The companion is not a static class, or its generic arity and constraints do
not exactly match the target type.

<a id="spcf0004"></a>
### SPCF0004 - missing ContractFor member

A target ordinary method has no exact companion member. A companion that is
intended to describe the target surface must cover each required ordinary
member.

<a id="spcf0005"></a>
### SPCF0005 - ContractFor member signature mismatch

A named companion method does not exactly match a target overload. Matching
includes the explicit receiver where required, generic constraints, ref/scoped
kinds, nullability, defaults, parameter types, and return type.

<a id="spcf0006"></a>
### SPCF0006 - ambiguous ContractFor member

A companion method shape can map to more than one target member, so symbol
identity cannot be established uniquely.

<a id="spcf0007"></a>
### SPCF0007 - ContractFor member body required

The companion member has no compiler-bound source body. Abstract, extern, or
otherwise bodyless declarations cannot carry executable compiler-bound
clauses.

<a id="spcf0008"></a>
### SPCF0008 - invalid ContractFor clause placement

A companion `Contract.Requires`, `Ensures`, or `Assume` call is not a direct,
reachable statement in the method's contiguous contract prologue. Conditional,
nested-callable, unreachable, late, and structurally nested clauses do not
describe the target member.

<a id="spcf0009"></a>
### SPCF0009 - ContractFor companion targets itself

The companion and target are the same type. A ContractFor companion must be a
distinct type so its specification members cannot be mistaken for the target's
executable implementation.

<a id="spcf0010"></a>
### SPCF0010 - cyclic ContractFor relationship

The companion-to-target edge participates in a cycle. Every edge in the cycle
is rejected so no cyclic companion can supply contracts or suppress analysis of
its executable method bodies.

## What diagnostics do not mean

- Diagnostic silence is not proof. Unannotated code may be unsupported or a
  diagnostic may be suppressed.
- SP0002, SP0016, SP0045, and SP0046 report inability to prove a contract, not a
  replayed violating execution.
- SP0027 is stronger: it is emitted only after concrete predicate replay
  evaluates to false.
- SP0047 is explicit incomplete analysis, SP0048 is explicit user/trusted
  evidence, and SP0049 is a compilation-collection infrastructure failure;
  none is a proof outcome.
- Worker `Unknown` reasons are protocol records, not Roslyn diagnostics. See
  [Typed abstention reasons](unknown-reasons.md).
