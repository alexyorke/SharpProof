using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Contracts;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerClosedParameterCaptureBoundaryTests
{
    private const string Subject = """
        using SharpProof.Attributes;
        public sealed class RowAttribute : System.Attribute { }
        public static class Subject
        {
            [return: Row]
            public static int Zero() => 0;
            public static int One([Positive] int value) => value;
            public static int Two([Positive] int first, [InRange(1, 10)] int second) => first + second;
        }
        """;

    [Test]
    public void RepeatedRealParameterRowRefusesPartialEvidence()
    {
        var image = Emit(Subject);
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        var reader = pe.GetMetadataReader();
        var (compilation, method) = Import(image, "One");
        var row = Definition(reader, method).GetParameters().Single();
        Assert.That(reader.GetParameter(row).SequenceNumber, Is.EqualTo(1));

        Assert.That(CompilerClosedParameterCapture.TryCapture(compilation, reader, method,
            new[] { row, row }, CancellationToken.None, out var attributes), Is.False);
        Assert.That(attributes, Is.Empty);
    }

    [Test]
    public void RealSecondParameterRowRefusesOneParameterSymbol()
    {
        var image = Emit(Subject);
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        var reader = pe.GetMetadataReader();
        var (compilation, one) = Import(image, "One");
        var (_, two) = Import(image, "Two");
        var row = Definition(reader, two).GetParameters()
            .Single(handle => reader.GetParameter(handle).SequenceNumber == 2);
        Assert.That(one.Parameters, Has.Length.EqualTo(1));
        Assert.That(reader.GetParameter(row).SequenceNumber, Is.EqualTo(2));

        Assert.That(CompilerClosedParameterCapture.TryCapture(compilation, reader, one,
            new[] { row }, CancellationToken.None, out var attributes), Is.False);
        Assert.That(attributes, Is.Empty);
    }

    [Test]
    public void LocalConstructorTokenRefusesUnrelatedShippedMethodSymbol()
    {
        var image = Emit("""
            namespace SharpProof.Attributes
            {
                public sealed class PositiveAttribute : System.Attribute { }
            }
            public static class Subject
            {
                public static int One([SharpProof.Attributes.Positive] int value) => value;
            }
            """, shipped: false);
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        var reader = pe.GetMetadataReader();
        var (_, local) = Import(image, "One", shipped: false);
        var compilation = CSharpCompilation.Create("ShippedCaptureSelections",
            references: TestMetadataReferences.WithSharpProof,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var method = compilation.GetTypeByMetadataName("SharpProof.Attributes.Contract")!
            .GetMembers("Requires").OfType<IMethodSymbol>()
            .Single(candidate => candidate.Parameters.Length == 1 &&
                candidate.Parameters[0].Type.SpecialType == SpecialType.System_Boolean);
        var expected = ContractSelectionInventory.ForCompilation(compilation).Positive;
        Assert.That(expected, Is.Not.Null);
        var expectedConstructor = expected!.InstanceConstructors.Single(candidate => candidate.Parameters.IsEmpty);
        Assert.That(method.Parameters, Has.Length.EqualTo(1));
        Assert.That(method.ContainingAssembly.Identity, Is.EqualTo(expected.ContainingAssembly.Identity));
        var row = Definition(reader, local).GetParameters().Single();
        Assert.That(reader.GetParameter(row).SequenceNumber, Is.EqualTo(1));
        var attribute = reader.GetCustomAttribute(reader.GetParameter(row).GetCustomAttributes().Single());
        Assert.That(attribute.Constructor.Kind, Is.EqualTo(HandleKind.MethodDefinition));
        var constructor = reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor);
        var type = reader.GetTypeDefinition(constructor.GetDeclaringType());
        Assert.That(reader.GetString(type.Namespace), Is.EqualTo("SharpProof.Attributes"));
        Assert.That(reader.GetString(type.Name), Is.EqualTo("PositiveAttribute"));
        Assert.That(reader.GetString(constructor.Name), Is.EqualTo(".ctor"));
        Assert.That(reader.GetBlobBytes(constructor.Signature), Is.EqualTo(new byte[] { 0x20, 0, 1 }));
        Assert.That(MetadataTokens.GetToken(attribute.Constructor), Is.Not.EqualTo(expectedConstructor.MetadataToken));

        Assert.That(CompilerClosedParameterCapture.TryCapture(compilation, reader, method,
            new[] { row }, CancellationToken.None, out var attributes), Is.False);
        Assert.That(attributes, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ShippedMemberReferencesPairInContractOrder(bool reverseRows)
    {
        var image = Emit(Subject);
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        var reader = pe.GetMetadataReader();
        var (compilation, method) = Import(image, "Two");
        var rows = Definition(reader, method).GetParameters().ToArray();
        foreach (var row in rows)
        {
            var attribute = reader.GetCustomAttribute(reader.GetParameter(row).GetCustomAttributes().Single());
            Assert.That(attribute.Constructor.Kind, Is.EqualTo(HandleKind.MemberReference));
        }
        if (reverseRows)
        {
            Array.Reverse(rows);
        }

        Assert.That(CompilerClosedParameterCapture.TryCapture(compilation, reader, method,
            rows, CancellationToken.None, out var attributes), Is.True);
        Assert.That(attributes.Select(attribute => attribute.ParameterOrdinal), Is.EqualTo(new[] { 0, 1 }));
        Assert.That(attributes.Select(attribute => attribute.Kind), Is.EqualTo(new[] { "Positive", "InRange" }));
        Assert.That(attributes[1].Minimum, Is.EqualTo(1));
        Assert.That(attributes[1].Maximum, Is.EqualTo(10));
    }

    [TestCase(1024, true)]
    [TestCase(1025, false)]
    public void ReturnRowsUseTheOriginalRowLimit(int count, bool accepted)
    {
        var image = Emit(Subject);
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        var reader = pe.GetMetadataReader();
        var (compilation, method) = Import(image, "Zero");
        var row = Definition(reader, method).GetParameters().Single();
        Assert.That(reader.GetParameter(row).SequenceNumber, Is.Zero);

        Assert.That(CompilerClosedParameterCapture.TryCapture(compilation, reader, method,
            Enumerable.Repeat(row, count).ToArray(), CancellationToken.None, out var attributes), Is.EqualTo(accepted));
        Assert.That(attributes, Is.Empty);
    }

    [TestCase(1022, true)]
    [TestCase(1023, false)]
    public void ParameterAndAttributeRowsShareTheOriginalRowLimit(int returns, bool accepted)
    {
        var image = Emit(Subject);
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        var reader = pe.GetMetadataReader();
        var (compilation, method) = Import(image, "One");
        var (_, zero) = Import(image, "Zero");
        var returnRow = Definition(reader, zero).GetParameters().Single();
        var parameterRow = Definition(reader, method).GetParameters().Single();
        var rows = Enumerable.Repeat(returnRow, returns).Append(parameterRow).ToArray();
        Assert.That(reader.GetParameter(returnRow).SequenceNumber, Is.Zero);
        Assert.That(reader.GetParameter(parameterRow).GetCustomAttributes().Count, Is.EqualTo(1));

        Assert.That(CompilerClosedParameterCapture.TryCapture(compilation, reader, method,
            rows, CancellationToken.None, out var attributes), Is.EqualTo(accepted));
        Assert.That(attributes.Length, Is.EqualTo(accepted ? 1 : 0));
    }

    [Test]
    public void CancellationBeforeTheFirstRealRowPropagates()
    {
        var image = Emit(Subject);
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        var reader = pe.GetMetadataReader();
        var (compilation, method) = Import(image, "One");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var attributes = ImmutableArray<TotalIlClosedAttribute>.Empty;

        Assert.Throws<OperationCanceledException>(() => CompilerClosedParameterCapture.TryCapture(
            compilation, reader, method, Definition(reader, method).GetParameters(),
            cancellation.Token, out attributes));
        Assert.That(attributes, Is.Empty);
    }

    private static MethodDefinition Definition(MetadataReader reader, IMethodSymbol method)
    {
        return reader.GetMethodDefinition((MethodDefinitionHandle)MetadataTokens.Handle(method.MetadataToken));
    }

    private static byte[] Emit(string source, bool shipped = true)
    {
        var compilation = CSharpCompilation.Create("CaptureRows",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12))],
            shipped ? TestMetadataReferences.WithSharpProof : TestMetadataReferences.WithoutSharpProof,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var result = compilation.Emit(image);
        Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.That(image.Length, Is.GreaterThan(0));
        return image.ToArray();
    }

    private static (CSharpCompilation Compilation, IMethodSymbol Method) Import(
        byte[] image, string name, bool shipped = true)
    {
        var reference = MetadataReference.CreateFromImage(image);
        var compilation = CSharpCompilation.Create("CaptureRowsConsumer", references:
            (shipped ? TestMetadataReferences.WithSharpProof : TestMetadataReferences.WithoutSharpProof).Add(reference),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var assembly = (IAssemblySymbol)compilation.GetAssemblyOrModuleSymbol(reference)!;
        var method = assembly.GetTypeByMetadataName("Subject")!.GetMembers(name).OfType<IMethodSymbol>().Single();
        Assert.That(method.MetadataToken, Is.GreaterThan(0));
        return (compilation, method);
    }
}
