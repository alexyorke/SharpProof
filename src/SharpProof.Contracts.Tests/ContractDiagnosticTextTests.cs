using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class ContractDiagnosticTextTests
{
    [TestCase(4)]
    [TestCase(520)]
    public void MetadataClosedAttributesRetainTheirConditionNames(int nameLength)
    {
        var parameterName = nameLength == 4 ? "text" : new string('t', nameLength);
        var library = TestCompilation.Create(
            "DiagnosticContractLibrary",
            $$"""
            using SharpProof.Attributes;
            public static class Library {
                public static string Read([NotNull] string {{parameterName}}, [Positive, InRange(1L, 10L)] long count) => {{parameterName}};
            }
            """);
        using var image = new MemoryStream();
        var emitted = library.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var compilation = TestCompilation.Create("DiagnosticConsumer", "public static class Consumer {}")
            .AddReferences(MetadataReference.CreateFromImage(ImmutableArray.Create(image.GetBuffer(), 0, checked((int)image.Length))));
        var target = compilation.GetTypeByMetadataName("Library")!
            .GetMembers("Read").OfType<IMethodSymbol>().Single();
        var result = new ContractBinder(compilation, new IrFactory()).Bind(target);

        Assert.That(result.IsSuccess, Is.True, result.Failure.ToString());
        Assert.That(result.Contracts!.Clauses.Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.DiagnosticText)),
            Is.EquivalentTo(ImmutableArray.Create(nameLength == 4 ? "[NotNull] text" : "[condition exceeds the display limit]",
                "[Positive] count", "[InRange(1, 10)] count")));
        Assert.That(result.Contracts.Clauses.All(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Evidence == BoundContractEvidence.ClosedAttribute)), Is.True);
    }

    [Test]
    public void LongClosedAttributeSyntaxIsBoundButItsDisplayIsBounded()
    {
        var compilation = TestCompilation.Create("BoundedAttributeDiagnostic", $$"""
            using SharpProof.Attributes;
            public static class Target {
                public static int Read([InRange({{new string(' ', 520)}}1L, 10L)] int value) => value;
            }
            """);
        var target = compilation.GetTypeByMetadataName("Target")!
            .GetMembers("Read").OfType<IMethodSymbol>().Single();
        var result = new ContractBinder(compilation, new IrFactory()).Bind(target);

        Assert.That(result.IsSuccess, Is.True, result.Failure.ToString());
        Assert.That(result.Contracts!.Clauses.Single().DiagnosticText,
            Is.EqualTo("[condition exceeds the display limit]"));
    }

    [Test]
    public void LongClauseSyntaxIsBoundButItsDisplayIsBounded()
    {
        var compilation = TestCompilation.Create("BoundedClauseDiagnostic", $$"""
            using SharpProof.Attributes;
            public static class Target {
                public static int Read(int value) {
                    Contract.Requires(value{{new string(' ', 520)}} > 0);
                    return value;
                }
            }
            """);
        var target = compilation.GetTypeByMetadataName("Target")!
            .GetMembers("Read").OfType<IMethodSymbol>().Single();
        var result = new ContractBinder(compilation, new IrFactory()).Bind(target);

        Assert.That(result.IsSuccess, Is.True, result.Failure.ToString());
        Assert.That(result.Contracts!.Clauses.Single().DiagnosticText,
            Is.EqualTo("[condition exceeds the display limit]"));
    }
}
