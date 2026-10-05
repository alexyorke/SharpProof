using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Ir;
using static SharpProof.Testing.ApiSpecTestFacets;

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
            IrTypeKind.String);
        var boolean = Variable(
            SpecVariableRole.Parameter,
            0,
            IrTypeKind.Boolean);
        var integer = Variable(
            SpecVariableRole.Parameter,
            1,
            IrTypeKind.Integer);
        var text = Variable(
            SpecVariableRole.Parameter,
            2,
            IrTypeKind.String);
        var reference = Variable(
            SpecVariableRole.Parameter,
            3,
            IrTypeKind.Reference);
        var sequence = Variable(
            SpecVariableRole.Parameter,
            4,
            IrTypeKind.Sequence);
        var result = Variable(
            SpecVariableRole.Result,
            -1,
            IrTypeKind.Boolean);
        var postconditions = new List<SpecTermDeclaration> {
            result,
            new SpecBooleanDeclaration(true),
            new SpecUnaryDeclaration(
                IrUnaryOperator.Not,
                new SpecBooleanDeclaration(false),
                IrTypeKind.Boolean),
            Equal(
                new SpecUnaryDeclaration(
                    IrUnaryOperator.Negate,
                    new SpecIntegerDeclaration(7),
                    IrTypeKind.Integer),
                new SpecIntegerDeclaration(-7)),
            Equal(Binary(
                IrBinaryOperator.Add,
                Integer(2),
                Integer(3),
                IrTypeKind.Integer), Integer(5)),
            Equal(Binary(
                IrBinaryOperator.Subtract,
                Integer(7),
                Integer(2),
                IrTypeKind.Integer), Integer(5)),
            Equal(Binary(
                IrBinaryOperator.Multiply,
                Integer(4),
                Integer(3),
                IrTypeKind.Integer), Integer(12)),
            Equal(Binary(
                IrBinaryOperator.Divide,
                Integer(12),
                Integer(4),
                IrTypeKind.Integer), Integer(3)),
            Equal(Binary(
                IrBinaryOperator.Remainder,
                Integer(14),
                Integer(4),
                IrTypeKind.Integer), Integer(2)),
            Binary(
                IrBinaryOperator.AndAlso,
                new SpecBooleanDeclaration(true),
                boolean,
                IrTypeKind.Boolean),
            Binary(
                IrBinaryOperator.OrElse,
                new SpecBooleanDeclaration(false),
                boolean,
                IrTypeKind.Boolean),
            Equal(integer, integer),
            Binary(
                IrBinaryOperator.NotEqual,
                Integer(1),
                Integer(2),
                IrTypeKind.Boolean),
            Compare(IrBinaryOperator.LessThan, 1, 2),
            Compare(IrBinaryOperator.LessThanOrEqual, 2, 2),
            Compare(IrBinaryOperator.GreaterThan, 2, 1),
            Compare(IrBinaryOperator.GreaterThanOrEqual, 2, 2),
            Equal(Binary(
                IrBinaryOperator.StringConcat,
                new SpecStringDeclaration("sharp"),
                new SpecStringDeclaration("proof"),
                IrTypeKind.String), new SpecStringDeclaration("sharpproof")),
            new SpecConditionalDeclaration(
                boolean,
                new SpecBooleanDeclaration(true),
                new SpecBooleanDeclaration(false),
                IrTypeKind.Boolean),
            Equal(
                new SpecLengthDeclaration(receiver),
                new SpecIntegerDeclaration(3)),
            Equal(
                new SpecNullDeclaration(IrTypeKind.String),
                new SpecNullDeclaration(IrTypeKind.String)),
            Equal(
                new SpecNullDeclaration(IrTypeKind.Reference),
                new SpecNullDeclaration(IrTypeKind.Reference)),
            Equal(text, text),
            Equal(reference, reference),
            Equal(sequence, sequence)
        };
        var template = CreateTemplate(
            isStatic: false,
            receiverType: IrTypeKind.String,
            parameterTypes: [
                IrTypeKind.Boolean,
                IrTypeKind.Integer,
                IrTypeKind.String,
                IrTypeKind.Reference,
                IrTypeKind.Sequence
            ],
            resultType: IrTypeKind.Boolean,
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
            parameterTypes: [IrTypeKind.Integer],
            resultType: null,
            [Equal(
                Variable(
                    SpecVariableRole.Parameter,
                    0,
                    IrTypeKind.Integer),
                Integer(0))]);
        var otherTemplate = CreateTemplate(
            isStatic: true,
            receiverType: null,
            parameterTypes: [IrTypeKind.Integer],
            resultType: null,
            [new SpecBooleanDeclaration(true)]);
        var factory = new IrFactory();
        var foreignFactory = new IrFactory();

        var cases = new[]
        {
            (
                Variable: otherTemplate.Parameters.Single(),
                Value: (IrTerm)factory.Integer(0),
                Failure: SpecInstantiationFailureKind.ForeignVariable),
            (
                Variable: template.Parameters.Single(),
                Value: (IrTerm)foreignFactory.Integer(0),
                Failure: SpecInstantiationFailureKind.ForeignIrTerm),
            (
                Variable: template.Parameters.Single(),
                Value: (IrTerm)factory.Boolean(false),
                Failure: SpecInstantiationFailureKind.TypeMismatch)
        };

        using (Assert.EnterMultipleScope())
        {
            foreach (var testCase in cases)
            {
                var instantiated =
                    ApiSpecInstantiator.InstantiatePostconditions(
                        template,
                        factory,
                        new Dictionary<SpecVarId, IrTerm>
                        {
                            [testCase.Variable] = testCase.Value
                        });
                AssertFailure(instantiated, testCase.Failure);
            }
        }
    }

    [Test]
    public void InstantiationUsesOneValidatedSubstitutionSnapshot()
    {
        var template = CreateTemplate(
            isStatic: true,
            receiverType: null,
            parameterTypes: [IrTypeKind.Integer],
            resultType: null,
            [Equal(
                Variable(
                    SpecVariableRole.Parameter,
                    0,
                    IrTypeKind.Integer),
                Integer(0))]);
        var variable = template.Parameters.Single();
        var factory = new IrFactory();
        var validated = factory.Integer(0);
        var foreignFactory = new IrFactory();
        IrTerm?[] lookupValues = [
            foreignFactory.Integer(0),
            factory.Boolean(false),
            null
        ];

        using (Assert.EnterMultipleScope())
        {
            foreach (var lookupValue in lookupValues)
            {
                var instantiated =
                    ApiSpecInstantiator.InstantiatePostconditions(
                        template,
                        factory,
                        new InconsistentSubstitutions(
                            variable,
                            validated,
                            lookupValue));

                Assert.That(
                    instantiated.Status,
                    Is.EqualTo(SpecInstantiationStatus.Succeeded));
                Assert.That(instantiated.Failure, Is.Null);
                Assert.That(instantiated.Postconditions, Has.Length.EqualTo(1));
            }
        }
    }

    [Test]
    public void SequenceNullProducesAnExplicitUnsupportedValueFailure()
    {
        var nullSequence =
            new SpecNullDeclaration(IrTypeKind.Sequence);
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

    [TestCase(SpecVariableRole.Receiver, IrBinaryOperator.Equal, false)]
    [TestCase(SpecVariableRole.Parameter, IrBinaryOperator.NotEqual, false)]
    [TestCase(SpecVariableRole.Result, IrBinaryOperator.Equal, true)]
    [TestCase(SpecVariableRole.Result, IrBinaryOperator.NotEqual, false)]
    public void ReferenceNullUsesTheExactSubstitutedOperandType(
        SpecVariableRole role,
        IrBinaryOperator @operator,
        bool nullOnLeft)
    {
        var variable = Variable(role, role == SpecVariableRole.Parameter ? 0 : -1,
            IrTypeKind.Reference);
        var nullValue = new SpecNullDeclaration(IrTypeKind.Reference);
        var comparison = Binary(
            @operator,
            nullOnLeft ? nullValue : variable,
            nullOnLeft ? variable : nullValue,
            IrTypeKind.Boolean);
        var template = CreateTemplate(
            isStatic: role != SpecVariableRole.Receiver,
            receiverType: role == SpecVariableRole.Receiver ? IrTypeKind.Reference : null,
            parameterTypes: role == SpecVariableRole.Parameter
                ? [IrTypeKind.Reference]
                : [],
            resultType: role == SpecVariableRole.Result ? IrTypeKind.Reference : null,
            [comparison]);
        var factory = new IrFactory();
        var widgetType = factory.GetOrCreateReferenceType(
            factory.CreateIdentity(), "Widget<string>");
        var replacement = factory.Variable(factory.CreateVariable("value", widgetType));
        var id = role switch
        {
            SpecVariableRole.Receiver => template.Receiver!.Value,
            SpecVariableRole.Parameter => template.Parameters.Single(),
            _ => template.Result!.Value
        };

        var instantiated = ApiSpecInstantiator.InstantiatePostconditions(
            template,
            factory,
            new Dictionary<SpecVarId, IrTerm> { [id] = replacement });

        Assert.That(instantiated.Status, Is.EqualTo(SpecInstantiationStatus.Succeeded));
        var binary = instantiated.Postconditions.Single() as IrBinaryTerm;
        Assert.That(binary, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(binary!.Left.Type, Is.EqualTo(widgetType));
            Assert.That(binary.Right.Type, Is.EqualTo(widgetType));
        }
    }

    [TestCase(IrTypeKind.Reference)]
    [TestCase(IrTypeKind.Sequence)]
    public void ExactTypesMustAgreeBeforeIrConstruction(IrTypeKind kind)
    {
        var left = Variable(SpecVariableRole.Parameter, 0, kind);
        var right = Variable(SpecVariableRole.Parameter, 1, kind);
        var template = CreateTemplate(
            isStatic: true,
            receiverType: null,
            parameterTypes: [kind, kind],
            resultType: null,
            [Equal(left, right)]);
        var factory = new IrFactory();
        var compatibleType = kind == IrTypeKind.Reference
            ? factory.GetOrCreateReferenceType(factory.CreateIdentity(), "Widget")
            : factory.GetOrCreateSequenceType(factory.IntegerType);
        var incompatibleType = kind == IrTypeKind.Reference
            ? factory.GetOrCreateReferenceType(factory.CreateIdentity(), "Other")
            : factory.GetOrCreateSequenceType(factory.StringType);
        var compatible = ApiSpecInstantiator.InstantiatePostconditions(
            template,
            factory,
            new Dictionary<SpecVarId, IrTerm>
            {
                [template.Parameters[0]] = factory.Variable(
                    factory.CreateVariable("left", compatibleType)),
                [template.Parameters[1]] = factory.Variable(
                    factory.CreateVariable("right", compatibleType))
            });
        var incompatible = ApiSpecInstantiator.InstantiatePostconditions(
            template,
            factory,
            new Dictionary<SpecVarId, IrTerm>
            {
                [template.Parameters[0]] = factory.Variable(
                    factory.CreateVariable("leftOther", compatibleType)),
                [template.Parameters[1]] = factory.Variable(
                    factory.CreateVariable("rightOther", incompatibleType))
            });

        Assert.That(compatible.Status, Is.EqualTo(SpecInstantiationStatus.Succeeded));
        AssertFailure(incompatible, SpecInstantiationFailureKind.TypeMismatch);
    }

    [TestCase(IrTypeKind.String)]
    [TestCase(IrTypeKind.Reference)]
    [TestCase(IrTypeKind.Sequence)]
    public void NullableTypesRetainTheirExactType(IrTypeKind declaredType)
    {
        var variable = Variable(
            SpecVariableRole.Parameter,
            0,
            declaredType);
        var template = CreateTemplate(
            isStatic: true,
            receiverType: null,
            parameterTypes: [declaredType],
            resultType: null,
            [Binary(
                declaredType == IrTypeKind.Sequence
                    ? IrBinaryOperator.Equal
                    : IrBinaryOperator.NotEqual,
                new SpecNullDeclaration(declaredType),
                variable,
                IrTypeKind.Boolean)]);
        var factory = new IrFactory();
        var exactType = declaredType switch
        {
            IrTypeKind.String => factory.StringType,
            IrTypeKind.Reference => factory.ObjectType,
            _ => factory.GetOrCreateSequenceType(factory.IntegerType)
        };

        var instantiated = ApiSpecInstantiator.InstantiatePostconditions(
            template,
            factory,
            new Dictionary<SpecVarId, IrTerm>
            {
                [template.Parameters.Single()] = factory.Variable(
                    factory.CreateVariable("value", exactType))
            });

        Assert.That(instantiated.Status, Is.EqualTo(SpecInstantiationStatus.Succeeded));
        var binary = instantiated.Postconditions.Single() as IrBinaryTerm;
        Assert.That(binary, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(binary!.Left.Type, Is.EqualTo(exactType));
            Assert.That(binary.Right.Type, Is.EqualTo(exactType));
        }
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

    private sealed class InconsistentSubstitutions(
        SpecVarId key,
        IrTerm enumeratedValue,
        IrTerm? lookupValue) : IReadOnlyDictionary<SpecVarId, IrTerm>
    {
        public IrTerm this[SpecVarId requestedKey] =>
            requestedKey == key
                ? lookupValue!
                : throw new KeyNotFoundException();

        public IEnumerable<SpecVarId> Keys => [key];

        public IEnumerable<IrTerm> Values => [enumeratedValue];

        public int Count => 1;

        public bool ContainsKey(SpecVarId requestedKey)
        {
            return requestedKey == key;
        }

        public bool TryGetValue(
            SpecVarId requestedKey,
            out IrTerm value)
        {
            if (requestedKey == key)
            {
                value = lookupValue!;
                return true;
            }

            value = null!;
            return false;
        }

        public IEnumerator<KeyValuePair<SpecVarId, IrTerm>> GetEnumerator()
        {
            yield return KeyValuePair.Create(key, enumeratedValue);
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    private static ApiSpecTemplate CreateTemplate(
        bool isStatic,
        IrTypeKind? receiverType,
        ImmutableArray<IrTypeKind> parameterTypes,
        IrTypeKind? resultType,
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
            NeutralFacets(Evidence),
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
        IrTypeKind type)
    {
        return new(role, ordinal, type);
    }

    private static SpecIntegerDeclaration Integer(long value)
    {
        return new(value);
    }

    private static SpecBinaryDeclaration Binary(
        IrBinaryOperator @operator,
        SpecTermDeclaration left,
        SpecTermDeclaration right,
        IrTypeKind type)
    {
        return new(@operator, left, right, type);
    }

    private static SpecBinaryDeclaration Equal(
        SpecTermDeclaration left,
        SpecTermDeclaration right)
    {
        return Binary(
            IrBinaryOperator.Equal,
            left,
            right,
            IrTypeKind.Boolean);
    }

    private static SpecBinaryDeclaration Compare(
        IrBinaryOperator @operator,
        long left,
        long right)
    {
        return Binary(
            @operator,
            Integer(left),
            Integer(right),
            IrTypeKind.Boolean);
    }
}
