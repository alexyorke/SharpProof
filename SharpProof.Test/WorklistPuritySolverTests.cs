using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Analyzer.Engine;
using SharpProof.Analyzer.Engine.Analysis;
using SharpProof.Attributes;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Test;

[TestFixture]
public class WorklistPuritySolverTests
{
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
        var graph = ImmutableDictionary.Create<IMethodSymbol, ImmutableHashSet<IMethodSymbol>>(
                SymbolEqualityComparer.Default)
            .Add(interfaceMethod, ImmutableHashSet.Create<IMethodSymbol>(SymbolEqualityComparer.Default))
            .Add(implementationMethod, ImmutableHashSet.Create<IMethodSymbol>(SymbolEqualityComparer.Default));
        var enforcePure = compilation.GetTypeByMetadataName(typeof(EnforcePureAttribute).FullName!)!;
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var results = WorklistPuritySolver.Solve(
            graph,
            compilation,
            enforcePure,
            allowSynchronizationAttributeSymbol: null,
            smtAnalysis,
            RequiresContractHelpers.OfficialAttributePolicy,
            tree => compilation.GetSemanticModel(tree),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(results[interfaceMethod].IsPure, Is.False);
            Assert.That(results[interfaceMethod].Evidence.Category, Is.EqualTo("unknown_external_call"));
            Assert.That(results[implementationMethod].IsPure, Is.False);
            Assert.That(results[implementationMethod].Evidence.Category, Is.EqualTo("unknown_external_call"));
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
