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
    }
}
