using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class AuthenticImplicitContractReferenceApiTests
{
    [Test]
    public void ActualImplicitDependencyIsRejectedAndExplicitReferenceResolves()
    {
        // Raw resolver API boundary only: CompilerCompilationCapture rejects
        // ResolveMissingAssemblies. Neither consumer is enrolled as proof input.
        var loaded = typeof(SharpProof.Attributes.Contract).Assembly;
        var attributesPath = loaded.Location;
        var attributesBytes = File.ReadAllBytes(attributesPath).ToImmutableArray();
        var attributesHash = Convert.ToHexString(SHA256.HashData(attributesBytes.AsSpan()));
        using (var pe = new PEReader(attributesBytes))
        {
            var reader = pe.GetMetadataReader();
            Assert.That(reader.GetGuid(reader.GetModuleDefinition().Mvid),
                Is.EqualTo(loaded.ManifestModule.ModuleVersionId));
        }
        var attributesReference = MetadataReference.CreateFromFile(attributesPath);
        Assert.That(attributesReference.FilePath, Is.EqualTo(attributesPath));
        using var temporary = new TempDirectory("SharpProofImplicitContractReference-");
        var libraryPath = Path.Combine(temporary.FullName, "ImplicitPayloadLibrary.dll");
        var library = CSharpCompilation.Create("ImplicitPayloadLibrary",
            [CSharpSyntaxTree.ParseText("""
                using SharpProof.Attributes;
                public static class Library {
                    public static int Identity([Positive] int value) => value;
                }
                """, path: "ImplicitPayloadLibrary.cs")],
            TestMetadataReferences.WithoutSharpProof.Add(attributesReference),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release));
        RequireNoErrors(library);
        var attributesOwner = (IAssemblySymbol)library.GetAssemblyOrModuleSymbol(attributesReference)!;
        Assert.That(attributesOwner, Is.Not.Null);
        Assert.That(attributesOwner.Identity.Name, Is.Not.EqualTo(library.AssemblyName));
        using (var output = File.Create(libraryPath))
        {
            var emitted = library.Emit(output);
            Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        }
        var libraryReference = MetadataReference.CreateFromFile(libraryPath);
        var missing = new AuthenticMissingReferenceResolver(attributesOwner.Identity, attributesReference);
        var implicitConsumer = CreateConsumer([libraryReference], missing);
        RequireNoErrors(implicitConsumer);
        RequireEmit(implicitConsumer);
        Assert.That(implicitConsumer.Options.MetadataReferenceResolver, Is.SameAs(missing));
        var candidate = implicitConsumer.GetTypeByMetadataName(ContractApiMetadata.Contract);
        Assert.That(candidate, Is.Not.Null, "The actual SDK must expose its implicitly resolved declaration.");
        Assert.That(candidate!.TypeKind, Is.Not.EqualTo(TypeKind.Error));
        Assert.That(candidate.Locations.Any(static location => location is null ? throw new System.ArgumentNullException(nameof(location)) : (location.IsInSource)), Is.False);
        Assert.That(candidate.Locations.Any(static location => location is null ? throw new System.ArgumentNullException(nameof(location)) : (location.IsInMetadata)), Is.True);
        Assert.That(candidate.ContainingAssembly.Identity, Is.EqualTo(attributesOwner.Identity));
        Assert.That(SymbolEqualityComparer.Default.Equals(candidate.ContainingAssembly,
            implicitConsumer.GetAssemblyOrModuleSymbol(attributesReference)), Is.True);
        Assert.That(implicitConsumer.References.Any(reference => reference is null ? throw new System.ArgumentNullException(nameof(reference)) : (ReferenceEquals(reference, attributesReference))), Is.False);
        Assert.That(implicitConsumer.References.Any(reference =>
            reference is null ? throw new System.ArgumentNullException(nameof(reference)) : (SymbolEqualityComparer.Default.Equals(candidate.ContainingAssembly,
                implicitConsumer.GetAssemblyOrModuleSymbol(reference)))), Is.False);
        var requested = missing.Requests.Where(request => request.Identity.Equals(attributesOwner.Identity)).ToImmutableArray();
        Assert.That(requested, Is.Not.Empty);
        foreach (var request in requested)
        {
            Assert.That(request.Definition, Is.SameAs(libraryReference));
            Assert.That(request.Returned, Is.SameAs(attributesReference));
            Assert.That(request.Returned!.FilePath, Is.EqualTo(attributesPath));
            TestContext.WriteLine($"Actual SDK missing dependency: {request.Identity}; definition={request.Definition.Display}; returned={request.Returned.FilePath}");
        }
        TestContext.WriteLine($"Authentic Attributes MVID={loaded.ManifestModule.ModuleVersionId}; SHA256={attributesHash}; bytes={attributesBytes.Length}");
        var rejected = ContractApiIdentityResolver.ForCompilation(implicitConsumer);
        Assert.That(rejected.Contract, Is.Null);
        Assert.That(rejected.Result, Is.Null);
        Assert.That(rejected.Old, Is.Null);
        Assert.That(rejected.ResolveAttribute(ContractApiMetadata.Positive), Is.Null);
        Assert.That(rejected.UnreadableContractApiReason, Is.Null);

        var explicitConsumer = CreateConsumer([libraryReference, attributesReference], null);
        RequireNoErrors(explicitConsumer);
        RequireEmit(explicitConsumer);
        var explicitCandidate = explicitConsumer.GetTypeByMetadataName(ContractApiMetadata.Contract);
        Assert.That(explicitCandidate, Is.Not.Null);
        Assert.That(SymbolEqualityComparer.Default.Equals(explicitCandidate!.ContainingAssembly,
            explicitConsumer.GetAssemblyOrModuleSymbol(attributesReference)), Is.True);
        Assert.That(explicitConsumer.References.Any(reference => reference is null ? throw new System.ArgumentNullException(nameof(reference)) : (ReferenceEquals(reference, attributesReference))), Is.True);
        var accepted = ContractApiIdentityResolver.ForCompilation(explicitConsumer);
        Assert.That(accepted.Contract, Is.SameAs(explicitCandidate));
        Assert.That(accepted.Result, Is.Not.Null);
        Assert.That(accepted.Old, Is.Not.Null);
        Assert.That(accepted.ResolveAttribute(ContractApiMetadata.Positive), Is.Not.Null);
        Assert.That(accepted.UnreadableContractApiReason, Is.Null);
        Assert.That(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(attributesPath))), Is.EqualTo(attributesHash));
    }

    private static CSharpCompilation CreateConsumer(ImmutableArray<MetadataReference> references,
        MetadataReferenceResolver? resolver)
    {

        if (references.IsDefault)
        {
            throw new System.ArgumentException("Array must be initialized.", nameof(references));
        }
        return CSharpCompilation.Create("ImplicitPayloadConsumer",
            [CSharpSyntaxTree.ParseText("""
                public static class Subject {
                    public static int Target(int value) => Library.Identity(value);
                }
                """, path: "ImplicitPayloadConsumer.cs")],
            TestMetadataReferences.WithoutSharpProof.AddRange(references),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release, metadataReferenceResolver: resolver));
    }

    private static void RequireNoErrors(CSharpCompilation compilation)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        var errors = compilation.GetDiagnostics().Where(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)).ToImmutableArray();
        Assert.That(errors, Is.Empty, string.Join(Environment.NewLine, errors.Select(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.ToString()))));
    }

    private static void RequireEmit(CSharpCompilation compilation)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        using var output = new MemoryStream();
        var emitted = compilation.Emit(output);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
    }

    private sealed class AuthenticMissingReferenceResolver(AssemblyIdentity identity,
        PortableExecutableReference reference) : MetadataReferenceResolver
    {
        private readonly ImmutableArray<(MetadataReference Definition, AssemblyIdentity Identity, PortableExecutableReference? Returned)>.Builder _requests =
            reference is null ? throw new System.ArgumentNullException(nameof(reference)) : identity is null ? throw new System.ArgumentNullException(nameof(identity)) :
            ImmutableArray.CreateBuilder<(MetadataReference Definition, AssemblyIdentity Identity, PortableExecutableReference? Returned)>();
        internal ImmutableArray<(MetadataReference Definition, AssemblyIdentity Identity, PortableExecutableReference? Returned)> Requests => _requests.ToImmutable();
        public override bool ResolveMissingAssemblies => true;

        public override PortableExecutableReference? ResolveMissingAssembly(MetadataReference definition,
            AssemblyIdentity referenceIdentity)
        {
            System.ArgumentNullException.ThrowIfNull(definition);
            System.ArgumentNullException.ThrowIfNull(referenceIdentity);
            var result = referenceIdentity.Equals(identity) ? reference : null;
            _requests.Add((definition, referenceIdentity, result));
            return result;
        }

        public override ImmutableArray<PortableExecutableReference> ResolveReference(string reference,
            string? baseFilePath, MetadataReferenceProperties properties /* SPINT001: all values valid - all constructed reference properties, including default assembly properties, are valid */)
        { System.ArgumentNullException.ThrowIfNull(reference, nameof(reference)); return []; }

        public override bool Equals(object? other)
        { return ReferenceEquals(this, other); }
        public override int GetHashCode()
        { return RuntimeHelpers.GetHashCode(this); }
    }
}
