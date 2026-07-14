using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
public class MathAndLinqTests
{
    [Test]
    public async Task ComplexPureLinqOperations_UnknownExternalEnumerator_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;
using System.Linq;
using System.Collections.Generic;



public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(IEnumerable<int> numbers)
    {
        // Pure LINQ delegate chain should stay diagnostic-free.
        return numbers
            .Where(x => x > 0)
            .Select(x => x * x)
            .OrderBy(x => x)
            .Take(5)
            .Sum();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ComplexNestedExpressions_NoDiagnostic()
    {
        await VerifyCS.VerifyAnalyzerAsync(MathAndAttributeTestSources.ComplexNestedExpressions);
    }

    [Test]
    public async Task SimpleMathMethod_NoDiagnostic()
    {
        await VerifyCS.VerifyAnalyzerAsync(MathAndAttributeTestSources.SimpleMathMethod);
    }

    [Test]
    public async Task MathConstant_NoDiagnostic()
    {
        await VerifyCS.VerifyAnalyzerAsync(MathAndAttributeTestSources.MathConstant);
    }

    [Test]
    public async Task MathMethodChain_NoDiagnostic()
    {
        await VerifyCS.VerifyAnalyzerAsync(MathAndAttributeTestSources.MathMethodChain);
    }

    [Test]
    public async Task ComplexLinqWithMath_UnknownExternalEnumerator_Diagnostic()
    {
        await VerifyCS.VerifyAnalyzerAsync(LinqAnalyzerTestSources.ComplexMathPipeline);
    }

    [Test]
    public async Task MethodWithLazyEvaluation_UnknownExternalEnumerator_Diagnostic()
    {
        await VerifyCS.VerifyAnalyzerAsync(LinqAnalyzerTestSources.LazyPipeline);
    }
}
