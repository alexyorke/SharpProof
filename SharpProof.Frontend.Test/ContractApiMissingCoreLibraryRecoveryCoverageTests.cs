using System.Collections.Immutable;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ContractApiMissingCoreLibraryRecoveryCoverageTests
{
    private const string SubjectSource = "public static class Subject { public static bool Target(bool value) => value; }\n";

    [Test]
    public void MissingCoreLibraryRejectsAuthenticContractAfterPrimitiveMetadataAdmission()
    {
        var (compilation, contract) = CreateSubject(includePlatform: false);
        Assert.That(compilation.References.Count(), Is.EqualTo(1));
        Assert.That(compilation.GetTypeByMetadataName("System.Diagnostics.ConditionalAttribute"), Is.Null);
        foreach (var special in new[] { SpecialType.System_String, SpecialType.System_Boolean, SpecialType.System_Void })
        {
            var missing = compilation.GetSpecialType(special);
            Assert.That(missing.SpecialType, Is.EqualTo(special));
            Assert.That(missing.TypeKind, Is.EqualTo(TypeKind.Error));
        }

        AssertPrimitiveMetadataShape(contract, TypeKind.Error);
        var diagnostics = compilation.GetDiagnostics();
        Assert.That(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Id), Does.Contain("CS0518"));
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.False);
        Assert.That(emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Id), Does.Contain("CS0518"));

        // This is compiler recovery evidence. The missing runtime references
        // prevent CLR execution; the genuine metadata primitives still reach
        // the conditional-attribute rejection in the production resolver.
        var resolver = ContractApiIdentityResolver.ForCompilation(compilation);
        Assert.That(resolver.Contract, Is.Null);
        Assert.That(resolver.Result, Is.Null);
        Assert.That(resolver.Old, Is.Null);
    }

    [Test]
    public void FullyReferencedAuthenticContractResolvesAndEmitsTheSameSubject()
    {
        var (compilation, contract) = CreateSubject(includePlatform: true);
        Assert.That(compilation.GetTypeByMetadataName("System.Diagnostics.ConditionalAttribute"), Is.Not.Null);
        AssertPrimitiveMetadataShape(contract, TypeKind.Struct);
        Assert.That(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error), Is.Empty);
        var resolver = ContractApiIdentityResolver.ForCompilation(compilation);
        Assert.That(SymbolEqualityComparer.Default.Equals(resolver.Contract, contract), Is.True);
        Assert.That(resolver.Result, Is.Not.Null);
        Assert.That(resolver.Old, Is.Not.Null);

        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var runtime = new AssemblyLoadContext("ContractMissingCoreLibrarySubject", isCollectible: true);
        try
        {
            image.Position = 0;
            var target = runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Target")!;
            foreach (var value in new[] { false, true })
            {
                var actual = target.Invoke(null, [value]);
                Assert.That(actual, Is.TypeOf<bool>());
                Assert.That(actual, Is.EqualTo(value));
            }
        }
        finally
        {
            runtime.Unload();
        }
    }

    private static (CSharpCompilation Compilation, INamedTypeSymbol Contract) CreateSubject(bool includePlatform)
    {
        var assembly = typeof(SharpProof.Attributes.Contract).Assembly;
        var reference = MetadataReference.CreateFromFile(assembly.Location);
        Assert.That(reference.FilePath, Is.EqualTo(assembly.Location));
        var metadata = (AssemblyMetadata)reference.GetMetadata();
        Assert.That(metadata.GetModules(), Has.Length.EqualTo(1));
        Assert.That(metadata.GetModules()[0].GetModuleVersionId(), Is.EqualTo(assembly.ManifestModule.ModuleVersionId));
        ImmutableArray<MetadataReference> references = includePlatform
            ? TestMetadataReferences.WithoutSharpProof.Add(reference) : [reference];
        var tree = CSharpSyntaxTree.ParseText(SubjectSource, new CSharpParseOptions(LanguageVersion.CSharp12),
            "contract-missing-core-library.cs");
        var compilation = CSharpCompilation.Create("ContractMissingCoreLibrarySubject", [tree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        var contract = compilation.GetTypeByMetadataName("SharpProof.Attributes.Contract")!;
        Assert.That(contract, Is.Not.Null);
        Assert.That(contract.TypeKind, Is.EqualTo(TypeKind.Class));
        Assert.That(contract.IsStatic, Is.True);
        Assert.That(contract.Arity, Is.Zero);
        Assert.That(contract.DeclaringSyntaxReferences, Is.Empty);
        Assert.That(contract.Locations.All(location => location.IsInMetadata), Is.True);
        Assert.That(SymbolEqualityComparer.Default.Equals(
            compilation.GetAssemblyOrModuleSymbol(reference), contract.ContainingAssembly), Is.True);
        Assert.That(contract.ContainingAssembly.Identity.Name, Is.EqualTo(assembly.GetName().Name));
        Assert.That(contract.ContainingAssembly.Identity.Version, Is.EqualTo(assembly.GetName().Version));
        Assert.That(contract.ContainingAssembly.Identity.CultureName, Is.Null.Or.Empty);

        var model = compilation.GetSemanticModel(tree);
        var declaration = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var target = (IMethodSymbol)model.GetDeclaredSymbol(declaration)!;
        Assert.That(model.Compilation, Is.SameAs(compilation));
        Assert.That(target.DeclaringSyntaxReferences.Single().SyntaxTree, Is.SameAs(tree));
        Assert.That(SymbolEqualityComparer.Default.Equals(target.ContainingAssembly, compilation.Assembly), Is.True);
        Assert.That(target.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Boolean));
        Assert.That(target.Parameters.Single().Type.SpecialType, Is.EqualTo(SpecialType.System_Boolean));
        return (compilation, contract);
    }

    private static void AssertPrimitiveMetadataShape(INamedTypeSymbol contract, TypeKind booleanKind)
    {
        var symbol = contract.GetMembers("ConditionalSymbol").OfType<IFieldSymbol>().Single();
        Assert.That(symbol.DeclaredAccessibility, Is.EqualTo(Accessibility.Public));
        Assert.That(symbol.IsStatic, Is.True);
        Assert.That(symbol.IsConst, Is.True);
        Assert.That(symbol.HasConstantValue, Is.True);
        Assert.That(symbol.Type.SpecialType, Is.EqualTo(SpecialType.System_String));
        Assert.That(symbol.Type.TypeKind, Is.EqualTo(booleanKind == TypeKind.Error ? TypeKind.Error : TypeKind.Class));
        Assert.That(symbol.ConstantValue, Is.EqualTo(SharpProof.Attributes.Contract.ConditionalSymbol));
        var requires = contract.GetMembers("Requires").OfType<IMethodSymbol>().Single();
        Assert.That(requires.MethodKind, Is.EqualTo(MethodKind.Ordinary));
        Assert.That(requires.DeclaredAccessibility, Is.EqualTo(Accessibility.Public));
        Assert.That(requires.IsStatic, Is.True);
        Assert.That(requires.Arity, Is.Zero);
        Assert.That(requires.ReturnsVoid, Is.True);
        Assert.That(requires.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Void));
        Assert.That(requires.ReturnType.TypeKind, Is.EqualTo(booleanKind));
        var parameter = requires.Parameters.Single();
        Assert.That(parameter.RefKind, Is.EqualTo(RefKind.None));
        Assert.That(parameter.ScopedKind, Is.EqualTo(ScopedKind.None));
        Assert.That(parameter.IsParams, Is.False);
        Assert.That(parameter.IsOptional, Is.False);
        Assert.That(parameter.Type.SpecialType, Is.EqualTo(SpecialType.System_Boolean));
        Assert.That(parameter.Type.TypeKind, Is.EqualTo(booleanKind));
    }
}
