using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.VisualBasic;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class BulkVisualBasicScalarDomainCoverageTests
{
    [Test]
    public void ActualIntegerDivideClosesWholeScalarRuleAndOrdinaryAdditionRemainsExact()
    {
        const string source = "Public Class Subject\nPublic Shared Function Target(ByVal value As Integer, ByVal divisor As Integer) As Integer\nReturn value \\ divisor\nEnd Function\nPublic Shared Function Add(ByVal value As Integer, ByVal divisor As Integer) As Integer\nReturn value + divisor\nEnd Function\nEnd Class";
        var tree = VisualBasicSyntaxTree.ParseText(source, path: "integer-division.vb");
        var compilation = VisualBasicCompilation.Create("ActualIntegerDivision", [tree], TestMetadataReferences.Platform,
            new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optionStrict: OptionStrict.On, optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        using var image = new System.IO.MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True);
        var runtime = new System.Runtime.Loader.AssemblyLoadContext("ActualIntegerDivision", isCollectible: true);
        try
        {
            image.Position = 0;
            var type = runtime.LoadFromStream(image).GetType("Subject")!;
            var divide = type.GetMethod("Target")!;
            Assert.That(divide.Invoke(null, [7, 2]), Is.EqualTo(3));
            Assert.That(divide.Invoke(null, [-7, 2]), Is.EqualTo(-3));
            var syntax = tree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.VisualBasic.Syntax.BinaryExpressionSyntax>()
                .Single(node => node.OperatorToken.ValueText == "\\");
            var operation = (IBinaryOperation)compilation.GetSemanticModel(tree).GetOperation(syntax)!;
            Assert.That(operation.OperatorKind, Is.EqualTo(BinaryOperatorKind.IntegerDivide));
            Assert.That(operation.OperatorMethod, Is.Null);
            Assert.That(operation.IsLifted, Is.False);
            var target = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
            var factory = new IrFactory(IrExecutionSemantics.Total);
            var context = new TotalLoweringContext(factory, target);
            var lowerer = new RoslynTotalExpressionLowerer(context);
            var children = operation.ChildOperations.Select(child => lowerer.LowerClause(child)).ToArray();
            Assert.That(children, Has.Length.EqualTo(2));
            Assert.That(children.All(child => child.Classification.IsExact), Is.True);
            var rule = CSharpOperationSemantics.Apply(factory, operation, [.. children.Select(child => child.Value)]);
            Assert.That(rule.Classification.IsExact, Is.False);
            Assert.That(rule.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedOperationKind));
            Assert.That(rule.Throws, Is.Empty);
            Assert.That(new IrInterpreter(factory).Evaluate(rule.Value).Value!.Boolean, Is.False);
            var clause = lowerer.LowerClause(operation);
            Assert.That(clause.Classification.IsExact, Is.False);
            Assert.That(clause.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedOperationKind));
            Assert.That(new IrInterpreter(factory).Evaluate(clause.SafeCondition).Value!.Boolean, Is.False);
            var addMethod = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Add").OfType<IMethodSymbol>().Single();
            var addContext = new TotalLoweringContext(factory, addMethod);
            var addSyntax = tree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.VisualBasic.Syntax.BinaryExpressionSyntax>().Single(node => node.OperatorToken.ValueText == "+");
            var addOperation = compilation.GetSemanticModel(tree).GetOperation(addSyntax)!;
            var builder = new IrProgramBuilder(factory);
            var entry = builder.CreateBlock("entry");
            var exceptionalExit = builder.CreateBlock("exceptional-exit");
            builder.SetEntry(entry);
            builder.ExceptionalExit(exceptionalExit, addContext.Site(addOperation));
            var add = new RoslynTotalExpressionLowerer(addContext, builder, exceptionalExit).LowerBodyValue(addOperation, entry);
            Assert.That(add.Classification.IsExact, Is.True);
            builder.Return(add.Continuation, addContext.Site(addOperation), add.Value);
            var environment = addContext.Parameters.ToDictionary(binding => binding.Current, binding => factory.CreateIntegerValue(binding.Parameter.Ordinal == 0 ? 7 : 2));
            var replay = new IrProgramInterpreter(factory).Execute(builder.Build(), environment);
            Assert.That(type.GetMethod("Add")!.Invoke(null, [7, 2]), Is.EqualTo(9));
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(replay.ConsumedApproximation, Is.False);
            Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(9)));
        }
        finally
        { runtime.Unload(); }
    }
}
