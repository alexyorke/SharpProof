using System.Collections.Immutable;
using System.Reflection.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Attributes;
namespace SharpProof.Frontend.Test;
[TestFixture]
public sealed class ReferencedTypeSymbolsTests
{
    [Test]
    public void ContractAttributePrefilterPreservesCompanionsAndPrunesUnrelatedReferences()
    {
        var companion = EmitReference(
            """
            using SharpProof.Attributes;
            namespace CompanionFixture {
                public interface Service { int Read(int value); }
                [ContractFor(typeof(Service))]
                public static class ServiceContracts {
                    public static int Read(Service receiver, int value) => value;
                }
                [ContractFor(typeof(Service))]
                public sealed class MalformedContracts { }
            }
            """,
            "CompanionFixture");
        var unrelated = EmitReference(
            """
            namespace UnrelatedFixture {
                public sealed class UnrelatedType { }
            }
            """,
            "UnrelatedFixture");
        var compilation = CreateCompilation(
            "public static class SourceType { }",
            companion,
            unrelated);
        var attribute = compilation.GetTypeByMetadataName(
            "SharpProof.Attributes.ContractForAttribute");
        Assert.That(attribute, Is.Not.Null);
        var all = ReferencedTypeSymbols.GetAll(compilation)
            .ToImmutableArray();
        var filtered = ReferencedTypeSymbols.GetAll(compilation, attribute!)
            .ToImmutableArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                all.Any(type => type.MetadataName == "UnrelatedType"),
                Is.True);
            Assert.That(
                filtered.Any(type => type.MetadataName == "UnrelatedType"),
                Is.False);
            Assert.That(
                filtered.Any(type => type.MetadataName == "ServiceContracts"),
                Is.True);
            Assert.That(
                filtered.Any(type => type.MetadataName == "MalformedContracts"),
                Is.True);
            Assert.That(
                filtered.Any(type => type.MetadataName == "SourceType"),
                Is.True);
            Assert.That(filtered.Length, Is.LessThan(all.Length));
        }
    }
    [Test]
    public void ContractAttributePrefilterScansEveryLinkedModule()
    {
        var moduleImage = EmitModuleImage(
            """
            using SharpProof.Attributes;
            namespace LinkedFixture {
                public interface Service { int Read(int value); }
                [ContractFor(typeof(Service))]
                public static class ServiceContracts {
                    public static int Read(Service receiver, int value) => value;
                }
            }
            """,
            "LinkedFixture");
        var moduleReference = MetadataReference.CreateFromImage(
            moduleImage,
            new MetadataReferenceProperties(MetadataImageKind.Module),
            filePath: "LinkedFixture.netmodule");
        var manifestImage = EmitManifestImage(moduleReference);
        using var assemblyMetadata = AssemblyMetadata.Create(
            ModuleMetadata.CreateFromImage(manifestImage),
            ModuleMetadata.CreateFromImage(moduleImage));
        var host = assemblyMetadata.GetReference(
            filePath: "LinkedFixture.dll");
        var compilation = CreateCompilation(
            "public static class SourceType { }",
            host);
        var attribute = compilation.GetTypeByMetadataName(
            "SharpProof.Attributes.ContractForAttribute");
        Assert.That(attribute, Is.Not.Null);
        var filtered = ReferencedTypeSymbols.GetAll(compilation, attribute!)
            .ToImmutableArray();
        Assert.That(
            filtered.Any(type => type.MetadataName == "ServiceContracts"),
            Is.True,
            string.Join(
                ", ",
                filtered.Select(static type => type.MetadataName)));
    }
    [Test]
    public void ContractAttributePrefilterPreservesTypeForwardedCompanions()
    {
        var implementation = EmitReference(
            """
            using SharpProof.Attributes;
            namespace ForwardedFixture {
                public interface Service { int Read(int value); }
                [ContractFor(typeof(Service))]
                public static class ServiceContracts {
                    public static int Read(Service receiver, int value) => value;
                }
            }
            """,
            "ForwardedImplementation");
        var facade = EmitReference(
            """
            using System.Runtime.CompilerServices;
            using ForwardedFixture;
            [assembly: TypeForwardedTo(typeof(Service))]
            [assembly: TypeForwardedTo(typeof(ServiceContracts))]
            """,
            "ForwardedFacade",
            implementation);
        var compilation = CreateCompilation(
            "public static class SourceType { }",
            facade,
            implementation);
        var attribute = compilation.GetTypeByMetadataName(
            "SharpProof.Attributes.ContractForAttribute");
        Assert.That(attribute, Is.Not.Null);
        var all = ReferencedTypeSymbols.GetAll(compilation)
            .ToImmutableArray();
        var filtered = ReferencedTypeSymbols.GetAll(compilation, attribute!)
            .ToImmutableArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                all.Any(type => type.MetadataName == "ServiceContracts"),
                Is.True);
            Assert.That(
                filtered.Any(type => type.MetadataName == "ServiceContracts"),
                Is.True);
        }
    }
    private static CSharpCompilation CreateCompilation(
        string source,
        params PortableExecutableReference[] additionalReferences)
    {
        var references = CreatePlatformReferences()
            .Add(MetadataReference.CreateFromFile(
                typeof(ContractForAttribute).Assembly.Location))
            .AddRange(additionalReferences);
        var compilation = CSharpCompilation.Create(
            "ReferencedTypesConsumer",
            [CSharpSyntaxTree.ParseText(
                source,
                CSharpParseOptions.Default.WithLanguageVersion(
                    LanguageVersion.CSharp12))],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        AssertNoErrors(compilation);
        return compilation;
    }
    private static PortableExecutableReference EmitReference(
        string source,
        string assemblyName,
        params MetadataReference[] additionalReferences)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(
                source,
                CSharpParseOptions.Default.WithLanguageVersion(
                    LanguageVersion.CSharp12))],
            CreatePlatformReferences()
                .Add(MetadataReference.CreateFromFile(
                    typeof(ContractForAttribute).Assembly.Location))
                .AddRange(additionalReferences),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        AssertNoErrors(compilation);
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.That(
            result.Success,
            Is.True,
            string.Join(
                Environment.NewLine,
                result.Diagnostics.Select(static diagnostic =>
                    diagnostic.ToString())));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }
    private static byte[] EmitModuleImage(
        string source,
        string moduleName)
    {
        var compilation = CSharpCompilation.Create(
            moduleName,
            [CSharpSyntaxTree.ParseText(
                source,
                CSharpParseOptions.Default.WithLanguageVersion(
                    LanguageVersion.CSharp12))],
            CreatePlatformReferences()
                .Add(MetadataReference.CreateFromFile(
                    typeof(ContractForAttribute).Assembly.Location)),
            new CSharpCompilationOptions(
                OutputKind.NetModule));
        AssertNoErrors(compilation);
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.That(
            result.Success,
            Is.True,
            string.Join(
                Environment.NewLine,
                result.Diagnostics.Select(static diagnostic =>
                    diagnostic.ToString())));
        return stream.ToArray();
    }
    private static byte[] EmitManifestImage(
        MetadataReference moduleReference)
    {
        var compilation = CSharpCompilation.Create(
            "LinkedFixture",
            [CSharpSyntaxTree.ParseText(
                "public sealed class ManifestType { }",
                CSharpParseOptions.Default.WithLanguageVersion(
                    LanguageVersion.CSharp12))],
            CreatePlatformReferences()
                .Add(MetadataReference.CreateFromFile(
                    typeof(ContractForAttribute).Assembly.Location))
                .Add(moduleReference),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        AssertNoErrors(compilation);
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.That(
            result.Success,
            Is.True,
            string.Join(
                Environment.NewLine,
                result.Diagnostics.Select(static diagnostic =>
                    diagnostic.ToString())));
        return stream.ToArray();
    }
    private static ImmutableArray<MetadataReference> CreatePlatformReferences()
    {
        var paths = (string?)AppContext.GetData(
                "TRUSTED_PLATFORM_ASSEMBLIES") ??
            throw new InvalidOperationException(
                "Trusted platform assemblies are unavailable.");
        return [.. paths
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path))];
    }
    private static void AssertNoErrors(Compilation compilation)
    {
        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
        Assert.That(
            errors,
            Is.Empty,
            string.Join(
                Environment.NewLine,
                errors.Select(static diagnostic =>
                    diagnostic.ToString())));
    }
}
