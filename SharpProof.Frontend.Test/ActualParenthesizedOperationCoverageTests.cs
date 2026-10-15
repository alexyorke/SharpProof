using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.VisualBasic;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ActualParenthesizedOperationCoverageTests
{
    [Test]
    public void ActualVisualBasicParenthesisPreservesOwnedIntegerClauseAndBodyValues()
    {
        const string source = "Public Class Subject\nPublic Shared Function Target(ByVal value As Integer) As Integer\nReturn (value)\nEnd Function\nEnd Class";
        var tree = VisualBasicSyntaxTree.ParseText(source, path: "parentheses.vb");
        var compilation = VisualBasicCompilation.Create("ActualParenthesizedSubject", [tree], TestMetadataReferences.Platform,
            new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optionStrict: OptionStrict.On, optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        using var image = new System.IO.MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True);
        var runtime = new System.Runtime.Loader.AssemblyLoadContext("ActualParenthesizedSubject", isCollectible: true);
        try
        {
            image.Position = 0;
            var method = runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Target")!;
            var native = method.Invoke(null, [17]);
            Assert.That(native, Is.EqualTo(17));
            var syntax = tree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.VisualBasic.Syntax.ParenthesizedExpressionSyntax>().Single();
            var operation = compilation.GetSemanticModel(tree).GetOperation(syntax)!;
            Assert.That(operation, Is.InstanceOf<IParenthesizedOperation>());
            var target = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
            var factory = new IrFactory(IrExecutionSemantics.Total);
            var context = new TotalLoweringContext(factory, target);
            var builder = new IrProgramBuilder(factory);
            var entry = builder.CreateBlock("entry");
            builder.SetEntry(entry);
            var lowerer = new RoslynTotalExpressionLowerer(context, builder);
            var environment = context.Parameters.ToDictionary(binding => binding.Current, _ => factory.CreateIntegerValue(17));
            var clause = lowerer.LowerClause(operation);
            Assert.That(clause.Classification.IsExact, Is.True);
            Assert.That(new IrInterpreter(factory).Evaluate(clause.SafeCondition, environment).Value!.Boolean, Is.True);
            Assert.That(new IrInterpreter(factory).Evaluate(clause.Value, environment).Value!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger((int)native!)));
            var body = lowerer.LowerBodyValue(operation, entry);
            Assert.That(body.Classification.IsExact, Is.True);
            builder.Return(body.Continuation, context.Site(operation), body.Value);
            var replay = new IrProgramInterpreter(factory).Execute(builder.Build(), environment);
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(replay.ConsumedApproximation, Is.False);
            Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger((int)native!)));
        }
        finally
        { runtime.Unload(); }
    }
}
