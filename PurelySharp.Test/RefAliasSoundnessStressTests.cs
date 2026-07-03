using System.Threading.Tasks;
using NUnit.Framework;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

namespace PurelySharp.Test
{
    [TestFixture]
    public class RefAliasSoundnessStressTests
    {
        [Test]
        public async Task RefLocalAliasToLocalMutation_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public int TestMethod(int value)
    {
        int local = value;
        ref int alias = ref local;
        alias++;
        return local;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task RefLocalAliasToRefParameterAssignment_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}(ref int value)
    {
        ref int alias = ref value;
        alias = 42;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task RefLocalAliasChainToRefParameter_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}(ref int value)
    {
        ref int first = ref value;
        ref int second = ref first;
        second++;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task RefLocalAliasChainToLocal_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public int TestMethod(int value)
    {
        int local = value;
        ref int first = ref local;
        ref int second = ref first;
        second++;
        return local;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task AssignmentToMutablyBorrowedLocal_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public int {|PS0002:TestMethod|}(int value)
    {
        int local = value;
        ref int alias = ref local;
        local = 42;
        return alias;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task RefLocalAliasToArrayParameterElementWrite_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}(int[] values)
    {
        ref int item = ref values[0];
        item++;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task RefLocalAliasToFreshArrayElementWrite_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public int TestMethod(int value)
    {
        var values = new int[1];
        ref int item = ref values[0];
        item = value;
        return values[0];
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task RefLocalAliasToInstanceFieldWrite_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public sealed class TestClass
{
    private int _field;

    [EnforcePure]
    public void {|PS0002:TestMethod|}()
    {
        ref int alias = ref _field;
        alias++;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task RefLocalAliasToLocalStructFieldWrite_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public struct MutableStruct
{
    public int Value;
}

public sealed class TestClass
{
    [EnforcePure]
    public int TestMethod(int value)
    {
        var data = new MutableStruct();
        ref int alias = ref data.Value;
        alias = value;
        return data.Value;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task LocalMutableStructFieldReadAfterMutation_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public struct MutableStruct
{
    public int Value;
}

public sealed class TestClass
{
    [EnforcePure]
    public int TestMethod(int value)
    {
        var data = new MutableStruct();
        data.Value = value;
        return data.Value;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task RefParameterStructFieldRead_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public struct MutableStruct
{
    public int Value;
}

public sealed class TestClass
{
    [EnforcePure]
    public int {|PS0002:TestMethod|}(ref MutableStruct data)
    {
        return data.Value;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task InParameterStructFieldRead_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public struct MutableStruct
{
    public int Value;
}

public sealed class TestClass
{
    [EnforcePure]
    public int TestMethod(in MutableStruct data)
    {
        return data.Value;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
