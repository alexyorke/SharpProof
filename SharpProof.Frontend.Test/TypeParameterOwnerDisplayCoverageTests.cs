using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class TypeParameterOwnerDisplayCoverageTests
{
    [Test]
    public void RealTypeParametersRetainOwnerNamesAndDistinctCompilerIdentities()
    {
        var compilation = CSharpCompilation.Create("TypeParameterOwnerDisplayControl",
            [CSharpSyntaxTree.ParseText("public sealed class Left<T> { } public sealed class Right<T> { }")],
            TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var left = compilation.GetTypeByMetadataName("Left`1")!.TypeParameters.Single();
        var right = compilation.GetTypeByMetadataName("Right`1")!.TypeParameters.Single();
        var leftDisplay = CompilerIdentityBridge.CreateSymbolDisplay(left);
        var rightDisplay = CompilerIdentityBridge.CreateSymbolDisplay(right);
        Assert.That(leftDisplay, Does.Contain("Left"));
        Assert.That(rightDisplay, Does.Contain("Right"));
        Assert.That(leftDisplay, Is.Not.EqualTo(rightDisplay));
        Assert.That(CompilerIdentityBridge.CreateSymbolDisplay(left), Is.EqualTo(leftDisplay));
        var factory = new IrFactory();
        var leftIdentity = CompilerIdentityBridge.InternType(factory, left);
        Assert.That(CompilerIdentityBridge.InternSymbol(factory, left), Is.EqualTo(leftIdentity));
        Assert.That(CompilerIdentityBridge.InternType(factory, right), Is.Not.EqualTo(leftIdentity));
    }
}
