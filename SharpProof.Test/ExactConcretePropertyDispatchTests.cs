using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class ExactConcretePropertyDispatchTests
{
    [Test]
    public async Task InterfacePropertyDispatch_ExactConcreteLocalWithImpureSubclass_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public interface IValueProvider
{
    int Value { get; }
}

public class ExactValueProvider : IValueProvider
{
    public virtual int Value => 1;
}

public class ImpureValueProvider : ExactValueProvider
{
    public override int {|SP0002:Value|}
    {
        [EnforcePure]
        get
        {
            Console.WriteLine(1);
            return 2;
        }
    }
}

public class TestClass
{
    [EnforcePure]
    public int ReadValue()
    {
        IValueProvider provider = new ExactValueProvider();
        return provider.Value;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task VirtualPropertyDispatch_ExactConcreteLocalWithImpureSubclass_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public abstract class BaseValue
{
    public abstract int Value { get; }
}

public class ExactValue : BaseValue
{
    public override int Value => 1;
}

public class ImpureValue : ExactValue
{
    public override int {|SP0002:Value|}
    {
        [EnforcePure]
        get
        {
            Console.WriteLine(1);
            return 2;
        }
    }
}

public class TestClass
{
    [EnforcePure]
    public int ReadValue()
    {
        BaseValue value = new ExactValue();
        return value.Value;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}