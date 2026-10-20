using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class SourceTypeSubstitutionTests
{
    [TestCase("[]")]
    [TestCase("[,]")]
    public void ConstructedNestedOwnersAndTupleArraysUseTheSameRuntimeType(string suffix)
    {
        System.ArgumentNullException.ThrowIfNull(suffix);
        var tree = CSharpSyntaxTree.ParseText("public class Box<T> { public class Inner<U> { public void Target() { } " +
            "public System.Collections.Generic.List<(T, U)>" + suffix + " Open; } } public class Expected { " +
            "public System.Collections.Generic.List<(int, string)>" + suffix + " Value; public Box<int>.Inner<string> Owner; }");
        var compilation = CSharpCompilation.Create("SourceSubstitution", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) :
            diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var model = compilation.GetSemanticModel(tree);
        var target = (IMethodSymbol)model.GetDeclaredSymbol(tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single())!;
        var context = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total), target) { Compilation = compilation };
        var owner = target.ContainingType;
        context.SpecializeTypeParameter(owner.ContainingType.TypeParameters[0], compilation.GetSpecialType(SpecialType.System_Int32));
        context.SpecializeTypeParameter(owner.TypeParameters[0], compilation.GetSpecialType(SpecialType.System_String));
        var expected = compilation.GetTypeByMetadataName("Expected")!;
        var field = (IFieldSymbol)owner.GetMembers("Open").Single();
        Assert.That(CompilerIdentityBridge.SameRuntimeType(context.SpecializeSourceType(field.Type)!, ((IFieldSymbol)expected.GetMembers("Value").Single()).Type), Is.True);
        Assert.That(CompilerIdentityBridge.SameRuntimeType(context.SpecializeSourceType(owner)!, ((IFieldSymbol)expected.GetMembers("Owner").Single()).Type), Is.True);
    }

    [Test]
    public void ConstructedArgumentsKeepNullableAnnotationsAfterSubstitution()
    {
        var tree = CSharpSyntaxTree.ParseText("#nullable enable\npublic class Box<T> { public void Target() { } public System.Collections.Generic.Dictionary<T, string?> Value = null!; }");
        var compilation = CSharpCompilation.Create("NullableConstructedSubstitution", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null
            ? throw new System.ArgumentNullException(nameof(diagnostic))
            : diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var target = (IMethodSymbol)compilation.GetSemanticModel(tree).GetDeclaredSymbol(
            tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single())!;
        var context = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total), target) { Compilation = compilation };
        var replacement = compilation.GetSpecialType(SpecialType.System_Int32);
        context.SpecializeTypeParameter(target.ContainingType.TypeParameters[0], replacement);
        var original = (INamedTypeSymbol)((IFieldSymbol)target.ContainingType.GetMembers("Value").Single()).Type;
        var specialized = (INamedTypeSymbol)context.SpecializeSourceType(original)!;
        Assert.That(SymbolEqualityComparer.Default.Equals(specialized.TypeArguments[0], replacement), Is.True);
        Assert.That(specialized.TypeArgumentNullableAnnotations[1], Is.EqualTo(NullableAnnotation.Annotated));
        Assert.That(SymbolEqualityComparer.IncludeNullability.Equals(specialized.TypeArguments[1], original.TypeArguments[1]), Is.True);
    }

    [Test]
    public void CompoundArraySubstitutionRequiresItsAuthenticatedCompilation()
    {
        var tree = CSharpSyntaxTree.ParseText("public class Box { public static void Target<T>(T[] values) { } }");
        var compilation = CSharpCompilation.Create("MissingSubstitutionCompilation", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var target = (IMethodSymbol)compilation.GetSemanticModel(tree).GetDeclaredSymbol(tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single())!;
        var context = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total), target);
        context.SpecializeTypeParameter(target.TypeParameters[0], compilation.GetSpecialType(SpecialType.System_Int32));
        Assert.Throws<FrontendLoweringRejectionException>((System.Action)(() => context.SpecializeSourceType(target.Parameters[0].Type)));
    }

    [Test]
    public void NestedSourceFramesKeepArraySymbolsWithoutAdmittingTheirCompilation()
    {
        var tree = CSharpSyntaxTree.ParseText("public class Box { public static void Target<T>(T[] values) { } }");
        var compilation = CSharpCompilation.Create("LazySubstitutionCompilation", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var target = (IMethodSymbol)compilation.GetSemanticModel(tree).GetDeclaredSymbol(tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single())!;
        var caller = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total), target) { Compilation = compilation };
        var first = caller.CreateFrame(target);
        var nested = first.CreateFrame(target);
        Assert.That(first.Compilation, Is.Null);
        Assert.That(nested.Compilation, Is.Null);
        nested.SpecializeTypeParameter(target.TypeParameters[0], compilation.GetSpecialType(SpecialType.System_Int32));
        var expected = compilation.CreateArrayTypeSymbol(compilation.GetSpecialType(SpecialType.System_Int32));
        Assert.That(CompilerIdentityBridge.SameRuntimeType(nested.SpecializeSourceType(target.Parameters[0].Type)!, expected), Is.True);
        Assert.That(nested.Compilation, Is.Null);
    }
    [Test]
    public void NullableReceiverElementSpecializationPreservesTheSourceFrame()
    {
        var tree = CSharpSyntaxTree.ParseText("public class Box { public static void Target<T>(T value) { } }");
        var compilation = CSharpCompilation.Create("NullableReceiverElement", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var target = (IMethodSymbol)compilation.GetSemanticModel(tree).GetDeclaredSymbol(tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single())!;
        var context = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total), target);
        var concrete = compilation.GetSpecialType(SpecialType.System_Int32);
        context.SpecializeTypeParameter(target.TypeParameters[0], concrete);
        var specialize = typeof(TotalLoweringContext).GetMethod("SpecializeReceiverElementCore", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        Assert.That(specialize.Invoke(context, [null]), Is.Null);
        Assert.That(specialize.Invoke(context, [target.TypeParameters[0]]), Is.SameAs(concrete));
        Assert.That(context.Compilation, Is.Null);
        Assert.That(context.Target, Is.SameAs(target));
    }
    [Test]
    public void CyclicSubstitutionAbstainsWithinItsConstructionBound()
    {
        var tree = CSharpSyntaxTree.ParseText("public class Box<T> { public void Target() { } }");
        var compilation = CSharpCompilation.Create("CyclicSubstitution", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);
        var target = (IMethodSymbol)model.GetDeclaredSymbol(tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single())!;
        var context = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total), target);
        context.SpecializeTypeParameter(target.ContainingType.TypeParameters[0], target.ContainingType);
        Assert.Throws<FrontendLoweringRejectionException>((System.Action)(() => context.SpecializeSourceType(target.ContainingType)));
    }
}
