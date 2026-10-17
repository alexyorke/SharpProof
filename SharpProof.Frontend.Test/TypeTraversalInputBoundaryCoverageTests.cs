using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class TypeTraversalInputBoundaryCoverageTests
{
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void TypeTraversalRejectsNullCompilationDuringEnumeration(int traversal)
    {
        var compilation = CreateCompilation();
        var sequence = traversal switch
        {
            0 => ReferencedTypeSymbols.GetAll(null!),
            1 => ReferencedTypeSymbols.GetAllCached(null!),
            _ => ReferencedTypeSymbols.GetAllCached(null!, compilation.Assembly)
        };
        var error = Assert.Throws<ArgumentNullException>((Action)(() => sequence.ToArray()));
        Assert.That(error!.ParamName, Is.EqualTo("compilation"));
    }

    [Test]
    public void CachedTypeTraversalRejectsNullReferencedAssembly()
    {
        var sequence = ReferencedTypeSymbols.GetAllCached(CreateCompilation(), null!);
        var error = Assert.Throws<ArgumentNullException>((Action)(() => sequence.ToArray()));
        Assert.That(error!.ParamName, Is.EqualTo("referencedAssembly"));
    }

    [Test]
    public void TypeTraversalsPreserveSourceTypeControl()
    {
        var compilation = CreateCompilation();
        var platformAssembly = compilation.GetSpecialType(SpecialType.System_Object).ContainingAssembly;
        foreach (var types in new[]
                 {
                     ReferencedTypeSymbols.GetAll(compilation),
                     ReferencedTypeSymbols.GetAllCached(compilation),
                     ReferencedTypeSymbols.GetAllCached(compilation, platformAssembly)
                 })
        {
            Assert.That(types.Any(type => type is null ? throw new System.ArgumentNullException(nameof(type)) : (type.Name == "Subject" &&
                SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly))), Is.True);
        }
    }

    private static CSharpCompilation CreateCompilation()
    {
        var compilation = CSharpCompilation.Create("TypeTraversalBoundaryControl",
            [CSharpSyntaxTree.ParseText("public static class Subject { public static int Target() => 17; }")],
            TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        return compilation;
    }
}
