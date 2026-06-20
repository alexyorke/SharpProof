using System.Threading.Tasks;
using NUnit.Framework;
using PurelySharp.Analyzer;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

namespace PurelySharp.Test
{
    [TestFixture]
    public class ImmutableStackTests
    {
        [Test]
        public async Task ImmutableStackPush_NoDiagnostic()
        {
            var test = @"
using System.Collections.Immutable;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public ImmutableStack<int> PushValue(ImmutableStack<int> stack, int value)
    {
        return stack.Push(value);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ImmutableStackClear_NoDiagnostic()
        {
            var test = @"
using System.Collections.Immutable;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public ImmutableStack<int> ClearStack(ImmutableStack<int> stack)
    {
        return stack.Clear();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ImmutableStackIsEmpty_NoDiagnostic()
        {
            var test = @"
using System.Collections.Immutable;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool IsEmpty(ImmutableStack<int> stack)
    {
        return stack.IsEmpty;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ImmutableStackPop_Diagnostic()
        {
            var test = @"
using System.Collections.Immutable;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public ImmutableStack<int> PopValue(ImmutableStack<int> stack)
    {
        return stack.Pop();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(
                test,
                VerifyCS.Diagnostic(PurelySharpDiagnostics.PurityNotVerifiedId)
                    .WithSpan(8, 32, 8, 40)
                    .WithArguments("PopValue"));
        }
    }
}
