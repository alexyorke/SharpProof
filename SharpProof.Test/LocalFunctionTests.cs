using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
public class LocalFunctionTests
{
    [Test]
    public async Task PureLocalFunction_NoDiagnostic()
    {
        var code = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int PureMethod()
    {
        int LocalFunc(int a)
        {
            return a + 1;
        }
        return LocalFunc(5);
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(code);
    }

    [Test]
    public async Task UnusedImpureLocalFunction_NoDiagnostic()
    {
        var code = @"
using SharpProof.Attributes;

public class TestClass
{
    private int _val = 0;

    [EnforcePure]
    public int PureMethod()
    {
        int Unused()
        {
            _val = 1;
            return _val;
        }

        return 0;
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(code);
    }

    [Test]
    public async Task ImpureLocalFunction_ReportsDiagnostic()
    {
        var code = @"
using SharpProof.Attributes;

public class TestClass
{
    private int _val = 0;

    [EnforcePure]
    public int ImpureMethod()
    {
        int LocalFunc()
        {
            _val = 1; // Impure: modifies outer state
            return 1;
        }
        return LocalFunc();
    }
}";
        var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
            .WithSpan(9, 16, 9, 28)
            .WithArguments("ImpureMethod");
        await VerifyCS.VerifyAnalyzerAsync(code, expected);
    }

    [Test]
    public async Task EscapingLocalFunctionDelegateCapturingFreshMutableObject_Diagnostic()
    {
        var code = MutableObjectTestSources.SystemUsings +
                   MutableObjectTestSources.Box + @"
public class TestClass
{
    [EnforcePure]
    public Func<int> {|SP0002:TestMethod|}()
    {
        var box = new Box();

        int Local()
        {
            return box.Value;
        }

        return Local;
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(code);
    }

    [Test]
    public async Task LocalFunctionReturningFreshMutableObjectUsedLocally_NoDiagnostic()
    {
        var code = MutableObjectTestSources.AttributeUsings +
                   MutableObjectTestSources.Box + @"
public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        Box CreateBox()
        {
            return new Box();
        }

        var box = CreateBox();
        box.Value = 1;
        return box.Value;
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(code);
    }

    [Test]
    public async Task LocalFunctionReturningFreshMutableObjectInitializedButUnused_NoDiagnostic()
    {
        var code = MutableObjectTestSources.AttributeUsings +
                   MutableObjectTestSources.Box + @"
public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        Box CreateBox()
        {
            return new Box();
        }

        var box = CreateBox();
        return 0;
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(code);
    }

    [Test]
    public async Task LocalFunctionReturningFreshMutableObjectMutatedButNotRead_NoDiagnostic()
    {
        var code = MutableObjectTestSources.AttributeUsings +
                   MutableObjectTestSources.Box + @"
public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        Box CreateBox()
        {
            return new Box();
        }

        var box = CreateBox();
        box.Value = 1;
        return 0;
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(code);
    }

    [Test]
    public async Task LocalFunctionReturningFreshMutableObjectReturnedFromContainingMethod_Diagnostic()
    {
        var code = MutableObjectTestSources.AttributeUsings +
                   MutableObjectTestSources.Box + @"
public class TestClass
{
    [EnforcePure]
    public Box {|SP0002:TestMethod|}()
    {
        Box CreateBox()
        {
            return new Box();
        }

        return CreateBox();
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(code);
    }

    [Test]
    public async Task LocalFunctionReturningFreshMutableObjectEscapesThroughWrapper_Diagnostic()
    {
        var code = MutableObjectTestSources.AttributeUsings +
                   MutableObjectTestSources.Box + @"
public sealed class Holder
{
    public readonly Box Value;

    [EnforcePure]
    public Holder(Box value)
    {
        Value = value;
    }
}

public class TestClass
{
    [EnforcePure]
    public Holder {|SP0002:TestMethod|}()
    {
        Box CreateBox()
        {
            return new Box();
        }

        return new Holder(CreateBox());
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(code);
    }
}