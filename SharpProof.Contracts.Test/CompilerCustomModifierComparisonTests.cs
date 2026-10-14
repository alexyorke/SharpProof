using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class CompilerCustomModifierComparisonTests
{
    [TestCase("Cdecl, SuppressGCTransition", "Cdecl, SuppressGCTransition", true)]
    [TestCase("Cdecl, SuppressGCTransition", "Stdcall, SuppressGCTransition", false)]
    [TestCase("Cdecl, SuppressGCTransition", "SuppressGCTransition", false)]
    [TestCase("SuppressGCTransition", "Cdecl, SuppressGCTransition", false)]
    public void OrderedModifierComparisonUsesActualCompilerProducedCollections(
        string leftConventions, string rightConventions, bool expected)
    {
        ArgumentNullException.ThrowIfNull(leftConventions);
        ArgumentNullException.ThrowIfNull(rightConventions);
        var source = $$"""
            public static unsafe class Subject {
                public static void Left(delegate* unmanaged[{{leftConventions}}]<void> value) { }
                public static void Right(delegate* unmanaged[{{rightConventions}}]<void> value) { }
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12));
        var compilation = CSharpCompilation.Create("ContractsCompilerCustomModifierComparison",
            [tree], TestMetadataReferences.WithSharpProof,
            TestCompilation.CreateOptions(OutputKind.DynamicallyLinkedLibrary).WithAllowUnsafe(true));
        Assert.That(compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var model = compilation.GetSemanticModel(tree);
        var methods = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .ToDictionary(static syntax => syntax.Identifier.ValueText,
                syntax => model.GetDeclaredSymbol(syntax)!);
        var left = methods["Left"];
        var right = methods["Right"];
        Assert.That(SymbolEqualityComparer.Default.Equals(left.ContainingAssembly, compilation.Assembly), Is.True);
        Assert.That(SymbolEqualityComparer.Default.Equals(right.ContainingAssembly, compilation.Assembly), Is.True);
        Assert.That(left.Parameters.Single().Type, Is.InstanceOf<IFunctionPointerTypeSymbol>());
        Assert.That(right.Parameters.Single().Type, Is.InstanceOf<IFunctionPointerTypeSymbol>());
        var leftSignature = ((IFunctionPointerTypeSymbol)left.Parameters.Single().Type).Signature;
        var rightSignature = ((IFunctionPointerTypeSymbol)right.Parameters.Single().Type).Signature;
        Assert.That(leftSignature.RefKind, Is.EqualTo(RefKind.None));
        Assert.That(rightSignature.RefKind, Is.EqualTo(RefKind.None));
        var leftModifiers = leftSignature.ReturnTypeCustomModifiers;
        var rightModifiers = rightSignature.ReturnTypeCustomModifiers;
        var leftNames = leftConventions.Split(',', StringSplitOptions.TrimEntries);
        var rightNames = rightConventions.Split(',', StringSplitOptions.TrimEntries);
        Assert.That(leftModifiers, Has.Length.EqualTo(leftNames.Length));
        Assert.That(rightModifiers, Has.Length.EqualTo(rightNames.Length));
        for (var index = 0; index < leftModifiers.Length; index++)
        {
            var actualType = compilation.GetTypeByMetadataName(
                "System.Runtime.CompilerServices.CallConv" + leftNames[index]);
            Assert.That(actualType, Is.Not.Null);
            Assert.That(leftModifiers[index].IsOptional, Is.True);
            Assert.That(SymbolEqualityComparer.Default.Equals(leftModifiers[index].Modifier, actualType), Is.True);
        }
        for (var index = 0; index < rightModifiers.Length; index++)
        {
            var actualType = compilation.GetTypeByMetadataName(
                "System.Runtime.CompilerServices.CallConv" + rightNames[index]);
            Assert.That(actualType, Is.Not.Null);
            Assert.That(rightModifiers[index].IsOptional, Is.True);
            Assert.That(SymbolEqualityComparer.Default.Equals(rightModifiers[index].Modifier, actualType), Is.True);
        }
        if (leftNames.Length == rightNames.Length && !expected)
        {
            Assert.That(leftModifiers, Has.Length.EqualTo(rightModifiers.Length));
            Assert.That(SymbolEqualityComparer.Default.Equals(
                leftModifiers[0].Modifier, rightModifiers[0].Modifier), Is.False);
        }
        Assert.That(ContractForSymbolMatcher.CustomModifiersMatch(leftModifiers, rightModifiers), Is.EqualTo(expected));
    }
}
