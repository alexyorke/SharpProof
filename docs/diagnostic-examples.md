# SharpProof diagnostics

The authoritative descriptors are static fields in
`SharpProof.Analyzer/GeneratedDiagnosticDescriptors.cs` and
`SharpProof.ContractForGenerator/GeneratedDiagnosticDescriptors.cs`.

The main package defaults to `SharpProofMode=off`, which omits both the analyzer
and the `ContractFor` generator from compiler analyzer items. Enable a mode in
the project:

```xml
<PropertyGroup>
  <SharpProofMode>all-experimental</SharpProofMode>
</PropertyGroup>
```

Main feature diagnostics are `Info` and disabled by default. Enabling a mode
selects a pipeline; it does not opt those IDs into editor reporting. Configure
the IDs you want:

```ini
dotnet_diagnostic.SP0002.severity = suggestion
dotnet_diagnostic.SP0016.severity = suggestion
dotnet_diagnostic.SP0045.severity = suggestion
dotnet_diagnostic.SP0046.severity = suggestion
dotnet_diagnostic.SP0027.severity = warning
```

`SP0024` is an enabled-by-default error and `SP0025` is an
enabled-by-default warning. The `SPCF` rules are enabled-by-default errors once
the generator is loaded.

## Main analyzer summary

| ID | Mode | Descriptor default | Emitted now? |
|---|---|---|---|
| `SP0002` | `effects` | Info, off | Yes |
| `SP0013` | `effects` | Info, off | Reserved |
| `SP0015` | `effects` | Info, off | Reserved |
| `SP0016` | `effects` | Info, off | Yes |
| `SP0024` | Any loaded mode | Error, on | Yes |
| `SP0025` | Invalid loaded configuration | Warning, on | Yes |
| `SP0027` | `contracts` | Info, off | Yes |
| `SP0030` | `effects` | Info, off | Reserved |
| `SP0045` | `effects` | Info, off | Yes |
| `SP0046` | `effects` | Info, off | Yes |

`all-experimental` enables both feature pipelines. Unsupported analyzer
callables abstain silently even when an ID is enabled.

<a id="sp0002"></a>
## SP0002 - purity not proven

`[EnforcePure]` cannot be established because the method summary is incomplete
or includes observable reads, writes, capabilities, or other effects forbidden
by observable purity.

This is a not-proven diagnostic. It does not claim a replayed impure trace.

<a id="sp0013"></a>
## SP0013 - allocation in a zero-allocation method

Reserved for a future replay-validated allocation witness. The current
path-insensitive may-effect analyzer never emits SP0013.

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

The compilation-global `sharpproof_mode` option or `SharpProofMode` build
property is not `off`, `effects`, `contracts`, or `all-experimental`.
SharpProof reports an enabled-by-default warning and analyzes the compilation
as `off`.

Tree-local attempts to set this compilation-global option are also invalid
unless they exactly match the global value.

<a id="sp0027"></a>
## SP0027 - precondition violated

A compiler-bound `Contract.Requires(...)` clause or closed parameter
precondition evaluates to false for an exact call site. SharpProof reports only
after exact receiver/argument substitution and concrete IR replay.

Unknown arguments, unsupported expressions, possible receiver/argument/prefix
throws, and non-definitely-executed calls remain silent.

Example:

```csharp
using SharpProof.Attributes;

static class Example {
    private static void Positive(int value) {
        Contract.Requires(value > 0);
    }

    internal static void Call() {
        Positive(0); // SP0027 when contracts mode and SP0027 are enabled.
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
until it has concrete effect-trace replay.

<a id="contractfor-generator-diagnostics"></a>
## ContractFor generator diagnostics

The incremental `ContractFor` generator validates companions and emits no
source. All eight rules are enabled-by-default errors once the generator is
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
        Contract.Ensures(
            Contract.Result<string?>() is null or not null);
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

## What diagnostics do not mean

- Diagnostic silence is not proof. A callable may be unsupported or a feature
  ID may still be disabled.
- SP0002, SP0016, SP0045, and SP0046 report inability to prove a contract, not a
  replayed violating execution.
- SP0027 is stronger: it is emitted only after concrete predicate replay
  evaluates to false.
- Worker `Unknown` reasons are protocol records, not Roslyn diagnostics. See
  [Typed abstention reasons](unknown-reasons.md).
