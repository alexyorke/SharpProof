using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Fuzz;

namespace SharpProof.Fuzz.Test;

public sealed class FrontendSemanticEdgeTopologyControls
{
    private static readonly ImmutableArray<string> OverloadedNames = ["EdgeTarget0", "EdgeTarget0"];
    private static readonly ImmutableArray<int> OverloadedParameterCounts = [0, 1];

    [Test]
    public void ActualSameNamedNamespaceSubjectIsRejected()
    {
        ImmutableArray<FrontendSemanticEdgeCase> cases =
        [
            Exact("long", "0L; } namespace Other { public static class SharpProofGeneratedFrontendEdges { " +
                "public static long Other() => 0L; } } public static class Tail { public static long Value => 0L", [])
        ];
        var root = CompileActual(cases);
        Assert.That(root.DescendantNodes().OfType<ClassDeclarationSyntax>().Count(type =>
            type is null ? throw new System.ArgumentNullException(nameof(type)) : (type.Identifier.ValueText == "SharpProofGeneratedFrontendEdges")), Is.EqualTo(2));
        var result = FrontendDifferentialOracle.CompareSemanticEdges(cases, cancellationToken: TestContext.CurrentContext.CancellationToken).Single();
        Assert.That(result.Status, Is.EqualTo(FuzzOracleStatus.Mismatch));
        Assert.That(result.Detail, Is.EqualTo("Roslyn exposed an unexpected semantic-edge type shape."));
    }

    [Test]
    public void ActualOverloadedNamesKeepBothIsolatedCohorts()
    {
        ImmutableArray<FrontendSemanticEdgeCase> cases =
        [
            Exact("long", "0L", []),
            Exact("/*", "*/ long EdgeTarget0(long value) => value", [7L])
        ];
        var root = CompileActual(cases);
        var subject = root.Members.OfType<ClassDeclarationSyntax>().Single();
        var methods = subject.Members.OfType<MethodDeclarationSyntax>().ToImmutableArray();
        Assert.That(methods.Length, Is.EqualTo(cases.Length));
        Assert.That(subject.Members.Count, Is.EqualTo(cases.Length));
        Assert.That(methods.Select(method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method.Identifier.ValueText)),
            Is.EqualTo(OverloadedNames));
        Assert.That(methods.Select(method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method.ParameterList.Parameters.Count)), Is.EqualTo(OverloadedParameterCounts));
        var results = FrontendDifferentialOracle.CompareSemanticEdges(cases, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(results.Select(result => result is null ? throw new System.ArgumentNullException(nameof(result)) : (result.Status)), Is.All.EqualTo(FuzzOracleStatus.Agreement),
            string.Join(Environment.NewLine, results.Select(result => result is null ? throw new System.ArgumentNullException(nameof(result)) : (result.Detail))));
    }

    [Test]
    public void ActualAbsentExpectedMethodIsRejected()
    {
        ImmutableArray<FrontendSemanticEdgeCase> cases = [Exact("/*", "*/ long EdgeTarget1() => 0L", [])];
        var root = CompileActual(cases);
        var subject = root.Members.OfType<ClassDeclarationSyntax>().Single();
        Assert.That(subject.Members.Count, Is.EqualTo(cases.Length));
        Assert.That(subject.Members.OfType<MethodDeclarationSyntax>().Single().Identifier.ValueText,
            Is.EqualTo("EdgeTarget1"));
        var result = FrontendDifferentialOracle.CompareSemanticEdges(cases, cancellationToken: TestContext.CurrentContext.CancellationToken).Single();
        Assert.That(result.Status, Is.EqualTo(FuzzOracleStatus.Mismatch));
        Assert.That(result.Detail, Is.EqualTo("Roslyn exposed an unexpected semantic-edge method shape."));
    }

    private static FrontendSemanticEdgeCase Exact(string returnType, string expression, ImmutableArray<object?> arguments)
    {
        if (arguments.IsDefault)
        { throw new ArgumentNullException(nameof(arguments)); }
        System.ArgumentNullException.ThrowIfNull(expression);
        System.ArgumentNullException.ThrowIfNull(returnType);
        return new(returnType, "", expression, arguments, FrontendSubsetDecision.Exact, FrontendAbstention.None);
    }

    private static CompilationUnitSyntax CompileActual(IReadOnlyList<FrontendSemanticEdgeCase> cases)
    {
        System.ArgumentNullException.ThrowIfNull(cases, nameof(cases));
        var source = new StringBuilder();
        source.AppendLine("#nullable enable");
        source.AppendLine("public enum SharpProofGeneratedEdgeEnum { One = 1 }");
        source.AppendLine("public readonly struct SharpProofGeneratedConvertible {");
        source.AppendLine("    private readonly long _value;");
        source.AppendLine("    public SharpProofGeneratedConvertible(long value) => _value = value;");
        source.AppendLine("    public static explicit operator long(SharpProofGeneratedConvertible value) => value._value;");
        source.AppendLine("}");
        source.AppendLine("public static class SharpProofGeneratedFrontendEdges {");
        for (var index = 0; index < cases.Count; index++)
        {
            var generated = cases[index];
            source.Append("    public static ");
            source.Append(generated.ReturnType);
            source.Append(" EdgeTarget");
            source.Append(index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            source.Append('(');
            source.Append(generated.Parameters);
            source.Append(") => ");
            source.Append(generated.Expression);
            source.AppendLine(";");
        }
        source.AppendLine("}");
        var tree = CSharpSyntaxTree.ParseText(source.ToString(), new CSharpParseOptions(LanguageVersion.CSharp12));
        var compilation = CSharpCompilation.Create("SharpProofFrontendSemanticEdges", [tree],
            TestMetadataReferences.SortedDistinctPlatform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release, checkOverflow: true,
                nullableContextOptions: NullableContextOptions.Enable));
        var emission = GeneratedCompilationEmitter.Emit(compilation, TestContext.CurrentContext.CancellationToken);
        using var image = emission.Image;
        Assert.That(emission.Emit.Success, Is.True, string.Join(Environment.NewLine, emission.Emit.Diagnostics));
        return (CompilationUnitSyntax)tree.GetRoot();
    }
}
