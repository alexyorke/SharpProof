using System.Collections.Immutable;
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
    private static readonly ImmutableArray<string> ExpectedDiagnosticIds = ["CS1010"];

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
            diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)).ToImmutableArray();
        Assert.That(errors.Select(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id)), Is.EqualTo(ExpectedDiagnosticIds));
        using var output = new MemoryStream();
        var emitted = compilation.Emit(output);
        Assert.That(emitted.Success, Is.False);
        Assert.That(emitted.Diagnostics.Where(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error))
            .Select(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id)), Is.EqualTo(ExpectedDiagnosticIds));
        var model = compilation.GetSemanticModel(tree);
        var methodSyntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var method = (IMethodSymbol)model.GetDeclaredSymbol(methodSyntax)!;
        Assert.That(SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, compilation.Assembly), Is.True);
        var syntax = methodSyntax.DescendantNodes().OfType<BinaryExpressionSyntax>().Single();
        Assert.That(syntax.IsKind(SyntaxKind.AddExpression), Is.True);
        Assert.That(syntax.Right.ContainsDiagnostics, Is.True);
        var rightToken = ((LiteralExpressionSyntax)syntax.Right).Token;
        Assert.That(rightToken.Text, Is.EqualTo("\""));
        Assert.That(rightToken.Span.Length, Is.EqualTo(1));
        Assert.That(errors.Single().Location.SourceTree, Is.SameAs(tree));
        Assert.That(errors.Single().Location.SourceSpan,
            Is.EqualTo(new Microsoft.CodeAnalysis.Text.TextSpan(rightToken.SpanStart, 0)));
        var operation = model.GetOperation(syntax);
        TestContext.WriteLine($"Actual recovery concat: kind={operation?.Kind}, constant={operation?.ConstantValue.HasValue}, left={operation?.ChildOperations.First().Kind}, right={operation?.ChildOperations.Last().Kind}");
        Assert.That(operation, Is.InstanceOf<IBinaryOperation>());
        var binary = (IBinaryOperation)operation!;
        Assert.That(binary.SemanticModel, Is.SameAs(model));
        Assert.That(binary.OperatorKind, Is.EqualTo(BinaryOperatorKind.Add));
        Assert.That(binary.OperatorMethod, Is.Null);
        Assert.That(binary.IsLifted, Is.False);
        TestContext.WriteLine($"Recovery binary type: kind={binary.Type?.TypeKind}, errorSymbol={binary.Type is IErrorTypeSymbol}, special={binary.Type?.SpecialType}, display={binary.Type?.ToDisplayString()}");
        var observationFactory = new IrFactory(IrExecutionSemantics.Total);
        var observationContext = new TotalLoweringContext(observationFactory, method);
        var observationBuilder = new IrProgramBuilder(observationFactory);
        var observationEntry = observationBuilder.CreateBlock("recovery-classification-observation-only");
        var observation = new RoslynTotalExpressionLowerer(observationContext, observationBuilder)
            .LowerBodyValue(binary, observationEntry);
        observationBuilder.Return(observation.Continuation, observationContext.Site(binary), observation.Value);
        var observationInstructions = observationBuilder.Build().Blocks.SelectMany(static block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions)).ToImmutableArray();
        TestContext.WriteLine($"Recovery expression API: exact={observation.Classification.IsExact}, reason={observation.Classification.Abstention}, sameEntry={observation.Continuation.Equals(observationEntry)}, value={observation.Value.GetType().Name}, type={observationFactory.GetTypeInfo(observation.Value.Type).Kind}, havocs={observationInstructions.OfType<IrHavocInstruction>().Count()}, assigns={observationInstructions.OfType<IrAssignInstruction>().Count()}");
        Assert.That(binary.Type, Is.InstanceOf<IErrorTypeSymbol>());
        Assert.That(binary.Type!.TypeKind, Is.EqualTo(TypeKind.Error));
        Assert.That(binary.Type.SpecialType, Is.EqualTo(SpecialType.None));
        Assert.That(binary.ConstantValue.HasValue, Is.False);
        Assert.That(CSharpOperationSemantics.IsStringConcatenation(binary), Is.False);
        foreach (var operand in ImmutableArray.Create(binary.LeftOperand, binary.RightOperand))
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
        Assert.That(lowered.Classification.IsExact, Is.False);
        Assert.That(lowered.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedType));
        Assert.That(lowered.Continuation, Is.EqualTo(entry));
        Assert.That(lowered.Value, Is.InstanceOf<IrVariableTerm>());
        Assert.That(lowered.Value.Type, Is.EqualTo(context.Type(binary.Type)));
        Assert.That(lowered.Value.Type, Is.Not.EqualTo(factory.StringType));
        Assert.That(factory.GetTypeInfo(lowered.Value.Type).Kind, Is.EqualTo(IrTypeKind.Reference));
        builder.Return(entry, context.Site(binary), lowered.Value);
        var expressionOutput = builder.Build();
        Assert.That(expressionOutput.Blocks, Has.Length.EqualTo(1));
        var instructions = expressionOutput.Blocks.Single().Instructions;
        var havocs = instructions.OfType<IrHavocInstruction>().ToImmutableArray();
        Assert.That(havocs, Has.Length.EqualTo(1));
        Assert.That(havocs[0].HavocKind, Is.EqualTo(IrHavocKind.Variables));
        Assert.That(havocs[0].Origin, Is.EqualTo(IrHavocOrigin.Approximation));
        Assert.That(havocs[0].Variables, Has.Length.EqualTo(1));
        Assert.That(havocs[0].Variables[0], Is.EqualTo(((IrVariableTerm)lowered.Value).Variable));
        Assert.That(instructions.OfType<IrAssignInstruction>(), Is.Empty);
        Assert.That(instructions.OfType<IrAllocationInstruction>(), Is.Empty);

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
