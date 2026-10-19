using SharpProof.Ir;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class CompilerMetadataResolutionTests
{
    [Test]
    public void ReferenceResolutionCachesTheMatchedModuleAndRejectsMissingIdentities()
    {
        var compilation = AnalyzerTestHost.CreateCompilation("internal class Subject { }", []);
        var reference = compilation.References
            .OfType<PortableExecutableReference>()
            .First(candidate => candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (candidate.FilePath != null &&
                compilation.GetAssemblyOrModuleSymbol(candidate) is IAssemblySymbol));
        var assembly = (IAssemblySymbol)compilation.GetAssemblyOrModuleSymbol(reference)!;
        var module = ((AssemblyMetadata)reference.GetMetadata()).GetModules()[0];
        var moduleName = CompilerCompilationCapture.ReadModuleName(
            module.GetMetadataReader());
        var context = new CompilerMetadataResolution.MetadataResolutionContext(
            compilation);

        var resolved = (IAssemblySymbol)context.Resolve(reference)!;
        Assert.That(resolved.Identity, Is.EqualTo(assembly.Identity));
        Assert.That(context.Resolve(reference), Is.SameAs(resolved));
        Assert.That(context.TryFindReference(assembly.Identity, moduleName,
            TestContext.CurrentContext.CancellationToken, out var foundReference, out var foundModule,
            out var foundPath), Is.True);
        using (foundModule)
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(foundReference, Is.SameAs(reference));
                Assert.That(CompilerCompilationCapture.ReadModuleName(
                    foundModule.GetMetadataReader()), Is.EqualTo(moduleName));
                Assert.That(foundPath, Is.EqualTo(Path.GetFullPath(reference.FilePath!)));
            }
            Assert.That(context.TryFindReference(assembly.Identity, moduleName,
                TestContext.CurrentContext.CancellationToken, out _, out _, out _), Is.True);
        }
        Assert.That(context.TryFindReference(new AssemblyIdentity("MissingReference"),
            moduleName, TestContext.CurrentContext.CancellationToken, out _, out _, out _), Is.False);
        Assert.That(context.TryFindReference(assembly.Identity,
            "Missing.netmodule", TestContext.CurrentContext.CancellationToken, out _, out _, out _),
            Is.False);
    }

    [Test]
    public void MethodTokenValidationAndManagedBodyClassificationUseFinalMetadata()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            using System.Runtime.InteropServices;
            public abstract class Subject
            {
                public int Managed() => 1;
                public abstract int Abstract();
                [DllImport("kernel32")]
                public static extern int Native();
            }
            """,
            []);
        using var image = new MemoryStream();
        var emit = compilation.Emit(image);
        Assert.That(emit.Success, Is.True, string.Join("\n", emit.Diagnostics));
        image.Position = 0;
        using var pe = new PEReader(image);
        var reader = pe.GetMetadataReader();
        var definitions = ImmutableDictionaryCapture.Create(reader.MethodDefinitions, handle => ((handle.IsNil) ? throw new System.ArgumentException("Value must be initialized.", nameof(handle)) : (reader.GetString(reader.GetMethodDefinition(handle).Name))), handle => handle);

        foreach (var (name, hasManagedBody) in ImmutableArray.Create(("Managed", true), ("Abstract", false), ("Native", false)))
        {
            var token = MetadataTokens.GetToken(definitions[name]);
            Assert.That(CompilerMetadataResolution.TryGetMethodDefinition(
                reader, token, out var handle, out var definition), Is.True);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(handle, Is.EqualTo(definitions[name]));
                Assert.That(CompilerMetadataResolution.HasManagedIlBody(definition),
                    Is.EqualTo(hasManagedBody), name);
            }
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(CompilerMetadataResolution.TryGetMethodDefinition(
                reader, MetadataTokens.GetToken(MetadataTokens.TypeDefinitionHandle(1)),
                out _, out _), Is.False);
            Assert.That(CompilerMetadataResolution.TryGetMethodDefinition(
                reader, MetadataTokens.GetToken(MetadataTokens.MethodDefinitionHandle(0)),
                out _, out _), Is.False);
            Assert.That(CompilerMetadataResolution.TryGetMethodDefinition(
                reader, MetadataTokens.GetToken(MetadataTokens.MethodDefinitionHandle(
                    reader.MethodDefinitions.Count + 1)), out _, out _), Is.False);
        }
    }

    [Test]
    public void ReferenceAssemblyMarkerRequiresTheExactCompilerServicesNamespace()
    {
        var marked = AnalyzerTestHost.CreateCompilation(
            """
            [assembly: System.Runtime.CompilerServices.ReferenceAssembly]
            namespace System.Runtime.CompilerServices { public class Marker { } }
            """,
            []);
        var ordinary = AnalyzerTestHost.CreateCompilation(
            "namespace System.Runtime.CompilerServices { public class Marker { } }",
            []);
        var nameSpace = marked.GetTypeByMetadataName(
            "System.Runtime.CompilerServices.Marker")!.ContainingNamespace;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(CompilerMetadataResolution.IsReferenceAssembly(marked.Assembly),
                Is.True);
            Assert.That(CompilerMetadataResolution.IsReferenceAssembly(ordinary.Assembly),
                Is.False);
            Assert.That(CompilerMetadataResolution.HasNamespace(nameSpace,
                "System", "Runtime", "CompilerServices"), Is.True);
            Assert.That(CompilerMetadataResolution.HasNamespace(nameSpace,
                "System", "Runtime"), Is.False);
            Assert.That(CompilerMetadataResolution.HasNamespace(nameSpace,
                "System", "Other", "CompilerServices"), Is.False);
        }
    }
}
