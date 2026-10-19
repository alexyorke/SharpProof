using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class Pr275TupleTypeMappingCoverageTests
{
    [TestCase("(int, string)")]
    [TestCase("(int First, string Second)")]
    [TestCase("(bool, long, int[])")]
    [TestCase("((int, string), bool)")]
    [TestCase("(string[], object)")]
    public void TupleAndUnderlyingValueTupleUseTheSameIrType(string tupleDeclaration)
    {
        System.ArgumentNullException.ThrowIfNull(tupleDeclaration);
        var tree = CSharpSyntaxTree.ParseText($"class Subject {{ static void Target({tupleDeclaration} value) {{ }} }}");
        var compilation = CSharpCompilation.Create("Pr275TupleTypes", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        var method = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
        var tuple = (INamedTypeSymbol)method.Parameters.Single().Type;
        Assert.That(tuple.IsTupleType, Is.True);
        var underlying = compilation.GetTypeByMetadataName("System.ValueTuple`" + tuple.Arity)!
            .Construct(tuple.TypeArguments, tuple.TypeArgumentNullableAnnotations);
        var mapper = new RoslynTypeMapper(new IrFactory());

        var tupleType = mapper.GetTypeId(tuple);
        var underlyingType = mapper.GetTypeId(underlying);

        Assert.That(tupleType, Is.EqualTo(underlyingType));
        Assert.That(mapper.GetTypeId(tuple), Is.EqualTo(tupleType));
    }
}
