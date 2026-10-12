using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class BulkCompilerChildGuardCoverageTests
{
    private static readonly string[] NullPatternErrors = ["CS0037"];

    [Test]
    public void ActualNullableCfgNullCheckRetainsLegacyChildTypeClosure()
    {
        using var subject = TypedProgramSubject.Create("int Target(int? value) => value ?? 7;");
        Assert.That(subject.Invoke([17]), Is.EqualTo(17));
        Assert.That(subject.Invoke([null!]), Is.EqualTo(7));
        var operation = subject.Graph.Blocks.Select(block => block.BranchValue).OfType<IIsNullOperation>().Single();
        var result = new RoslynOperationLowerer(new IrFactory()).Lower(operation);
        Assert.That(result.IsExact, Is.False);
        Assert.That(result.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedType));
        using var supported = TypedProgramSubject.Create("string Target(string value) => value ?? \"fallback\";");
        Assert.That(supported.Invoke(["word"]), Is.EqualTo("word"));
        Assert.That(supported.Invoke([null!]), Is.EqualTo("fallback"));
        var supportedOperation = supported.Graph.Blocks.Select(block => block.BranchValue).OfType<IIsNullOperation>().Single();
        var factory = new IrFactory();
        var lowerer = new RoslynOperationLowerer(factory);
        var exact = lowerer.Lower(supportedOperation);
        Assert.That(exact.IsExact, Is.True);
        var capture = lowerer.CreateCaptureBindings().Single();
        var environment = new Dictionary<IrVarId, IrValue> { [capture] = factory.CreateStringValue("word") };
        Assert.That(new IrInterpreter(factory).Evaluate(exact.Term, environment).Value!.Boolean, Is.False);
        environment[capture] = factory.CreateNullValue(factory.StringType);
        Assert.That(new IrInterpreter(factory).Evaluate(exact.Term, environment).Value!.Boolean, Is.True);
    }

    [Test]
    public void RealGenericTypeTestPreservesUnsupportedUnboxingChild()
    {
        using var subject = TypedProgramSubject.Create("bool Target(object value) => Helper<int>(value); static bool Helper<T>(object value) => ((T)value) is int;");
        Assert.That(subject.Invoke([17]), Is.True);
        Assert.That(subject.Invoke(["word"]), Is.InstanceOf<InvalidCastException>());
        var tree = subject.Compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == "Helper");
        var model = subject.Compilation.GetSemanticModel(tree);
        var target = (IMethodSymbol)model.GetDeclaredSymbol(syntax)!;
        var operation = (IIsTypeOperation)model.GetOperation(syntax.ExpressionBody!.Expression)!;
        Assert.That(CSharpOperationSemantics.OpaqueTypeTestOperand(operation), Is.SameAs(operation.ValueOperand));
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        var exit = builder.CreateBlock("exceptional-exit");
        builder.SetEntry(entry);
        var body = new RoslynTotalExpressionLowerer(context, builder, exit).LowerBodyValue(operation, entry);
        Assert.That(body.Classification.IsExact, Is.False);
        Assert.That(body.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedType));
        using var supported = TypedProgramSubject.Create("bool Target(int value) => value == 17;");
        Assert.That(supported.Invoke([17]), Is.True);
        var lowering = supported.Lower();
        Assert.That(lowering.IsExact, Is.True);
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        var replay = supported.Execute(lowering, [17]);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.Boolean, Is.True);
    }

    [Test]
    public void RealRejectedScalarNullPatternCannotProduceAnExactConstantClause()
    {
        var tree = CSharpSyntaxTree.ParseText("public static class Subject { public static bool Target(int value) => value is null; }", new CSharpParseOptions(LanguageVersion.CSharp12), "scalar-null-pattern.cs");
        var compilation = CSharpCompilation.Create("ScalarNullPattern", [tree], TestMetadataReferences.Platform, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Select(diagnostic => diagnostic.Id), Is.EqualTo(NullPatternErrors));
        using var image = new System.IO.MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.False);
        var syntax = tree.GetRoot().DescendantNodes().OfType<IsPatternExpressionSyntax>().Single();
        var model = compilation.GetSemanticModel(tree);
        var operation = (IIsPatternOperation)model.GetOperation(syntax)!;
        var methodSyntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var target = (IMethodSymbol)model.GetDeclaredSymbol(methodSyntax)!;
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        Assert.That(operation.Pattern, Is.InstanceOf<IConstantPatternOperation>());
        var value = ((IConstantPatternOperation)operation.Pattern).Value;
        Assert.That(value.ConstantValue.HasValue, Is.True);
        Assert.That(value.ConstantValue.Value, Is.Null);
        var clause = new RoslynTotalExpressionLowerer(context).LowerClause(operation);
        Assert.That(clause.Classification.IsExact, Is.False);
        Assert.That(clause.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedOperationKind));
        Assert.That(new IrInterpreter(factory).Evaluate(clause.SafeCondition).Value!.Boolean, Is.False);
        using var supported = TypedProgramSubject.Create("bool Target(int value) => value is 17;");
        var lowering = supported.Lower();
        Assert.That(lowering.IsExact, Is.True);
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        for (var ordinal = 0; ordinal < 2; ordinal++)
        {
            var input = ordinal == 0 ? 17 : 9;
            var expected = ordinal == 0;
            Assert.That(supported.Invoke([input]), Is.EqualTo(expected));
            var replay = supported.Execute(lowering, [input]);
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(replay.ConsumedApproximation, Is.False);
            Assert.That(replay.ReturnValue!.Boolean, Is.EqualTo(expected));
        }
    }
}
