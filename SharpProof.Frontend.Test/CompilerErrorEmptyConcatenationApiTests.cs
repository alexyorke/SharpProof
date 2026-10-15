using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class CompilerErrorEmptyConcatenationApiTests
{
    private static readonly string[] ExpectedDiagnosticIds = ["CS1010"];

    [Test]
    public void ActualNewlineRecoveryKeepsEmptyLeavesWithoutFoldingTheirBinary()
    {
        const string source = "public static class Subject { public static string Target() { return \"\" + \"\n;\n} }";
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12), "empty-concat-recovery.cs");
        var compilation = CSharpCompilation.Create("EmptyConcatenationRecovery", [tree],
            TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release));
        var errors = compilation.GetDiagnostics().Where(static diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.That(errors.Select(static diagnostic => diagnostic.Id), Is.EqualTo(ExpectedDiagnosticIds));
        using var output = new MemoryStream();
        var emitted = compilation.Emit(output);
        Assert.That(emitted.Success, Is.False);
        Assert.That(emitted.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(static diagnostic => diagnostic.Id), Is.EqualTo(ExpectedDiagnosticIds));
        var model = compilation.GetSemanticModel(tree);
        var methodSyntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var method = (IMethodSymbol)model.GetDeclaredSymbol(methodSyntax)!;
        Assert.That(SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, compilation.Assembly), Is.True);
        var syntax = methodSyntax.DescendantNodes().OfType<BinaryExpressionSyntax>().Single();
        Assert.That(syntax.IsKind(SyntaxKind.AddExpression), Is.True);
        Assert.That(syntax.Right.ContainsDiagnostics, Is.True);
        Assert.That(errors.Single().Location.SourceSpan, Is.EqualTo(((LiteralExpressionSyntax)syntax.Right).Token.Span));
        var operation = model.GetOperation(syntax);
        TestContext.WriteLine($"Actual recovery concat: kind={operation?.Kind}, constant={operation?.ConstantValue.HasValue}, left={operation?.ChildOperations.First().Kind}, right={operation?.ChildOperations.Last().Kind}");
        Assert.That(operation, Is.InstanceOf<IBinaryOperation>());
        var binary = (IBinaryOperation)operation!;
        Assert.That(binary.SemanticModel, Is.SameAs(model));
        Assert.That(binary.OperatorKind, Is.EqualTo(BinaryOperatorKind.Add));
        Assert.That(binary.OperatorMethod, Is.Null);
        Assert.That(binary.IsLifted, Is.False);
        Assert.That(binary.Type!.SpecialType, Is.EqualTo(SpecialType.System_String));
        Assert.That(binary.ConstantValue.HasValue, Is.False);
        Assert.That(CSharpOperationSemantics.IsStringConcatenation(binary), Is.True);
        foreach (var operand in new[] { binary.LeftOperand, binary.RightOperand })
        {
            Assert.That(operand, Is.InstanceOf<ILiteralOperation>());
            Assert.That(operand.Parent, Is.SameAs(binary));
            Assert.That(operand.Type!.SpecialType, Is.EqualTo(SpecialType.System_String));
            Assert.That(operand.ConstantValue.HasValue, Is.True);
            Assert.That(operand.ConstantValue.Value, Is.EqualTo(string.Empty));
        }

        // This failed compilation supplies only a real expression API input.
        // Inspect its owned builder output, without CFG preparation or replay.
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, method);
        Assert.That(context.Target, Is.SameAs(method));
        Assert.That(context.Factory, Is.SameAs(factory));
        Assert.That(context.Compilation, Is.Null);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("empty-concat-recovery-expression-only");
        var lowered = new RoslynTotalExpressionLowerer(context, builder).LowerBodyValue(binary, entry);
        Assert.That(lowered.Classification.IsExact, Is.True);
        Assert.That(lowered.Classification.Abstention, Is.EqualTo(FrontendAbstention.None));
        Assert.That(lowered.Continuation, Is.EqualTo(entry));
        Assert.That(lowered.Value, Is.InstanceOf<IrVariableTerm>());
        Assert.That(lowered.Value.Type, Is.EqualTo(factory.StringType));
        builder.Return(entry, context.Site(binary), lowered.Value);
        var expressionOutput = builder.Build();
        Assert.That(expressionOutput.Blocks, Has.Length.EqualTo(1));
        var instructions = expressionOutput.Blocks.Single().Instructions;
        Assert.That(instructions.OfType<IrHavocInstruction>(), Is.Empty);
        Assert.That(instructions.OfType<IrAllocationInstruction>(), Is.Empty);
        var assignments = instructions.OfType<IrAssignInstruction>().ToArray();
        Assert.That(assignments, Has.Length.EqualTo(3));
        foreach (var assignment in assignments)
        {
            Assert.That(assignment.Value, Is.InstanceOf<IrStringTerm>());
            Assert.That(factory.GetString(((IrStringTerm)assignment.Value).Value), Is.EqualTo(string.Empty));
        }

        // Separate successfully emitted source supplies the CLR/full-IR control.
        using var control = TypedProgramSubject.Create("string Target() => \"\" + \"\";");
        Assert.That(control.Invoke([]), Is.EqualTo(string.Empty));
        var whole = control.Lower();
        Assert.That(whole.IsExact, Is.True);
        Assert.That(whole.Classification.Abstention, Is.EqualTo(FrontendAbstention.None));
        Assert.That(whole.ConstructionLimitExceeded, Is.False);
        var replay = control.Execute(whole, []);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ReturnValue!.String, Is.EqualTo(string.Empty));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ApproximationVariables, Is.Empty);
    }
}
