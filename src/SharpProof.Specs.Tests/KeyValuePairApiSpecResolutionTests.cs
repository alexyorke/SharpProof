using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Specs;

namespace SharpProof.Specs.Test;

[TestFixture]
public sealed class KeyValuePairApiSpecResolutionTests
{
    [Test]
    public void GetterFacetsDoNotApproveConstructorsFormattingOrResultRelations()
    {
        var compilation = FrameworkCompilation();
        var resolved = new ApiSpecResolver(ApiSpecTable.Default).Resolve(compilation);
        var pair = compilation.GetTypeByMetadataName("System.Collections.Generic.KeyValuePair`2")!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolved.Failures, Is.Empty);
            foreach (var name in new[] { "Key", "Value" })
            {
                var getter = pair.GetMembers(name).OfType<IPropertySymbol>().Single().GetMethod!;
                Assert.That(resolved.TryGet(getter, out _), Is.True);
            }
            foreach (var method in pair.GetMembers().OfType<IMethodSymbol>().Where(method => method is null ? throw new ArgumentNullException(nameof(method)) : method.MethodKind == MethodKind.Constructor || method.Name is "ToString" or "Deconstruct"))
            {
                Assert.That(resolved.TryGet(method, out _), Is.False, method.ToDisplayString());
            }
            Assert.That(ApiSpecTable.Default.Templates.Where(template => template is null ? throw new ArgumentNullException(nameof(template)) : template.Target.WitnessIdentifier.StartsWith("bcl.key-value-pair.", StringComparison.Ordinal)).SelectMany(template => template is null ? throw new ArgumentNullException(nameof(template)) : template.Postconditions), Is.Empty);
        }
    }

    [Test]
    public void SourceDefinedPairLookalikesReceiveNoFrameworkGetterEvidence()
    {
        var compilation = FrameworkCompilation().AddSyntaxTrees(CSharpSyntaxTree.ParseText("namespace System.Collections.Generic { public struct KeyValuePair<TKey, TValue> { public TKey Key => throw new System.Exception(); public TValue Value => throw new System.Exception(); } }"));
        var pair = compilation.Assembly.GetTypeByMetadataName("System.Collections.Generic.KeyValuePair`2")!;
        var resolved = new ApiSpecResolver(ApiSpecTable.Default).Resolve(compilation);
        foreach (var property in pair.GetMembers().OfType<IPropertySymbol>())
        {
            Assert.That(resolved.TryGet(property.GetMethod!, out _), Is.False);
        }
    }

    private static CSharpCompilation FrameworkCompilation()
    {
        var paths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? throw new InvalidOperationException("Runtime references unavailable.")).Split(Path.PathSeparator);
        return CSharpCompilation.Create("PairResolution", references: paths.Select(path => path is null ? throw new ArgumentNullException(nameof(path)) : MetadataReference.CreateFromFile(path)));
    }
}
