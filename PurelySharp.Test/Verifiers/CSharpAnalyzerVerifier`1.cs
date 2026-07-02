using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using System.Threading;
using System.Threading.Tasks;

namespace PurelySharp.Test
{
    public static partial class CSharpAnalyzerVerifier<TAnalyzer>
        where TAnalyzer : DiagnosticAnalyzer, new()
    {

        public static DiagnosticResult Diagnostic()
            => CSharpAnalyzerVerifier<TAnalyzer, DefaultVerifier>.Diagnostic();


        public static DiagnosticResult Diagnostic(string diagnosticId)
            => CSharpAnalyzerVerifier<TAnalyzer, DefaultVerifier>.Diagnostic(diagnosticId);


        public static DiagnosticResult Diagnostic(DiagnosticDescriptor descriptor)
            => CSharpAnalyzerVerifier<TAnalyzer, DefaultVerifier>.Diagnostic(descriptor);


        public static Task VerifyAnalyzerAsync(string source, params DiagnosticResult[] expected)
        {
            var test = new Test
            {
                TestCode = source,
            };

            test.TestState.AdditionalReferences.Add(PurelySharpVerifierReferences.AnalyzerReference);
            test.TestState.AdditionalReferences.Add(PurelySharpVerifierReferences.EnforcePureAttributeReference);
            test.TestState.AdditionalReferences.Add(PurelySharpVerifierReferences.PureAttributeReference);

            test.ExpectedDiagnostics.AddRange(expected);
            return test.RunAsync(CancellationToken.None);
        }
    }
}

