using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class Pr275ObjectFieldInputCoverageTests
{
    [TestCase("boolean not")]
    [TestCase("integer negation")]
    [TestCase("and left")]
    [TestCase("and right")]
    [TestCase("or left")]
    [TestCase("or right")]
    [TestCase("addition left")]
    [TestCase("addition right")]
    [TestCase("comparison")]
    [TestCase("equality")]
    [TestCase("string equality left")]
    [TestCase("string equality right")]
    [TestCase("string concat left")]
    [TestCase("string concat right")]
    [TestCase("conditional")]
    [TestCase("length")]
    [TestCase("sequence receiver")]
    [TestCase("sequence index")]
    public void OperationsRejectAFieldInputWithTheWrongValueType(string scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        var factory = new IrFactory();
        var sequenceType = factory.GetOrCreateSequenceType(factory.IntegerType);
        var owner = factory.CreateVariable("owner", factory.ObjectType);
        var fieldType = scenario switch
        {
            "boolean not" or "and left" or "and right" or "or left" or "or right" or "conditional" => factory.BooleanType,
            "string equality left" or "string equality right" or "string concat left" or "string concat right" => factory.StringType,
            "length" or "sequence receiver" => sequenceType,
            _ => factory.IntegerType
        };
        var field = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType,
            "field:Value", fieldType, isStatic: false);
        var fieldRead = factory.PureOpaque(field, factory.Variable(owner));
        var input = fieldType == factory.BooleanType ? factory.CreateIntegerValue(1) : factory.CreateBooleanValue(true);
        var state = new IrObjectState().WithField(field, input);
        var variables = new Dictionary<IrVarId, IrValue>
        {
            [owner] = factory.CreateReferenceValue(factory.ObjectType, state)
        };
        var gate = factory.CreateVariable("gate", factory.BooleanType);
        variables[gate] = factory.CreateBooleanValue(scenario is not ("or left" or "or right"));
        var gateTerm = factory.Variable(gate);
        IrTerm expression;
        if (scenario == "sequence index")
        {
            var array = factory.CreateVariable("array", sequenceType);
            variables[array] = factory.CreateSequenceValue(sequenceType, [factory.CreateIntegerValue(3)]);
            expression = factory.SequenceAccess(factory.Variable(array), fieldRead);
        }
        else
        {
            expression = scenario switch
            {
                "boolean not" => factory.Unary(IrUnaryOperator.Not, fieldRead),
                "integer negation" => factory.Unary(IrUnaryOperator.Negate, fieldRead),
                "and left" => factory.Binary(IrBinaryOperator.AndAlso, fieldRead, gateTerm),
                "and right" => factory.Binary(IrBinaryOperator.AndAlso, gateTerm, fieldRead),
                "or left" => factory.Binary(IrBinaryOperator.OrElse, fieldRead, gateTerm),
                "or right" => factory.Binary(IrBinaryOperator.OrElse, gateTerm, fieldRead),
                "addition left" => factory.Binary(IrBinaryOperator.Add, fieldRead, factory.Integer(1)),
                "addition right" => factory.Binary(IrBinaryOperator.Add, factory.Integer(1), fieldRead),
                "comparison" => factory.Binary(IrBinaryOperator.LessThan, fieldRead, factory.Integer(1)),
                "equality" => factory.Binary(IrBinaryOperator.Equal, fieldRead, factory.Integer(1)),
                "string equality left" => factory.Binary(IrBinaryOperator.StringEquals, fieldRead, factory.String("text")),
                "string equality right" => factory.Binary(IrBinaryOperator.StringEquals, factory.String("text"), fieldRead),
                "string concat left" => factory.Binary(IrBinaryOperator.StringConcat, fieldRead, factory.String("text")),
                "string concat right" => factory.Binary(IrBinaryOperator.StringConcat, factory.String("text"), fieldRead),
                "conditional" => factory.Conditional(fieldRead, factory.Integer(1), factory.Integer(2)),
                "length" => factory.Length(fieldRead),
                "sequence receiver" => factory.SequenceAccess(fieldRead, factory.Integer(0)),
                _ => throw new ArgumentOutOfRangeException(nameof(scenario))
            };
        }

        var result = new IrInterpreter(factory).Evaluate(expression, variables);

        Assert.That(result.Status, Is.EqualTo(IrEvaluationStatus.Unsupported));
        Assert.That(result.Unsupported!.Reason, Is.EqualTo(IrUnsupportedReason.InvalidVariableValue));
    }
}
