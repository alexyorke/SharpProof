using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.Specs;

namespace SharpProof.Specs.Test;

[TestFixture]
public sealed class CollectionApiSpecResolutionTests
{
    [Test]
    public void GetterEvidenceDoesNotApproveSettersInterfacesComparersOrResultRelations()
    {
        var paths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? throw new InvalidOperationException("Runtime references unavailable.")).Split(Path.PathSeparator).ToImmutableArray();
        var compilation = CSharpCompilation.Create("CollectionResolution", references: paths.Append(typeof(Contract).Assembly.Location).Distinct(StringComparer.Ordinal).Select(path => path is null ? throw new global::System.ArgumentNullException(nameof(path)) : (MetadataReference.CreateFromFile(path))));
        var resolved = new ApiSpecResolver(ApiSpecTable.Default).Resolve(compilation);
        Assert.That(resolved.Failures, Is.Empty);
        IPropertySymbol Indexer(string name)
        {
            global::System.ArgumentNullException.ThrowIfNull(name);
            return compilation.GetTypeByMetadataName(name)!.GetMembers().OfType<IPropertySymbol>().Single(property => property is null ? throw new global::System.ArgumentNullException(nameof(property)) : (property.IsIndexer));
        }

        var list = Indexer("System.Collections.Generic.List`1");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolved.TryGet(list.GetMethod!, out _), Is.True);
            Assert.That(resolved.TryGet(list.SetMethod!, out _), Is.False);
            Assert.That(resolved.TryGet(Indexer("System.Collections.Generic.IList`1").GetMethod!, out _), Is.False);
            Assert.That(resolved.TryGet(Indexer("System.Collections.Generic.Dictionary`2").GetMethod!, out _), Is.False);
            Assert.That(ApiSpecTable.Default.Templates.Where(template => template is null ? throw new global::System.ArgumentNullException(nameof(template)) : (template.Target.WitnessIdentifier is "bcl.dictionary.count" or "bcl.list.count" or "bcl.queue.count" or "bcl.stack.count" or "bcl.list.item.int32")).SelectMany(template => template is null ? throw new global::System.ArgumentNullException(nameof(template)) : (template.Postconditions)), Is.Empty);
        }
    }
}
