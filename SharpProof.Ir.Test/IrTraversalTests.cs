using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class IrTraversalTests
{
    [Test]
    public void ChildrenCoverEveryTermKindInSemanticOrder()
    {
        var factory = new IrFactory();
        var boolean = factory.CreateVariable("boolean", factory.BooleanType);
        var integer = factory.CreateVariable("integer", factory.IntegerType);
        var receiverType = factory.GetOrCreateReferenceType(
            factory.CreateIdentity(),
            "Receiver");
        var receiver = factory.CreateVariable("receiver", receiverType);
        var sequenceType = factory.GetOrCreateSequenceType(factory.IntegerType);
        var sequence = factory.CreateVariable("sequence", sequenceType);
        var member = factory.GetOrCreateMember(
            factory.CreateIdentity(),
            receiverType,
            "Read",
            factory.IntegerType,
            isStatic: false,
            factory.IntegerType);
        var booleanTerm = factory.Variable(boolean);
        var integerTerm = factory.Variable(integer);
        var receiverTerm = factory.Variable(receiver);
        var sequenceTerm = factory.Variable(sequence);
        var opaque = factory.PureOpaque(
            member,
            receiverTerm,
            integerTerm);
        var shapes =
            new (IrTermKind Kind, IrTerm Term, IrTerm[] Children)[] {
                (IrTermKind.Boolean, factory.Boolean(true), []),
                (IrTermKind.Integer, factory.Integer(1), []),
                (IrTermKind.String, factory.String("text"), []),
                (IrTermKind.Null, factory.Null(receiverType), []),
                (IrTermKind.Variable, integerTerm, []),
                (IrTermKind.Opaque, opaque, [receiverTerm, integerTerm]),
                (IrTermKind.Unary,
                    factory.Unary(IrUnaryOperator.Not, booleanTerm),
                    [booleanTerm]),
                (IrTermKind.Binary,
                    factory.Binary(
                        IrBinaryOperator.Add,
                        integerTerm,
                        factory.Integer(1)),
                    [integerTerm, factory.Integer(1)]),
                (IrTermKind.Conditional,
                    factory.Conditional(
                        booleanTerm,
                        integerTerm,
                        factory.Integer(2)),
                    [booleanTerm, integerTerm, factory.Integer(2)]),
                (IrTermKind.Cast,
                    factory.Cast(factory.ObjectType, receiverTerm),
                    [receiverTerm]),
                (IrTermKind.Length,
                    factory.Length(sequenceTerm),
                    [sequenceTerm]),
                (IrTermKind.SequenceAccess,
                    factory.SequenceAccess(sequenceTerm, integerTerm),
                    [sequenceTerm, integerTerm])
            };

        Assert.That(
            shapes.Select(static shape => shape.Kind),
            Is.EquivalentTo(Enum.GetValues<IrTermKind>()));
        foreach (var shape in shapes)
        {
            Assert.That(shape.Term.Kind, Is.EqualTo(shape.Kind));
            Assert.That(
                IrTraversal.GetChildren(shape.Term),
                Is.EqualTo(shape.Children),
                shape.Kind.ToString());
        }
    }

    [Test]
    public void VariableCollectionDeduplicatesSharedTermsAcrossRoots()
    {
        var factory = new IrFactory();
        var condition = factory.CreateVariable("condition", factory.BooleanType);
        var first = factory.CreateVariable("first", factory.IntegerType);
        var second = factory.CreateVariable("second", factory.IntegerType);
        var shared = factory.Binary(
            IrBinaryOperator.Add,
            factory.Variable(first),
            factory.Variable(second));
        var root = factory.Conditional(
            factory.Variable(condition),
            shared,
            shared);

        var variables = IrTraversal.CollectVariables(
            [root, shared, factory.Variable(first)]);

        Assert.That(
            variables,
            Is.EquivalentTo(new[] { condition, first, second }));
    }
}
