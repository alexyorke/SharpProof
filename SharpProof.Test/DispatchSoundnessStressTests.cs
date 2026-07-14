using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
public class DispatchSoundnessStressTests
{
    [Test]
    public async Task InterfaceMethodDispatch_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public interface IValueProvider
{
    int GetValue();
}

public sealed class ClockValueProvider : IValueProvider
{
    public int GetValue() => DateTime.Now.Millisecond;
}

public sealed class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(IValueProvider provider)
    {
        return provider.GetValue();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task InterfacePropertyDispatch_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public interface IValueProvider
{
    int Value { get; }
}

public sealed class ClockValueProvider : IValueProvider
{
    public int Value => DateTime.Now.Millisecond;
}

public sealed class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(IValueProvider provider)
    {
        return provider.Value;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task VirtualMethodDispatch_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class BaseValueProvider
{
    public virtual int GetValue() => 1;
}

public sealed class ClockValueProvider : BaseValueProvider
{
    public override int GetValue() => DateTime.Now.Millisecond;
}

public sealed class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(BaseValueProvider provider)
    {
        return provider.GetValue();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task VirtualPropertyDispatch_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class BaseValueProvider
{
    public virtual int Value => 1;
}

public sealed class ClockValueProvider : BaseValueProvider
{
    public override int Value => DateTime.Now.Millisecond;
}

public sealed class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(BaseValueProvider provider)
    {
        return provider.Value;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task AbstractMethodDispatch_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public abstract class BaseValueProvider
{
    public abstract int GetValue();
}

public sealed class ClockValueProvider : BaseValueProvider
{
    public override int GetValue() => DateTime.Now.Millisecond;
}

public sealed class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(BaseValueProvider provider)
    {
        return provider.GetValue();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task GenericInterfaceConstraintDispatch_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public interface IValueProvider
{
    int GetValue();
}

public sealed class ClockValueProvider : IValueProvider
{
    public int GetValue() => DateTime.Now.Millisecond;
}

public sealed class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}<T>(T provider) where T : IValueProvider
    {
        return provider.GetValue();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DirectSealedMethodDispatch_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class ValueProvider
{
    [EnforcePure]
    public int GetValue() => 42;
}

public sealed class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        var provider = new ValueProvider();
        return provider.GetValue();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DirectSealedPropertyDispatch_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class ValueProvider
{
    public int Value => 42;
}

public sealed class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        var provider = new ValueProvider();
        return provider.Value;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}