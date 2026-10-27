using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;
using SharpProof.Contracts;
using SharpProof.Frontend;
using SharpProof.Logging;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerParameterAttributeReferenceRejectionTests
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp12,
        preprocessorSymbols: [Contract.ConditionalSymbol]);

    private const string OrdinaryAttributeSource = """
        using System;
        namespace SharpProof.Attributes
        {
            [AttributeUsage(AttributeTargets.Parameter)]
            public sealed class PositiveAttribute : Attribute { }
        }
        """ + "\n";

    private const string RetargetableAttributeSource = """
        using System;
        using System.Reflection;
        [assembly: AssemblyFlags(AssemblyNameFlags.Retargetable)]
        namespace SharpProof.Attributes
        {
            [AttributeUsage(AttributeTargets.Parameter)]
            public sealed class PositiveAttribute : Attribute { }
        }
        """ + "\n";

    private const string LibrarySource = """
        public static class Library
        {
            public static int Target([SharpProof.Attributes.Positive] int value) => value;
        }
        """ + "\n";

    private const string ConsumerSource = """
        public static class Subject
        {
            public static int Target(int value) => Library.Target(value);
        }
        """ + "\n";

    [Test]
    public Task ShippedParameterAttributeDependencyIsAdmitted()
    {
        return RunAsync(DependencyKind.Shipped);
    }

    [Test]
    public Task OmittedOrdinaryAttributeDependencyIsRejected()
    {
        return RunAsync(DependencyKind.Ordinary);
    }

    [Test]
    public Task OmittedRetargetableAttributeDependencyIsRejected()
    {
        return RunAsync(DependencyKind.Retargetable);
    }

    private enum DependencyKind { Shipped, Ordinary, Retargetable }

    private static async Task RunAsync(DependencyKind kind)
    {
        using var directory = new TempDirectory("sharpproof-parameter-reference-");
        var runtime = new AssemblyLoadContext("parameter-reference-" + kind, isCollectible: true);
        try
        {
            var shipped = kind == DependencyKind.Shipped;
            var retargetable = kind == DependencyKind.Retargetable;
            var foreignName = "OrdinaryParameterAttributes_" + kind;
            ImmutableArray<byte>? attributeImage = null;
            string? attributePath = null;
            AssemblyIdentity? foreignIdentity = null;
            var libraryReferences = TestMetadataReferences.WithSharpProof;
            if (!shipped)
            {
                var attributes = Create(foreignName,
                    retargetable ? RetargetableAttributeSource : OrdinaryAttributeSource,
                    TestMetadataReferences.WithoutSharpProof);
                Assert.That(attributes.Assembly.Identity.IsRetargetable, Is.EqualTo(retargetable));
                Assert.That(attributes.Assembly.Identity.PublicKey, Is.Empty);
                foreignIdentity = attributes.Assembly.Identity;
                attributeImage = Emit(attributes);
                using (var attributePe = new PEReader(attributeImage.Value))
                {
                    var flags = attributePe.GetMetadataReader().GetAssemblyDefinition().Flags;
                    Assert.That((flags & AssemblyFlags.Retargetable) != 0, Is.EqualTo(retargetable));
                }
                attributePath = Path.Combine(directory.FullName, foreignName + ".dll");
                await File.WriteAllBytesAsync(attributePath, attributeImage.Value.AsMemory());
                libraryReferences = TestMetadataReferences.WithoutSharpProof.Add(MetadataReference.CreateFromFile(attributePath));
            }

            var libraryName = "OrdinaryParameterLibrary_" + kind;
            var library = Create(libraryName, LibrarySource, libraryReferences);
            var libraryImage = Emit(library);
            var libraryPath = Path.Combine(directory.FullName, libraryName + ".dll");
            await File.WriteAllBytesAsync(libraryPath, libraryImage.AsMemory());
            var libraryReference = MetadataReference.CreateFromFile(libraryPath);
            var consumer = Create("OrdinaryParameterConsumer_" + kind, ConsumerSource,
                TestMetadataReferences.WithSharpProof.Add(libraryReference));
            var consumerImage = Emit(consumer);
            var consumerHasForeignReference = consumer.References.Select(consumer.GetAssemblyOrModuleSymbol)
                .OfType<IAssemblySymbol>().Any(assembly => assembly is null ? throw new System.ArgumentNullException(nameof(assembly)) : (assembly.Name == foreignName));
            Assert.That(consumerHasForeignReference, Is.False);
            if (attributePath != null)
            {
                Assert.That(consumer.References.OfType<PortableExecutableReference>()
                    .Any(reference => reference is null ? throw new System.ArgumentNullException(nameof(reference)) : (reference.FilePath == attributePath)), Is.False);
            }

            var selected = ContractSelectionInventory.ForCompilation(consumer).Positive;
            Assert.That(selected, Is.Not.Null);
            var actualShipped = consumer.GetTypeByMetadataName(typeof(PositiveAttribute).FullName!)!;
            Assert.That(SymbolEqualityComparer.Default.Equals(selected, actualShipped), Is.True);
            Assert.That(selected!.ContainingAssembly.Identity.Name, Is.EqualTo(typeof(PositiveAttribute).Assembly.GetName().Name));
            Assert.That(selected.ContainingAssembly.Identity.IsRetargetable, Is.False);
            Assert.That(selected.ContainingAssembly.Identity.PublicKeyToken, Is.Not.Empty);

            var tree = consumer.SyntaxTrees.Single();
            var root = await tree.GetRootAsync();
            var invocation = root.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
            var operation = (IInvocationOperation)consumer.GetSemanticModel(tree).GetOperation(invocation)!;
            var method = operation.TargetMethod;
            Assert.That(method.ContainingAssembly.Name, Is.EqualTo(libraryName));
            Assert.That(method.DeclaringSyntaxReferences, Is.Empty);
            Assert.That(method.Parameters.Single().Type.SpecialType, Is.EqualTo(SpecialType.System_Int32));
            Assert.That(method.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Int32));
            Assert.That(CompilerScalarIlDecoder.Candidate(method), Is.True);

            using var pe = new PEReader(libraryImage);
            var reader = pe.GetMetadataReader();
            var definition = reader.MethodDefinitions.Select(reader.GetMethodDefinition)
                .Single(candidate => reader.GetString(candidate.Name) == "Target");
            var parameter = definition.GetParameters().Select(reader.GetParameter)
                .Single(candidate => candidate.SequenceNumber == 1);
            var attribute = reader.GetCustomAttribute(parameter.GetCustomAttributes().Single());
            Assert.That(attribute.Constructor.Kind, Is.EqualTo(HandleKind.MemberReference));
            var constructor = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
            Assert.That(reader.GetString(constructor.Name), Is.EqualTo(".ctor"));
            Assert.That(constructor.Parent.Kind, Is.EqualTo(HandleKind.TypeReference));
            var type = reader.GetTypeReference((TypeReferenceHandle)constructor.Parent);
            Assert.That(reader.GetString(type.Namespace), Is.EqualTo("SharpProof.Attributes"));
            Assert.That(reader.GetString(type.Name), Is.EqualTo("PositiveAttribute"));
            Assert.That(type.ResolutionScope.Kind, Is.EqualTo(HandleKind.AssemblyReference));
            var assemblyHandle = (AssemblyReferenceHandle)type.ResolutionScope;
            var assemblyReference = reader.GetAssemblyReference(assemblyHandle);
            var referenceRetargetable = (assemblyReference.Flags & AssemblyFlags.Retargetable) != 0;
            Assert.That(referenceRetargetable, Is.EqualTo(retargetable));
            Assert.That(reader.GetString(assemblyReference.Name),
                Is.EqualTo(shipped ? selected.ContainingAssembly.Identity.Name : foreignName));
            if (!shipped)
            {
                Assert.That(reader.GetBlobBytes(assemblyReference.PublicKeyOrToken), Is.Empty);
            }
            var identityRead = CompilerTotalIlBodyProvider.TryReadAssemblyIdentity(reader, assemblyHandle,
                selected.ContainingAssembly.Identity, out var identity);
            // The omitted foreign attributes are unsigned; the shipped contract API is now signed.
            // The identity reader must reject their empty key before constructing a matching contract-API identity.
            Assert.That(identityRead, Is.EqualTo(shipped));
            if (identityRead)
            {
                Assert.That(identity, Is.EqualTo(selected.ContainingAssembly.Identity));
            }
            if (!shipped && !retargetable)
            {
                // Keep a positive structural control: this is a valid ordinary attribute dependency,
                // omitted from the consumer's references, rather than malformed assembly-reference data.
                Assert.That(foreignIdentity, Is.Not.Null);
                Assert.That(CompilerTotalIlBodyProvider.TryReadAssemblyIdentity(reader, assemblyHandle,
                    foreignIdentity!, out var parsedForeign), Is.True);
                Assert.That(parsedForeign, Is.EqualTo(foreignIdentity));
                Assert.That(parsedForeign, Is.Not.EqualTo(selected.ContainingAssembly.Identity));
            }

            var references = CompilerCompilationCapture.CaptureReferences(consumer.References,
                CompilerCompilationCapture.ReferenceCaptureLimits.Default, TestContext.CurrentContext.CancellationToken);
            var provider = new CompilerTotalIlBodyProvider(consumer, references);
            TotalIlBody? decoded = null;
            Assert.DoesNotThrow((Action)(() => decoded = provider.Resolve(method, TestContext.CurrentContext.CancellationToken)));
            if (shipped)
            {
                Assert.That(decoded, Is.Not.Null);
                Assert.That(SymbolEqualityComparer.Default.Equals(decoded!.Method, method), Is.True);
                Assert.That(decoded.ParameterAttributes.Single().Kind, Is.EqualTo("Positive"));
                Assert.That(decoded.Instructions.Last().Code, Is.EqualTo(nameof(ILOpCode.Ret)));
            }
            else
            {
                Assert.That(decoded, Is.Null);
            }

            _ = runtime.LoadFromAssemblyPath(typeof(PositiveAttribute).Assembly.Location);
            if (attributeImage is { } dependencyImage)
            {
                using var attributeStream = new MemoryStream(dependencyImage.ToArray(), writable: false);
                _ = runtime.LoadFromStream(attributeStream);
            }
            using var libraryStream = new MemoryStream(libraryImage.ToArray(), writable: false);
            var runtimeLibrary = runtime.LoadFromStream(libraryStream);
            using var consumerStream = new MemoryStream(consumerImage.ToArray(), writable: false);
            var runtimeConsumer = runtime.LoadFromStream(consumerStream);
            var libraryResult = runtimeLibrary.GetType("Library")!.GetMethod("Target")!.Invoke(null, [7]);
            var consumerResult = runtimeConsumer.GetType("Subject")!.GetMethod("Target")!.Invoke(null, [7]);
            Assert.That(libraryResult, Is.EqualTo(7));
            Assert.That(consumerResult, Is.EqualTo(7));

            new RepositoryLogger(Console.Out, Console.Error).Info(JsonSerializer.Serialize(new
            {
                Case = kind.ToString(),
                ConsumerHasForeignCompilerReference = consumerHasForeignReference,
                SelectedAssembly = selected.ContainingAssembly.Identity.ToString(),
                AttributeAssemblyReference = reader.GetString(assemblyReference.Name),
                AttributeReferenceRetargetable = referenceRetargetable,
                AttributeReferenceKeyLength = reader.GetBlobBytes(assemblyReference.PublicKeyOrToken).Length,
                AssemblyIdentityRead = identityRead,
                ProviderAdmitted = decoded != null,
                LibraryClrResult = libraryResult,
                ConsumerClrResult = consumerResult
            }));
        }
        finally
        {
            runtime.Unload();
        }
    }

    private static CSharpCompilation Create(string name, string source, ImmutableArray<MetadataReference> /* SPINT001: all values valid - Roslyn accepts default as an empty reference list for malformed-reference test fixtures */ references)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        System.ArgumentNullException.ThrowIfNull(name);
        var compilation = CSharpCompilation.Create(name,
            [CSharpSyntaxTree.ParseText(source, ParseOptions)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release, nullableContextOptions: NullableContextOptions.Enable));
        TestCompilation.AssertNoErrors(compilation);
        return compilation;
    }

    private static ImmutableArray<byte> Emit(CSharpCompilation compilation)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics));
        return ImmutableArray.Create<byte>(stream.GetBuffer().AsSpan(0, checked((int)stream.Length)));
    }
}
