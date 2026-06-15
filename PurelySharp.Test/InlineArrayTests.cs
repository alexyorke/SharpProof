using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using System.Threading.Tasks;
using PurelySharp.Analyzer;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

namespace PurelySharp.Test
{
    [TestFixture]
    public class InlineArrayTests
    {




        [Test]
        public async Task ReadOnlyArray_IsPure()
        {
            var test = @"
using System;
using PurelySharp.Attributes;



public class TestClass
{
    [EnforcePure]
    public int ReadArray()
    {
        int[] buffer = new int[10];
        // Reading from an array is pure
        return buffer[5];
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task WritingToFreshLocalArray_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;



public class TestClass
{
    [EnforcePure]
    public void WriteToArray()
    {
        int[] buffer = new int[10];
        buffer[5] = 42;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ReturningInitializedFreshLocalArray_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;



public class TestClass
{
    [EnforcePure]
    public int[] {|PS0002:InitializeArray|}()
    {
        int[] buffer = new int[5];
        for (int i = 0; i < 5; i++)
        {
            buffer[i] = i * 2;
        }
        return buffer;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task InlineArrayRead_NoDiagnostic()
        {
            var test = @"
using System.Runtime.CompilerServices;
using PurelySharp.Attributes;

[InlineArray(4)]
public struct Buffer
{
    private int _element0;
}

public class TestClass
{
    [EnforcePure]
    public int ReadInlineArray()
    {
        Buffer buffer = default;
        return buffer[0];
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task InlineArrayWriteToLocal_NoDiagnostic()
        {
            var test = @"
using System.Runtime.CompilerServices;
using PurelySharp.Attributes;

[InlineArray(4)]
public struct Buffer
{
    private int _element0;
}

public class TestClass
{
    [EnforcePure]
    public void WriteInlineArray()
    {
        Buffer buffer = default;
        buffer[0] = 42;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task InlineArrayAccessWithImpureIndex_Diagnostic()
        {
            var test = @"
using System;
using System.Runtime.CompilerServices;
using PurelySharp.Attributes;

[InlineArray(32)]
public struct Buffer
{
    private int _element0;
}

public class TestClass
{
    [EnforcePure]
    public int {|PS0002:ReadInlineArray|}()
    {
        Buffer buffer = default;
        return buffer[DateTime.Now.Day];
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
