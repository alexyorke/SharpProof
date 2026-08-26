using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class InvocationEmissionPolicyTests
{
    [Test]
    public void ConditionalInvocationCacheSupportsConcurrentCompilationAnalysis()
    {
        var trees = Enumerable.Range(0, 64)
            .Select(index => CSharpSyntaxTree.ParseText(
                $$"""
                using System.Diagnostics;
                public static class Subject{{index}} {
                    [Conditional("UNDEFINED")]
                    public static void Trace() { }
                    public static void Call() { Trace(); }
                }
                """,
                CSharpParseOptions.Default.WithLanguageVersion(
                    LanguageVersion.CSharp12),
                path: $"EffectsConditional{index}.cs"))
            .ToArray();
        var compilation = EffectTestHost.CreateCompilation(trees);
        var invocations = trees.Select(tree =>
        {
            var model = compilation.GetSemanticModel(tree);
            var syntax = tree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single();
            return (IInvocationOperation)model.GetOperation(syntax)!;
        }).ToArray();
        var policy = new InvocationEmissionPolicy(compilation);

        Parallel.ForEach(invocations, invocation =>
        {
            Assert.That(policy.IsElided(invocation), Is.True);
        });

        var cache = typeof(InvocationEmissionPolicy)
            .GetField(
                "_definedPreprocessorSymbols",
                BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(cache, Is.Not.Null);
        Assert.That(
            cache!.FieldType,
            Is.EqualTo(typeof(ConcurrentDictionary<
                SyntaxTree,
                ImmutableHashSet<string>>)));
    }
}
