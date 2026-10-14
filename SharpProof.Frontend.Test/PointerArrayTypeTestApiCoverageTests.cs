using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class PointerArrayTypeTestApiCoverageTests
{
    [Test]
    public void ActualPointerArrayTypeTestRetainsConservativeElementDomainPolicy()
    {
        const string members = "public static bool Target(int value) => Helper(value); private static unsafe bool Helper<T>(T value) => value is int*[];";
        var tree = CSharpSyntaxTree.ParseText(FrontendTestHelpers.WrapSubjectMembers(members),
            new CSharpParseOptions(LanguageVersion.CSharp12));
        var compilation = CSharpCompilation.Create("PointerArrayTypeTestApi", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release, allowUnsafe: true));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        using var output = new MemoryStream();
        var emitted = compilation.Emit(output);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(syntax => syntax.Identifier.ValueText == "Helper");
        var operation = compilation.GetSemanticModel(tree).GetOperation(method.ExpressionBody!.Expression);
        Assert.That(operation, Is.AssignableTo<IIsTypeOperation>());
        var test = (IIsTypeOperation)operation!;
        Assert.That(test.ValueOperand.Type, Is.AssignableTo<ITypeParameterSymbol>());
        Assert.That(test.ValueOperand.Type!.IsReferenceType, Is.False);
        Assert.That(test.TypeOperand, Is.AssignableTo<IArrayTypeSymbol>());
        var array = (IArrayTypeSymbol)test.TypeOperand;
        Assert.That(array.IsSZArray, Is.True);
        Assert.That(array.ElementType, Is.AssignableTo<IPointerTypeSymbol>());
        Assert.That(((IPointerTypeSymbol)array.ElementType).PointedAtType.SpecialType,
            Is.EqualTo(SpecialType.System_Int32));
        Assert.That(CSharpOperationSemantics.OpaqueTypeTestOperand(test), Is.SameAs(test.ValueOperand));
        Assert.That(CSharpOperationSemantics.OpaqueTypeTestMayAllocate(test, compilation), Is.True);
        var loadContext = new AssemblyLoadContext("PointerArrayTypeTestApi", isCollectible: true);
        try
        {
            output.Position = 0;
            var assembly = loadContext.LoadFromStream(output);
            var target = assembly.GetType("Subject")!.GetMethod("Target")!;
            Assert.That(target.Invoke(null, [17]), Is.EqualTo(false));
        }
        finally
        {
            loadContext.Unload();
        }
    }
}
