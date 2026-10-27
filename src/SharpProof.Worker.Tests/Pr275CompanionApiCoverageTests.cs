using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Contracts;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class Pr275CompanionApiCoverageTests
{
    private static readonly CSharpCompilation Compilation = CSharpCompilation.Create(
        "Pr275CompanionApi",
        [CSharpSyntaxTree.ParseText("""
            public class Target {
                public Target() { }
                public void Read() { }
                public int Value { get; set; }
            }
            """)],
        TestMetadataReferences.WithSharpProof,
        TestCompilation.CreateOptions(OutputKind.DynamicallyLinkedLibrary));

    [TestCase("attribute companion", "companion")]
    [TestCase("attribute type", "contractFor")]
    [TestCase("attribute target", "attribute")]
    [TestCase("target type", "target")]
    [TestCase("ordinary methods", "type")]
    [TestCase("relationship discovery", "compilation")]
    [TestCase("companion resolution", "target")]
    [TestCase("companion type", "type")]
    [TestCase("signature target", "target")]
    [TestCase("signature companion", "companion")]
    public void CompanionApisReportTheMissingArgument(string api, string expectedParameter)
    {
        System.ArgumentNullException.ThrowIfNull(api);
        System.ArgumentNullException.ThrowIfNull(expectedParameter);
        TestCompilation.AssertNoErrors(Compilation);
        var type = Compilation.GetTypeByMetadataName("Target")!;
        var method = type.GetMembers("Read").OfType<IMethodSymbol>().Single();
        var exception = Assert.Throws<ArgumentNullException>((Action)(() =>
        {
            switch (api)
            {
                case "attribute companion":
                    _ = ContractForSymbolMatcher.GetAttributes(null!, type);
                    break;
                case "attribute type":
                    _ = ContractForSymbolMatcher.GetAttributes(type, null!);
                    break;
                case "attribute target":
                    _ = ContractForSymbolMatcher.TryGetTarget(null!, out _);
                    break;
                case "target type":
                    _ = ContractForSymbolMatcher.TargetsType((type, false), null!);
                    break;
                case "ordinary methods":
                    _ = ContractForSymbolMatcher.GetOrdinaryMethods(null!);
                    break;
                case "relationship discovery":
                    _ = ContractForSymbolMatcher.DiscoverCompanionRelationships(null!, cancellationToken: TestContext.CurrentContext.CancellationToken);
                    break;
                case "companion resolution":
                    _ = ContractForSymbolMatcher.ResolveCompanion([], null!);
                    break;
                case "companion type":
                    _ = ContractForSymbolMatcher.IsCompanionType([], null!);
                    break;
                case "signature target":
                    _ = ContractForSymbolMatcher.MemberSignaturesMatch(null!, method);
                    break;
                case "signature companion":
                    _ = ContractForSymbolMatcher.MemberSignaturesMatch(method, null!);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(api));
            }
        }));
        Assert.That(exception!.ParamName, Is.EqualTo(expectedParameter));
    }

    [Test]
    public void OrdinaryMethodEnumerationOmitsConstructorsAndPropertyAccessors()
    {
        TestCompilation.AssertNoErrors(Compilation);
        var type = Compilation.GetTypeByMetadataName("Target")!;
        var methods = ContractForSymbolMatcher.GetOrdinaryMethods(type).ToImmutableArray();

        Assert.That(methods, Has.Length.EqualTo(1));
        Assert.That(methods[0].Name, Is.EqualTo("Read"));
        Assert.That(ContractForSymbolMatcher.IsCompanionType([], type), Is.False);
        Assert.That(ContractForSymbolMatcher.TargetsType((type, false), type), Is.True);
    }

}
