using System.Threading.Tasks;
using NUnit.Framework;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test
{
    [TestFixture]
    public class BitOperationsTests
    {
        [Test]
        public async Task BitOperationsDeterministicHelpers_NoDiagnostic()
        {
            var test = @"
using System.Numerics;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(uint value, ulong wider)
    {
        return BitOperations.LeadingZeroCount(value) + BitOperations.PopCount(wider);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task BitOperationsWithImpureArgument_Diagnostic()
        {
            var test = @"
using System;
using System.Numerics;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        return BitOperations.PopCount((ulong)Console.Read());
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task BitOperationsAdditionalDeterministicHelpers_NoDiagnostic()
        {
            var test = @"
using System.Numerics;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(uint value, ulong wider)
    {
        return
            BitOperations.Log2(value | 1u) +
            BitOperations.Log2(wider | 1ul) +
            BitOperations.TrailingZeroCount((int)value) +
            BitOperations.TrailingZeroCount(value) +
            BitOperations.TrailingZeroCount((long)wider) +
            BitOperations.TrailingZeroCount(wider) +
            BitOperations.LeadingZeroCount(wider) +
            BitOperations.PopCount(value) +
            (BitOperations.IsPow2((int)value) ? 1 : 0) +
            (BitOperations.IsPow2(value) ? 1 : 0) +
            (BitOperations.IsPow2((long)wider) ? 1 : 0) +
            (BitOperations.IsPow2(wider) ? 1 : 0) +
            (int)BitOperations.RotateLeft(value, 3) +
            (int)BitOperations.RotateRight(value, 3) +
            (int)BitOperations.RotateLeft(wider, 3) +
            (int)BitOperations.RotateRight(wider, 3) +
            (int)BitOperations.RoundUpToPowerOf2(value | 1u) +
            (int)BitOperations.RoundUpToPowerOf2(wider | 1ul);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
