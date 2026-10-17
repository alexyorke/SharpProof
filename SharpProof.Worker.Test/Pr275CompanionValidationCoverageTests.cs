using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.ContractForValidation;
using SharpProof.Contracts;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class Pr275CompanionValidationCoverageTests
{
    private static readonly CSharpCompilation Compilation = CSharpCompilation.Create(
        "Pr275CompanionValidation",
        [CSharpSyntaxTree.ParseText("""
            using SharpProof.Attributes;
            public class Target { public void Read() { } }
            [ContractFor(typeof(Target))]
            public static class TargetContracts {
                public static void Read(Target receiver) { }
            }
            """, path: "Subject.cs")],
        TestMetadataReferences.WithSharpProof,
        TestCompilation.CreateOptions(OutputKind.DynamicallyLinkedLibrary));

    [TestCase("relationship", "companion")]
    [TestCase("relationship", "relationships")]
    [TestCase("relationship", "diagnostics")]
    [TestCase("validation", "companion")]
    [TestCase("validation", "compilation")]
    [TestCase("validation", "clauses")]
    [TestCase("validation", "diagnostics")]
    [TestCase("location", "symbol")]
    [TestCase("location", "compilation")]
    [TestCase("location", "fallback")]
    public void ValidationApisReportMissingRequiredInputs(string api, string missing)
    {
        TestCompilation.AssertNoErrors(Compilation);
        var target = Compilation.GetTypeByMetadataName("Target")!;
        var companion = new ResolvedCompanion(Compilation.GetTypeByMetadataName("TargetContracts")!,
            target, Location.None, isOpenTarget: false);
        var relationships = ContractForSymbolMatcher.ClassifyCompanionRelationships(
            ContractForSymbolMatcher.DiscoverCompanionRelationships(Compilation, cancellationToken: CancellationToken.None), cancellationToken: CancellationToken.None);
        var diagnostics = new List<Diagnostic>();
        var clauses = new ContractClauseInventoryBuilder(Compilation);

        var exception = Assert.Throws<ArgumentNullException>((Action)(() =>
        {
            switch (api)
            {
                case "relationship":
                    _ = ContractForCompanionValidator.ValidateRelationship(
                        missing == "companion" ? null! : companion,
                        missing == "relationships" ? null! : relationships,
                        missing == "diagnostics" ? null! : diagnostics);
                    break;
                case "validation":
                    ContractForCompanionValidator.Validate(
                        missing == "companion" ? null! : companion,
                        missing == "compilation" ? null! : Compilation,
                        missing == "clauses" ? null! : clauses,
                        missing == "diagnostics" ? null! : diagnostics, default);
                    break;
                case "location":
                    _ = ContractForCompanionValidator.GetSourceLocation(
                        missing == "symbol" ? null! : target,
                        missing == "compilation" ? null! : Compilation,
                        missing == "fallback" ? null! : Location.None);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(api));
            }
        }));

        Assert.That(exception!.ParamName, Is.EqualTo(missing));
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public void ValidDiscoveredCompanionRelationshipProducesNoDiagnostic()
    {
        TestCompilation.AssertNoErrors(Compilation);
        var relationships = ContractForSymbolMatcher.ClassifyCompanionRelationships(
            ContractForSymbolMatcher.DiscoverCompanionRelationships(Compilation, cancellationToken: CancellationToken.None), cancellationToken: CancellationToken.None);
        var descriptor = relationships.Accepted.Single();
        var companion = new ResolvedCompanion(descriptor.Type, descriptor.Target,
            Location.None, descriptor.ContractTarget.IsOpen);
        var diagnostics = new List<Diagnostic>();

        Assert.That(ContractForCompanionValidator.ValidateRelationship(companion, relationships, diagnostics), Is.True);
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public void SourceLocationUsesTheCurrentCompilationAndOtherwiseReturnsFallback()
    {
        var target = Compilation.GetTypeByMetadataName("Target")!;
        var source = ContractForCompanionValidator.GetSourceLocation(target, Compilation, Location.None);
        var unrelated = CSharpCompilation.Create("Unrelated");
        var fallback = ContractForCompanionValidator.GetSourceLocation(target, unrelated, source);

        Assert.That(source.IsInSource, Is.True);
        Assert.That(source.SourceTree!.FilePath, Is.EqualTo("Subject.cs"));
        Assert.That(fallback, Is.SameAs(source));
    }
}
