using System.Threading.Tasks;
using NUnit.Framework;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test
{
    [TestFixture]
    public class InstancePropertyGetterTests
    {
        [Test]
        public async Task InstancePropertyWithImpureGetter_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        private int _counter;

        public int Counter
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
        public async Task InParameterPropertyWithImpureGetter_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

namespace TestNamespace
{
    public readonly struct CounterStruct
    {
        private readonly int _value;

        [EnforcePure]
        public CounterStruct(int value)
        {
            _value = value;
        }

        public int Value
        {
            get
            {
                Console.WriteLine(_value);
                return _value;
            }
        }
    }

    public class TestClass
    {
        [EnforcePure]
        public int {|SP0002:TestMethod|}(in CounterStruct counter)
        {
            return counter.Value;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task PureMarkedGetterWithImpureBody_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class Data
    {
        public int Value
        {
            [Pure]
            get
            {
                Console.WriteLine(1);
                return 1;
            }
        }
    }

    public class TestClass
    {
        [EnforcePure]
        public int {|SP0002:TestMethod|}(Data data)
        {
            return data.Value;
        }
    }
}";

            var expectedGetter = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
                .WithSpan(9, 20, 9, 25)
                .WithArguments("get_Value");

            await VerifyCS.VerifyAnalyzerAsync(test, expectedGetter);
        }
    }
}
