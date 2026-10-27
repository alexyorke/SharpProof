using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;
using SharpProof.Contracts;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class Pr275BindingAndCaptureRequiredInputTests
{
    [Test]
    public void ReachableSourceCollectionRequiresAnInitializedTreeSnapshot()
    {
        var compilation = CreateCompilation();
        var targets = new ClaimManifestBuilder(compilation).Build().Targets.Values;
        var packs = CompilerSpecificationPackProvider.ResolveConfiguration(null);
        var error = Assert.Throws<ArgumentNullException>((Action)(() =>
            CompilerReachableSourceCollector.Collect(
                compilation, targets, default, packs, default)));
        Assert.That(error!.ParamName, Is.EqualTo("trees"));
    }

    private static CSharpCompilation CreateCompilation()
    {
        return TestCompilation.CreateExact("Pr275RequiredBindingCapture",
            [("Subject.cs", """
                using SharpProof.Attributes;
                internal static class Subject {
                    internal static int Target(int value) {
                        Contract.Ensures(Contract.Result<int>() == value);
                        return value;
                    }
                }
                """),
            ("Other.cs", "internal sealed class Other { }")]);
    }

    [Test]
    public void SuccessfulBindingRequiresItsBoundContracts()
    {
        var error = Assert.Throws<ArgumentNullException>((Action)(() =>
            ContractBindingResult.Success(null!)));

        Assert.That(error!.ParamName, Is.EqualTo("contracts"));
    }

    [Test]
    public void SuccessfulBindingRetainsContractsFromTheActualSourceBinder()
    {
        var compilation = CreateCompilation();
        var method = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Target")
            .OfType<IMethodSymbol>().Single();
        var original = new ContractBinder(compilation, new IrFactory()).Bind(method);
        Assert.That(original.IsSuccess, Is.True);

        var result = ContractBindingResult.Success(original.Contracts!);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Contracts, Is.SameAs(original.Contracts));
    }

    [Test]
    public void ReachableSourceCollectionRequiresEveryActualTreeSnapshot()
    {
        var compilation = CreateCompilation();
        var targets = new ClaimManifestBuilder(compilation).Build().Targets.Values;
        var trees = CompilerCompilationCapture.CaptureTrees(compilation, default);
        var packs = CompilerSpecificationPackProvider.ResolveConfiguration(null);
        var partialTrees = trees.Take(trees.Count - 1).ToImmutableArray();

        var error = Assert.Throws<ArgumentException>((Action)(() =>
            CompilerReachableSourceCollector.Collect(compilation, targets, partialTrees, packs, default)));

        Assert.That(error!.ParamName, Is.EqualTo("trees"));
    }

    [Test]
    public void ReachableSourceCollectionAcceptsTheCompleteActualTreeCapture()
    {
        var compilation = CreateCompilation();
        var targets = new ClaimManifestBuilder(compilation).Build().Targets.Values;
        var trees = CompilerCompilationCapture.CaptureTrees(compilation, default);
        var packs = CompilerSpecificationPackProvider.ResolveConfiguration(null);

        var result = CompilerReachableSourceCollector.Collect(compilation, targets, trees.ToImmutableArray(), packs, default);

        Assert.That(result.CollectionComplete, Is.True);
        Assert.That(result.Roots, Has.Length.EqualTo(1));
        Assert.That(result.Bodies, Is.Not.Empty);
    }

    [Test]
    public void ARealAccessorTargetCannotBeUsedAsABaseMethodDeclaration()
    {
        var compilation = TestCompilation.Create("Pr275AccessorDeclaration",
            "using SharpProof.Attributes; internal sealed class Subject { " +
            "internal int Value { [DoesNotThrow] get { return 0; } } }");
        var target = new ClaimManifestBuilder(compilation).Build().Targets.Values.Single();

        Assert.That(target.Method.MethodKind, Is.EqualTo(MethodKind.PropertyGet));
        Assert.That(target.VerifierSemanticModel, Is.Not.Null);
        Assert.Throws<InvalidOperationException>((Action)(() => _ = target.VerifierDeclaration));
    }
}
