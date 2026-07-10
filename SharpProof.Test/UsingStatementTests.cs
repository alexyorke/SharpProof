using NUnit.Framework;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class UsingStatementTests
{
    [Test]
    public async Task UsingStatement_WithImpureDisposable_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;
using System.IO;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        using (var file = File.OpenRead(""test.txt""))
        {
            // Some operation
        }
    }
}";

        var expectedSP0002 = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
            .WithSpan(9, 17, 9, 27)
            .WithArguments("TestMethod");


        await VerifyCS.VerifyAnalyzerAsync(test, expectedSP0002);
    }

    [Test]
    public async Task UsingStatementExpressionResource_WithImpureDispose_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        using (new ImpureDisposable())
        {
        }
    }
}

public class ImpureDisposable : IDisposable
{
    private int _disposeCount;

    public void Dispose()
    {
        _disposeCount++;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task AwaitUsingStatementExpressionResource_WithImpureDisposeAsync_Diagnostic()
    {
        var test = @"
using System;
using System.Threading.Tasks;
using SharpProof.Attributes;

public static class GlobalState
{
    public static int Count;
}

public sealed class AsyncResource : IAsyncDisposable
{
    public ValueTask DisposeAsync()
    {
        GlobalState.Count++;
        return ValueTask.CompletedTask;
    }
}

public class TestClass
{
    [EnforcePure]
    public async Task {|SP0002:TestMethod|}()
    {
        await using (new AsyncResource())
        {
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task AwaitUsingStatementExpressionResource_PrefersImpureDisposeAsyncOverPureDispose_Diagnostic()
    {
        var test = @"
using System;
using System.Threading.Tasks;
using SharpProof.Attributes;

public static class GlobalState
{
    public static int Count;
}

public sealed class DualDisposable : IDisposable, IAsyncDisposable
{
    public void Dispose()
    {
    }

    public ValueTask DisposeAsync()
    {
        GlobalState.Count++;
        return ValueTask.CompletedTask;
    }
}

public class TestClass
{
    [EnforcePure]
    public async Task {|SP0002:TestMethod|}()
    {
        await using (new DualDisposable())
        {
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task AwaitUsingStatementExpressionResource_WithImpureDisposeAsyncAwaiter_Diagnostic()
    {
        var test = @"
using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using SharpProof.Attributes;

public static class GlobalState
{
    public static int Count;
}

public sealed class AsyncResource
{
    [EnforcePure]
    public DisposeAwaitable DisposeAsync() => new DisposeAwaitable();
}

public sealed class DisposeAwaitable
{
    [EnforcePure]
    public DisposeAwaiter GetAwaiter() => new DisposeAwaiter();
}

public sealed class DisposeAwaiter : INotifyCompletion
{
    public bool IsCompleted => true;

    public void OnCompleted(Action continuation) => continuation();

    public void GetResult()
    {
        GlobalState.Count++;
    }
}

public class TestClass
{
    [EnforcePure]
    public async Task {|SP0002:TestMethod|}()
    {
        await using (new AsyncResource())
        {
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task UsingStatementExpressionCastToInterface_WithPureDispose_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        using ((IDisposable)new PureDisposable())
        {
        }
    }
}

public class PureDisposable : IDisposable
{
    public void Dispose() { }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task UsingDeclaration_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;
using System.IO;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        using var file = File.OpenRead(""test.txt"");
        // Some operation
    }
}";

        var expectedSP0002 = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
            .WithSpan(9, 17, 9, 27)
            .WithArguments("TestMethod");


        await VerifyCS.VerifyAnalyzerAsync(test, expectedSP0002);
    }

    [Test]
    public async Task UsingDeclarationWithPureDisposable_NoDiagnostics()
    {
        var code = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        using var disposable = new PureDisposable(); // Empty Dispose body is accepted here.
    }
}

public class PureDisposable : IDisposable
{
    // Empty Dispose is treated as pure.
    public void Dispose() { }
}";

        await VerifyCS.VerifyAnalyzerAsync(code);
    }

    [Test]
    public async Task UsingStatementWithPureDisposable_NoDiagnostics()
    {
        var test = @"
using System;
using SharpProof.Attributes;
using System.IO;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        // Using a local disposable with an empty Dispose body.
        using (var disposable = new PureDisposable())
        {
            // Some operation
        }
    }
}

public class PureDisposable : IDisposable
{
    // Empty Dispose is treated as pure.
    public void Dispose() { }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task UsingDeclarationLocalReference_DoesNotFlagResourceAsImpure()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        var disposable = new PureDisposable();
        using (disposable)
        {
        }
    }
}

        public class PureDisposable : IDisposable
        {
            public void Dispose() { }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task UsingStatementExistingInterfaceLocal_WithPureDispose_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        IDisposable disposable = new PureDisposable();
        using (disposable)
        {
        }
    }
}

public class PureDisposable : IDisposable
{
    public void Dispose() { }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task UsingStatementExistingInterfaceLocalExplicitCast_WithPureDispose_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        IDisposable disposable = (IDisposable)new PureDisposable();
        using (disposable)
        {
        }
    }
}

public class PureDisposable : IDisposable
{
    public void Dispose() { }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task UsingStatementExistingInterfaceLocalAssignedAfterDeclaration_WithPureDispose_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        IDisposable disposable;
        disposable = new PureDisposable();
        using (disposable)
        {
        }
    }
}

public class PureDisposable : IDisposable
{
    public void Dispose() { }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task UsingStatementExistingLocal_WithImpureDispose_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        var disposable = new ImpureDisposable();
        using (disposable)
        {
        }
    }
}

public class ImpureDisposable : IDisposable
{
    private int _disposeCount;

    public void Dispose()
    {
        _disposeCount++;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task UsingStatementExistingInterfaceLocal_WithImpureDispose_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        IDisposable disposable = new ImpureDisposable();
        using (disposable)
        {
        }
    }
}

public class ImpureDisposable : IDisposable
{
    private int _disposeCount;

    public void Dispose()
    {
        _disposeCount++;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task UsingStatementExistingLocalReassignedByDeconstruction_WithImpureDispose_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        IDisposable disposable = new PureDisposable();
        (disposable, _) = (new ImpureDisposable(), 0);

        using (disposable)
        {
        }
    }
}

public class PureDisposable : IDisposable
{
    public void Dispose() { }
}

public class ImpureDisposable : IDisposable
{
    private int _disposeCount;

    public void Dispose()
    {
        _disposeCount++;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task UsingStatementExistingLocalReassignedByRefCall_WithImpureDispose_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [PureExternal]
    private static void Replace(ref IDisposable disposable)
    {
        disposable = new ImpureDisposable();
    }

    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        IDisposable disposable = new PureDisposable();
        Replace(ref disposable);

        using (disposable)
        {
        }
    }
}

public class PureDisposable : IDisposable
{
    public void Dispose() { }
}

public class ImpureDisposable : IDisposable
{
    private int _disposeCount;

    public void Dispose()
    {
        _disposeCount++;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task UsingStatementExistingLocalReassignedThroughRefLocalAlias_WithImpureDispose_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        IDisposable disposable = new PureDisposable();
        ref IDisposable alias = ref disposable;
        alias = new ImpureDisposable();

        using (disposable)
        {
        }
    }
}

public class PureDisposable : IDisposable
{
    public void Dispose() { }
}

public class ImpureDisposable : IDisposable
{
    private int _disposeCount;

    public void Dispose()
    {
        _disposeCount++;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task UsingStatementExistingPolymorphicLocalReassignedWithImpureDispose_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        BaseDisposable disposable = new PureDisposable();
        disposable = new ImpureDisposable();

        using (disposable)
        {
        }
    }
}

public abstract class BaseDisposable : IDisposable
{
    public virtual void Dispose()
    {
    }
}

public sealed class PureDisposable : BaseDisposable
{
}

public sealed class ImpureDisposable : BaseDisposable
{
    private int _disposeCount;

    public override void Dispose()
    {
        _disposeCount++;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task UsingDeclarationPatternDisposableRefStruct_WithImpureDispose_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public ref struct Lease
{
    public void Dispose()
    {
        System.Console.WriteLine(""disposed"");
    }
}

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        using var lease = new Lease();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task UsingDeclarationPatternDisposableRefStruct_WithPureDispose_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public ref struct Lease
{
    [EnforcePure]
    public void Dispose()
    {
    }
}

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        using var lease = new Lease();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}