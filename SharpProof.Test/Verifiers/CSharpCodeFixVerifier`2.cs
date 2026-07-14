using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace SharpProof.Test;

public static partial class CSharpCodeFixVerifier<TAnalyzer, TCodeFix>
    where TAnalyzer : DiagnosticAnalyzer, new()
    where TCodeFix : CodeFixProvider, new()
{
    public static DiagnosticResult Diagnostic()
    {
        return CSharpCodeFixVerifier<TAnalyzer, TCodeFix, DefaultVerifier>.Diagnostic();
    }


    public static DiagnosticResult Diagnostic(string diagnosticId)
    {
        return CSharpCodeFixVerifier<TAnalyzer, TCodeFix, DefaultVerifier>.Diagnostic(diagnosticId);
    }


    public static DiagnosticResult Diagnostic(DiagnosticDescriptor descriptor)
    {
        return CSharpCodeFixVerifier<TAnalyzer, TCodeFix, DefaultVerifier>.Diagnostic(descriptor);
    }


    public static async Task VerifyAnalyzerAsync(string source, params DiagnosticResult[] expected)
    {
        var test = new Test
        {
            TestCode = source
        };

        AddSharpProofReferences(test, source);
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync(CancellationToken.None);
    }


    public static async Task VerifyCodeFixAsync(string source, string fixedSource)
    {
        await VerifyCodeFixAsync(source, DiagnosticResult.EmptyDiagnosticResults, fixedSource);
    }


    public static async Task VerifyCodeFixAsync(string source, DiagnosticResult expected, string fixedSource)
    {
        await VerifyCodeFixAsync(source, new[] { expected }, fixedSource);
    }


    public static async Task VerifyCodeFixAsync(string source, DiagnosticResult expected, string fixedSource,
        int codeActionIndex)
    {
        await VerifyCodeFixAsync(source, new[] { expected }, fixedSource, codeActionIndex);
    }


    public static async Task VerifyCodeFixAsync(string source, DiagnosticResult expected, string fixedSource,
        string codeActionEquivalenceKey)
    {
        await VerifyCodeFixAsync(source, new[] { expected }, fixedSource, null, codeActionEquivalenceKey);
    }


    public static async Task VerifyCodeFixAsync(string source, DiagnosticResult[] expected, string fixedSource,
        int? codeActionIndex = null, string? codeActionEquivalenceKey = null)
    {
        await VerifyCodeFixCoreAsync(
            source,
            expected,
            fixedSource,
            null,
            codeActionIndex,
            codeActionEquivalenceKey);
    }


    public static async Task VerifyCodeFixAsync(
        string source,
        DiagnosticResult[] expected,
        string fixedSource,
        ImmutableDictionary<string, string> analyzerOptions,
        int? codeActionIndex = null,
        string? codeActionEquivalenceKey = null)
    {
        await VerifyCodeFixCoreAsync(
            source,
            expected,
            fixedSource,
            analyzerOptions,
            codeActionIndex,
            codeActionEquivalenceKey);
    }

    public static async Task VerifyNonLocalCodeFixAsync(
        string source,
        DiagnosticResult expected,
        string fixedSource,
        string codeActionEquivalenceKey)
    {
        await VerifyCodeFixCoreAsync(
            source,
            new[] { expected },
            fixedSource,
            null,
            null,
            codeActionEquivalenceKey,
            CodeFixTestBehaviors.SkipLocalDiagnosticCheck);
    }


    private static async Task VerifyCodeFixCoreAsync(
        string source,
        DiagnosticResult[] expected,
        string fixedSource,
        ImmutableDictionary<string, string>? analyzerOptions,
        int? codeActionIndex,
        string? codeActionEquivalenceKey,
        CodeFixTestBehaviors codeFixTestBehaviors = CodeFixTestBehaviors.None)
    {
        var test = new Test
        {
            TestCode = NormalizeLineEndings(source),
            FixedCode = NormalizeLineEndings(fixedSource)
        };
        test.CodeFixTestBehaviors = codeFixTestBehaviors;

        if (codeActionIndex.HasValue)
            test.CodeActionIndex = codeActionIndex.Value;
        if (codeActionEquivalenceKey != null)
            test.CodeActionEquivalenceKey = codeActionEquivalenceKey;

        AddSharpProofReferences(test, source, analyzerOptions);
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync(CancellationToken.None);
    }

    private static string NormalizeLineEndings(string text)
    {
        return text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine);
    }

    private static void AddSharpProofReferences(
        Test test,
        string source,
        ImmutableDictionary<string, string>? analyzerOptions = null)
    {
        test.TestState.AdditionalReferences.Add(SharpProofVerifierReferences.EnforcePureAttributeReference);
        test.TestState.AnalyzerConfigFiles.Add(("/.globalconfig",
            CSharpVerifierHelper.CreateGlobalConfigText(source, analyzerOptions)));
    }
}
