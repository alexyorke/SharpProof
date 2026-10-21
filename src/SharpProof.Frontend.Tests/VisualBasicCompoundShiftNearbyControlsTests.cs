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
public sealed class VisualBasicCompoundShiftNearbyControlsTests
{
    [TestCase("SByte", (sbyte)6)]
    [TestCase("Byte", (byte)6)]
    [TestCase("Short", (short)6)]
    [TestCase("UShort", (ushort)6)]
    public void NarrowObjectCountRemainsUnsupported(string storageType, object input)
    {
        System.ArgumentNullException.ThrowIfNull(storageType);
        ArgumentNullException.ThrowIfNull(input);
        var compilation = CreateCompilation(storageType, "Object", true, OptionStrict.Off);
        AssertRuntime(compilation, input, 1, 12);
        var (graph, assignment, context, factory) = CreateCandidate(compilation);
        Assert.That(assignment.Value.Type!.SpecialType, Is.EqualTo(SpecialType.System_Object));
        var expressions = new RoslynTotalExpressionLowerer(context);
        var left = expressions.LowerClause(assignment.Target);
        var right = expressions.LowerClause(assignment.Value);
        Assert.That(left.Classification.IsExact, Is.True);
        Assert.That(right.Classification.IsExact, Is.True);
        Assert.That(factory.GetTypeInfo(left.Value.Type).Kind, Is.EqualTo(IrTypeKind.Integer));
        Assert.That(factory.GetTypeInfo(right.Value.Type).Kind, Is.EqualTo(IrTypeKind.Reference));
        var lowering = new RoslynProgramLowerer(factory).LowerCandidate(graph, context);
        Assert.That(lowering.IsExact, Is.False);
        Assert.That(lowering.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedMutation));
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
    }

    [TestCase("Integer", 6, 32, true, 6)]
    [TestCase("UInteger", 6U, 32, true, 6)]
    [TestCase("Long", 6L, 64, true, 6)]
    [TestCase("ULong", 6UL, 64, true, 6)]
    [TestCase("Integer", int.MinValue, -1, false, -1)]
    [TestCase("UInteger", 2147483648U, -1, false, 1)]
    [TestCase("Long", long.MinValue, -1, false, -1)]
    [TestCase("ULong", 9223372036854775808UL, -1, false, 1)]
    public void WideTypedCountKeepsNativeMaskAndSign(string storageType, object input, int count, bool leftShift, int expected)
    {
        System.ArgumentNullException.ThrowIfNull(storageType);
        ArgumentNullException.ThrowIfNull(input);
        var compilation = CreateCompilation(storageType, "Integer", leftShift, OptionStrict.On);
        AssertRuntime(compilation, input, count, expected);
        var (graph, assignment, context, factory) = CreateCandidate(compilation);
        Assert.That(assignment.Value.Type!.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        var lowering = new RoslynProgramLowerer(factory).LowerCandidate(graph, context);
        Assert.That(lowering.IsExact, Is.True, lowering.Classification.Abstention.ToString());
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        var environment = ImmutableDictionaryCapture.Create(context.Parameters, binding => binding is null ? throw new System.ArgumentNullException(nameof(binding)) : (binding.Entry),
            binding => binding is null ? throw new System.ArgumentNullException(nameof(binding)) : (binding.Parameter.Ordinal == 0 && input is ulong unsigned
                ? factory.CreateIntegerValue(factory.GetVariableInfo(binding.Entry).Type, unsigned)
                : factory.CreateIntegerValue(factory.GetVariableInfo(binding.Entry).Type,
                    binding.Parameter.Ordinal == 0 ? Convert.ToInt64(input, CultureInfo.InvariantCulture) : count)));
        var replay = new IrProgramInterpreter(factory).Execute(lowering.Program, environment);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(expected)));
    }

    private static VisualBasicCompilation CreateCompilation(string storageType, string countType, bool leftShift, OptionStrict strict)
    {
        System.ArgumentNullException.ThrowIfNull(countType);
        System.ArgumentNullException.ThrowIfNull(storageType);
        var source = "Public Class Subject\nPublic Shared Function Target(value As " + storageType + ", count As " + countType + ") As " + storageType +
            "\nvalue " + (leftShift ? "<<=" : ">>=") + " count\nReturn value\nEnd Function\nEnd Class";
        var tree = VisualBasicSyntaxTree.ParseText(source, path: "compound-shift-nearby-control.vb");
        var compilation = VisualBasicCompilation.Create("CompoundShiftNearbyControl", [tree], TestMetadataReferences.Platform,
            new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optionStrict: strict,
                checkOverflow: true, optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        return compilation;
    }

    private static (ControlFlowGraph Graph, ICompoundAssignmentOperation Assignment, TotalLoweringContext Context, IrFactory Factory)
        CreateCandidate(VisualBasicCompilation compilation)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var method = tree.GetRoot().DescendantNodes().OfType<MethodBlockSyntax>().Single();
        var statement = model.GetOperation(method.Statements.OfType<AssignmentStatementSyntax>().Single());
        Assert.That(statement, Is.InstanceOf<IExpressionStatementOperation>());
        var expression = ((IExpressionStatementOperation)statement!).Operation;
        Assert.That(expression, Is.InstanceOf<ICompoundAssignmentOperation>());
        var assignment = (ICompoundAssignmentOperation)expression;
        Assert.That(assignment.SemanticModel!.Compilation, Is.SameAs(compilation));
        Assert.That(assignment.IsLifted, Is.False);
        Assert.That(assignment.OperatorMethod, Is.Null);
        Assert.That(assignment.InConversion.IsUserDefined, Is.False);
        Assert.That(assignment.OutConversion.IsUserDefined, Is.False);
        var target = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        var graph = ControlFlowGraph.Create(method, model)!;
        Assert.That(context.HasScalarSignature, Is.True);
        Assert.That(context.OwnsBody(graph.OriginalOperation), Is.True);
        return (graph, assignment, context, factory);
    }

    private static void AssertRuntime(VisualBasicCompilation compilation, object input, int count, int expected)
    {
        System.ArgumentNullException.ThrowIfNull(input);
        System.ArgumentNullException.ThrowIfNull(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var runtime = new AssemblyLoadContext("CompoundShiftNearbyControl", isCollectible: true);
        try
        {
            image.Position = 0;
            var actual = runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Target")!.Invoke(null, [input, count]);
            Assert.That(actual, Is.TypeOf(input.GetType()));
            Assert.That(Convert.ToInt64(actual, CultureInfo.InvariantCulture), Is.EqualTo(expected));
        }
        finally
        {
            runtime.Unload();
        }
    }
}
