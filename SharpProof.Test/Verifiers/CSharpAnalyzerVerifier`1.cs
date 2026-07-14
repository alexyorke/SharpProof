using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace SharpProof.Test;

public static partial class CSharpAnalyzerVerifier<TAnalyzer>
    where TAnalyzer : DiagnosticAnalyzer, new()
{
    public static DiagnosticResult Diagnostic()
    {
        return CSharpAnalyzerVerifier<TAnalyzer, DefaultVerifier>.Diagnostic();
    }


    public static DiagnosticResult Diagnostic(string diagnosticId)
    {
        return CSharpAnalyzerVerifier<TAnalyzer, DefaultVerifier>.Diagnostic(diagnosticId);
    }


    public static DiagnosticResult Diagnostic(DiagnosticDescriptor descriptor)
    {
        return CSharpAnalyzerVerifier<TAnalyzer, DefaultVerifier>.Diagnostic(descriptor);
    }


    public static Task VerifyAnalyzerAsync(string source, params DiagnosticResult[] expected)
    {
        var test = new Test
        {
            TestCode = source
        };

        test.TestState.AdditionalReferences.Add(SharpProofVerifierReferences.EnforcePureAttributeReference);
        test.TestState.AnalyzerConfigFiles.Add(("/.globalconfig",
            CSharpVerifierHelper.CreateGlobalConfigText(source)));

        test.ExpectedDiagnostics.AddRange(expected);
        return test.RunAsync(CancellationToken.None);
    }

    public static Task VerifyAnalyzerAsync(
        IReadOnlyList<(string Filename, string Source)> sources,
        params DiagnosticResult[] expected)
    {
        if (sources.Count == 0) throw new ArgumentException("At least one source is required.", nameof(sources));

        var test = new Test { TestCode = sources[0].Source };
        for (var index = 1; index < sources.Count; index++)
            test.TestState.Sources.Add(sources[index]);

        test.TestState.AdditionalReferences.Add(SharpProofVerifierReferences.EnforcePureAttributeReference);
        test.TestState.AnalyzerConfigFiles.Add(("/.globalconfig",
            CSharpVerifierHelper.CreateGlobalConfigText(sources[0].Source, suppressMissingPurity: true)));
        test.ExpectedDiagnostics.AddRange(expected);
        return test.RunAsync(CancellationToken.None);
    }
}
