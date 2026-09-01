using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class CrossTreeFreshArrayIdentityRegressionTests
{
    private const int CollidingArrayCreationOffset = 256;

    [Test]
    public void PartialMemberInitializerKeepsItsRuntimeArrayType()
    {
        var initializerTree = ParseWithArrayCreationAtCollision(
            """
            public partial class Sample {
                private object value = ((object[])
            """,
            """
            new string[1])[0] = new object();
            }
            """,
            "Initializer.cs");
        var constructorTree = ParseWithArrayCreationAtCollision(
            """
            public partial class Sample {
                public Sample() {
                    object[] other =
            """,
            """
            new object[1];
                }
            }
            """,
            "Constructor.cs");
        var compilation = EffectTestHost.CreateCompilation(
            [initializerTree, constructorTree]);
        var arrayCreations = compilation.SyntaxTrees
            .Select(tree => tree.GetRoot()
                .DescendantNodes()
                .OfType<ArrayCreationExpressionSyntax>()
                .Single())
            .ToArray();
        var runtimeTypes = arrayCreations
            .Select(creation =>
                ((IArrayCreationOperation)compilation
                    .GetSemanticModel(creation.SyntaxTree)
                    .GetOperation(creation)!).Type!.ToDisplayString())
            .ToArray();
        var type = EffectTestHost.RequireType(compilation, "Sample");
        var constructor = type.InstanceConstructors.Single(
            static candidate => !candidate.IsImplicitlyDeclared);
        var result = new EffectAnalysisSession(compilation)
            .Analyze(constructor);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                arrayCreations[0].SpanStart,
                Is.EqualTo(CollidingArrayCreationOffset));
            Assert.That(
                arrayCreations[1].SpanStart,
                Is.EqualTo(CollidingArrayCreationOffset));
            Assert.That(runtimeTypes[0], Is.EqualTo("string[]"));
            Assert.That(runtimeTypes[1], Is.EqualTo("object[]"));
            Assert.That(
                result.Summary.Throws.Types.Select(
                    static exception => exception.ToDisplayString()),
                Does.Contain("System.ArrayTypeMismatchException"));
            Assert.That(result.Summary.Throws.IncludesUnknown, Is.False);
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
            Assert.That(
                result.Projection.Effects & SharpProofEffect.Throws,
                Is.EqualTo(SharpProofEffect.Throws));
        }
    }

    private static SyntaxTree ParseWithArrayCreationAtCollision(
        string prefix,
        string suffix,
        string path)
    {
        if (prefix.Length >= CollidingArrayCreationOffset)
        {
            throw new ArgumentException(
                "The regression prefix must end before the collision offset.",
                nameof(prefix));
        }

        return CSharpSyntaxTree.ParseText(
            prefix +
            new string(
                ' ',
                CollidingArrayCreationOffset - prefix.Length) +
            suffix,
            CSharpParseOptions.Default.WithLanguageVersion(
                LanguageVersion.CSharp12),
            path: path);
    }
}
