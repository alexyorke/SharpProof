using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using SharpProof.Frontend;

namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class RecursiveBuiltinWitnessAdmissionTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task SealedNominalFieldAliasRequiresACompatibleConcreteBuiltin(bool requireAlias)
    {
        const string source = "public sealed class Cell { public readonly string Text = \"\"; } public static class Example { public static string Test(Cell cell) => cell.Text; }";
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create("RecursiveBuiltinWitness", [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var root = await tree.GetRootAsync().ConfigureAwait(false);
        var syntax = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var model = compilation.GetSemanticModel(tree);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, (IMethodSymbol)model.GetDeclaredSymbol(syntax)!);
        var graph = ControlFlowGraph.Create(syntax, model);
        Assert.That(graph, Is.Not.Null);
        var lowered = new RoslynProgramLowerer(factory).LowerCandidate(graph!, context);
        Assert.That(lowered.IsExact, Is.True);
        var binding = context.Parameters.Single();
        var receiver = binding.Entry;
        var receiverTerm = factory.Variable(receiver);
        var actualRead = lowered.Program.Blocks.SelectMany(block => block.Instructions)
            .OfType<IrAssignInstruction>().Select(instruction => instruction.Value)
            .OfType<IrOpaqueTerm>().Single(term => factory.GetString(factory.GetMemberInfo(term.Member).Name)
                .StartsWith("field:", StringComparison.Ordinal));
        var field = IrSubstitution.Substitute(factory, actualRead, binding.Current, receiverTerm);
        Assert.That(field.Type, Is.EqualTo(factory.StringType));
        var nonnullReceiver = factory.Binary(IrBinaryOperator.NotEqual, receiverTerm, factory.Null(receiverTerm.Type));
        var nonnullField = factory.Binary(IrBinaryOperator.NotEqual, field, factory.Null(field.Type));
        var alias = factory.Binary(requireAlias ? IrBinaryOperator.Equal : IrBinaryOperator.NotEqual,
            factory.Cast(factory.ObjectType, field), factory.Cast(factory.ObjectType, receiverTerm));
        var query = new VerificationQuery(factory,
            new[] { nonnullReceiver, nonnullField, alias }.Select(predicate => new Assumption(factory, predicate,
                new LoweredJustification(factory.CreateOperation()))),
            new Goal(factory, factory.Boolean(false), ProofDiagnosticKind.Postcondition, new SourceLocationId(0)), [receiver]);
        using var backend = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var outcome = await new ProofKernel(backend).VerifyAsync(query, CancellationToken.None);
        Assert.That(outcome, requireAlias ? Is.TypeOf<UnknownOutcome>() : Is.TypeOf<RefutedOutcome>(), outcome.ToString());
    }
}
