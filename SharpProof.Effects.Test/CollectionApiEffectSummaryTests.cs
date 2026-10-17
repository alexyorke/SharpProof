using SharpProof.Specs;

namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class CollectionApiEffectSummaryTests
{
    [Test]
    public void KnownExceptionAndAllocationFacetsSurviveUnknownIndexerEffects()
    {
        var paths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ??
            throw new InvalidOperationException("Runtime references unavailable.")).Split(Path.PathSeparator);
        var compilation = CSharpCompilation.Create("CollectionEffectProjection", references: paths
            .Append(typeof(Contract).Assembly.Location).Distinct(StringComparer.Ordinal).Select(path => MetadataReference.CreateFromFile(path)));
        var getter = compilation.GetTypeByMetadataName("System.Collections.Generic.List`1")!.GetMembers()
            .OfType<IPropertySymbol>().Single(property => property.IsIndexer).GetMethod!;
        var summary = new ExternalEffectResolver(compilation, ApiSpecTable.Default).Resolve(getter);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(summary.Reads.IsUnknown, Is.True);
            Assert.That(summary.Writes.IsUnknown, Is.True);
            Assert.That(summary.Throws.IncludesUnknown, Is.False);
            Assert.That(summary.Throws.Types.Single().ToDisplayString(), Is.EqualTo("System.ArgumentOutOfRangeException"));
            Assert.That(summary.Allocation, Is.Not.EqualTo(EffectAllocationKind.None));
        }
    }
}
