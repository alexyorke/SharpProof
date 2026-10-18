using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class IrPublicFieldValueBoundaryTests
{
    [TestCase(true)]
    [TestCase(false)]
    public void EntryFieldReadsRejectValuesOfADifferentDeclaredType(bool booleanField)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var fieldType = booleanField ? factory.BooleanType : factory.IntegerType;
        var field = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "field:Value", fieldType, false);
        var owner = factory.CreateVariable("owner", factory.ObjectType);
        var read = factory.PureOpaque(field, factory.Variable(owner));
        var interpreter = new IrInterpreter(factory);
        var matching = booleanField ? factory.CreateBooleanValue(true) : factory.CreateIntegerValue(3);
        var validOwner = factory.CreateReferenceValue(factory.ObjectType, new IrObjectState().WithField(field, matching));
        var valid = interpreter.Evaluate(read, new Dictionary<IrVarId, IrValue> { [owner] = validOwner });
        Assert.That(valid.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(valid.Value, Is.SameAs(matching));

        var mismatched = booleanField ? factory.CreateIntegerValue(3) : factory.CreateBooleanValue(true);
        var invalidOwner = factory.CreateReferenceValue(factory.ObjectType, new IrObjectState().WithField(field, mismatched));
        var invalid = interpreter.Evaluate(read, new Dictionary<IrVarId, IrValue> { [owner] = invalidOwner });
        Assert.That(invalid.Status, Is.EqualTo(IrEvaluationStatus.Unsupported));
        Assert.That(invalid.Unsupported!.Reason, Is.EqualTo(IrUnsupportedReason.InvalidVariableValue));
    }
    [TestCase(true)]
    [TestCase(false)]
    public void ProgramFieldReadsRejectMismatchedEntryValuesInTheCurrentHeap(bool booleanField)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var fieldType = booleanField ? factory.BooleanType : factory.IntegerType;
        var field = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "field:Value", fieldType, false);
        var owner = factory.CreateVariable("owner", factory.ObjectType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Return(entry, factory.CreateOperation(), factory.PureOpaque(field, factory.Variable(owner)));
        var program = builder.Build();
        var interpreter = new IrProgramInterpreter(factory);
        var matching = booleanField ? factory.CreateBooleanValue(true) : factory.CreateIntegerValue(3);
        var validOwner = factory.CreateReferenceValue(factory.ObjectType, new IrObjectState().WithField(field, matching));
        var valid = interpreter.Execute(program, new Dictionary<IrVarId, IrValue> { [owner] = validOwner });
        Assert.That(valid.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(valid.ReturnValue, Is.SameAs(matching));

        var mismatched = booleanField ? factory.CreateIntegerValue(3) : factory.CreateBooleanValue(true);
        var invalidOwner = factory.CreateReferenceValue(factory.ObjectType, new IrObjectState().WithField(field, mismatched));
        var invalid = interpreter.Execute(program, new Dictionary<IrVarId, IrValue> { [owner] = invalidOwner });
        Assert.That(invalid.Status, Is.EqualTo(IrProgramExecutionStatus.Unsupported));
        Assert.That(invalid.Unsupported!.Reason, Is.EqualTo(IrUnsupportedReason.InvalidVariableValue));
    }
}
