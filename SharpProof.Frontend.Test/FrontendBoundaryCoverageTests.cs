using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class FrontendBoundaryCoverageTests
{
    private static readonly string[] ExpectedDefinedSymbols = ["LOCAL", "RETAINED"];
    [Test]
    public void ProgramAbstentionRequiresIdentityAndReason()
    {
        var factory = new IrFactory();
        var identity = factory.CreateOperation("boundary");
        var missingIdentity = Assert.Throws<ArgumentException>((Action)(() =>
            _ = new FrontendProgramAbstention(default, FrontendAbstention.InvalidOperation)));
        var missingReason = Assert.Throws<ArgumentException>((Action)(() =>
            _ = new FrontendProgramAbstention(identity, FrontendAbstention.None)));
        Assert.That(missingIdentity!.ParamName, Is.EqualTo("operation"));
        Assert.That(missingReason!.ParamName, Is.EqualTo("reason"));
    }

    [Test]
    public void AttributeLookupRejectsMissingMetadataName()
    {
        Assert.That(ContractApiMetadata.TryGetAttribute(null!, out var descriptor), Is.False);
        Assert.That(descriptor, Is.EqualTo(default(ContractApiAttributeDescriptor)));
        Assert.That(ContractApiMetadata.TryGetAttribute(
            "SharpProof.Attributes.NotNullAttribute", out _), Is.True);
    }

    [Test]
    public void CachedTypeTraversalRetainsCurrentCompilationTypesAndObservesCancellation()
    {
        var compilation = CSharpCompilation.Create("CachedBoundary",
            [CSharpSyntaxTree.ParseText("namespace N { public class Outer { public class Inner {} } }")],
            TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var first = ReferencedTypeSymbols.GetAllCached(compilation).ToArray();
        var second = ReferencedTypeSymbols.GetAllCached(compilation).ToArray();
        Assert.That(first, Is.EqualTo(second));
        Assert.That(first.Select(type => type is null ? throw new System.ArgumentNullException(nameof(type)) : (type.ToDisplayString())), Does.Contain("N.Outer.Inner"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>((Action)(() =>
            _ = ReferencedTypeSymbols.GetAllCached(compilation, cancellation.Token).ToArray()));
    }

    [Test]
    public void DefinedSymbolInventoryCombinesActiveDefinesAndUndefines()
    {
        var tree = CSharpSyntaxTree.ParseText(
            "#define LOCAL\n#undef OPTION\n#if NEVER\n#define INACTIVE\n#endif\nclass Subject {}",
            new CSharpParseOptions(preprocessorSymbols: ["OPTION", "RETAINED"]));
        Assert.That(CSharpPreprocessorSymbols.GetDefined(tree),
            Is.EquivalentTo(ExpectedDefinedSymbols));
        var invalid = Assert.Throws<ArgumentException>((Action)(() =>
            _ = CSharpPreprocessorSymbols.IsDefined(tree, " ")));
        Assert.That(invalid!.ParamName, Is.EqualTo("symbol"));
    }

    [TestCase(8, true)]
    [TestCase(70, false)]
    public void ClosedReferenceCertificateRespectsNestedGenericDepth(int depth, bool expected)
    {
        var typeText = string.Concat(Enumerable.Repeat("Box<", depth)) + "int" + new string('>', depth);
        var tree = CSharpSyntaxTree.ParseText("public sealed class Box<T> {} public class Subject { public " +
            typeText + " Value = null; }");
        var compilation = CSharpCompilation.Create("GenericDepthBoundary", [tree],
            TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(d => d is null ? throw new System.ArgumentNullException(nameof(d)) : (d.Severity == DiagnosticSeverity.Error)), Is.Empty);
        var field = tree.GetRoot().DescendantNodes().OfType<FieldDeclarationSyntax>().Single();
        var type = compilation.GetSemanticModel(tree).GetTypeInfo(field.Declaration.Type).Type!;
        Assert.That(CompilerIdentityBridge.IsClosedReferenceType(type), Is.EqualTo(expected));
        Assert.That(CompilerIdentityBridge.IsClosedSealedReferenceType(type), Is.EqualTo(expected));
    }

    [Test]
    public void ErrorTypeRemainsOpaqueWhenMappingMalformedSource()
    {
        var tree = CSharpSyntaxTree.ParseText("public class Subject { public Missing Target() => null; }");
        var compilation = CSharpCompilation.Create("ErrorTypeBoundary", [tree],
            TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var symbol = (IMethodSymbol)compilation.GetSemanticModel(tree).GetDeclaredSymbol(syntax)!;
        Assert.That(symbol.ReturnType.TypeKind, Is.EqualTo(TypeKind.Error));
        var factory = new IrFactory();
        var mapper = new RoslynTypeMapper(factory);
        var type = mapper.GetTypeId(symbol.ReturnType);
        Assert.That(factory.GetTypeInfo(type).Kind, Is.EqualTo(IrTypeKind.Reference));
        Assert.That(mapper.IsSupportedValueDomain(symbol.ReturnType), Is.False);
    }
}
