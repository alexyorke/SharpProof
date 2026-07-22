# SharpProof diagnostics

The authoritative diagnostic titles, severities, messages, descriptions, and configuration are maintained in `SharpProof.Analyzer/AnalyzerDiagnosticCatalog.cs`. The links below provide stable anchors for analyzer help URLs. Purity is reported only through the `[EnforcePure]` contract; all verdicts are derived from the canonical method-effect result.

<a id="sp0002"></a> SP0002 - observable purity was not proven.
<a id="sp0013"></a> SP0013 - allocation violates `[ZeroAllocations]`.
<a id="sp0015"></a> SP0015 - disallowed capability.
<a id="sp0016"></a> SP0016 - capability contract is unknown.
<a id="sp0018"></a> SP0018 - postcondition disproven.
<a id="sp0019"></a> SP0019 - postcondition unknown.
<a id="sp0021"></a> SP0021 - complexity bound exceeded.
<a id="sp0022"></a> SP0022 - complexity bound unknown.
<a id="sp0024"></a> SP0024 - invalid contract argument.
<a id="sp0025"></a> SP0025 - invalid analyzer configuration.
<a id="sp0027"></a> SP0027 - precondition disproven.
<a id="sp0028"></a> SP0028 - precondition unknown.
<a id="sp0030"></a> SP0030 - exception contract violation.
<a id="sp0041"></a> SP0041 - nullable return contract violation.
<a id="sp0042"></a> SP0042 - nullable parameter contract violation.
<a id="sp0043"></a> SP0043 - nullable member contract violation.
<a id="sp0044"></a> SP0044 - unsafe null-forgiving operator.
<a id="sp0045"></a> SP0045 - `[ZeroAllocations]` could not be verified because allocation analysis is incomplete.
<a id="sp0046"></a> SP0046 - `[DoesNotThrow]` or `[AllowedExceptions]` could not be verified because exception escape analysis is incomplete.
<a id="sp0047"></a> SP0047 - a nullable flow contract could not be verified because its target or symbolic result is unknown.
