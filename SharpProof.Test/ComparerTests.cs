using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class ComparerTests
{
    [Test]
    public async Task EqualityComparerDefault_NoDiagnostic()
    {
        var test = @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public EqualityComparer<int> TestMethod()
    {
        return EqualityComparer<int>.Default;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ComparerDefault_NoDiagnostic()
    {
        var test = @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Comparer<int> TestMethod()
    {
        return Comparer<int>.Default;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}