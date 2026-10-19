using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class DiscardedStaticConversionDepthApiTests
{
    [TestCase(8, true)]
    [TestCase(256, false)]
    public void ActualNumericConversionChainRespectsDefaultDepthBoundary(int conversionCount, bool exact)
    {
        var expression = "State++";
        for (var i = 0; i < conversionCount; i++)
        {
            expression = (i % 2 == 0 ? "(long)" : "(int)") + expression;
        }
        var source = "public static class Subject { public static int State; public static int Target() { unchecked { _ = " +
            expression + "; } return 0; } }";
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12));
        var compilation = CSharpCompilation.Create("StaticConversionDepth_" + conversionCount, [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics().Where(d => d is null ? throw new System.ArgumentNullException(nameof(d)) : (d.Severity == DiagnosticSeverity.Error)), Is.Empty);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var target = (IMethodSymbol)model.GetDeclaredSymbol(syntax)!;
        Assert.That(target.ContainingType.StaticConstructors, Is.Empty);
        var graph = ControlFlowGraph.Create(syntax, model)!;
        var statement = graph.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Operations)).OfType<IExpressionStatementOperation>().Single();
        Assert.That(statement.Operation, Is.InstanceOf<ISimpleAssignmentOperation>());
        var assignment = (ISimpleAssignmentOperation)statement.Operation;
        Assert.That(assignment.IsRef, Is.False);
        Assert.That(assignment.Target, Is.InstanceOf<IDiscardOperation>());
        IOperation current = assignment.Value;
        for (var i = 0; i < conversionCount; i++)
        {
            Assert.That(current, Is.InstanceOf<IConversionOperation>());
            var conversion = (IConversionOperation)current;
            Assert.That(conversion.IsImplicit, Is.False);
            Assert.That(conversion.IsChecked, Is.False);
            Assert.That(conversion.OperatorMethod, Is.Null);
            Assert.That(conversion.Conversion.IsNumeric, Is.True);
            Assert.That(conversion.Type!.SpecialType,
                Is.EqualTo(i % 2 == 0 ? SpecialType.System_Int32 : SpecialType.System_Int64));
            Assert.That(conversion.Operand.Type!.SpecialType,
                Is.EqualTo(i % 2 == 0 ? SpecialType.System_Int64 : SpecialType.System_Int32));
            current = conversion.Operand;
        }
        Assert.That(current, Is.InstanceOf<IIncrementOrDecrementOperation>());
        var increment = (IIncrementOrDecrementOperation)current;
        Assert.That(increment.Kind, Is.EqualTo(OperationKind.Increment));
        Assert.That(increment.IsPostfix, Is.True);
        Assert.That(increment.IsChecked, Is.False);
        Assert.That(increment.OperatorMethod, Is.Null);
        Assert.That(increment.Target, Is.InstanceOf<IFieldReferenceOperation>());
        var field = (IFieldReferenceOperation)increment.Target;
        Assert.That(field.Instance, Is.Null);
        Assert.That(field.Field.IsStatic, Is.True);
        Assert.That(field.Field.Type.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        Assert.That(SymbolEqualityComparer.Default.Equals(field.Field.ContainingAssembly, compilation.Assembly), Is.True);
        var runtime = new AssemblyLoadContext("static-conversion-depth-" + conversionCount, isCollectible: true);
        try
        {
            image.Position = 0;
            var type = runtime.LoadFromStream(image).GetType("Subject")!;
            Assert.That(type.GetField("State")!.GetValue(null), Is.EqualTo(0));
            Assert.That(type.GetMethod("Target")!.Invoke(null, null), Is.EqualTo(0));
            Assert.That(type.GetField("State")!.GetValue(null), Is.EqualTo(1));
        }
        finally
        {
            runtime.Unload();
        }
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        Assert.That(context.OwnsBody(graph.OriginalOperation), Is.True);
        Assert.That(context.HasScalarSignature, Is.True);
        var lowered = new RoslynProgramLowerer(factory).LowerCandidate(graph, context);
        Assert.That(lowered.IsExact, Is.EqualTo(exact));
        Assert.That(lowered.ConstructionLimitExceeded, Is.False);
        if (!exact)
        {
            Assert.That(lowered.Classification.Abstention, Is.EqualTo(FrontendAbstention.ExpressionDepthLimit));
            return;
        }
        Assert.That(lowered.Classification.Abstention, Is.EqualTo(FrontendAbstention.None));
        var writes = lowered.Program.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions)).OfType<IrWriteInstruction>().ToImmutableArray();
        Assert.That(writes, Has.Length.EqualTo(1));
        Assert.That(writes[0].Region, Is.EqualTo(IrWriteRegion.Static));
        var replay = new IrProgramInterpreter(factory).Execute(lowered.Program);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(BigInteger.Zero));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ApproximationVariables, Is.Empty);
    }
}
