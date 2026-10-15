using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class CompilerAssemblyDisplayCoverageTests
{
    [TestCase("int[]", false)]
    [TestCase("int", false)]
    [TestCase("System.Collections.Generic.List<int>", true)]
    [TestCase("System.Collections.Generic.Dictionary<int, int>", true)]
    public void CompilerTypeCertificatesDistinguishClosedClassesFromOtherDomains(string typeName, bool closed)
    {
        using var subject = TypedProgramSubject.Create(typeName + " Target(" + typeName + " value) => value;");
        var target = (IMethodSymbol)subject.Compilation.GetTypeByMetadataName("Subject")!
            .GetMembers("Target").Single();
        Assert.That(CompilerIdentityBridge.IsClosedReferenceType(target.ReturnType), Is.EqualTo(closed));
        Assert.That(CompilerIdentityBridge.IsClosedSealedReferenceType(target.ReturnType), Is.False);
    }

    [Test]
    public void AssemblyAndModuleDisplaysRemainDistinctAndStable()
    {
        using var subject = TypedProgramSubject.Create("int Target() => 17;");
        Assert.That(subject.Invoke([]), Is.EqualTo(17));
        var assembly = subject.Compilation.Assembly;
        var module = subject.Compilation.SourceModule;
        var assemblyDisplay = CompilerIdentityBridge.CreateSymbolDisplay(assembly);
        var moduleDisplay = CompilerIdentityBridge.CreateSymbolDisplay(module);
        Assert.That(assemblyDisplay, Does.EndWith("/Assembly:TypedSubject"));
        Assert.That(moduleDisplay, Does.EndWith("/NetModule:TypedSubject.dll"));
        Assert.That(moduleDisplay, Does.StartWith(assembly.Identity + "::"));
        Assert.That(moduleDisplay, Is.Not.EqualTo(assemblyDisplay));
        Assert.That(CompilerIdentityBridge.CreateSymbolDisplay(assembly), Is.EqualTo(assemblyDisplay));
        Assert.That(CompilerIdentityBridge.CreateSymbolDisplay(module), Is.EqualTo(moduleDisplay));
    }
}
