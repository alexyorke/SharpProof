using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class SourceStringIndexFaultCoverageTests
{
    [TestCase(null, 7)]
    [TestCase("", 9)]
    public void SourceReceiverCallPreservesDocumentedLastCharacterFaults(string? input, int expected)
    {
        using var subject = TypedProgramSubject.Create(
            "int Target(string text) { try { _ = Read(text)[^1]; return 0; } " +
            "catch (System.NullReferenceException) { return 7; } " +
            "catch (System.IndexOutOfRangeException) { return 9; } } " +
            "static string Read(string value) => value;");
        ImmutableArray<object> arguments = [input!];
        Assert.That(subject.Invoke(arguments), Is.EqualTo(expected));
        var tree = subject.Compilation.SyntaxTrees.Single();
        var indexer = tree.GetRoot().DescendantNodes().OfType<ElementAccessExpressionSyntax>().Single();
        var operation = subject.Compilation.GetSemanticModel(tree).GetOperation(indexer)!;
        var builder = new IrProgramBuilder(subject.Factory);
        var entry = builder.CreateBlock("entry");
        builder.SetEntry(entry);
        var child = new RoslynTotalExpressionLowerer(subject.Context, builder).LowerBodyValue(operation, entry);
        Assert.That(child.Classification.IsExact, Is.False);
        Assert.That(child.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedOperationKind));
        var withoutSource = subject.Lower();
        Assert.That(withoutSource.IsExact, Is.False);
        Assert.That(withoutSource.Classification.Abstention, Is.Not.EqualTo(FrontendAbstention.None));
        var lowering = subject.LowerSourceCalls();
        Assert.That(lowering.IsExact, Is.True, lowering.Classification.Abstention.ToString());
        var parameter = subject.Context.Parameters.Single().Entry;
        var type = subject.Factory.GetVariableInfo(parameter).Type;
        var value = input == null ? subject.Factory.CreateNullValue(type) : subject.Factory.CreateStringValue(input);
        var inputs = global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(parameter, value);
        var execution = new IrProgramInterpreter(subject.Factory).Execute(lowering.Program, inputs);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(expected)));
    }
}
