using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using PurelySharp.Analyzer;
using PurelySharp.Attributes;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

#nullable enable

namespace PurelySharp.Test
{
    [TestFixture]
    public class MemoryInteropTests
    {


        [Test]
        public async Task Span_Creation_From_Array_Diagnostic()
        {
            var test = @"
#nullable enable
using System;
using PurelySharp.Attributes;



public class TestClass
{
    [EnforcePure]
    public Span<byte> {|PS0002:TestMethod|}(byte[] data)
    {
        return new Span<byte>(data);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Span_Creation_From_Owned_Array_NoDiagnostic()
        {
            var test = @"
#nullable enable
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Span<byte> TestMethod()
    {
        var data = new byte[] { 1, 2, 3 };
        return new Span<byte>(data);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Span_Slice_NoDiagnostic()
        {
            var test = @"
#nullable enable
using System;
using PurelySharp.Attributes;



public class TestClass
{
    [EnforcePure]
    public Span<byte> TestMethod(Span<byte> initialSpan)
    {
        // Pure: Creates a new view/slice
        return initialSpan.Slice(1, 2);
    }
}";




            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Memory_Slice_NoDiagnostic()
        {
            var test = @"
#nullable enable
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Memory<byte> TestMethod(Memory<byte> memory)
    {
        return memory.Slice(1, 2);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }













        [Test]
        public async Task MarshalPtrToStructure_Diagnostic()
        {
            var test = @"
#nullable enable
using System;
using System.Runtime.InteropServices;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|PS0002:TestMethod|}(IntPtr ptr)
    {
        return Marshal.PtrToStructure<int>(ptr);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MyStruct { public int Value; }




    }
}
