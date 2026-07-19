using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
public class AttributeResolutionTests
{
    [Test]
    public async Task UnrelatedGlobalEnforcePureAttribute_ReportsIdentityDiagnosticAndDoesNotRunPurity()
    {
        var test = @"
using System;

[System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Constructor | System.AttributeTargets.Class | System.AttributeTargets.Struct | System.AttributeTargets.Interface)]
public sealed class EnforcePureAttribute : System.Attribute { }

public class TestClass
{
    private int _field = 0;

    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        _field = 1;
    }
}";

        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(test);

        var identityDiagnostic = diagnostics.Single(diagnostic =>
            diagnostic.Id == "SP0026");
        Assert.That(identityDiagnostic.Properties["sharpproof.attribute_identity.name"],
            Is.EqualTo("EnforcePureAttribute"));
        Assert.That(identityDiagnostic.Properties["sharpproof.attribute_identity.namespace"],
            Is.EqualTo("<global>"));
        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == "SP0002"),
            Is.False);
    }

    [Test]
    public async Task ConfiguredStubNamespaceEnforcePureAttribute_ImpureMethod_RunsPurity()
    {
        var test = @"
using System;

namespace Contracts
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class EnforcePureAttribute : Attribute
    {
    }
}

public class TestClass
{
    private int _field = 0;

    [Contracts.EnforcePure]
    public void TestMethod()
    {
        _field = 1;
    }
}";

        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            test,
            ImmutableDictionary<string, string>.Empty.Add("build_property.sharpproof_attribute_stub_namespaces",
                "Contracts"));

        Assert.That(
            diagnostics.Any(diagnostic => diagnostic.Id == "SP0026"),
            Is.False);
        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == "SP0002"), Is.True);
    }

    [Test]
    public async Task UnrelatedExternalPureAttribute_ReportsIdentityDiagnosticAndDoesNotRunPurity()
    {
        var test = @"
using System;

namespace External
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class PureAttribute : Attribute
    {
    }
}

public static class TestClass
{
    [External.Pure]
    public static int Bad()
    {
        System.Console.WriteLine(1);
        return 0;
    }
}";

        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(test);

        var identityDiagnostic = diagnostics.Single(diagnostic =>
            diagnostic.Id == "SP0026");
        Assert.That(identityDiagnostic.Properties["sharpproof.attribute_identity.name"],
            Is.EqualTo("PureAttribute"));
        Assert.That(identityDiagnostic.Properties["sharpproof.attribute_identity.namespace"],
            Is.EqualTo("External"));
        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == "SP0002"),
            Is.False);
    }
}