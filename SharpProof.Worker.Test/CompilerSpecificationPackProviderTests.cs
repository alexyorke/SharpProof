using NUnit.Framework;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharpProof.CompilerArtifact;
using SharpProof.Frontend;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerSpecificationPackProviderTests
{
    [TestCase(int.MinValue, int.MaxValue)]
    [TestCase(int.MaxValue, int.MinValue)]
    [TestCase(int.MinValue, int.MinValue)]
    [TestCase(int.MaxValue, int.MaxValue)]
    [TestCase(-1, 0)]
    [TestCase(0, -1)]
    [TestCase(0, 0)]
    public void TotalSpecificationPackAgreesWithRuntimeMaximum(int left, int right)
    {
        var compilation = CSharpCompilation.Create("PackRuntimeOracle",
            references: [MetadataReference.CreateFromFile(typeof(Math).Assembly.Location)]);
        var method = compilation.GetTypeByMetadataName("System.Math")!.GetMembers("Max").OfType<IMethodSymbol>()
            .Single(candidate => candidate.Parameters.Length == 2 &&
                candidate.Parameters.All(parameter => parameter.Type.SpecialType == SpecialType.System_Int32));
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var disabled = new CompilerSpecificationPackProvider(factory, (IEnumerable<string>?)null);
        Assert.That(disabled.ResolveTotal(method), Is.Null);
        var enabled = new CompilerSpecificationPackProvider(factory, ["dotnet.scalar"]);
        var model = enabled.ResolveTotal(method);
        Assert.That(model, Is.Not.Null);
        var integerType = factory.GetOrCreateIntegerType(32, true);
        var rule = model!.Apply([factory.Integer(integerType, left), factory.Integer(integerType, right)]);
        Assert.That(rule.Classification.IsExact, Is.True);
        Assert.That(rule.Throws, Is.Empty);
        var interpreted = new IrInterpreter(factory).Evaluate(rule.Value, new Dictionary<IrVarId, IrValue>());
        Assert.That(interpreted.Value!.Integer, Is.EqualTo(Math.Max(left, right)));
        Assert.That(interpreted.Value.Type, Is.EqualTo(integerType));
        Assert.That(model.Apply([factory.Boolean(true), factory.Integer(integerType, right)]).Classification.IsExact, Is.False);
        Assert.That(model.Apply([]).Classification.IsExact, Is.False);
    }

    [Test]
    public void ExplicitPackProviderUsesTheValidatedCatalogSelection()
    {
        Assert.That(new CompilerSpecificationPackProvider(new IrFactory(), ["dotnet.scalar"]), Is.Not.Null);
        Assert.Throws<InvalidOperationException>((Action)(() =>
            new CompilerSpecificationPackProvider(new IrFactory(), ["missing.pack"])));
    }

    [Test]
    public void SerializedPackSelectionsRejectAmbiguousAndForeignIdentities()
    {
        var version = CompilerSpecificationPackCatalogVersions.Current;
        var hash = CompilerSpecificationPackCatalogVersions.Sha256;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(CompilerSpecificationPackSelection.IsValid(["dotnet.scalar"], version, hash), Is.True);
            Assert.That(CompilerSpecificationPackSelection.IsValid(["dotnet.scalar", "dotnet.scalar"], version, hash), Is.False);
            Assert.That(CompilerSpecificationPackSelection.IsValid(["z.pack", "dotnet.scalar"], version, hash), Is.False);
            Assert.That(CompilerSpecificationPackSelection.IsValid(["dotnet.scalar"], version + 1, hash), Is.False);
            Assert.That(CompilerSpecificationPackSelection.IsValid(null, version, hash), Is.False);
            Assert.That(CompilerSpecificationPackSelection.Matches(new CompilerManifestArtifact
            {
                SpecificationPackIds = ["dotnet.scalar"],
                SpecificationPackCatalogVersion = version,
                SpecificationPackCatalogSha256 = hash
            }), Is.True);
            Assert.That(CompilerSpecificationPackSelection.Matches(new CompilerManifestArtifact
            {
                Compilation = null!
            }), Is.False);
            Assert.That(CompilerSpecificationPackSelection.GetSummaryPrefix((CompilerSummaryOrigin)int.MaxValue), Is.Null);
        }
    }

    [Test]
    public void SelectionAuthorityIsExplicitCanonicalAndCatalogBound()
    {
        var unset = CompilerSpecificationPackProvider.ResolveConfiguration(null);
        var selected = CompilerSpecificationPackProvider.ResolveConfiguration(
            [" dotnet.scalar "]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(unset.SpecificationPackIds, Is.Empty);
            Assert.That(selected.SpecificationPackIds,
                Is.EqualTo(["dotnet.scalar"]));
            Assert.That(unset.SpecificationPackCatalogVersion,
                Is.EqualTo(CompilerSpecificationPackCatalogVersions.Current));
            Assert.That(unset.SpecificationPackCatalogSha256,
                Is.EqualTo(CompilerSpecificationPackCatalogVersions.Sha256));
        }

        Assert.Throws<InvalidOperationException>((Action)(() =>
            CompilerSpecificationPackProvider.ResolveConfiguration(
                ["dotnet.scalar", "dotnet.scalar"])));
        Assert.Throws<InvalidOperationException>((Action)(() =>
            CompilerSpecificationPackProvider.ResolveConfiguration(
                ["dotnet.scalar", "missing.pack"])));
    }
}
