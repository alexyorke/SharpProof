using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace SharpProof.Frontend.Test;

[TestFixture]
[NonParallelizable]
public sealed class ExplicitLayoutFieldAuthorityFallbackTests
{
    private const string OrdinarySource = """
        public sealed class Cell
        {
            public int Value;
        }
        """ + "\n";

    private const string UnresolvedAttributeSource = """
        [@StructLayoutAttribute(global::System.Runtime.InteropServices.LayoutKind.Explicit)]
        public sealed class Cell
        {
            public int Value;
        }
        """ + "\n";

    [Test]
    public async Task DisposedOwnedMetadataDeclinesExactFieldStorage()
    {
        var directory = EvidenceDirectory("disposed-owned-metadata");
        var producer = CreateCompilation("OwnedStorageProducer", OrdinarySource, TestMetadataReferences.Platform);
        AssertNoErrors(producer);
        using var image = new MemoryStream();
        var emitted = producer.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join("\n", emitted.Diagnostics));
        var imageBytes = image.ToArray();
        await File.WriteAllTextAsync(Path.Combine(directory, "producer-source.cs"), OrdinarySource);
        await File.WriteAllBytesAsync(Path.Combine(directory, "emitted-producer.dll"), imageBytes);

        using var ownedModule = ModuleMetadata.CreateFromImage(ImmutableArray.CreateRange(imageBytes));
        using var ownedAssembly = AssemblyMetadata.Create(ownedModule);
        var reference = ownedAssembly.GetReference();
        var consumer = CreateCompilation("OwnedStorageConsumer", "public sealed class Consumer { }\n",
            TestMetadataReferences.Platform.Add(reference));
        AssertNoErrors(consumer);
        var owner = consumer.GetTypeByMetadataName("Cell");
        Assert.That(owner, Is.Not.Null);
        var field = owner!.GetMembers("Value").OfType<IFieldSymbol>().Single();
        var originalOwner = field.OriginalDefinition.ContainingType.OriginalDefinition;
        var ownerModule = originalOwner.ContainingModule;
        var ownerToken = originalOwner.MetadataToken;
        var fieldToken = field.OriginalDefinition.MetadataToken;
        var foreignOwner = !SymbolEqualityComparer.Default.Equals(originalOwner.ContainingAssembly, consumer.Assembly);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(SymbolEqualityComparer.Default.Equals(owner, originalOwner), Is.True);
            Assert.That(originalOwner.TypeKind, Is.EqualTo(TypeKind.Class));
            Assert.That(originalOwner.IsReferenceType, Is.True);
            Assert.That(originalOwner.DeclaringSyntaxReferences, Is.Empty);
            Assert.That(originalOwner.StaticConstructors, Is.Empty);
            Assert.That(ownerToken, Is.Not.Zero);
            Assert.That(foreignOwner, Is.True);
            Assert.That(field.Type.SpecialType, Is.EqualTo(SpecialType.System_Int32));
            Assert.That(field.DeclaredAccessibility, Is.EqualTo(Accessibility.Public));
            Assert.That(field.IsStatic, Is.False);
            Assert.That(field.IsReadOnly, Is.False);
            Assert.That(field.IsConst, Is.False);
            Assert.That(field.IsVolatile, Is.False);
            Assert.That(field.HasConstantValue, Is.False);
        }

        var borrowed = ownerModule.GetMetadata();
        Assert.That(borrowed, Is.Not.Null);
        var reader = borrowed!.GetMetadataReader();
        var originalReader = ownedModule.GetMetadataReader();
        var ownerHandle = MetadataTokens.Handle(ownerToken);
        var fieldHandle = MetadataTokens.Handle(fieldToken);
        Assert.That(ownerHandle.Kind, Is.EqualTo(HandleKind.TypeDefinition));
        Assert.That(fieldHandle.Kind, Is.EqualTo(HandleKind.FieldDefinition));
        var definition = reader.GetTypeDefinition((TypeDefinitionHandle)ownerHandle);
        var fieldDefinition = reader.GetFieldDefinition((FieldDefinitionHandle)fieldHandle);
        var mvid = reader.GetGuid(reader.GetModuleDefinition().Mvid);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reader.GetString(definition.Name), Is.EqualTo("Cell"));
            Assert.That(definition.Attributes & TypeAttributes.LayoutMask, Is.EqualTo(TypeAttributes.AutoLayout));
            Assert.That(fieldDefinition.GetDeclaringType(), Is.EqualTo((TypeDefinitionHandle)ownerHandle));
            Assert.That(reader.GetString(fieldDefinition.Name), Is.EqualTo("Value"));
            Assert.That(mvid, Is.EqualTo(originalReader.GetGuid(originalReader.GetModuleDefinition().Mvid)));
            Assert.That(ownedModule.IsDisposed, Is.False);
        }

        var beforeRead = CSharpOperationSemantics.IsSupportedFieldRead(field);
        var beforeWrite = CSharpOperationSemantics.IsSupportedFieldWrite(field, consumer.Assembly);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(beforeRead, Is.True);
            Assert.That(beforeWrite, Is.False);
        }

        await Save(directory, "live-metadata-binding.json", new
        {
            SourceSHA256 = Hash(OrdinarySource),
            PESHA256 = Convert.ToHexString(SHA256.HashData(imageBytes)),
            OwnerToken = ownerToken,
            FieldToken = fieldToken,
            Mvid = mvid,
            Layout = "AutoLayout",
            ForeignOwner = foreignOwner,
            BeforeRead = beforeRead,
            BeforeWrite = beforeWrite,
            MetadataOwnership = "The test owns ownedAssembly and its one freshly created module; borrowed and platform metadata are never disposed."
        });

        ownedAssembly.Dispose();
        Assert.That(ownedModule.IsDisposed, Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(field.OriginalDefinition.MetadataToken, Is.EqualTo(fieldToken));
            Assert.That(field.OriginalDefinition.ContainingType.OriginalDefinition, Is.SameAs(originalOwner));
            Assert.That(originalOwner.MetadataToken, Is.EqualTo(ownerToken));
            Assert.That(originalOwner.ContainingModule, Is.SameAs(ownerModule));
        }
        var moduleFault = Assert.Throws<ObjectDisposedException>(new Action(() =>
        {
            _ = ownerModule.GetMetadata();
        }));
        var readerFault = Assert.Throws<ObjectDisposedException>(new Action(() =>
        {
            _ = borrowed.GetMetadataReader();
        }));
        var actualRead = CSharpOperationSemantics.IsSupportedFieldRead(field);
        var actualWrite = CSharpOperationSemantics.IsSupportedFieldWrite(field, consumer.Assembly);
        await Save(directory, "admission-observation.json", new
        {
            Case = "disposed-owned-metadata",
            SourceSHA256 = Hash(OrdinarySource),
            OwnedModuleDisposed = ownedModule.IsDisposed,
            OwnerToken = ownerToken,
            FieldToken = fieldToken,
            Mvid = mvid,
            ModuleAccessFault = moduleFault!.GetType().FullName,
            ReaderAccessFault = readerFault!.GetType().FullName,
            ActualRead = actualRead,
            ActualWrite = actualWrite,
            Policy = "Unavailable layout authority cannot support exact instance-field storage; this is a public metadata lifecycle robustness check, with no CLR/native claim."
        });
        using (Assert.EnterMultipleScope())
        {
            Assert.That(actualRead, Is.False);
            Assert.That(actualWrite, Is.False);
        }
    }

    [Test]
    public async Task UnresolvedLayoutAttributeDeclinesExactFieldStorage()
    {
        var directory = EvidenceDirectory("unresolved-layout-attribute");
        var compilation = CreateCompilation("UnresolvedStorageAttribute", UnresolvedAttributeSource, TestMetadataReferences.Platform);
        var tree = compilation.SyntaxTrees.Single();
        var root = await tree.GetRootAsync();
        var syntax = root.DescendantNodes().OfType<AttributeSyntax>().Single();
        var errors = compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.That(errors, Has.Length.EqualTo(1), string.Join("\n", errors.Select(static diagnostic => diagnostic.ToString())));
        var error = errors.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(error.Id, Is.EqualTo("CS0246"));
            Assert.That(error.Location.SourceTree, Is.SameAs(tree));
            Assert.That(error.Location.SourceSpan, Is.EqualTo(syntax.Name.Span));
            Assert.That(error.GetMessage(System.Globalization.CultureInfo.InvariantCulture), Does.Contain("StructLayoutAttribute"));
        }

        var bclAttribute = compilation.GetTypeByMetadataName("System.Runtime.InteropServices.StructLayoutAttribute");
        var bclKind = compilation.GetTypeByMetadataName("System.Runtime.InteropServices.LayoutKind");
        Assert.That(bclAttribute, Is.Not.Null);
        Assert.That(bclKind, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(bclAttribute!.TypeKind, Is.EqualTo(TypeKind.Class));
            Assert.That(SymbolEqualityComparer.Default.Equals(bclAttribute.ContainingAssembly, compilation.Assembly), Is.False);
            Assert.That(bclKind!.TypeKind, Is.EqualTo(TypeKind.Enum));
            Assert.That(bclKind.EnumUnderlyingType!.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        }

        var owner = compilation.GetTypeByMetadataName("Cell");
        Assert.That(owner, Is.Not.Null);
        var field = owner!.GetMembers("Value").OfType<IFieldSymbol>().Single();
        var attribute = owner.GetAttributes().Single();
        Assert.That(attribute.AttributeClass, Is.Not.Null);
        Assert.That(attribute.ApplicationSyntaxReference, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(owner.TypeKind, Is.EqualTo(TypeKind.Class));
            Assert.That(owner.IsReferenceType, Is.True);
            Assert.That(owner.MetadataToken, Is.Zero);
            Assert.That(owner.DeclaringSyntaxReferences, Has.Length.EqualTo(1));
            Assert.That(SymbolEqualityComparer.Default.Equals(owner.ContainingAssembly, compilation.Assembly), Is.True);
            Assert.That(owner.StaticConstructors, Is.Empty);
            Assert.That(attribute.ApplicationSyntaxReference!.SyntaxTree, Is.SameAs(tree));
            Assert.That(attribute.ApplicationSyntaxReference.Span, Is.EqualTo(syntax.Span));
            Assert.That(attribute.AttributeClass!.TypeKind, Is.EqualTo(TypeKind.Error));
            Assert.That(attribute.AttributeClass.Name, Is.EqualTo("StructLayoutAttribute"));
            Assert.That(attribute.AttributeConstructor, Is.Null);
            Assert.That(SymbolEqualityComparer.Default.Equals(field.ContainingType, owner), Is.True);
            Assert.That(field.Type.SpecialType, Is.EqualTo(SpecialType.System_Int32));
            Assert.That(field.DeclaredAccessibility, Is.EqualTo(Accessibility.Public));
            Assert.That(field.IsStatic, Is.False);
            Assert.That(field.IsReadOnly, Is.False);
            Assert.That(field.IsConst, Is.False);
            Assert.That(field.IsVolatile, Is.False);
            Assert.That(field.HasConstantValue, Is.False);
        }

        var actualRead = CSharpOperationSemantics.IsSupportedFieldRead(field);
        var actualWrite = CSharpOperationSemantics.IsSupportedFieldWrite(field, compilation.Assembly);
        await File.WriteAllTextAsync(Path.Combine(directory, "producer-source.cs"), UnresolvedAttributeSource);
        await Save(directory, "admission-observation.json", new
        {
            Case = "unresolved-layout-attribute",
            SourceSHA256 = Hash(UnresolvedAttributeSource),
            Error = new
            {
                error.Id,
                Severity = error.Severity.ToString(),
                Start = error.Location.SourceSpan.Start,
                Length = error.Location.SourceSpan.Length,
                Message = error.GetMessage(System.Globalization.CultureInfo.InvariantCulture)
            },
            AttributeTypeKind = attribute.AttributeClass!.TypeKind.ToString(),
            AttributeName = attribute.AttributeClass.Name,
            ConstructorAbsent = attribute.AttributeConstructor == null,
            OwnerToken = owner.MetadataToken,
            OwnerSyntaxReferences = owner.DeclaringSyntaxReferences.Length,
            ActualRead = actualRead,
            ActualWrite = actualWrite,
            Policy = "Exactly qualified invalid-source admission robustness; this source is not emitted and makes no CLR/native claim."
        });
        using (Assert.EnterMultipleScope())
        {
            Assert.That(actualRead, Is.False);
            Assert.That(actualWrite, Is.False);
        }
    }

    private static CSharpCompilation CreateCompilation(string name, string source, ImmutableArray<MetadataReference> references)
    {
        return CSharpCompilation.Create(name,
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12), "Subject.cs")],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release, nullableContextOptions: NullableContextOptions.Enable));
    }

    private static void AssertNoErrors(Compilation compilation)
    {
        var errors = compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.That(errors, Is.Empty, string.Join("\n", errors.Select(static diagnostic => diagnostic.ToString())));
    }

    private static string EvidenceDirectory(string scenario)
    {
        var repositoryRoot = Environment.GetEnvironmentVariable("SHARPPROOF_REPO_ROOT");
        Assert.That(repositoryRoot, Is.Not.Null.And.Not.Empty);
        var directory = Path.Combine(repositoryRoot!, "artifacts", "correctness", "explicit-layout-field-authority-fallback-audit",
            scenario + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string Hash(string source)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
    }

    private static Task Save(string directory, string name, object value)
    {
        return File.WriteAllTextAsync(Path.Combine(directory, name), JsonSerializer.Serialize(value));
    }
}
