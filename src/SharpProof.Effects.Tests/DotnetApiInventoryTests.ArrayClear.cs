using System.Collections.Immutable;
using Microsoft.CodeAnalysis.FlowAnalysis;
using SharpProof.Frontend;
using SharpProof.Ir;

namespace SharpProof.Effects.Test;

public sealed partial class DotnetApiInventoryTests
{
    [Test]
    public void ArrayClearOwnerHasNoStaticTypeInitializer()
    {
        Assert.That(typeof(Array).TypeInitializer, Is.Null, typeof(Array).Assembly.FullName);
    }

    private static bool CheckArrayClearIntrinsic(CSharpCompilation references, string id, bool wrongClaim)
    {
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(id);
        var whole = id == "M:System.Array.Clear(System.Array)";
        var tree = CSharpSyntaxTree.ParseText("public static class ClearProbe { public static int Check(int[] data, int start, int length) { var alias = data; System.Array.Clear(data" +
            (whole ? "" : ", start, length") + "); return alias[0] + alias[1] + alias[2] + alias[3]; } }");
        var compilation = references.AddSyntaxTrees(tree);
        if (compilation.GetDiagnostics().Any(diagnostic => diagnostic is null ? throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Severity == DiagnosticSeverity.Error))
        { return false; }
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, model.GetDeclaredSymbol(syntax)!);
        var lowered = new RoslynProgramLowerer(factory).LowerCandidate(ControlFlowGraph.Create(syntax, model)!, context,
            static _ => true, null, TestContext.CurrentContext.CancellationToken, opaqueCalls: true);
        if (!lowered.IsExact)
        { return false; }
        for (var start = 0; start <= 4; start++)
        {
            for (var length = 0; length <= 4 - start; length++)
            {
                int[] expected = [3, 5, 7, 11];
                var argumentBuilder = ImmutableDictionary.CreateBuilder<ScopedIrId<IrVariableTag>, IrValue>();
                argumentBuilder[context.Parameters[0].Entry] = factory.CreateSequenceValue(factory.GetVariableInfo(context.Parameters[0].Entry).Type,
                    expected.Select(value => factory.CreateIntegerValue(value)));
                argumentBuilder[context.Parameters[1].Entry] = factory.CreateIntegerValue(start);
                argumentBuilder[context.Parameters[2].Entry] = factory.CreateIntegerValue(length);
                var arguments = argumentBuilder.ToImmutable();
                if (whole)
                { Array.Clear(expected); }
                else
                { Array.Clear(expected, start, length); }
                var actual = new IrProgramInterpreter(factory).Execute(lowered.Program, arguments);
                if (actual.Status != IrProgramExecutionStatus.Returned || actual.ConsumedApproximation ||
                    actual.ReturnValue!.Integer != expected.Sum() + (wrongClaim ? 1 : 0))
                { return false; }
            }
        }
        return true;
    }
}
