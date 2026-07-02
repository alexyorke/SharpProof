using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using System.Threading;
using System.Threading.Tasks;

namespace PurelySharp.Test
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

            AddPurelySharpReferences(test);
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
                TestCode = source,
                FixedCode = fixedSource,
            };

            if (codeActionIndex.HasValue)
                test.CodeActionIndex = codeActionIndex.Value;
            if (codeActionEquivalenceKey != null)
                test.CodeActionEquivalenceKey = codeActionEquivalenceKey;

            AddPurelySharpReferences(test);
            test.ExpectedDiagnostics.AddRange(expected);
            await test.RunAsync(CancellationToken.None);
        }

        private static void AddPurelySharpReferences(Test test)
        {
            test.TestState.AdditionalReferences.Add(PurelySharpVerifierReferences.AnalyzerReference);
            test.TestState.AdditionalReferences.Add(PurelySharpVerifierReferences.EnforcePureAttributeReference);
            test.TestState.AdditionalReferences.Add(PurelySharpVerifierReferences.PureAttributeReference);
        }
    }
}

