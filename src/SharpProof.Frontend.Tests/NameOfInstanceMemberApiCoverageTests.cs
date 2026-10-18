using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class NameOfInstanceMemberApiCoverageTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void ActualNameOfInstanceMemberHasNoRuntimeReceiver(bool property)
    {
        var declaration = property ? "public int Value { get; }" : "public int Value;";
        using var subject = TypedProgramSubject.Create("bool Target() => nameof(Cell.Value) == \"Value\";",
            "public sealed class Cell { " + declaration + " }");
        Assert.That(subject.Invoke([]), Is.True);
        var tree = subject.Compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(s => s is null ? throw new System.ArgumentNullException(nameof(s)) : (s.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" }));
        var operation = subject.Compilation.GetSemanticModel(tree).GetOperation(syntax);
        Assert.That(operation, Is.InstanceOf<INameOfOperation>());
        var nameOf = (INameOfOperation)operation!;
        Assert.That(nameOf.ConstantValue.Value, Is.EqualTo("Value"));
        var argument = nameOf.Argument;
        Assert.That(argument.ConstantValue.HasValue, Is.False);
        Assert.That(argument.Type!.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        IFieldSymbol field;
        if (property)
        {
            Assert.That(argument, Is.InstanceOf<IPropertyReferenceOperation>());
            var reference = (IPropertyReferenceOperation)argument;
            Assert.That(reference.Property.IsStatic, Is.False);
            Assert.That(reference.Instance, Is.Null);
            Assert.That(reference.Arguments, Is.Empty);
            field = CSharpOperationSemantics.GetterField(reference.Property)!;
            Assert.That(field, Is.Not.Null);
            Assert.That(SymbolEqualityComparer.Default.Equals(field.AssociatedSymbol, reference.Property), Is.True);
        }
        else
        {
            Assert.That(argument, Is.InstanceOf<IFieldReferenceOperation>());
            var reference = (IFieldReferenceOperation)argument;
            Assert.That(reference.Instance, Is.Null);
            field = reference.Field;
        }
        Assert.That(field.IsStatic, Is.False);
        Assert.That(CSharpOperationSemantics.IsSupportedFieldRead(field), Is.True);
        Assert.That(SymbolEqualityComparer.Default.Equals(field.ContainingAssembly, subject.Compilation.Assembly), Is.True);
        var builder = new IrProgramBuilder(subject.Factory);
        var entry = builder.CreateBlock("nameof-member-api");
        var lowered = new RoslynTotalExpressionLowerer(subject.Context, builder).LowerBodyValue(argument, entry);
        Assert.That(lowered.Classification.IsExact, Is.False);
        Assert.That(lowered.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedOperationKind));
        Assert.That(lowered.Continuation, Is.EqualTo(entry));
        Assert.That(lowered.Value, Is.InstanceOf<IrVariableTerm>());
        Assert.That(lowered.Value.Type, Is.EqualTo(subject.Factory.IntegerType));
        var whole = subject.Lower();
        Assert.That(whole.IsExact, Is.True);
        Assert.That(whole.ConstructionLimitExceeded, Is.False);
        var replay = subject.Execute(whole, []);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.Boolean, Is.True);
    }
}
