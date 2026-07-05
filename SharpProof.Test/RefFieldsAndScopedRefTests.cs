using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using System.Threading.Tasks;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;
using SharpProof.Attributes;
using System;
using Microsoft.CodeAnalysis.CSharp.Testing;

namespace SharpProof.Test
{
    [TestFixture]
    public class RefFieldsAndScopedRefTests
    {

        private const string MinimalEnforcePureAttributeSource = @"
namespace SharpProof.Attributes
{
    [System.AttributeUsage(System.AttributeTargets.All)]
    public sealed class EnforcePureAttribute : System.Attribute { }
}
";

        [Test]
        public async Task ScopedRef_PureMethod_NoDiagnostic()
        {
            var test = @"
// Requires LangVersion 11+
#nullable enable
using System;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class ScopedRefTest
    {
        [EnforcePure]
        public ref readonly int GetValue(scoped ref readonly int[] array, int index)
        {
            // Using scoped ref readonly parameters (pure)
            return ref array[index];
        }
    }
}
" + MinimalEnforcePureAttributeSource;

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ScopedRef_ImpureMethod_Diagnostic()
        {
            var test = @"
// Requires LangVersion 11+
#nullable enable
using System;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class ScopedRefImpureTest
    {
        [EnforcePure]
        public void ModifyValue(scoped ref int[] array, int index)
        {
            // Using scoped ref parameter but modifying the array (impure)
            array[index] = 42;
        }
    }
}
" + MinimalEnforcePureAttributeSource;


            var expected = new[] {
                VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule).WithSpan(12, 21, 12, 32).WithArguments("ModifyValue"),
            };

            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task ScopedRefLocal_PureMethod_NoDiagnostic()
        {
            var test = @"
// Requires LangVersion 11+
#nullable enable
using System;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class ScopedRefLocalTest
    {
        [EnforcePure]
        public int GetValueSum(int[] array)
        {
            // Using scoped ref readonly for locals (pure)
            scoped ref readonly int first = ref array[0];
            scoped ref readonly int last = ref array[array.Length - 1];
            
            // Pure operation - just reading values
            return first + last;
        }
    }
}
" + MinimalEnforcePureAttributeSource;

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ModifyRefArray_ImpureMethod_Diagnostic()
        {
            var test = @"
// Requires LangVersion 11+
#nullable enable
using System;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class RefArrayModifierTest
    {
        [EnforcePure]
        public void ModifyArray(int[] array)
        {
            // Directly modify array element (impure)
            array[0] = 42;
        }
    }
}
" + MinimalEnforcePureAttributeSource;


            var expected = new[]
            {
                VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule).WithSpan(12, 21, 12, 32).WithArguments("ModifyArray"),
            };

            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task RefArrayAssignment_ImpureMethod_Diagnostic()
        {
            var test = @"
// Requires LangVersion 11+
#nullable enable
using System;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class RefArrayAssignmentTest
    {
        [EnforcePure]
        public void AssignValues(int[] array)
        {
            int temp = array[0];
            array[0] = array[1]; // Impure modification
            array[1] = temp;     // Impure modification
        }
    }
}
" + MinimalEnforcePureAttributeSource;


            var expected = new[] {
                VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule).WithSpan(12, 21, 12, 33).WithArguments("AssignValues"),
            };

            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }
    }
}


