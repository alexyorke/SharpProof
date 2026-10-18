using System.Globalization;
using System.Numerics;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Contracts;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

public sealed class SourceCallImportBudgetSweepTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void PreconditionsAndFilterImportsCloseCleanlyAcrossWorkAllowances(bool filtered)
    {
        var requirements = string.Join(" ", Enumerable.Range(0, 8).Select(index =>
            "SharpProof.Attributes.Contract.Requires(value >= " + index.ToString(CultureInfo.InvariantCulture) + ");"));
        const string calls = "value = Helper(value); value = Helper(value); return value;";
        var source = "public static class Subject { public static int Target(int value) { " +
            (filtered ? "try { " + calls + " } catch (System.Exception) when (value == 0) { return -1; }" : calls) +
            " } public static int Helper(int value) { " + requirements + " return value + 1; } }";
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12), "source-call-budget-sweep.cs");
        var compilation = CSharpCompilation.Create("SourceCallBudgetSweep", [tree], TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new ArgumentNullException(nameof(diagnostic)) :
            diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        Assert.That(emission.Success, Is.True, string.Join(Environment.NewLine, emission.Diagnostics));
        var load = new AssemblyLoadContext("SourceCallBudgetSweep", isCollectible: true);
        try
        {
            image.Position = 0;
            var runtime = load.LoadFromStream(image).GetType("Subject")!;
            Assert.That(runtime.GetMethod("Target")!.Invoke(null, [17]), Is.EqualTo(19));
            var model = compilation.GetSemanticModel(tree);
            var declarations = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
            var target = declarations.Single(method => method is null ? throw new ArgumentNullException(nameof(method)) : method.Identifier.ValueText == "Target");
            var helper = declarations.Single(method => method is null ? throw new ArgumentNullException(nameof(method)) : method.Identifier.ValueText == "Helper");
            var targetSymbol = model.GetDeclaredSymbol(target)!;
            var helperSymbol = model.GetDeclaredSymbol(helper)!;
            var graph = ControlFlowGraph.Create(target, model)!;
            var exactSeen = false;
            var limitSeen = false;
            for (var available = 0; available <= RoslynTotalProgramLowerer.MaximumRegionSteps; available++)
            {
                var factory = new IrFactory(IrExecutionSemantics.Total);
                var context = new TotalLoweringContext(factory, targetSymbol);
                bool Prepare(TotalLoweringContext candidate)
                {
                    ArgumentNullException.ThrowIfNull(candidate);
                    var binding = new ContractBinder(compilation, factory).BindTotal(candidate);
                    Assert.That(SymbolEqualityComparer.Default.Equals(candidate.Target, helperSymbol), Is.True);
                    Assert.That(binding.IsSuccess, Is.True, binding.Failure.ToString());
                    Assert.That(binding.Clauses, Has.Length.EqualTo(8));
                    candidate.SourceCallPreconditions = [.. binding.Clauses.Select(clause =>
                        clause is null ? throw new ArgumentNullException(nameof(clause)) : new TotalSourcePrecondition(clause.Value, clause.SafeCondition, clause.SourceOperation))];
                    return true;
                }
                var session = new TotalSourceCallSession(compilation, Prepare, null, default);
                Assert.That(session.Spend(RoslynTotalProgramLowerer.MaximumRegionSteps - available), Is.True);
                var lowered = new RoslynTotalProgramLowerer(context, default, session).Lower(graph);
                Assert.That(session.IsActive(targetSymbol), Is.False);
                Assert.That(session.IsActive(helperSymbol), Is.False);
                Assert.That(lowered.ConstructionLimitExceeded, Is.EqualTo(session.ConstructionLimitExceeded));
                if (lowered.ConstructionLimitExceeded)
                {
                    Assert.That(exactSeen, Is.False, "A larger allowance must not invalidate an admissible source-call import.");
                    Assert.That(lowered.IsExact, Is.False);
                    limitSeen = true;
                    continue;
                }
                Assert.That(lowered.IsExact, Is.True);
                Assert.That(lowered.CallPreconditions, Has.Count.EqualTo(16));
                var replay = new IrProgramInterpreter(factory).Execute(lowered.Program,
                    new Dictionary<ScopedIrId<IrVariableTag>, IrValue> { [context.Parameters[0].Entry] = factory.CreateIntegerValue(17) });
                Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
                Assert.That(replay.ConsumedApproximation, Is.False);
                Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(19)));
                if (!exactSeen)
                { TestContext.WriteLine($"First exact import: filtered={filtered}; available={available}"); }
                exactSeen = true;
            }
            Assert.That(limitSeen, Is.True);
            Assert.That(exactSeen, Is.True);
        }
        finally
        { load.Unload(); }
    }
}
