using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using System.Threading.Tasks;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test
{
    [TestFixture]
    public class FixedCollectionExpressionTests
    {
        [Test]
        public async Task PureMethod_MutableArrayWithArrayCreation_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;



public class CollectionExpressionExample
{
    [EnforcePure]
    public int[] GetNumbers()
    {
        // Using new[] array creation expression
        return new[] { 1, 2, 3, 4, 5 };
    }
}";

            var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
                                 .WithSpan(10, 18, 10, 28)
                                 .WithArguments("GetNumbers");


            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task PureMethod_MutableArrayCollectionExpressionSyntax_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;



public class CollectionExpressionExample
{
    [EnforcePure]
    public int[] {|SP0002:GetArray|}()
    {
        // Using collection expression syntax
        return [1, 2, 3, 4, 5];
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}


