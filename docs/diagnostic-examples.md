# SharpProof diagnostics

The authoritative diagnostic titles, severities, messages, descriptions, and configuration are maintained in `SharpProof.Analyzer/AnalyzerDiagnosticCatalog.json`. The links below provide stable anchors for analyzer help URLs. Purity is reported only through the `[EnforcePure]` contract; all verdicts are derived from the canonical method-effect result.

<a id="sp0002"></a> SP0002 - observable purity was not proven.

<a id="sp0003"></a> SP0003 - `[EnforcePure]` is misplaced.

<a id="sp0010"></a> SP0010 - exception summary.
<a id="sp0011"></a> SP0011 - uncaught exception site.
<a id="sp0013"></a> SP0013 - allocation violates `[ZeroAllocations]`.
<a id="sp0014"></a> SP0014 - `[ZeroAllocations]` is misplaced.
<a id="sp0015"></a> SP0015 - disallowed capability.
<a id="sp0016"></a> SP0016 - capability contract is unknown.
<a id="sp0017"></a> SP0017 - `[AllowedCapabilities]` is misplaced.
<a id="sp0018"></a> SP0018 - postcondition disproven.
<a id="sp0019"></a> SP0019 - postcondition unknown.
<a id="sp0020"></a> SP0020 - `[Ensures]` is misplaced.
<a id="sp0021"></a> SP0021 - complexity bound exceeded.
<a id="sp0022"></a> SP0022 - complexity bound unknown.
<a id="sp0023"></a> SP0023 - `[ExpectedComplexity]` is misplaced.
<a id="sp0024"></a> SP0024 - invalid contract argument.
<a id="sp0025"></a> SP0025 - invalid analyzer configuration.
<a id="sp0026"></a> SP0026 - unrecognized attribute identity.
<a id="sp0027"></a> SP0027 - precondition disproven.
<a id="sp0028"></a> SP0028 - precondition unknown.
<a id="sp0029"></a> SP0029 - `[Requires]` is misplaced.
<a id="sp0030"></a> SP0030 - exception contract violation.
<a id="sp0031"></a> SP0031 - exception contract is misplaced.
<a id="sp0032"></a> SP0032 - invalid analyzer input.
<a id="sp0033"></a> SP0033 - runtime hazard unknown.
<a id="sp0034"></a> SP0034 - inferred allocation contract.
<a id="sp0035"></a> SP0035 - inferred capability contract.
<a id="sp0036"></a> SP0036 - inferred complexity contract.
<a id="sp0037"></a> SP0037 - inferred exception contract.
<a id="sp0038"></a> SP0038 - inferred postcondition.
<a id="sp0039"></a> SP0039 - inferred precondition.
<a id="sp0040"></a> SP0040 - trusted effect boundary review.
<a id="sp0041"></a> SP0041 - nullable return contract violation.
<a id="sp0042"></a> SP0042 - nullable parameter contract violation.
<a id="sp0043"></a> SP0043 - nullable member contract violation.
<a id="sp0044"></a> SP0044 - unsafe null-forgiving operator.
<a id="sp0045"></a> SP0045 - unnecessary null-forgiving operator.
<a id="sp0046"></a> SP0046 - inferred nullable contract.
<a id="sp0047"></a> SP0047 - nullable verification unknown.
