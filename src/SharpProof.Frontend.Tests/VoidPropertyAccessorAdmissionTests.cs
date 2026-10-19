using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class VoidPropertyAccessorAdmissionTests
{
    [Test]
    public void CompilerRejectedVoidPropertyIsNotAdmittedAsAnAutoGetter()
    {
        const string source = "public sealed class Subject { public static void Value { get; set; } }";
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12), "void-property.cs");
        var compilation = CSharpCompilation.Create("VoidProperty", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        var errors = compilation.GetDiagnostics().Where(d => d is null ? throw new System.ArgumentNullException(nameof(d)) : (d.Severity == DiagnosticSeverity.Error)).Select(d => d is null ? throw new System.ArgumentNullException(nameof(d)) : (d.Id)).ToImmutableArray();
        Assert.That(errors, Does.Contain("CS0547"));
        using var image = new MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.False);
        var declaration = tree.GetRoot().DescendantNodes().OfType<AccessorDeclarationSyntax>()
            .Single(accessor => accessor is null ? throw new System.ArgumentNullException(nameof(accessor)) : (accessor.IsKind(SyntaxKind.GetAccessorDeclaration)));
        var model = compilation.GetSemanticModel(tree);
        var target = (IMethodSymbol)model.GetDeclaredSymbol(declaration)!;
        var property = (IPropertySymbol)target.AssociatedSymbol!;
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        Assert.That(SymbolEqualityComparer.Default.Equals(context.Target, property.GetMethod), Is.True);
        var result = new RoslynProgramLowerer(factory).LowerAutoAccessor(context, declaration);
        Assert.That(result, Is.Null);
    }
}
