using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class VisualBasicNarrowCompoundShiftTests
{
    // SP-VB-NARROW-SHIFT-001: genuine compiler graphs must agree with emitted CLR code.
    [TestCase("SByte", (sbyte)6, 8, true, 6)]
    [TestCase("Byte", (byte)6, 8, true, 6)]
    [TestCase("Short", (short)6, 16, true, 6)]
    [TestCase("UShort", (ushort)6, 16, true, 6)]
    [TestCase("SByte", (sbyte)6, 8, false, 6)]
    [TestCase("Byte", (byte)6, 8, false, 6)]
    [TestCase("Short", (short)6, 16, false, 6)]
    [TestCase("UShort", (ushort)6, 16, false, 6)]
    [TestCase("SByte", (sbyte)64, 1, true, -128)]
    [TestCase("Byte", (byte)128, 1, true, 0)]
    [TestCase("Short", (short)16384, 1, true, -32768)]
    [TestCase("UShort", (ushort)32768, 1, true, 0)]
    [TestCase("SByte", (sbyte)-128, -1, false, -1)]
    [TestCase("Byte", (byte)128, -1, false, 1)]
    [TestCase("Short", (short)-32768, -1, false, -1)]
    [TestCase("UShort", (ushort)32768, -1, false, 1)]
    public void VisualBasicShiftMatchesEmittedRuntime(string storageType, object input, int count, bool leftShift, int expected)
    {
        System.ArgumentNullException.ThrowIfNull(storageType);
        ArgumentNullException.ThrowIfNull(input);
        var source = "Public Class Subject\nPublic Shared Function Target(value As " + storageType + ", count As Integer) As " + storageType +
            "\nvalue " + (leftShift ? "<<=" : ">>=") + " count\nReturn value\nEnd Function\nEnd Class";
        var tree = VisualBasicSyntaxTree.ParseText(source, path: "narrow-compound-shift.vb");
        var compilation = VisualBasicCompilation.Create("NarrowCompoundShift", [tree], TestMetadataReferences.Platform,
            new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optionStrict: OptionStrict.On,
                checkOverflow: true, optimizationLevel: OptimizationLevel.Release));
        var model = compilation.GetSemanticModel(tree);
        var method = tree.GetRoot().DescendantNodes().OfType<MethodBlockSyntax>().Single();
        var statement = model.GetOperation(method.Statements.OfType<AssignmentStatementSyntax>().Single());
        Assert.That(statement, Is.InstanceOf<IExpressionStatementOperation>());
        var expression = ((IExpressionStatementOperation)statement!).Operation;
        Assert.That(expression, Is.InstanceOf<ICompoundAssignmentOperation>());
        var assignment = (ICompoundAssignmentOperation)expression;
        Assert.That(assignment.SemanticModel!.Compilation, Is.SameAs(compilation));
        Assert.That(assignment.OperatorKind, Is.EqualTo(leftShift ? BinaryOperatorKind.LeftShift : BinaryOperatorKind.RightShift));
        Assert.That(assignment.IsLifted, Is.False);
        Assert.That(assignment.OperatorMethod, Is.Null);
        Assert.That(assignment.InConversion.IsUserDefined, Is.False);
        Assert.That(assignment.OutConversion.IsUserDefined, Is.False);
        Assert.That(assignment.Value.Type!.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        Assert.That(compilation.Options.CheckOverflow, Is.True);
        AssertRuntimeAndReplay(compilation, ControlFlowGraph.Create(method, model)!, input, count, expected, false);
    }

    [TestCase("sbyte", (sbyte)64, 8, -128)]
    [TestCase("byte", (byte)128, 8, 0)]
    [TestCase("short", (short)16384, 16, -32768)]
    [TestCase("ushort", (ushort)32768, 16, 0)]
    public void CSharpKeepsPromotedMaskAndCheckedStorage(string storageType, object boundary, int width, int wrapped)
    {
        System.ArgumentNullException.ThrowIfNull(storageType);
        ArgumentNullException.ThrowIfNull(boundary);
        foreach (var isChecked in new[] { false, true })
        {
            var source = "public class Subject { public static " + storageType + " Target(" + storageType +
                " value, int count) { " + (isChecked ? "checked" : "unchecked") + " { value <<= count; return value; } } }";
            var tree = CSharpSyntaxTree.ParseText(source, path: "narrow-compound-shift.cs");
            var compilation = CSharpCompilation.Create("CSharpNarrowCompoundShift", [tree], TestMetadataReferences.Platform,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, checkOverflow: !isChecked,
                    optimizationLevel: OptimizationLevel.Release));
            var model = compilation.GetSemanticModel(tree);
            var method = tree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>().Single();
            var graph = ControlFlowGraph.Create(method, model)!;
            AssertRuntimeAndReplay(compilation, graph, boundary, 1, wrapped, isChecked);
            var six = Convert.ChangeType(6, boundary.GetType(), CultureInfo.InvariantCulture);
            AssertRuntimeAndReplay(compilation, graph, six, width, 0, isChecked);
            AssertRuntimeAndReplay(compilation, graph, six, 1, 12, false);
        }
    }

    private static void AssertRuntimeAndReplay(Compilation compilation, ControlFlowGraph graph, object input,
        int count, int expected, bool overflow)
    {
        System.ArgumentNullException.ThrowIfNull(input);
        System.ArgumentNullException.ThrowIfNull(graph);
        System.ArgumentNullException.ThrowIfNull(compilation);
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var runtime = new AssemblyLoadContext("NarrowCompoundShift", isCollectible: true);
        try
        {
            image.Position = 0;
            var method = runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Target")!;
            if (overflow)
            {
                var exception = Assert.Throws<TargetInvocationException>((Action)(() => method.Invoke(null, [input, count])));
                Assert.That(exception!.InnerException, Is.TypeOf<OverflowException>());
            }
            else
            {
                var actual = method.Invoke(null, [input, count]);
                Assert.That(actual, Is.TypeOf(input.GetType()));
                Assert.That(Convert.ToInt64(actual, CultureInfo.InvariantCulture), Is.EqualTo(expected), "emitted CLR result");
            }
        }
        finally
        {
            runtime.Unload();
        }

        var target = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        Assert.That(context.HasScalarSignature, Is.True);
        Assert.That(context.OwnsBody(graph.OriginalOperation), Is.True);
        var lowering = new RoslynProgramLowerer(factory).LowerCandidate(graph, context);
        Assert.That(lowering.IsExact, Is.True, lowering.Classification.Abstention.ToString());
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        var environment = context.Parameters.ToDictionary(binding => binding is null ? throw new System.ArgumentNullException(nameof(binding)) : (binding.Entry),
            binding => binding is null ? throw new System.ArgumentNullException(nameof(binding)) : (factory.CreateIntegerValue(factory.GetVariableInfo(binding.Entry).Type,
                binding.Parameter.Ordinal == 0 ? Convert.ToInt64(input, CultureInfo.InvariantCulture) : count)));
        var replay = new IrProgramInterpreter(factory).Execute(lowering.Program, environment);
        Assert.That(replay.ConsumedApproximation, Is.False);
        if (overflow)
        {
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Exception));
            Assert.That(replay.Exception!.Kind, Is.EqualTo(IrExceptionKind.Overflow));
        }
        else
        {
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(expected)), "exact IR result");
        }
    }
}
