using System.Threading.Tasks;
using NUnit.Framework;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

namespace PurelySharp.Test
{
    [TestFixture]
    public class UsingDisposeSoundnessStressTests
    {
        [Test]
        public async Task UsingExistingLocalWithImpureDispose_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class ImpureDisposable : IDisposable
{
    public static int Count;
    public void Dispose() => Count++;
}

public sealed class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}(ImpureDisposable resource)
    {
        using (resource)
        {
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task UsingNewImpureDisposable_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class ImpureDisposable : IDisposable
{
    public static int Count;
    public void Dispose() => Count++;
}

public sealed class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}()
    {
        using (new ImpureDisposable())
        {
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task UsingNewPureDisposable_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        using (new PureDisposable())
        {
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task UsingVarPureDisposable_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        using var resource = new PureDisposable();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task UsingFactoryImpure_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}()
    {
        using var resource = Create();
    }

    private static PureDisposable Create()
    {
        _ = DateTime.Now.Millisecond;
        return new PureDisposable();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task UsingPureResourceImpureBody_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}()
    {
        using (new PureDisposable())
        {
            Console.WriteLine(""impure"");
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ExplicitDoubleDisposeSameLocal_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}()
    {
        var resource = new PureDisposable();
        resource.Dispose();
        resource.Dispose();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ExplicitDisposeAfterReassignment_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        var resource = new PureDisposable();
        resource.Dispose();
        resource = new PureDisposable();
        resource.Dispose();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ExplicitUseAfterDisposeSameLocal_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }

    [EnforcePure]
    public int Use()
    {
        return 1;
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public int {|PS0002:TestMethod|}()
    {
        var resource = new PureDisposable();
        resource.Dispose();
        _ = resource.Use();
        return 1;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ExplicitPropertyReadAfterDisposeSameLocal_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }

    public int Value
    {
        [EnforcePure]
        get { return 1; }
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public int {|PS0002:TestMethod|}()
    {
        var resource = new PureDisposable();
        resource.Dispose();
        return resource.Value;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ExplicitFieldReadAfterDisposeThroughAlias_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    public readonly int Value = 1;

    [EnforcePure]
    public void Dispose()
    {
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public int {|PS0002:TestMethod|}()
    {
        var resource = new PureDisposable();
        var alias = resource;
        resource.Dispose();
        return alias.Value;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ExplicitUseAfterReassignment_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }

    [EnforcePure]
    public int Use()
    {
        return 1;
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        var resource = new PureDisposable();
        resource.Dispose();
        resource = new PureDisposable();
        _ = resource.Use();
        resource.Dispose();
        return 1;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ExplicitReturnUseAfterDisposeSameLocal_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }

    [EnforcePure]
    public int Use()
    {
        return 1;
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public int {|PS0002:TestMethod|}()
    {
        var resource = new PureDisposable();
        resource.Dispose();
        return resource.Use();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ExplicitReturnUseAfterReassignment_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }

    [EnforcePure]
    public int Use()
    {
        return 1;
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        var resource = new PureDisposable();
        resource.Dispose();
        resource = new PureDisposable();
        var value = resource.Use();
        resource.Dispose();
        return value;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task MissingDisposeForOwnedLocal_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}()
    {
        var resource = new PureDisposable();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ExplicitDisposeAsyncSatisfiesOwnedLocalDisposal_NoDiagnostic()
        {
            var test = @"
using System;
using System.Threading.Tasks;
using PurelySharp.Attributes;

public sealed class PureAsyncDisposable : IAsyncDisposable
{
    [EnforcePure]
    public ValueTask DisposeAsync()
    {
        return default;
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        var resource = new PureAsyncDisposable();
        resource.DisposeAsync();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task MissingDisposeForOwnedAsyncLocal_Diagnostic()
        {
            var test = @"
using System;
using System.Threading.Tasks;
using PurelySharp.Attributes;

public sealed class PureAsyncDisposable : IAsyncDisposable
{
    [EnforcePure]
    public ValueTask DisposeAsync()
    {
        return default;
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}()
    {
        var resource = new PureAsyncDisposable();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ExplicitDoubleDisposeAsyncSameLocal_Diagnostic()
        {
            var test = @"
using System;
using System.Threading.Tasks;
using PurelySharp.Attributes;

public sealed class PureAsyncDisposable : IAsyncDisposable
{
    [EnforcePure]
    public ValueTask DisposeAsync()
    {
        return default;
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}()
    {
        var resource = new PureAsyncDisposable();
        resource.DisposeAsync();
        resource.DisposeAsync();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ExplicitUseAfterDisposeAsyncSameLocal_Diagnostic()
        {
            var test = @"
using System;
using System.Threading.Tasks;
using PurelySharp.Attributes;

public sealed class PureAsyncDisposable : IAsyncDisposable
{
    [EnforcePure]
    public ValueTask DisposeAsync()
    {
        return default;
    }

    [EnforcePure]
    public int Use()
    {
        return 1;
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public int {|PS0002:TestMethod|}()
    {
        var resource = new PureAsyncDisposable();
        resource.DisposeAsync();
        return resource.Use();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task AwaitUsingDeclarationSatisfiesOwnedAsyncLocalDisposal_NoDiagnostic()
        {
            var test = @"
using System;
using System.Threading.Tasks;
using PurelySharp.Attributes;

public sealed class PureAsyncDisposable : IAsyncDisposable
{
    [EnforcePure]
    public ValueTask DisposeAsync()
    {
        return default;
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public async Task TestMethod()
    {
        await using var resource = new PureAsyncDisposable();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task UseAfterAwaitUsingStatementSameLocal_Diagnostic()
        {
            var test = @"
using System;
using System.Threading.Tasks;
using PurelySharp.Attributes;

public sealed class PureAsyncDisposable : IAsyncDisposable
{
    [EnforcePure]
    public ValueTask DisposeAsync()
    {
        return default;
    }

    [EnforcePure]
    public int Use()
    {
        return 1;
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public async Task<int> {|PS0002:TestMethod|}()
    {
        var resource = new PureAsyncDisposable();
        await using (resource)
        {
        }

        return resource.Use();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task DisposeAfterAwaitUsingStatementSameLocal_Diagnostic()
        {
            var test = @"
using System;
using System.Threading.Tasks;
using PurelySharp.Attributes;

public sealed class PureAsyncDisposable : IAsyncDisposable
{
    [EnforcePure]
    public ValueTask DisposeAsync()
    {
        return default;
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public async Task {|PS0002:TestMethod|}()
    {
        var resource = new PureAsyncDisposable();
        await using (resource)
        {
        }

        resource.DisposeAsync();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ConditionalDisposeOnlyOneBranch_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}(bool dispose)
    {
        var resource = new PureDisposable();
        if (dispose)
        {
            resource.Dispose();
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ConditionalDisposeBothBranches_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod(bool dispose)
    {
        var resource = new PureDisposable();
        if (dispose)
        {
            resource.Dispose();
        }
        else
        {
            resource.Dispose();
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SwitchDisposeAllArms_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod(int mode)
    {
        var resource = new PureDisposable();
        switch (mode)
        {
            case 0:
                resource.Dispose();
                break;
            default:
                resource.Dispose();
                break;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SwitchDisposeMissingDefault_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}(int mode)
    {
        var resource = new PureDisposable();
        switch (mode)
        {
            case 0:
                resource.Dispose();
                break;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SwitchReturnOrDisposeAllArms_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public PureDisposable TestMethod(int mode)
    {
        var resource = new PureDisposable();
        switch (mode)
        {
            case 0:
                return resource;
            default:
                resource.Dispose();
                return new PureDisposable();
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task WhileDisposeOnly_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}(bool dispose)
    {
        var resource = new PureDisposable();
        while (dispose)
        {
            resource.Dispose();
            break;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ForDisposeOnly_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}(bool dispose)
    {
        var resource = new PureDisposable();
        for (; dispose; )
        {
            resource.Dispose();
            break;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task DoWhileDisposeSatisfiesOwnedLocalDisposal_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod(bool repeat)
    {
        var resource = new PureDisposable();
        do
        {
            resource.Dispose();
        }
        while (repeat);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task FinallyDisposeSatisfiesOwnedLocalDisposal_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        var resource = new PureDisposable();
        try
        {
        }
        finally
        {
            resource.Dispose();
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task FinallyDisposeThroughAliasSatisfiesOwnedLocalDisposal_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        var resource = new PureDisposable();
        var alias = resource;
        try
        {
        }
        finally
        {
            alias.Dispose();
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TryReturnOwnedLocalWithFinally_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public PureDisposable TestMethod()
    {
        var resource = new PureDisposable();
        try
        {
            return resource;
        }
        finally
        {
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task UseAfterFinallyDispose_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }

    [EnforcePure]
    public int Use()
    {
        return 1;
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public int {|PS0002:TestMethod|}()
    {
        var resource = new PureDisposable();
        try
        {
        }
        finally
        {
            resource.Dispose();
        }

        return resource.Use();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task UseAfterConditionalDisposeBothBranches_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }

    [EnforcePure]
    public int Use()
    {
        return 1;
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public int {|PS0002:TestMethod|}(bool dispose)
    {
        var resource = new PureDisposable();
        if (dispose)
        {
            resource.Dispose();
        }
        else
        {
            resource.Dispose();
        }

        return resource.Use();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task DoubleDisposeAfterConditionalDisposeBothBranches_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}(bool dispose)
    {
        var resource = new PureDisposable();
        if (dispose)
        {
            resource.Dispose();
        }
        else
        {
            resource.Dispose();
        }

        resource.Dispose();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ConditionalDisposeThroughOwnerOrAlias_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod(bool disposeOwner)
    {
        var resource = new PureDisposable();
        var alias = resource;
        if (disposeOwner)
        {
            resource.Dispose();
        }
        else
        {
            alias.Dispose();
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task UseAfterConditionalDisposeThroughOwnerOrAlias_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }

    [EnforcePure]
    public int Use()
    {
        return 1;
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public int {|PS0002:TestMethod|}(bool disposeOwner)
    {
        var resource = new PureDisposable();
        var alias = resource;
        if (disposeOwner)
        {
            resource.Dispose();
        }
        else
        {
            alias.Dispose();
        }

        return resource.Use();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task DoubleDisposeAfterConditionalDisposeThroughOwnerOrAlias_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}(bool disposeOwner)
    {
        var resource = new PureDisposable();
        var alias = resource;
        if (disposeOwner)
        {
            resource.Dispose();
        }
        else
        {
            alias.Dispose();
        }

        resource.Dispose();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ConditionalReturnOrDispose_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public PureDisposable TestMethod(bool giveAway)
    {
        var resource = new PureDisposable();
        if (giveAway)
        {
            return resource;
        }

        resource.Dispose();
        return new PureDisposable();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ConditionalReturnOnlyOneBranch_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public PureDisposable {|PS0002:TestMethod|}(bool giveAway)
    {
        var resource = new PureDisposable();
        if (giveAway)
        {
            return resource;
        }

        return null;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task MissingDisposeForAliasedOwnedLocalAfterOwnerReassignment_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}()
    {
        var resource = new PureDisposable();
        var alias = resource;
        resource = new PureDisposable();
        resource.Dispose();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task DisposeAliasAfterOwnerReassignment_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        var resource = new PureDisposable();
        var alias = resource;
        resource = new PureDisposable();
        alias.Dispose();
        resource.Dispose();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task UseAliasAfterAliasDisposeAndOwnerReassignment_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }

    [EnforcePure]
    public int Use()
    {
        return 1;
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public int {|PS0002:TestMethod|}()
    {
        var resource = new PureDisposable();
        var alias = resource;
        resource = new PureDisposable();
        alias.Dispose();
        var value = alias.Use();
        resource.Dispose();
        return value;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task DoubleDisposeAliasAfterOwnerReassignment_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}()
    {
        var resource = new PureDisposable();
        var alias = resource;
        resource = new PureDisposable();
        alias.Dispose();
        alias.Dispose();
        resource.Dispose();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task UseOldAliasAfterOwnerDisposeAndReassignment_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }

    [EnforcePure]
    public int Use()
    {
        return 1;
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public int {|PS0002:TestMethod|}()
    {
        var resource = new PureDisposable();
        var alias = resource;
        resource.Dispose();
        resource = new PureDisposable();
        var value = alias.Use();
        resource.Dispose();
        return value;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task DoubleDisposeOldAliasAfterOwnerDisposeAndReassignment_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}()
    {
        var resource = new PureDisposable();
        var alias = resource;
        resource.Dispose();
        resource = new PureDisposable();
        alias.Dispose();
        resource.Dispose();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ReturnedOwnedLocalDisposable_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public PureDisposable TestMethod()
    {
        var resource = new PureDisposable();
        return resource;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ReturnedAliasToOwnedLocalDisposable_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public PureDisposable TestMethod()
    {
        var resource = new PureDisposable();
        var alias = resource;
        return alias;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ReturnedOldAliasAfterOwnerReassignmentAndNewOwnerDisposed_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public PureDisposable TestMethod()
    {
        var resource = new PureDisposable();
        var alias = resource;
        resource = new PureDisposable();
        resource.Dispose();
        return alias;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ReturnedNewOwnerAfterAliasDisposed_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public PureDisposable TestMethod()
    {
        var resource = new PureDisposable();
        var alias = resource;
        resource = new PureDisposable();
        alias.Dispose();
        return resource;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ExplicitDisposeAliasThenDisposeOriginal_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}()
    {
        var resource = new PureDisposable();
        var alias = resource;
        alias.Dispose();
        resource.Dispose();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ExplicitDisposeAliasThenUseOriginal_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }

    [EnforcePure]
    public int Use()
    {
        return 1;
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public int {|PS0002:TestMethod|}()
    {
        var resource = new PureDisposable();
        var alias = resource;
        alias.Dispose();
        return resource.Use();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ExplicitDisposeAliasSatisfiesOwnedLocalDisposal_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        var resource = new PureDisposable();
        var alias = resource;
        alias.Dispose();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task UseAfterUsingStatementExistingLocal_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }

    [EnforcePure]
    public int Use()
    {
        return 1;
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public int {|PS0002:TestMethod|}()
    {
        var resource = new PureDisposable();
        using (resource)
        {
        }

        return resource.Use();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ExplicitDisposeAfterUsingStatementExistingLocal_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}()
    {
        var resource = new PureDisposable();
        using (resource)
        {
        }

        resource.Dispose();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task UseAfterUsingStatementAlias_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }

    [EnforcePure]
    public int Use()
    {
        return 1;
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public int {|PS0002:TestMethod|}()
    {
        var resource = new PureDisposable();
        var alias = resource;
        using (alias)
        {
        }

        return resource.Use();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task UseAfterNestedUsingDeclarationAlias_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }

    [EnforcePure]
    public int Use()
    {
        return 1;
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public int {|PS0002:TestMethod|}()
    {
        PureDisposable resource;
        {
            using var alias = new PureDisposable();
            resource = alias;
        }

        return resource.Use();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ExplicitDisposeAfterNestedUsingDeclarationAlias_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}()
    {
        PureDisposable resource;
        {
            using var alias = new PureDisposable();
            resource = alias;
        }

        resource.Dispose();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
