using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Attributes;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ContractApiOwnedMetadataLifetimeTests
{
    private const string Source = """
        using SharpProof.Attributes;
        public static class Subject {
            public static int Target(int value) {
                Contract.Requires(value >= 0);
                return value;
            }
        }
        """;

    [Test]
    public void LiveOwnedImageResolvesTheShippedContractApi()
    {
        using var owner = AssemblyMetadata.CreateFromImage(
            File.ReadAllBytes(typeof(Contract).Assembly.Location));
        var reference = owner.GetReference(filePath: null);
        Assert.That(reference.FilePath, Is.Null);
        var compilation = CreateAndEmit(reference);
        var candidate = ReadAndAssertPrefix(compilation, reference);

        var resolver = ContractApiIdentityResolver.ForCompilation(compilation);

        Assert.That(resolver.Contract, Is.SameAs(candidate));
        Assert.That(resolver.Result, Is.Not.Null);
        Assert.That(resolver.Old, Is.Not.Null);
        Assert.That(resolver.UnreadableContractApiReason, Is.Null);
    }

    [Test]
    public void DisposedOwnedImageReportsTheUnreadableContractApi()
    {
        using var owner = AssemblyMetadata.CreateFromImage(
            File.ReadAllBytes(typeof(Contract).Assembly.Location));
        var reference = owner.GetReference(filePath: null);
        Assert.That(reference.FilePath, Is.Null);
        var compilation = CreateAndEmit(reference);
        var before = ReadAndAssertPrefix(compilation, reference);

        owner.Dispose();
        var disposed = Assert.Throws<ObjectDisposedException>((Action)(() =>
        {
            _ = owner.GetModules();
        }));
        var after = ReadAndAssertPrefix(compilation, reference);
        Assert.That(after, Is.SameAs(before));

        var resolver = ContractApiIdentityResolver.ForCompilation(compilation);

        Assert.That(resolver.Contract, Is.Null);
        Assert.That(resolver.Result, Is.Null);
        Assert.That(resolver.Old, Is.Null);
        Assert.That(resolver.UnreadableContractApiReason, Is.EqualTo(disposed!.Message));
    }

    private static CSharpCompilation CreateAndEmit(PortableExecutableReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var options = new CSharpParseOptions(LanguageVersion.CSharp12,
            preprocessorSymbols: [Contract.ConditionalSymbol]);
        var tree = CSharpSyntaxTree.ParseText(Source, options);
        var compilation = CSharpCompilation.Create(
            "OwnedMetadataLifetime",
            [tree],
            TestMetadataReferences.WithoutSharpProof.Add(reference),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(
            diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True,
            string.Join(Environment.NewLine, emitted.Diagnostics));
        Assert.That(image.Length, Is.GreaterThan(0));
        return compilation;
    }

    private static INamedTypeSymbol ReadAndAssertPrefix(
        CSharpCompilation compilation,
        PortableExecutableReference reference)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(reference);
        Assert.That(compilation.GetTypeByMetadataName("System.Attribute"), Is.Not.Null);
        Assert.That(compilation.GetTypeByMetadataName("System.Diagnostics.ConditionalAttribute"),
            Is.Not.Null);
        var candidate = compilation.GetTypeByMetadataName("SharpProof.Attributes.Contract");
        Assert.That(candidate, Is.Not.Null);
        Assert.That(candidate!.ContainingType, Is.Null);
        Assert.That(candidate.MetadataName, Is.EqualTo("Contract"));
        Assert.That(candidate.ContainingNamespace.Name, Is.EqualTo("Attributes"));
        Assert.That(candidate.ContainingNamespace.ContainingNamespace.Name, Is.EqualTo("SharpProof"));
        Assert.That(candidate.ContainingNamespace.ContainingNamespace.ContainingNamespace.IsGlobalNamespace,
            Is.True);
        Assert.That(candidate.Locations.Any(location => location is null ? throw new System.ArgumentNullException(nameof(location)) : (location.IsInSource)), Is.False);
        var assembly = candidate.ContainingAssembly;
        Assert.That(assembly.Identity.Name, Is.EqualTo("SharpProof.Attributes"));
        Assert.That(assembly.Identity.CultureName, Is.Null.Or.Empty);
        Assert.That(assembly.Identity.Version, Is.Not.Null);
        Assert.That(compilation.References.Any(item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item is CompilationReference)), Is.False);
        Assert.That(compilation.GetAssemblyOrModuleSymbol(reference), Is.SameAs(assembly));
        return candidate;
    }
}
