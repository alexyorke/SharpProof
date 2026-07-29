using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Specs.Test;

[TestFixture]
public sealed class ApiSpecInstantiationCoverageTests
{
    private static readonly SpecEvidence Evidence =
        new(SpecEvidenceKind.Observed, "instantiation-coverage");

    [Test]
    public void EveryTotalExpressionShapeInstantiatesIntoDestinationIr()
    {
        var receiver = Variable(
            SpecVariableRole.Receiver,
            -1,
            SpecValueType.String);
        var boolean = Variable(
            SpecVariableRole.Parameter,
            0,
            SpecValueType.Boolean);
        var integer = Variable(
            SpecVariableRole.Parameter,
            1,
            SpecValueType.Integer);
        var text = Variable(
            SpecVariableRole.Parameter,
            2,
            SpecValueType.String);
        var reference = Variable(
            SpecVariableRole.Parameter,
            3,
            SpecValueType.Reference);
        var sequence = Variable(
            SpecVariableRole.Parameter,
            4,
            SpecValueType.Sequence);
        var result = Variable(
            SpecVariableRole.Result,
            -1,
            SpecValueType.Boolean);
        var postconditions = new List<SpecTermDeclaration> {
            result,
            new SpecBooleanDeclaration(true),
            new SpecUnaryDeclaration(
                SpecUnaryOperator.Not,
                new SpecBooleanDeclaration(false),
                SpecValueType.Boolean),
            Equal(
                new SpecUnaryDeclaration(
                    SpecUnaryOperator.Negate,
                    new SpecIntegerDeclaration(7),
                    SpecValueType.Integer),
                new SpecIntegerDeclaration(-7)),
            Equal(Binary(
                SpecBinaryOperator.Add,
                Integer(2),
                Integer(3),
                SpecValueType.Integer), Integer(5)),
            Equal(Binary(
                SpecBinaryOperator.Subtract,
                Integer(7),
                Integer(2),
                SpecValueType.Integer), Integer(5)),
            Equal(Binary(
                SpecBinaryOperator.Multiply,
                Integer(4),
                Integer(3),
                SpecValueType.Integer), Integer(12)),
            Equal(Binary(
                SpecBinaryOperator.Divide,
                Integer(12),
                Integer(4),
                SpecValueType.Integer), Integer(3)),
            Equal(Binary(
                SpecBinaryOperator.Remainder,
                Integer(14),
                Integer(4),
                SpecValueType.Integer), Integer(2)),
            Binary(
                SpecBinaryOperator.AndAlso,
                new SpecBooleanDeclaration(true),
                boolean,
                SpecValueType.Boolean),
            Binary(
                SpecBinaryOperator.OrElse,
                new SpecBooleanDeclaration(false),
                boolean,
                SpecValueType.Boolean),
            Equal(integer, integer),
            Binary(
                SpecBinaryOperator.NotEqual,
                Integer(1),
                Integer(2),
                SpecValueType.Boolean),
            Compare(SpecBinaryOperator.LessThan, 1, 2),
            Compare(SpecBinaryOperator.LessThanOrEqual, 2, 2),
            Compare(SpecBinaryOperator.GreaterThan, 2, 1),
            Compare(SpecBinaryOperator.GreaterThanOrEqual, 2, 2),
            Equal(Binary(
                SpecBinaryOperator.StringConcat,
                new SpecStringDeclaration("sharp"),
                new SpecStringDeclaration("proof"),
                SpecValueType.String), new SpecStringDeclaration("sharpproof")),
            new SpecConditionalDeclaration(
                boolean,
                new SpecBooleanDeclaration(true),
                new SpecBooleanDeclaration(false),
                SpecValueType.Boolean),
            Equal(
                new SpecLengthDeclaration(receiver),
                new SpecIntegerDeclaration(3)),
            Equal(
                new SpecNullDeclaration(SpecValueType.String),
                new SpecNullDeclaration(SpecValueType.String)),
            Equal(
                new SpecNullDeclaration(SpecValueType.Reference),
                new SpecNullDeclaration(SpecValueType.Reference)),
            Equal(text, text),
            Equal(reference, reference),
            Equal(sequence, sequence)
        };
        var template = CreateTemplate(
            isStatic: false,
            receiverType: SpecValueType.String,
            parameterTypes: [
                SpecValueType.Boolean,
                SpecValueType.Integer,
                SpecValueType.String,
                SpecValueType.Reference,
                SpecValueType.Sequence
            ],
            resultType: SpecValueType.Boolean,
            postconditions);
        var factory = new IrFactory();
        var sequenceType =
            factory.GetOrCreateSequenceType(factory.IntegerType);
        var substitutions = new Dictionary<SpecVarId, IrTerm>
        {
            [template.Receiver!.Value] = factory.String("abc"),
            [template.Parameters[0]] = factory.Boolean(true),
            [template.Parameters[1]] = factory.Integer(42),
            [template.Parameters[2]] = factory.String("value"),
            [template.Parameters[3]] = factory.Variable(
                factory.CreateVariable(
                    "reference",
                    factory.ObjectType)),
            [template.Parameters[4]] = factory.Variable(
                factory.CreateVariable(
                    "sequence",
                    sequenceType)),
            [template.Result!.Value] = factory.Boolean(true)
        };

        var instantiated =
            ApiSpecInstantiator.InstantiatePostconditions(
                template,
                factory,
                substitutions);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                instantiated.Status,
                Is.EqualTo(SpecInstantiationStatus.Succeeded));
            Assert.That(instantiated.Failure, Is.Null);
            Assert.That(
                instantiated.Postconditions,
                Has.Length.EqualTo(postconditions.Count));
            Assert.That(
                instantiated.Postconditions.Select(
                    term => factory.GetTypeInfo(term.Type).Kind),
                Is.All.EqualTo(IrTypeKind.Boolean));
        }
    }

    [Test]
    public void SubstitutionOwnershipAndTypesFailClosedWithTypedReasons()
    {
        var template = CreateTemplate(
            isStatic: true,
            receiverType: null,
            parameterTypes: [SpecValueType.Integer],
            resultType: null,
            [Equal(
                Variable(
                    SpecVariableRole.Parameter,
                    0,
                    SpecValueType.Integer),
                Integer(0))]);
        var otherTemplate = CreateTemplate(
            isStatic: true,
            receiverType: null,
            parameterTypes: [SpecValueType.Integer],
            resultType: null,
            [new SpecBooleanDeclaration(true)]);
        var factory = new IrFactory();
        var foreignFactory = new IrFactory();

        var foreignVariable =
            ApiSpecInstantiator.InstantiatePostconditions(
                template,
                factory,
                new Dictionary<SpecVarId, IrTerm>
                {
                    [otherTemplate.Parameters.Single()] =
                        factory.Integer(0)
                });
        var foreignTerm =
            ApiSpecInstantiator.InstantiatePostconditions(
                template,
                factory,
                new Dictionary<SpecVarId, IrTerm>
                {
                    [template.Parameters.Single()] =
                        foreignFactory.Integer(0)
                });
        var wrongType =
            ApiSpecInstantiator.InstantiatePostconditions(
                template,
                factory,
                new Dictionary<SpecVarId, IrTerm>
                {
                    [template.Parameters.Single()] =
                        factory.Boolean(false)
                });

        using (Assert.EnterMultipleScope())
        {
            AssertFailure(
                foreignVariable,
                SpecInstantiationFailureKind.ForeignVariable);
            AssertFailure(
                foreignTerm,
                SpecInstantiationFailureKind.ForeignIrTerm);
            AssertFailure(
                wrongType,
                SpecInstantiationFailureKind.TypeMismatch);
        }
    }

    [Test]
    public void SequenceNullProducesAnExplicitUnsupportedValueFailure()
    {
        var nullSequence =
            new SpecNullDeclaration(SpecValueType.Sequence);
        var template = CreateTemplate(
            isStatic: true,
            receiverType: null,
            parameterTypes: [],
            resultType: null,
            [Equal(nullSequence, nullSequence)]);

        var instantiated =
            ApiSpecInstantiator.InstantiatePostconditions(
                template,
                new IrFactory(),
                ImmutableDictionary<SpecVarId, IrTerm>.Empty);

        AssertFailure(
            instantiated,
            SpecInstantiationFailureKind.UnsupportedValueType);
    }

    private static void AssertFailure(
        SpecInstantiationResult result,
        SpecInstantiationFailureKind kind)
    {
        Assert.That(
            result.Status,
            Is.EqualTo(SpecInstantiationStatus.Failed));
        Assert.That(result.Postconditions, Is.Empty);
        Assert.That(result.Failure, Is.Not.Null);
        Assert.That(result.Failure!.Kind, Is.EqualTo(kind));
        Assert.That(result.Failure.Detail, Is.Not.Empty);
    }

    private static ApiSpecTemplate CreateTemplate(
        bool isStatic,
        SpecValueType? receiverType,
        ImmutableArray<SpecValueType> parameterTypes,
        SpecValueType? resultType,
        IEnumerable<SpecTermDeclaration> postconditions)
    {
        var declaration = new ApiSpecDeclaration(
            new ApiSpecTarget(
                "instantiation-" + Guid.NewGuid().ToString("N"),
                "M:Coverage.Target",
                "Coverage",
                SpecTargetMemberKind.Method,
                "Target",
                isStatic,
                0,
                receiverType,
                parameterTypes,
                resultType,
                [new ApiSpecAssemblyIdentity("Coverage", string.Empty)]),
            new ApiSpecFacets(
                new SpecEffectFacet(SpecEffect.None, Evidence),
                new SpecAllocationFacet(
                    SpecAllocationBehavior.None,
                    Evidence),
                new SpecThrowFacet(
                    SpecThrowBehavior.DoesNotThrow,
                    [],
                    Evidence),
                new SpecNullnessFacet(
                    SpecNullness.NotApplicable,
                    Evidence),
                new SpecCardinalityFacet(
                    SpecCardinality.NotApplicable,
                    null,
                    Evidence)),
            [.. postconditions.Select(
                condition =>
                    new SpecPostconditionDeclaration(
                        condition,
                        Evidence))]);
        return ApiSpecTable.Create([declaration]).Templates.Single();
    }

    private static SpecVariableDeclaration Variable(
        SpecVariableRole role,
        int ordinal,
        SpecValueType type)
    {
        return new(role, ordinal, type);
    }

    private static SpecIntegerDeclaration Integer(long value)
    {
        return new(value);
    }

    private static SpecBinaryDeclaration Binary(
        SpecBinaryOperator @operator,
        SpecTermDeclaration left,
        SpecTermDeclaration right,
        SpecValueType type)
    {
        return new(@operator, left, right, type);
    }

    private static SpecBinaryDeclaration Equal(
        SpecTermDeclaration left,
        SpecTermDeclaration right)
    {
        return Binary(
            SpecBinaryOperator.Equal,
            left,
            right,
            SpecValueType.Boolean);
    }

    private static SpecBinaryDeclaration Compare(
        SpecBinaryOperator @operator,
        long left,
        long right)
    {
        return Binary(
            @operator,
            Integer(left),
            Integer(right),
            SpecValueType.Boolean);
    }
}
