using System.Collections.Immutable;
using System.Numerics;
using System.Reflection;
using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class ResourceExpressionBoundaryTests
{
    private static ResourceExpression Input(string name = "n", int ordinal = 0)
    {
        ArgumentNullException.ThrowIfNull(name);
        return ResourceExpression.InputSize(new ResourceInput(ordinal, ResourceInputKind.NonNegativeScalar, name, 1000));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void MetadataIdentityRequiresEveryBoundedPrintableComponent(int component)
    {
        foreach (var invalid in new[] { "", " ", "bad\nidentity", new string('x', 513) })
        {
            Action construct = () => _ = new ResourceMetadataReference(component == 0 ? invalid : "witness",
                component == 1 ? invalid : "catalog", component == 2 ? invalid : "assembly");
            Assert.Throws<ArgumentException>(construct);
        }
        Action absent = () => _ = new ResourceMetadataReference(component == 0 ? null! : "witness",
            component == 1 ? null! : "catalog", component == 2 ? null! : "assembly");
        Assert.Throws<ArgumentNullException>(absent);
    }

    [Test]
    public void MetadataLeafPreservesItsReferenceAndRejectsNull()
    {
        var reference = new ResourceMetadataReference("witness", "catalog", "assembly");
        var program = ResourceProgram.MetadataCall(reference);
        Assert.That(program.Kind, Is.EqualTo(ResourceProgramKind.MetadataCall));
        Assert.That(program.Metadata, Is.SameAs(reference));
        Assert.That(program.Children, Is.Empty);
        Assert.That(reference.WitnessIdentifier, Is.EqualTo("witness"));
        Assert.That(reference.CatalogSha256, Is.EqualTo("catalog"));
        Assert.That(reference.AssemblyIdentity, Is.EqualTo("assembly"));
        Action absent = () => ResourceProgram.MetadataCall(null!);
        Assert.Throws<ArgumentNullException>(absent);
    }
    [Test]
    public void InputDomainsAndLabelsHaveFiniteConstructionBudgets()
    {
        Action negative = () => _ = new ResourceInput(0, ResourceInputKind.ArrayLength, "n", -1);
        Action huge = () => _ = new ResourceInput(0, ResourceInputKind.ArrayLength, "n", BigInteger.One << 65536);
        Action label = () => _ = new ResourceInput(0, ResourceInputKind.ArrayLength, new string('n', ResourceExpression.MaximumTextLength + 1), 10);
        Action literal = () => ResourceExpression.Literal(BigInteger.One << 65536);
        Assert.Throws<ArgumentOutOfRangeException>(negative);
        Assert.Throws<ResourceExpressionLimitException>(huge);
        Assert.Throws<ResourceExpressionLimitException>(label);
        Assert.Throws<ResourceExpressionLimitException>(literal);
    }

    [Test]
    public void AlignmentAndDifferenceRejectInvalidUnitsAndFoldExactConstants()
    {
        var input = Input();
        Action lower = () => ResourceExpression.Difference(input, -1);
        Action alignment = () => ResourceExpression.Aligned(input, 0);
        Assert.Throws<ArgumentOutOfRangeException>(lower);
        Assert.Throws<ArgumentOutOfRangeException>(alignment);
        Assert.That(ResourceExpression.Aligned(input, 1), Is.SameAs(input));
        Assert.That(ResourceExpression.Aligned(ResourceExpression.Literal(9), 8).Constant, Is.EqualTo(new BigInteger(16)));
    }

    [Test]
    public void GeometricArithmeticCannotConsumeAnUnboundedNumberOfSteps()
    {
        Action ascending = () => ResourceExpression.Geometric(ResourceExpression.Literal(BigInteger.One << 4097), 2, false, TestContext.CurrentContext.CancellationToken);
        Action descending = () => ResourceExpression.Geometric(ResourceExpression.Literal(BigInteger.One << 4096), 2, true, TestContext.CurrentContext.CancellationToken);
        Assert.Throws<ResourceExpressionLimitException>(ascending);
        Assert.Throws<ResourceExpressionLimitException>(descending);
    }

    [Test]
    public void DisplayIsBoundedAndNeverEstablishesSemanticIdentity()
    {
        var input = Input();
        Assert.That(ResourceExpression.Max(input, Input("m", 1)).ToDisplayString(), Is.EqualTo("max(max(0, n), max(0, m))"));
        Assert.That(ResourceExpression.Difference(input, 3).ToDisplayString(), Is.EqualTo("max(0, (max(0, n) - 3))"));
        Assert.That(ResourceExpression.Aligned(input, 8).ToDisplayString(), Is.EqualTo("align(max(0, n), 8)"));
        object boxed = input;
        Assert.That(boxed.Equals(Input()), Is.True);
        Assert.That(boxed.Equals("n"), Is.False);
        Action tooLong = () => Input(new string('n', ResourceExpression.MaximumTextLength)).ToDisplayString();
        Assert.Throws<ResourceExpressionLimitException>(tooLong);
    }

    [Test]
    public void BudgetExceptionsPreserveDiagnosticContext()
    {
        var inner = new InvalidDataException("inner");
        Assert.That(new ResourceExpressionLimitException("context").Message, Is.EqualTo("context"));
        var wrapped = new ResourceExpressionLimitException("context", inner);
        Assert.That(wrapped.InnerException, Is.SameAs(inner));
    }

    private static ResourceExpression Construct(object?[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var constructor = typeof(ResourceExpression).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        return (ResourceExpression)constructor.Invoke(arguments);
    }

    [Test]
    public void CorruptDetachedRecordsCannotBypassOperandAndNumericBudgets()
    {
        Action missing = () => Construct([ResourceExpressionKind.Add, BigInteger.Zero, null, default(ImmutableArray<ResourceExpression>), BigInteger.Zero]);
        Action nullChild = () => Construct([ResourceExpressionKind.Add, BigInteger.Zero, null, ImmutableArray<ResourceExpression>.Empty.Add(null!), BigInteger.Zero]);
        Action maximum = () => Construct([ResourceExpressionKind.Add, BigInteger.Zero, null, ImmutableArray<ResourceExpression>.Empty, BigInteger.One << 65536]);
        Assert.That(Assert.Throws<TargetInvocationException>(missing)!.InnerException, Is.TypeOf<ArgumentException>());
        Assert.That(Assert.Throws<TargetInvocationException>(nullChild)!.InnerException, Is.TypeOf<ArgumentException>());
        Assert.That(Assert.Throws<TargetInvocationException>(maximum)!.InnerException, Is.TypeOf<ResourceExpressionLimitException>());
        var undefined = Construct([(ResourceExpressionKind)999, BigInteger.Zero, null, ImmutableArray.Create(ResourceExpression.One), BigInteger.One]);
        Action evaluate = () => undefined.Evaluate(static _ => BigInteger.Zero, TestContext.CurrentContext.CancellationToken);
        Action display = () => undefined.ToDisplayString();
        Assert.Throws<InvalidOperationException>(evaluate);
        Assert.Throws<InvalidOperationException>(display);
    }
    [Test]
    public void DetachedProgramRecordsMustHaveInitializedNonNullChildren()
    {
        var constructor = typeof(ResourceProgram).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        Action missing = () => constructor.Invoke([ResourceProgramKind.Sequence, null, null, ResourceRepeatKind.Loop, default(ImmutableArray<ResourceProgram>), null, null, null, null]);
        Action nullChild = () => constructor.Invoke([ResourceProgramKind.Sequence, null, null, ResourceRepeatKind.Loop, ImmutableArray<ResourceProgram>.Empty.Add(null!), null, null, null, null]);
        Assert.That(Assert.Throws<TargetInvocationException>(missing)!.InnerException, Is.TypeOf<ArgumentException>());
        Assert.That(Assert.Throws<TargetInvocationException>(nullChild)!.InnerException, Is.TypeOf<ArgumentException>());
    }
}
