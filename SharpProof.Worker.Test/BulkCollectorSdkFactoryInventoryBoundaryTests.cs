using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.CompilerArtifact;

namespace SharpProof.Worker.Test;

// Actual SDK syntax/model input at the optional inventory rejection boundary.
// The missing-header compilation is never admitted as a valid artifact or proof.
[TestFixture]
public sealed class BulkCollectorSdkFactoryInventoryBoundaryTests
{
    [Test]
    public async Task SdkFactoryMissingHeadersRetainTheActualInventoryRejectionBoundary()
    {
        var method = (MethodDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration("static int Read() => 7;")!;
        var first = SyntaxFactory.ClassDeclaration(SyntaxFactory.MissingToken(SyntaxKind.IdentifierToken))
            .WithKeyword(SyntaxFactory.MissingToken(SyntaxKind.ClassKeyword))
            .WithOpenBraceToken(SyntaxFactory.MissingToken(SyntaxKind.OpenBraceToken))
            .WithCloseBraceToken(SyntaxFactory.MissingToken(SyntaxKind.CloseBraceToken))
            .AddMembers(method);
        var second = SyntaxFactory.InterfaceDeclaration(SyntaxFactory.MissingToken(SyntaxKind.IdentifierToken))
            .WithKeyword(SyntaxFactory.MissingToken(SyntaxKind.InterfaceKeyword))
            .WithOpenBraceToken(SyntaxFactory.MissingToken(SyntaxKind.OpenBraceToken))
            .WithCloseBraceToken(SyntaxFactory.MissingToken(SyntaxKind.CloseBraceToken));
        var seed = TestCompilation.Create("SdkFactoryInventoryBoundary",
            "public static class Seed { public static int Read() => 7; }");
        var tree = CSharpSyntaxTree.Create(SyntaxFactory.CompilationUnit().AddMembers(first, second),
            (CSharpParseOptions)seed.SyntaxTrees.Single().Options, "sdk-factory-boundary.cs", Encoding.UTF8);
        var compilation = seed.RemoveAllSyntaxTrees().AddSyntaxTrees(tree);
        var root = (CompilationUnitSyntax)await tree.GetRootAsync();
        var ownedClass = (ClassDeclarationSyntax)root.Members[0];
        var ownedInterface = (InterfaceDeclarationSyntax)root.Members[1];
        var ownedRead = ownedClass.Members.OfType<MethodDeclarationSyntax>().Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(compilation.ContainsSyntaxTree(tree), Is.True);
            Assert.That(ownedClass.Span.Length, Is.GreaterThan(0));
            Assert.That(ownedClass.Identifier.IsMissing, Is.True);
            Assert.That(ownedClass.Identifier.SpanStart, Is.EqualTo(ownedClass.SpanStart));
            Assert.That(ownedInterface.Span.Length, Is.Zero);
            Assert.That(ownedInterface.Identifier.IsMissing, Is.True);
            Assert.That(ownedInterface.Identifier.SpanStart, Is.EqualTo(ownedClass.Span.End));
            Assert.That(ownedRead.Modifiers.Any(SyntaxKind.StaticKeyword), Is.True);
            Assert.That(ownedRead.ExpressionBody, Is.Not.Null);
        }
        var actualTypes = compilation.Assembly.GlobalNamespace.GetTypeMembers();
        Assert.That(actualTypes, Has.Length.EqualTo(2));
        var actualClass = actualTypes.Single(type => type.TypeKind == TypeKind.Class);
        var actualInterface = actualTypes.Single(type => type.TypeKind == TypeKind.Interface);
        Assert.That(await actualClass.DeclaringSyntaxReferences.Single().GetSyntaxAsync(), Is.SameAs(ownedClass));
        Assert.That(await actualInterface.DeclaringSyntaxReferences.Single().GetSyntaxAsync(), Is.SameAs(ownedInterface));
        var actualRead = actualClass.GetMembers("Read").OfType<IMethodSymbol>().Single();
        Assert.That(await actualRead.DeclaringSyntaxReferences.Single().GetSyntaxAsync(), Is.SameAs(ownedRead));
        Assert.That(actualInterface.GetMembers(), Is.Empty);
        var model = compilation.GetSemanticModel(tree);
        Assert.That(model.SyntaxTree, Is.SameAs(tree));
        Assert.That(SymbolEqualityComparer.Default.Equals(model.GetDeclaredSymbol(ownedClass), actualInterface), Is.True);
        Assert.That(SymbolEqualityComparer.Default.Equals(model.GetDeclaredSymbol(ownedClass), actualClass), Is.False);
        Assert.That(model.GetDeclaredSymbol(ownedRead), Is.Null);
        TestContext.WriteLine(string.Join(Environment.NewLine, compilation.GetDiagnostics()));

        var inventory = new ClaimManifestBuilder(compilation).BuildPotentialCallShadow();
        Assert.That(inventory.Owners, Is.Empty);
        Assert.That(inventory.Gaps, Has.Length.EqualTo(1));
        var gap = inventory.Gaps.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(gap.Reason, Is.EqualTo("MissingSymbol"));
            Assert.That(gap.TreeOrdinal, Is.Zero);
            Assert.That(gap.Start, Is.EqualTo(ownedRead.SpanStart));
            Assert.That(gap.Length, Is.EqualTo(ownedRead.Span.Length));
        }
    }

    [Test]
    public async Task OrdinaryOwnedSourceRetainsItsSdkSymbolAndClrResult()
    {
        var compilation = TestCompilation.Create("SdkFactoryInventoryOrdinaryControl",
            "public static class Target { public static int Read() => 7; }");
        TestCompilation.AssertNoErrors(compilation);
        var tree = compilation.SyntaxTrees.Single();
        var root = await tree.GetRootAsync();
        var declaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var actualRead = compilation.GetTypeByMetadataName("Target")!.GetMembers("Read").OfType<IMethodSymbol>().Single();
        Assert.That(await actualRead.DeclaringSyntaxReferences.Single().GetSyntaxAsync(), Is.SameAs(declaration));
        Assert.That(SymbolEqualityComparer.Default.Equals(compilation.GetSemanticModel(tree).GetDeclaredSymbol(declaration), actualRead), Is.True);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var runtime = new System.Runtime.Loader.AssemblyLoadContext("SdkFactoryInventoryOrdinaryControl", isCollectible: true);
        try
        {
            image.Position = 0;
            var read = runtime.LoadFromStream(image).GetType("Target")!.GetMethod("Read")!;
            Assert.That(read.Invoke(null, null), Is.EqualTo(7));
        }
        finally { runtime.Unload(); }
        var inventory = new ClaimManifestBuilder(compilation).BuildPotentialCallShadow();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(inventory.Gaps, Is.Empty);
            Assert.That(inventory.Owners, Has.Length.EqualTo(1));
            Assert.That(SymbolEqualityComparer.Default.Equals(inventory.Owners.Single().Method, actualRead), Is.True);
            Assert.That(inventory.Owners.Single().Declaration, Is.SameAs(declaration));
            Assert.That(inventory.Owners.Single().DiscoveryComplete, Is.True);
        }
    }
}