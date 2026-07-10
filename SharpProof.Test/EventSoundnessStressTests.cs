using NUnit.Framework;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class EventSoundnessStressTests
{
    [Test]
    public async Task InstanceEventSubscription_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public sealed class Publisher
{
    public event Action Changed;
}

public sealed class TestClass
{
    [EnforcePure]
    private static void Handler()
    {
    }

    [EnforcePure]
    public void {|SP0002:TestMethod|}(Publisher publisher)
    {
        publisher.Changed += Handler;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task InstanceEventUnsubscription_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public sealed class Publisher
{
    public event Action Changed;
}

public sealed class TestClass
{
    [EnforcePure]
    private static void Handler()
    {
    }

    [EnforcePure]
    public void {|SP0002:TestMethod|}(Publisher publisher)
    {
        publisher.Changed -= Handler;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task StaticEventSubscription_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public static class Publisher
{
    public static event Action Changed;
}

public sealed class TestClass
{
    [EnforcePure]
    private static void Handler()
    {
    }

    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        Publisher.Changed += Handler;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task EventInvocation_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    public event Action Changed;

    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        Changed?.Invoke();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task CustomEventAccessorSubscription_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public sealed class Publisher
{
    public event Action Changed
    {
        add
        {
            Console.WriteLine(value);
        }
        remove
        {
        }
    }
}

public sealed class TestClass
{
    [EnforcePure]
    private static void Handler()
    {
    }

    [EnforcePure]
    public void {|SP0002:TestMethod|}(Publisher publisher)
    {
        publisher.Changed += Handler;
    }
}";

        var expectedRemoveChanged = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
            .WithSpan(13, 9, 13, 15)
            .WithArguments("remove_Changed");

        await VerifyCS.VerifyAnalyzerAsync(test, expectedRemoveChanged);
    }

    [Test]
    public async Task EventDeclarationAlone_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    public event Action Changed;

    [EnforcePure]
    public int TestMethod(int value)
    {
        return value + 1;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}