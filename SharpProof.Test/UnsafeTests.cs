using System.Threading.Tasks;
using NUnit.Framework;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test
{
    [TestFixture]
    public class UnsafeTests
    {
        [Test]
        public async Task UnsafeReadUnaligned_NoDiagnostic()
        {
            var test = @"
using System.Runtime.CompilerServices;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        byte[] bytes = new byte[] { 1, 0, 0, 0 };
        return Unsafe.ReadUnaligned<int>(ref bytes[0]);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task UnsafeAs_NoDiagnostic()
        {
            var test = @"
using System.Runtime.CompilerServices;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(ref int value)
    {
        ref int alias = ref Unsafe.As<int, int>(ref value);
        return alias;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task UnsafeSizeOf_NoDiagnostic()
        {
            var test = @"
using System.Runtime.CompilerServices;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        return Unsafe.SizeOf<int>();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task UnsafeWriteUnaligned_Diagnostic()
        {
            var test = @"
using System.Runtime.CompilerServices;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(ref byte value)
    {
        Unsafe.WriteUnaligned(ref value, 42);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
