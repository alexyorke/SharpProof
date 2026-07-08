using System.Threading.Tasks;
using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test
{
    [TestFixture]
    public class AttributePlacementPurityTests
    {
        [Test]
        public async Task PureAttributeOnProperty_NoPlacementDiagnostic()
        {
            var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [Pure]
    public int Value => 42;

    [EnforcePure]
    public int TestMethod()
    {
        return Value;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task PureAttributeOnIndexer_NoPlacementDiagnostic()
        {
            var test = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    [Pure]
    public int this[int value] => value;

    [EnforcePure]
    public int TestMethod(int value)
    {
        return this[value];
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task EnforcePureAttributeOnProperty_PlacementDiagnostic()
        {
            var test = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    [{|SP0003:EnforcePure|}]
    public int Value => 42;
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task MultipleMisplacedAttributesOnProperty_ReportEachDiagnostic()
        {
            var test = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    [{|SP0003:EnforcePure|}, {|SP0014:ZeroAllocations|}]
    public int Value => 42;
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task EnforcePureAttributeOnConversionOperator_NoPlacementDiagnostic()
        {
            var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public readonly struct Temperature
{
    private readonly int _celsius;

    public Temperature(int celsius)
    {
        _celsius = celsius;
    }

    [EnforcePure]
    public static explicit operator int(Temperature value)
    {
        return value._celsius;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
