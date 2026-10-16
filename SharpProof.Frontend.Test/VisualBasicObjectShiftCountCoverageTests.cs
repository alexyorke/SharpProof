using System.Globalization;
using System.Numerics;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class VisualBasicObjectShiftCountCoverageTests
{
    private static readonly string[] RuntimeMethodNames = ["Target", "Control"];

    [TestCase("SByte", (sbyte)6, true)]
    [TestCase("Byte", (byte)6, true)]
    [TestCase("Short", (short)6, true)]
    [TestCase("UShort", (ushort)6, true)]
    [TestCase("Integer", 6, true)]
    [TestCase("UInteger", 6U, true)]
    [TestCase("Long", 6L, true)]
    [TestCase("ULong", 6UL, true)]
    [TestCase("SByte", (sbyte)6, false)]
    [TestCase("Byte", (byte)6, false)]
    [TestCase("Short", (short)6, false)]
    [TestCase("UShort", (ushort)6, false)]
    [TestCase("Integer", 6, false)]
    [TestCase("UInteger", 6U, false)]
    [TestCase("Long", 6L, false)]
    [TestCase("ULong", 6UL, false)]
    public void ActualObjectCountRefusesMutationAndIntegerCountReplaysExactly(string storageType, object input, bool leftShift)
    {
        var assignment = leftShift ? "<<=" : ">>=";
        var source = "Public Class Subject\nPublic Shared Function Target(value As " + storageType + ", count As Object) As " + storageType +
            "\nvalue " + assignment + " count\nReturn value\nEnd Function\nPublic Shared Function Control(value As " + storageType +
            ", count As Integer) As " + storageType + "\nvalue " + assignment + " count\nReturn value\nEnd Function\nEnd Class";
        var tree = VisualBasicSyntaxTree.ParseText(source, path: "object-shift-count.vb");
        var compilation = VisualBasicCompilation.Create("ObjectShiftCount", [tree], TestMetadataReferences.Platform,
            new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optionStrict: OptionStrict.Off,
                optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var runtime = new AssemblyLoadContext("ObjectShiftCount", isCollectible: true);
        var expected = leftShift ? 12 : 3;
        try
        {
            image.Position = 0;
            var type = runtime.LoadFromStream(image).GetType("Subject")!;
            foreach (var name in RuntimeMethodNames)
            {
                var actual = type.GetMethod(name)!.Invoke(null, [input, 1]);
                Assert.That(actual, Is.TypeOf(input.GetType()));
                Assert.That(Convert.ToInt64(actual, CultureInfo.InvariantCulture), Is.EqualTo(expected));
            }
        }
        finally
        {
            runtime.Unload();
        }

        var model = compilation.GetSemanticModel(tree);
        var methods = tree.GetRoot().DescendantNodes().OfType<MethodBlockSyntax>().ToArray();
        Assert.That(methods, Has.Length.EqualTo(2));
        var operation = (ICompoundAssignmentOperation)model.GetOperation(methods[0].Statements.OfType<AssignmentStatementSyntax>().Single())!;
        Assert.That(operation.SemanticModel!.Compilation, Is.SameAs(compilation));
        Assert.That(operation.OperatorKind, Is.EqualTo(leftShift ? BinaryOperatorKind.LeftShift : BinaryOperatorKind.RightShift));
        Assert.That(operation.IsLifted, Is.False);
        Assert.That(operation.OperatorMethod, Is.Null);
        Assert.That(operation.InConversion.IsUserDefined, Is.False);
        Assert.That(operation.OutConversion.IsUserDefined, Is.False);
        Assert.That(operation.Value.Type!.SpecialType, Is.EqualTo(SpecialType.System_Object));
        var target = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        Assert.That(context.HasScalarSignature, Is.True);
        Assert.That(SymbolEqualityComparer.Default.Equals(operation.Target.Type, target.Parameters[0].Type), Is.True);
        Assert.That(operation.Target.Type!.SpecialType, Is.EqualTo(target.Parameters[0].Type.SpecialType));
        Assert.That(CSharpOperationSemantics.IsScalar(operation.Target.Type), Is.True);
        var expressions = new RoslynTotalExpressionLowerer(context);
        var left = expressions.LowerClause(operation.Target);
        var right = expressions.LowerClause(operation.Value);
        Assert.That(left.Classification.IsExact, Is.True);
        Assert.That(right.Classification.IsExact, Is.True);
        Assert.That(factory.GetTypeInfo(left.Value.Type).Kind, Is.EqualTo(IrTypeKind.Integer));
        Assert.That(factory.GetTypeInfo(right.Value.Type).Kind, Is.EqualTo(IrTypeKind.Reference));
        var graph = ControlFlowGraph.Create(methods[0], model)!;
        Assert.That(context.OwnsBody(graph.OriginalOperation), Is.True);
        var lowering = new RoslynProgramLowerer(factory).LowerCandidate(graph, context);
        Assert.That(lowering.IsExact, Is.False);
        Assert.That(lowering.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedMutation));
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);

        var control = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Control").OfType<IMethodSymbol>().Single();
        var controlFactory = new IrFactory(IrExecutionSemantics.Total);
        var controlContext = new TotalLoweringContext(controlFactory, control);
        var controlGraph = ControlFlowGraph.Create(methods[1], model)!;
        Assert.That(controlContext.OwnsBody(controlGraph.OriginalOperation), Is.True);
        var controlLowering = new RoslynProgramLowerer(controlFactory).LowerCandidate(controlGraph, controlContext);
        Assert.That(controlLowering.IsExact, Is.True, controlLowering.Classification.Abstention.ToString());
        Assert.That(controlLowering.ConstructionLimitExceeded, Is.False);
        var environment = controlContext.Parameters.ToDictionary(binding => binding.Entry,
            binding => controlFactory.CreateIntegerValue(controlFactory.GetVariableInfo(binding.Entry).Type, binding.Parameter.Ordinal == 0 ? 6 : 1));
        var replay = new IrProgramInterpreter(controlFactory).Execute(controlLowering.Program, environment);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(expected)));
    }
}
