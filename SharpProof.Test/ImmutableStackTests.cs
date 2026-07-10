using NUnit.Framework;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class ImmutableStackTests
{
    [Test]
    public async Task ImmutableStackPush_NoDiagnostic()
    {
        var test = @"
using System.Collections.Immutable;
using SharpProof.Attributes;

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
using SharpProof.Attributes;

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
using SharpProof.Attributes;

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
using SharpProof.Attributes;

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
            VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
                .WithSpan(8, 32, 8, 40)
                .WithArguments("PopValue"));
    }

    [Test]
    public async Task IImmutableStackPop_KnownConcreteReceiver_Diagnostic()
    {
        var test = @"
using System.Collections.Immutable;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public IImmutableStack<int> PopValue()
    {
        IImmutableStack<int> stack = ImmutableStack<int>.Empty;
        return stack.Pop();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(
            test,
            VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
                .WithSpan(8, 33, 8, 41)
                .WithArguments("PopValue"));
    }
}