using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

public sealed class DictionaryKeyedLoopTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void KeyedLoopStoresRequireMemoryCutAndReplayTheirActualKeys(bool wide)
    {
        var subject = new PassiveCallableVcTests.ScalarSubject();
        var factory = subject.Factory;
        var builder = subject.Builder;
        var owner = new PassiveParameterBinding(factory.CreateVariable("owner:entry", factory.ObjectType),
            factory.CreateVariable("owner:current", factory.ObjectType), factory.CreateVariable("owner:old", factory.ObjectType));
        var keyType = wide ? factory.GetOrCreateIntegerType(64, true) : factory.IntegerType;
        var field = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "field:keyed-values", factory.IntegerType, false, keyType);
        var entry = builder.CreateBlock();
        var header = builder.CreateBlock();
        var body = builder.CreateBlock();
        var exit = builder.CreateBlock();
        var headerSite = factory.CreateOperation("keyed-loop-header");
        builder.Assign(entry, subject.Site, subject.Parameter.Current, factory.Variable(subject.Parameter.Entry));
        builder.Assign(entry, subject.Site, owner.Current, factory.Variable(owner.Entry));
        builder.Goto(entry, subject.Site, header);
        builder.Branch(header, headerSite, factory.Binary(IrBinaryOperator.GreaterThan, factory.Variable(subject.Parameter.Current), factory.Integer(0)), body, exit);
        var key = wide ? factory.Cast(keyType, factory.Variable(subject.Parameter.Current)) : factory.Variable(subject.Parameter.Current);
        builder.FieldStore(body, subject.Site, IrWriteRegion.Field, factory.Variable(owner.Current), field, factory.Integer(2), key);
        builder.Assign(body, subject.Site, subject.Parameter.Current,
            factory.Binary(IrBinaryOperator.Subtract, factory.Variable(subject.Parameter.Current), factory.Integer(1)));
        builder.Goto(body, subject.Site, header);
        builder.Return(exit, subject.Site, factory.PureOpaque(field, factory.Variable(owner.Current), factory.Integer(keyType, 1)));
        var candidate = new PassiveCallableCandidate("keyed-loop", builder.Build(), [subject.Parameter, owner], subject.Result, [],
            [new(factory.Boolean(true), factory.Boolean(true), subject.Site)]);
        Assert.That(PassiveLoopCutter.TryCreate(candidate, out var proof, out var search, out var reason, TestContext.CurrentContext.CancellationToken), Is.True, reason.ToString());
        Assert.That(proof!.Program.Blocks.SelectMany(block => block is null ? throw new ArgumentNullException(nameof(block)) : block.Instructions)
            .OfType<IrHavocInstruction>().Any(havoc => havoc is null ? throw new ArgumentNullException(nameof(havoc)) :
                havoc.Operation == headerSite && havoc.HavocKind is IrHavocKind.Memory or IrHavocKind.VariablesAndMemory), Is.True);
        var inputs = new Dictionary<ScopedIrId<IrVariableTag>, IrValue>
        {
            [subject.Parameter.Entry] = factory.CreateIntegerValue(3),
            [owner.Entry] = factory.CreateReferenceValue(factory.ObjectType, new IrObjectState())
        };
        foreach (var program in new[] { candidate.Program, search!.Program })
        {
            var result = new IrProgramInterpreter(factory).Execute(program, inputs, 1000);
            Assert.That(result.Status, Is.EqualTo(IrProgramExecutionStatus.Returned), result.Unsupported?.Detail);
            Assert.That(result.ReturnValue!.Integer, Is.EqualTo(2));
            Assert.That(result.ConsumedApproximation, Is.False);
        }
    }
}
