using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Ir;
using static SharpProof.Testing.ApiSpecTestFacets;

namespace SharpProof.Specs.Test;

[TestFixture]
internal sealed class ApiSpecInstantiationCoverageTests
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
        var postconditions = ImmutableArray.Create<SpecTermDeclaration>(result, new SpecBooleanDeclaration(true), new SpecUnaryDeclaration(
                IrUnaryOperator.Not,
                new SpecBooleanDeclaration(false),
                IrTypeKind.Boolean), Equal(
                new SpecUnaryDeclaration(
                    IrUnaryOperator.Negate,
                    new SpecIntegerDeclaration(7),
                    IrTypeKind.Integer),
                new SpecIntegerDeclaration(-7)), Equal(Binary(
                IrBinaryOperator.Add,
                Integer(2),
                Integer(3),
                IrTypeKind.Integer), Integer(5)), Equal(Binary(
                IrBinaryOperator.Subtract,
                Integer(7),
                Integer(2),
                IrTypeKind.Integer), Integer(5)), Equal(Binary(
                IrBinaryOperator.Multiply,
                Integer(4),
                Integer(3),
                IrTypeKind.Integer), Integer(12)), Equal(Binary(
                IrBinaryOperator.Divide,
                Integer(12),
                Integer(4),
                IrTypeKind.Integer), Integer(3)), Equal(Binary(
                IrBinaryOperator.Remainder,
                Integer(14),
                Integer(4),
                IrTypeKind.Integer), Integer(2)), Binary(
                IrBinaryOperator.AndAlso,
                new SpecBooleanDeclaration(true),
                boolean,
                IrTypeKind.Boolean), Binary(
                IrBinaryOperator.OrElse,
                new SpecBooleanDeclaration(false),
                boolean,
                IrTypeKind.Boolean), Equal(integer, integer), Binary(
                IrBinaryOperator.NotEqual,
                Integer(1),
                Integer(2),
                IrTypeKind.Boolean), Compare(IrBinaryOperator.LessThan, 1, 2), Compare(IrBinaryOperator.LessThanOrEqual, 2, 2), Compare(IrBinaryOperator.GreaterThan, 2, 1), Compare(IrBinaryOperator.GreaterThanOrEqual, 2, 2), Equal(Binary(
                IrBinaryOperator.StringConcat,
                new SpecStringDeclaration("sharp"),
                new SpecStringDeclaration("proof"),
                IrTypeKind.String), new SpecStringDeclaration("sharpproof")), new SpecConditionalDeclaration(
                boolean,
                new SpecBooleanDeclaration(true),
                new SpecBooleanDeclaration(false),
                IrTypeKind.Boolean), Equal(
                new SpecLengthDeclaration(receiver),
                new SpecIntegerDeclaration(3)), Equal(
                new SpecNullDeclaration(IrTypeKind.String),
                new SpecNullDeclaration(IrTypeKind.String)), Equal(
                new SpecNullDeclaration(IrTypeKind.Reference),
                new SpecNullDeclaration(IrTypeKind.Reference)), Equal(text, text), Equal(reference, reference), Equal(sequence, sequence));
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
        var substitutions = ImmutableDictionary<SpecVarId, IrTerm>.Empty.SetItem(template.Receiver!.Value, factory.String("abc")).SetItem(template.Parameters[0], factory.Boolean(true)).SetItem(template.Parameters[1], factory.Integer(42)).SetItem(template.Parameters[2], factory.String("value")).SetItem(template.Parameters[3], factory.Variable(
                factory.CreateVariable(
                    "reference",
                    factory.ObjectType))).SetItem(template.Parameters[4], factory.Variable(
                factory.CreateVariable(
                    "sequence",
                    sequenceType))).SetItem(template.Result!.Value, factory.Boolean(true));

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
                Has.Length.EqualTo(postconditions.Length));
            Assert.That(
                instantiated.Postconditions.Select(
                    term => term is null ? throw new System.ArgumentNullException(nameof(term)) : (factory.GetTypeInfo(term.Type).Kind)),
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

        var cases = ImmutableArray.Create(
            (
                Variable: template.Parameters.Single(),
                Value: (IrTerm)null!,
                Failure: SpecInstantiationFailureKind.ForeignIrTerm),
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
        );

        using (Assert.EnterMultipleScope())
        {
            foreach (var testCase in cases)
            {
                var instantiated =
                    ApiSpecInstantiator.InstantiatePostconditions(
                        template,
                        factory,
                        ImmutableDictionary<SpecVarId, IrTerm>.Empty.SetItem(testCase.Variable, testCase.Value));
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
        ImmutableArray<IrTerm?> lookupValues = [
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

    [Test]
    public void ConditionalInstantiationReportsAnOmittedConditionSubstitution()
    {
        var condition = Variable(
            SpecVariableRole.Parameter, 0, IrTypeKind.Boolean);
        var template = CreateTemplate(
            isStatic: true,
            receiverType: null,
            parameterTypes: [IrTypeKind.Boolean],
            resultType: null,
            [new SpecConditionalDeclaration(
                condition, new SpecBooleanDeclaration(true),
                new SpecBooleanDeclaration(false), IrTypeKind.Boolean)]);
        var factory = new IrFactory();

        var missing = ApiSpecInstantiator.InstantiatePostconditions(
            template, factory, ImmutableDictionary<SpecVarId, IrTerm>.Empty);
        var supplied = ApiSpecInstantiator.InstantiatePostconditions(
            template, factory, ImmutableDictionary<SpecVarId, IrTerm>.Empty.SetItem(template.Parameters.Single(), factory.Boolean(true)));

        using (Assert.EnterMultipleScope())
        {
            AssertFailure(missing, SpecInstantiationFailureKind.MissingSubstitution);
            Assert.That(missing.Failure!.Variable,
                Is.EqualTo(template.Parameters.Single()));
            Assert.That(supplied.Status, Is.EqualTo(SpecInstantiationStatus.Succeeded));
            Assert.That(supplied.Postconditions, Has.Length.EqualTo(1));
        }
    }

    [Test]
    public void CompoundNullInstantiationReportsEachOmittedSubstitution()
    {
        var outerCondition = Variable(
            SpecVariableRole.Parameter, 0, IrTypeKind.Boolean);
        var innerCondition = Variable(
            SpecVariableRole.Parameter, 1, IrTypeKind.Boolean);
        var peer = Variable(
            SpecVariableRole.Parameter, 2, IrTypeKind.Reference);
        var nil = new SpecNullDeclaration(IrTypeKind.Reference);
        var inner = new SpecConditionalDeclaration(
            innerCondition, nil, nil, IrTypeKind.Reference);
        var outer = new SpecConditionalDeclaration(
            outerCondition, inner, nil, IrTypeKind.Reference);
        var template = CreateTemplate(
            isStatic: true,
            receiverType: null,
            parameterTypes: [
                IrTypeKind.Boolean, IrTypeKind.Boolean, IrTypeKind.Reference
            ],
            resultType: null,
            [Equal(outer, peer)]);
        var factory = new IrFactory();
        var reference = factory.Variable(
            factory.CreateVariable("reference", factory.ObjectType));
        var outerValue = factory.Boolean(true);
        var innerValue = factory.Boolean(false);

        SpecInstantiationResult Instantiate(bool includeOuter, bool includeInner,
            bool includePeer)
        {
            var substitutionsBuilder = ImmutableDictionary.CreateBuilder<SpecVarId, IrTerm>();
            if (includeOuter)
            {
                substitutionsBuilder[template.Parameters[0]] = outerValue;
            }
            if (includeInner)
            {
                substitutionsBuilder[template.Parameters[1]] = innerValue;
            }
            if (includePeer)
            {
                substitutionsBuilder[template.Parameters[2]] = reference;
            }

            var substitutions = substitutionsBuilder.ToImmutable();
            return ApiSpecInstantiator.InstantiatePostconditions(
                template, factory, substitutions);
        }

        var missingOuter = Instantiate(false, false, true);
        var missingInner = Instantiate(true, false, true);
        var missingPeer = Instantiate(true, true, false);
        var complete = Instantiate(true, true, true);
        using (Assert.EnterMultipleScope())
        {
            AssertFailure(missingOuter,
                SpecInstantiationFailureKind.MissingSubstitution);
            Assert.That(missingOuter.Failure!.Variable,
                Is.EqualTo(template.Parameters[0]));
            AssertFailure(missingInner,
                SpecInstantiationFailureKind.MissingSubstitution);
            Assert.That(missingInner.Failure!.Variable,
                Is.EqualTo(template.Parameters[1]));
            AssertFailure(missingPeer,
                SpecInstantiationFailureKind.MissingSubstitution);
            Assert.That(missingPeer.Failure!.Variable,
                Is.EqualTo(template.Parameters[2]));
            Assert.That(complete.Status,
                Is.EqualTo(SpecInstantiationStatus.Succeeded));
            Assert.That(complete.Postconditions, Has.Length.EqualTo(1));
        }
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
            ImmutableDictionary<SpecVarId, IrTerm>.Empty.SetItem(id, replacement));

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
            ImmutableDictionary<SpecVarId, IrTerm>.Empty.SetItem(template.Parameters[0], factory.Variable(
                    factory.CreateVariable("left", compatibleType))).SetItem(template.Parameters[1], factory.Variable(
                    factory.CreateVariable("right", compatibleType))));
        var incompatible = ApiSpecInstantiator.InstantiatePostconditions(
            template,
            factory,
            ImmutableDictionary<SpecVarId, IrTerm>.Empty.SetItem(template.Parameters[0], factory.Variable(
                    factory.CreateVariable("leftOther", compatibleType))).SetItem(template.Parameters[1], factory.Variable(
                    factory.CreateVariable("rightOther", incompatibleType))));

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
            ImmutableDictionary<SpecVarId, IrTerm>.Empty.SetItem(template.Parameters.Single(), factory.Variable(
                    factory.CreateVariable("value", exactType))));

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
        System.ArgumentNullException.ThrowIfNull(result);
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
        IrTerm? lookupValue /* SPINT001: all values valid - the test dictionary deliberately forwards null to exercise caller validation */) : IReadOnlyDictionary<SpecVarId, IrTerm>
    {
        private readonly IrTerm _guarded_enumeratedValue = enumeratedValue ?? throw new System.ArgumentNullException(nameof(enumeratedValue));
        public IrTerm this[SpecVarId requestedKey] =>
            requestedKey == key
                ? lookupValue!
                : throw new KeyNotFoundException();

        public IEnumerable<SpecVarId> Keys => [key];

        public IEnumerable<IrTerm> Values => [_guarded_enumeratedValue];

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
            yield return KeyValuePair.Create(key, _guarded_enumeratedValue);
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

        System.ArgumentNullException.ThrowIfNull(postconditions);
        if (parameterTypes.IsDefault)
        {
            throw new System.ArgumentException("Array must be initialized.", nameof(parameterTypes));
        }
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
                    condition is null ? throw new System.ArgumentNullException(nameof(condition)) : (new SpecPostconditionDeclaration(
                        condition,
                        Evidence)))]);
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
        System.ArgumentNullException.ThrowIfNull(left);
        System.ArgumentNullException.ThrowIfNull(right);
        return new(@operator, left, right, type);
    }

    private static SpecBinaryDeclaration Equal(
        SpecTermDeclaration left,
        SpecTermDeclaration right)
    {
        System.ArgumentNullException.ThrowIfNull(left);
        System.ArgumentNullException.ThrowIfNull(right);
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
