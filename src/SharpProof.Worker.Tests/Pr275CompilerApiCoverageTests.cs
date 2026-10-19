using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class Pr275CompilerApiCoverageTests
{
    private static readonly CSharpCompilation Compilation = TestCompilation.Create(
        "Pr275CompilerApi", ("Subject.cs", """
            using SharpProof.Attributes;
            internal static class Subject {
                internal static int Identity(int value) {
                    Contract.Ensures(Contract.Result<int>() == value);
                    return value;
                }
            }
            """));

    [TestCase("entry", "compilation")]
    [TestCase("entry", "target")]
    [TestCase("entry", "capturedTrees")]
    [TestCase("callable", "compilation")]
    [TestCase("callable", "target")]
    [TestCase("callable", "capturedTrees")]
    [TestCase("callers", "compilation")]
    [TestCase("callers", "capturedTrees")]
    [TestCase("initialization", "compilation")]
    [TestCase("initialization", "type")]
    public void TotalSourceLoweringRequiresItsInputs(string api, string missing)
    {
        System.ArgumentNullException.ThrowIfNull(api);
        System.ArgumentNullException.ThrowIfNull(missing);
        var target = new ClaimManifestBuilder(Compilation).Build().Targets.Values.Single();
        var trees = CompilerCompilationCapture.CaptureTrees(Compilation, default);
        var packs = CompilerSpecificationPackProvider.ResolveConfiguration(null);
        var type = Compilation.GetTypeByMetadataName("Subject")!;

        var exception = Assert.Throws<ArgumentNullException>((Action)(() =>
        {
            switch (api)
            {
                case "entry":
                    _ = CompilerTotalCallableLowerer.PrepareEntry(
                        missing == "compilation" ? null! : Compilation,
                        missing == "target" ? null! : target,
                        missing == "capturedTrees" ? default(ImmutableArray<CompilerSyntaxTreeSnapshot>) : trees.ToImmutableArray(), default);
                    break;
                case "callable":
                    _ = CompilerTotalCallableLowerer.Prepare(
                        missing == "compilation" ? null! : Compilation,
                        missing == "target" ? null! : target,
                        missing == "capturedTrees" ? default(ImmutableArray<CompilerSyntaxTreeSnapshot>) : trees.ToImmutableArray(), null, packs, default);
                    break;
                case "callers":
                    _ = CompilerTotalCallableLowerer.PrepareShadowCallers(
                        missing == "compilation" ? null! : Compilation, WorkerFeatureSet.All,
                        missing == "capturedTrees" ? default(ImmutableArray<CompilerSyntaxTreeSnapshot>) : trees.ToImmutableArray(), null, packs, default);
                    break;
                case "initialization":
                    _ = CompilerTotalCallableLowerer.HasNoEffectEntryInitialization(
                        missing == "compilation" ? null! : Compilation,
                        missing == "type" ? null! : type, default);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(api));
            }
        }));

        Assert.That(exception!.ParamName, Is.EqualTo(missing));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DefaultReferenceSnapshotIsRejectedBeforeAdmission(bool shadow)
    {
        var target = new ClaimManifestBuilder(Compilation).Build().Targets.Values.Single();
        var trees = CompilerCompilationCapture.CaptureTrees(Compilation, default).ToImmutableArray();
        var packs = CompilerSpecificationPackProvider.ResolveConfiguration(null);
        var exception = Assert.Throws<ArgumentNullException>((Action)(() =>
        {
            if (shadow)
            {
                _ = CompilerTotalCallableLowerer.PrepareShadowCallers(
                    Compilation, WorkerFeatureSet.All, trees,
                    default(ImmutableArray<CompilerReferenceSnapshot>), packs, default);
            }
            else
            {
                _ = CompilerTotalCallableLowerer.Prepare(
                    Compilation, target, trees,
                    default(ImmutableArray<CompilerReferenceSnapshot>), packs, default);
            }
        }));
        Assert.That(exception!.ParamName, Is.EqualTo("capturedReferences"));
    }

    [Test]
    public void CapturedScalarSourceHasEntryAndBodyPreparations()
    {
        var target = new ClaimManifestBuilder(Compilation).Build().Targets.Values.Single();
        var trees = CompilerCompilationCapture.CaptureTrees(Compilation, default);
        var packs = CompilerSpecificationPackProvider.ResolveConfiguration(null);

        var entry = CompilerTotalCallableLowerer.PrepareEntry(Compilation, target, trees.ToImmutableArray(), default);
        var callable = CompilerTotalCallableLowerer.Prepare(Compilation, target, trees.ToImmutableArray(), null, packs, default);

        Assert.That(entry, Is.Not.Null);
        Assert.That(callable, Is.Not.Null);
        Assert.That(CompilerTotalCallableLowerer.HasNoEffectEntryInitialization(
            Compilation, target.Method.ContainingType, default), Is.True);
    }

    [Test]
    public void MissingCompilerConstructionInputsReportTheirArgumentNames()
    {
        var packs = CompilerSpecificationPackProvider.ResolveConfiguration(null);
        var compilationError = Assert.Throws<ArgumentNullException>((Action)(() =>
            _ = new CompilerCallableLowerer(null!, new IrFactory(), packs)));
        var factoryError = Assert.Throws<ArgumentNullException>((Action)(() =>
            _ = new CompilerCallableLowerer(Compilation, null!, packs)));
        var packsError = Assert.Throws<ArgumentNullException>((Action)(() =>
            _ = new CompilerCallableLowerer(Compilation, new IrFactory(),
                (CompilerSpecificationPackConfiguration)null!)));
        var optionsError = Assert.Throws<ArgumentNullException>((Action)(() =>
            _ = CompilerOptionWireMappings.ReadInternalBoolean((CSharpCompilationOptions)null!, "ReferencesSupersedeLowerVersions")));
        var locationError = Assert.Throws<ArgumentNullException>((Action)(() =>
            CompilerSourceCoordinates.RememberTree(null!, 0)));

        Assert.That(compilationError!.ParamName, Is.EqualTo("compilation"));
        Assert.That(factoryError!.ParamName, Is.EqualTo("factory"));
        Assert.That(packsError!.ParamName, Is.EqualTo("specificationPackAuthority"));
        Assert.That(optionsError!.ParamName, Is.EqualTo("options"));
        Assert.That(locationError!.ParamName, Is.EqualTo("location"));
    }
}
