using System;
using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class Pr275InvariantRequiredInputTests
{
    [Test]
    public void RelationRecognitionRequiresItsFactory()
    {
        var factory = new IrFactory();
        var term = factory.Boolean(true);
        var error = Assert.Throws<ArgumentNullException>(
            (Action)(() => IrInvariantRelations.IsRelation(null!, term)));

        Assert.That(error!.ParamName, Is.EqualTo("factory"));
    }

    [Test]
    public void CreateRejectsNullFactoryAndReportsFactoryParameter()
    {
        var error = Assert.Throws<ArgumentNullException>(
            (Action)(() => IrInvariantRelations.Create(null!, 0, default)));

        Assert.That(error!.ParamName, Is.EqualTo("factory"));
    }

    [Test]
    public void CreateRejectsDefaultStateAndReportsStateParameter()
    {
        var factory = new IrFactory();

        var error = Assert.Throws<ArgumentException>(
            (Action)(() => IrInvariantRelations.Create(factory, 0, default)));

        Assert.That(error!.ParamName, Is.EqualTo("state"));
    }

    [Test]
    public void CreateAcceptsInitializedEmptyState()
    {
        var factory = new IrFactory();
        var state = ImmutableArray<IrTerm>.Empty;

        Assert.That(state.IsDefault, Is.False);
        Assert.That(default(ImmutableArray<IrTerm>).IsDefault, Is.True);

        var relation = IrInvariantRelations.Create(factory, 0, state);

        Assert.That(IrInvariantRelations.IsRelation(factory, relation), Is.True);
    }

    [Test]
    public void IsRelationRecognizesCreatedRelationAndOrdinaryTerms()
    {
        var factory = new IrFactory();
        var integerVariable = factory.CreateVariable("integerValue", factory.IntegerType);
        var booleanVariable = factory.CreateVariable("booleanValue", factory.BooleanType);
        var variableTerm = factory.Variable(integerVariable);
        var booleanVariableTerm = factory.Variable(booleanVariable);
        var integerLiteral = factory.Integer(7);
        var booleanLiteral = factory.Boolean(true);
        var state = ImmutableArray.Create<IrTerm>(
            variableTerm,
            booleanVariableTerm,
            integerLiteral,
            booleanLiteral);

        var relation = IrInvariantRelations.Create(factory, 1, state);

        Assert.That(IrInvariantRelations.IsRelation(factory, relation), Is.True);
        Assert.That(IrInvariantRelations.IsRelation(factory, variableTerm), Is.False);
        Assert.That(IrInvariantRelations.IsRelation(factory, integerLiteral), Is.False);
    }
}
