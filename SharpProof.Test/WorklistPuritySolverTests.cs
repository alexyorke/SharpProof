using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Analyzer.Engine;
using SharpProof.Attributes;

namespace SharpProof.Test;

[TestFixture]
public class CompilationPurityServiceTests
{
    [Test]
    public async Task PureRecursiveMethodConvergesWithoutSp0002()
    {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            """
            using SharpProof.Attributes;

            public static class Recursive
            {
                [EnforcePure]
                public static int Sum(int value) => value <= 0 ? 0 : value + Sum(value - 1);
            }
            """,
            globalOptions: ImmutableDictionary<string, string>.Empty
                .Add("sharpproof_suggest_missing_enforce_pure", "false"),
            concurrentAnalysis: true,
            compilationName: "PureRecursiveMethod");

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Not.Contain("SP0002"));
    }

    [Test]
    public void MetadataInterfaceAndImplementation_AreNotAssumedPure()
    {
        const string externalSource = @"
public interface IExternal
{
    int Read();
}

public sealed class External : IExternal
{
    private int value;
    public int Read() => ++value;
}";
        var externalReference = CompileReference(externalSource);
        var consumerTree = CSharpSyntaxTree.ParseText("public sealed class Consumer { }");
        var compilation = CSharpCompilation.Create(
            "Consumer",
            new[] { consumerTree },
            new MetadataReference[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(EnforcePureAttribute).Assembly.Location),
                externalReference
            },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var interfaceMethod = compilation.GetTypeByMetadataName("IExternal")!.GetMembers("Read")
            .OfType<IMethodSymbol>().Single();
        var implementationMethod = compilation.GetTypeByMetadataName("External")!.GetMembers("Read")
            .OfType<IMethodSymbol>().Single();
        var enforcePure = compilation.GetTypeByMetadataName(typeof(EnforcePureAttribute).FullName!)!;
        var semanticModel = compilation.GetSemanticModel(consumerTree);
        using var service = new CompilationPurityService(compilation);
        var interfaceResult = service.GetPurity(
            interfaceMethod, semanticModel, enforcePure, null, CancellationToken.None);
        var implementationResult = service.GetPurity(
            implementationMethod, semanticModel, enforcePure, null, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(interfaceResult.IsPure, Is.False);
            Assert.That(interfaceResult.Evidence.Category, Is.EqualTo("unknown_external_call"));
            Assert.That(implementationResult.IsPure, Is.False);
            Assert.That(implementationResult.Evidence.Category, Is.EqualTo("unknown_external_call"));
        });
    }

    private static MetadataReference CompileReference(string source)
    {
        var compilation = CSharpCompilation.Create(
            "ExternalAssembly",
            new[] { CSharpSyntaxTree.ParseText(source) },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        Assert.That(emitResult.Success, Is.True, string.Join(Environment.NewLine, emitResult.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }
}
