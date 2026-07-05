using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;
using SharpProof.Attributes;

namespace SharpProof.Test
{
    [TestFixture]
    public class NullComparisonTests
    {
        [Test]
        public async Task PureMethodWithNullComparison_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(object obj)
    {
        // Null comparison is considered pure
        return obj == null;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ImpureMethodWithNullComparison_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(object obj)
    {
        // Null comparison with console write is impure
        if (obj == null)
        {
            Console.WriteLine(""Object is null"");
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task MethodWithNullComparisonAndImpureOperation_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;
using SharpProof.Attributes;

public class TestClass
{
    private int _field;

    [EnforcePure]
    public bool {|SP0002:TestMethod|}(object obj)
    {
        // Null comparison is pure, but field increment is impure
        bool isNull = obj == null;
        _field++;
        return isNull;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}


