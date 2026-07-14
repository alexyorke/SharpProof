using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace SharpProof.Test;

public static partial class CSharpAnalyzerVerifier<TAnalyzer>
    where TAnalyzer : DiagnosticAnalyzer, new()
{
    public class Test : CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
    {
        public Test()
        {
            ReferenceAssemblies = CSharpVerifierHelper.Net80ReferenceAssemblies;
            SolutionTransforms.Add(CSharpVerifierHelper.ConfigureCompilationOptions);
        }
    }
}
