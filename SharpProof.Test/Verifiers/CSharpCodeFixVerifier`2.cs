using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using System.Threading;
using System.Threading.Tasks;

namespace SharpProof.Test
{
    public static partial class CSharpCodeFixVerifier<TAnalyzer, TCodeFix>
        where TAnalyzer : DiagnosticAnalyzer, new()
        where TCodeFix : CodeFixProvider, new()
    {

        public static DiagnosticResult Diagnostic()
            => CSharpCodeFixVerifier<TAnalyzer, TCodeFix, DefaultVerifier>.Diagnostic();


        public static DiagnosticResult Diagnostic(string diagnosticId)
            => CSharpCodeFixVerifier<TAnalyzer, TCodeFix, DefaultVerifier>.Diagnostic(diagnosticId);


        public static DiagnosticResult Diagnostic(DiagnosticDescriptor descriptor)
            => CSharpCodeFixVerifier<TAnalyzer, TCodeFix, DefaultVerifier>.Diagnostic(descriptor);


        public static async Task VerifyAnalyzerAsync(string source, params DiagnosticResult[] expected)
        {
            var test = new Test
            {
                TestCode = source,
            };

            AddSharpProofReferences(test, source);
            test.ExpectedDiagnostics.AddRange(expected);
            await test.RunAsync(CancellationToken.None);
        }


        public static async Task VerifyCodeFixAsync(string source, string fixedSource)
            => await VerifyCodeFixAsync(source, DiagnosticResult.EmptyDiagnosticResults, fixedSource, null);


        public static async Task VerifyCodeFixAsync(string source, DiagnosticResult expected, string fixedSource)
            => await VerifyCodeFixAsync(source, new[] { expected }, fixedSource, null, null);


        public static async Task VerifyCodeFixAsync(string source, DiagnosticResult expected, string fixedSource, int codeActionIndex)
            => await VerifyCodeFixAsync(source, new[] { expected }, fixedSource, codeActionIndex, null);


        public static async Task VerifyCodeFixAsync(string source, DiagnosticResult expected, string fixedSource, string codeActionEquivalenceKey)
            => await VerifyCodeFixAsync(source, new[] { expected }, fixedSource, null, codeActionEquivalenceKey);


        public static async Task VerifyCodeFixAsync(string source, DiagnosticResult[] expected, string fixedSource, int? codeActionIndex = null, string? codeActionEquivalenceKey = null)
        {
            var test = new Test
            {
                TestCode = NormalizeLineEndings(source),
                FixedCode = NormalizeLineEndings(fixedSource),
            };

            if (codeActionIndex.HasValue)
                test.CodeActionIndex = codeActionIndex.Value;
            if (codeActionEquivalenceKey != null)
                test.CodeActionEquivalenceKey = codeActionEquivalenceKey;

            AddSharpProofReferences(test, source);
            test.ExpectedDiagnostics.AddRange(expected);
            await test.RunAsync(CancellationToken.None);
        }

        private static string NormalizeLineEndings(string text)
            => text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", System.Environment.NewLine);

        private static void AddSharpProofReferences(Test test, string source)
        {
            test.TestState.AdditionalReferences.Add(SharpProofVerifierReferences.EnforcePureAttributeReference);
            var globalConfigText = "is_global = true\nsharpproof_attribute_stub_namespaces = <global>\n";
            if (AnalyzerTestHost.HasFileLevelMissingPuritySuppression(source))
            {
                globalConfigText += "sharpproof_suggest_missing_enforce_pure = false\n";
            }

            test.TestState.AnalyzerConfigFiles.Add(("/.globalconfig", globalConfigText));
        }
    }
}
