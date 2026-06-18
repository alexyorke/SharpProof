using System.Threading.Tasks;
using NUnit.Framework;
using PurelySharp.Analyzer;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

namespace PurelySharp.Test
{
    [TestFixture]
    public class PropertyGetterDispatchTests
    {
        [Test]
        public async Task InterfacePropertyGetter_WithImpureImplementation_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public interface ICounter
{
    int Count { get; }
}

public sealed class ImpureCounter : ICounter
{
    private int _reads;

    public int Count
    {
        get
        {
            _reads++;
            return _reads;
        }
    }
}

public class TestClass
{
    [EnforcePure]
    public int {|PS0002:Read|}(ICounter counter) => counter.Count;
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task PureInterfacePropertyGetter_WithImpureImplementation_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public interface ICounter
{
    int Count
    {
        [Pure]
        get;
    }
}

public sealed class ImpureCounter : ICounter
{
    private int _reads;

    public int Count
    {
        get
        {
            _reads++;
            return _reads;
        }
    }
}

public class TestClass
{
    [EnforcePure]
    public int Read(ICounter counter) => counter.Count;
}";

            var expectedGetter = VerifyCS.Diagnostic(PurelySharpDiagnostics.PurityNotVerifiedId)
                .WithSpan(17, 16, 17, 21)
                .WithArguments("get_Count");
            var expectedRead = VerifyCS.Diagnostic(PurelySharpDiagnostics.PurityNotVerifiedId)
                .WithSpan(30, 16, 30, 20)
                .WithArguments("Read");

            await VerifyCS.VerifyAnalyzerAsync(test, expectedGetter, expectedRead);
        }

        [Test]
        public async Task InterfacePropertyGetter_OnLocalInitializedWithSealedImplementation_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public interface ILocalCounter
{
    int Count { get; }
}

public sealed class SealedLocalCounter : ILocalCounter
{
    public int Count => 1;
}

public sealed class ImpureLocalCounter : ILocalCounter
{
    private int _reads;

    public int Count
    {
        get
        {
            _reads++;
            return _reads;
        }
    }
}

public class TestClass
{
    [EnforcePure]
    public int Read()
    {
        ILocalCounter counter = new SealedLocalCounter();
        return counter.Count;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task InterfacePropertyGetter_OnLocalInitializedFromPreviousDeclarator_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public interface IAliasCounter
{
    int Count { get; }
}

public sealed class SealedAliasCounter : IAliasCounter
{
    public int Count => 1;
}

public sealed class ImpureAliasCounter : IAliasCounter
{
    private int _reads;

    public int Count
    {
        get
        {
            _reads++;
            return _reads;
        }
    }
}

public class TestClass
{
    [EnforcePure]
    public int Read()
    {
        IAliasCounter first = new SealedAliasCounter(), second = first;
        return second.Count;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task InterfacePropertyGetter_OnLocalReassignedFromUnknownImplementation_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public interface IReassignedCounter
{
    int Count { get; }
}

public sealed class SealedReassignedCounter : IReassignedCounter
{
    public int Count => 1;
}

public sealed class ImpureReassignedCounter : IReassignedCounter
{
    private int _reads;

    public int Count
    {
        get
        {
            _reads++;
            return _reads;
        }
    }
}

public class TestClass
{
    [EnforcePure]
    public int {|PS0002:Read|}(IReassignedCounter unknown)
    {
        IReassignedCounter counter = new SealedReassignedCounter();
        counter = unknown;
        return counter.Count;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task InterfacePropertyGetter_OnSealedImplementationThroughCast_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public interface ICastCounter
{
    int Count { get; }
}

public sealed class SealedCastCounter : ICastCounter
{
    public int Count => 1;
}

public sealed class ImpureCastCounter : ICastCounter
{
    private int _reads;

    public int Count
    {
        get
        {
            _reads++;
            return _reads;
        }
    }
}

public class TestClass
{
    [EnforcePure]
    public int Read()
    {
        return ((ICastCounter)new SealedCastCounter()).Count;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task FinalVirtualFrameworkPropertyGetter_OnBaseTypedReceiver_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int Read(Exception error)
    {
        _ = error.InnerException;
        return error.HResult;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task InterfacePropertyGetter_OnConditionalSealedImplementationBranches_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public interface IConditionalCounter
{
    int Count { get; }
}

public sealed class SealedConditionalCounter : IConditionalCounter
{
    public int Count => 1;
}

public sealed class ImpureConditionalCounter : IConditionalCounter
{
    private int _reads;

    public int Count
    {
        get
        {
            _reads++;
            return _reads;
        }
    }
}

public class TestClass
{
    [EnforcePure]
    public int Read(bool flag)
    {
        return (flag ? (IConditionalCounter)new SealedConditionalCounter() : new SealedConditionalCounter()).Count;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task VirtualPropertyGetter_WithImpureOverride_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class BaseCounter
{
    public virtual int Count => 0;
}

public sealed class ImpureCounter : BaseCounter
{
    private int _reads;

    public override int Count
    {
        get
        {
            _reads++;
            return _reads;
        }
    }
}

public class TestClass
{
    [EnforcePure]
    public int {|PS0002:Read|}(BaseCounter counter) => counter.Count;
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task PureVirtualPropertyGetter_WithImpureOverride_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class BaseCounter
{
    public virtual int Count
    {
        [Pure]
        get => 0;
    }
}

public sealed class ImpureCounter : BaseCounter
{
    private int _reads;

    public override int Count
    {
        get
        {
            _reads++;
            return _reads;
        }
    }
}

public class TestClass
{
    [EnforcePure]
    public int Read(BaseCounter counter) => counter.Count;
}";

            var expectedGetter = VerifyCS.Diagnostic(PurelySharpDiagnostics.PurityNotVerifiedId)
                .WithSpan(17, 25, 17, 30)
                .WithArguments("get_Count");
            var expectedRead = VerifyCS.Diagnostic(PurelySharpDiagnostics.PurityNotVerifiedId)
                .WithSpan(30, 16, 30, 20)
                .WithArguments("Read");

            await VerifyCS.VerifyAnalyzerAsync(test, expectedGetter, expectedRead);
        }
    }
}
