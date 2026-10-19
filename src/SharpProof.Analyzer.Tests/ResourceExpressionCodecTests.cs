using System.Numerics;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class ResourceExpressionCodecTests
{
    private const string Zero = "{\"kind\":0,\"number\":\"0\",\"input\":null,\"operands\":[]}";
    private static string Document(string node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return "{\"version\":1,\"expression\":" + node + "}";
    }

    [Test]
    public void ArithmeticAndInputIdentityRoundTripCanonically()
    {
        var input = ResourceExpression.InputSize(new ResourceInput(1, ResourceInputKind.ArrayLength, "values", int.MaxValue));
        var expression = ResourceExpression.Aligned(ResourceExpression.Multiply(input, ResourceExpression.Literal(3)), 8);
        var encoded = ResourceExpressionCodec.Encode(expression, TestContext.CurrentContext.CancellationToken);
        var decoded = ResourceExpressionCodec.Decode(encoded, TestContext.CurrentContext.CancellationToken);
        Assert.That(decoded.Equals(expression), Is.True);
        Assert.That(ResourceExpressionCodec.Encode(decoded, TestContext.CurrentContext.CancellationToken), Is.EqualTo(encoded));
        Assert.That(decoded.Evaluate(static _ => 5, TestContext.CurrentContext.CancellationToken), Is.EqualTo(new BigInteger(16)));
    }

    [TestCase("{\"kind\":99,\"number\":\"0\",\"input\":null,\"operands\":[]}")]
    [TestCase("{\"kind\":0,\"kind\":0,\"number\":\"0\",\"input\":null,\"operands\":[]}")]
    [TestCase("{\"kind\":0,\"number\":\"-1\",\"input\":null,\"operands\":[]}")]
    [TestCase("{\"kind\":0,\"number\":\"00\",\"input\":null,\"operands\":[]}")]
    [TestCase("{\"kind\":0,\"number\":0,\"input\":null,\"operands\":[]}")]
    [TestCase("{\"kind\":0,\"number\":\"0\",\"input\":null,\"operands\":null}")]
    [TestCase("{\"kind\":0,\"number\":\"0\",\"input\":null,\"operands\":[],\"extra\":true}")]
    [TestCase("{\"kind\":1,\"number\":\"0\",\"input\":{\"ordinal\":-1,\"kind\":0,\"name\":\"n\",\"maximum\":\"10\"},\"operands\":[]}")]
    [TestCase("{\"kind\":1,\"number\":\"0\",\"input\":{\"ordinal\":0,\"kind\":99,\"name\":\"n\",\"maximum\":\"10\"},\"operands\":[]}")]
    public void MalformedNodesCannotBecomeBounds(string node)
    {
        ArgumentNullException.ThrowIfNull(node);
        Action decode = () => ResourceExpressionCodec.Decode(Encoding.UTF8.GetBytes(Document(node)), TestContext.CurrentContext.CancellationToken);
        Assert.Throws<InvalidDataException>(decode);
    }

    [TestCase("{}")]
    [TestCase("{\"version\":99,\"expression\":null}")]
    [TestCase("{\"version\":1,\"version\":1,\"expression\":null}")]
    [TestCase("[]")]
    public void InvalidEnvelopeIsRejected(string document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Action decode = () => ResourceExpressionCodec.Decode(Encoding.UTF8.GetBytes(document), TestContext.CurrentContext.CancellationToken);
        Assert.Throws<InvalidDataException>(decode);
    }

    [Test]
    public void RawNodeBudgetIsEnforcedBeforeConstantFolding()
    {
        var node = Zero;
        for (int level = 0; level < 12; level++)
        { node = "{\"kind\":2,\"number\":\"0\",\"input\":null,\"operands\":[" + node + "," + node + "]}"; }
        var payload = Encoding.UTF8.GetBytes(Document(node));
        Assert.That(payload.Length, Is.LessThan(ResourceExpressionCodec.MaximumPayloadBytes));
        Action decode = () => ResourceExpressionCodec.Decode(payload, TestContext.CurrentContext.CancellationToken);
        var exception = Assert.Throws<InvalidDataException>(decode);
        Assert.That(exception!.InnerException, Is.InstanceOf<ResourceExpressionLimitException>());
    }

    [Test]
    public void PayloadAndDepthBudgetsAreEnforced()
    {
        Action tooLarge = () => ResourceExpressionCodec.Decode(new byte[ResourceExpressionCodec.MaximumPayloadBytes + 1], TestContext.CurrentContext.CancellationToken);
        Assert.Throws<InvalidDataException>(tooLarge);
        var node = Zero;
        for (int level = 0; level < ResourceExpression.MaximumDepth + 1; level++)
        { node = "{\"kind\":6,\"number\":\"1\",\"input\":null,\"operands\":[" + node + "]}"; }
        Action tooDeep = () => ResourceExpressionCodec.Decode(Encoding.UTF8.GetBytes(Document(node)), TestContext.CurrentContext.CancellationToken);
        Assert.Throws<InvalidDataException>(tooDeep);
    }

    [Test]
    public void CodecCancellationIsNotConvertedToMalformedEvidence()
    {
        Action decode = () => ResourceExpressionCodec.Decode(Encoding.UTF8.GetBytes(Document(Zero)), new CancellationToken(true));
        Action encode = () => ResourceExpressionCodec.Encode(ResourceExpression.One, new CancellationToken(true));
        Assert.Throws<OperationCanceledException>(decode);
        Assert.Throws<OperationCanceledException>(encode);
    }

    [Test]
    public void AnalyzerBoundsCrossTheArtifactCodecWithInputOrdinals()
    {
        const string source = "public class C { public void M(int n) { for(int i=0;i<n;i++) { var a = new int[2]; } } }";
        var compilation = AnalyzerTestHost.CreateCompilation(source, []);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var result = ResourceBoundsAnalysis.Analyze((IMethodSymbol)model.GetDeclaredSymbol(method)!,
            model.GetOperation(method.Body!), TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Proven, Is.True, result.UnknownReason);
        var payload = ResourceExpressionCodec.Encode(result.Bounds!.Elements.Expression, TestContext.CurrentContext.CancellationToken);
        var expression = ResourceExpressionCodec.Decode(payload, TestContext.CurrentContext.CancellationToken);
        Assert.That(expression.Evaluate(input => input is null ? throw new ArgumentNullException(nameof(input)) :
            input.Ordinal == 0 ? 3 : throw new InvalidOperationException("Input is not owned by this fixture."), TestContext.CurrentContext.CancellationToken), Is.EqualTo(new BigInteger(6)));
    }
}
