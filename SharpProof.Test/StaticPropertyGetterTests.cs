using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class StaticPropertyGetterTests
{
    [Test]
    public async Task StaticPropertyWithImpureGetter_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        private static int _counter;

        public static int Counter
        {
            get
            {
                Console.WriteLine(_counter);
                return ++_counter;
            }
        }

        [EnforcePure]
        public int {|SP0002:TestMethod|}()
        {
            return Counter;
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task StaticPropertyWithPureGetter_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        public static int Value
        {
            [EnforcePure]
            get
            {
                return 42;
            }
        }

        [EnforcePure]
        public int TestMethod()
        {
            return Value;
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}