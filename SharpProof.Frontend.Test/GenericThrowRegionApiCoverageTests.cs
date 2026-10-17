using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class GenericThrowRegionApiCoverageTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void ActualThrownTypeParameterRetainsConservativeRegionAdmission(bool generic)
    {
        var source = generic
            ? "int Target() => Helper<System.Exception>(new System.Exception()); static int Helper<T>(T error) where T : System.Exception { try { throw error; } catch (System.Exception) { return 17; } }"
            : "int Target() => Helper(new System.Exception()); static int Helper(System.Exception error) { try { throw error; } catch (System.Exception) { return 17; } }";
        using var subject = TypedProgramSubject.Create(source);
        Assert.That(subject.Invoke([]), Is.EqualTo(17));
        var tree = subject.Compilation.SyntaxTrees.Single();
        var model = subject.Compilation.GetSemanticModel(tree);
        var callSyntax = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var invocation = (IInvocationOperation)model.GetOperation(callSyntax)!;
        Assert.That(invocation.Instance, Is.Null);
        Assert.That(invocation.Arguments, Has.Length.EqualTo(1));
        Assert.That(invocation.Arguments[0].ArgumentKind, Is.EqualTo(ArgumentKind.Explicit));
        Assert.That(SymbolEqualityComparer.Default.Equals(invocation.Arguments[0].Parameter!.ContainingSymbol,
            invocation.TargetMethod), Is.True);
        var helperSyntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method.Identifier.ValueText == "Helper"));
        var helperSymbol = model.GetDeclaredSymbol(helperSyntax)!;
        Assert.That(SymbolEqualityComparer.Default.Equals(helperSymbol, invocation.TargetMethod.OriginalDefinition), Is.True);
        var graph = ControlFlowGraph.Create(helperSyntax, model)!;
        var throwSyntax = helperSyntax.DescendantNodes().OfType<ThrowStatementSyntax>().Single();
        var thrown = (IThrowOperation)model.GetOperation(throwSyntax)!;
        var operand = CSharpOperationSemantics.ThrownOperand(thrown.Exception!);
        if (generic)
        {
            Assert.That(thrown.Exception, Is.AssignableTo<IConversionOperation>());
            var conversion = (IConversionOperation)thrown.Exception!;
            Assert.That(conversion.IsImplicit, Is.True);
            Assert.That(conversion.OperatorMethod, Is.Null);
            Assert.That(conversion.Conversion.IsReference, Is.True);
            Assert.That(operand, Is.SameAs(conversion.Operand));
        }
        else
        {
            Assert.That(thrown.Exception, Is.AssignableTo<IParameterReferenceOperation>());
            Assert.That(operand, Is.SameAs(thrown.Exception));
        }
        Assert.That(operand, Is.AssignableTo<IParameterReferenceOperation>());
        Assert.That(operand.Type!.TypeKind, Is.EqualTo(generic ? TypeKind.TypeParameter : TypeKind.Class));
        Assert.That(CSharpOperationSemantics.ThrownType(subject.Compilation, operand) == null, Is.EqualTo(generic));
        var preparationCalls = 0;
        bool PrepareCallee(TotalLoweringContext candidate)
        {
            System.ArgumentNullException.ThrowIfNull(candidate);
            preparationCalls++;
            Assert.That(SymbolEqualityComparer.Default.Equals(candidate.Target, helperSymbol), Is.True);
            Assert.That(candidate.Factory, Is.SameAs(subject.Factory));
            Assert.That(graph.OriginalOperation.SemanticModel!.Compilation, Is.SameAs(subject.Compilation));
            Assert.That(candidate.HasScalarSignature, Is.True);
            var prepared = new RoslynProgramLowerer(subject.Factory).LowerCandidate(graph, candidate);
            Assert.That(candidate.Compilation, Is.SameAs(subject.Compilation));
            Assert.That(prepared.IsExact, Is.EqualTo(!generic));
            Assert.That(prepared.ConstructionLimitExceeded, Is.False);
            Assert.That(prepared.Classification.Abstention,
                Is.EqualTo(generic ? FrontendAbstention.UnsupportedControlFlow : FrontendAbstention.None));
            return prepared.IsExact;
        }
        var session = new TotalSourceCallSession(subject.Compilation, PrepareCallee, null, default);
        Assert.That(session.TryPrepare(subject.Context, invocation.TargetMethod, invocation.Instance,
            invocation.Arguments, out var frame, out var preparedGraph), Is.EqualTo(!generic));
        Assert.That(preparationCalls, Is.EqualTo(1));
        Assert.That(frame == null, Is.EqualTo(generic));
        Assert.That(preparedGraph == null, Is.EqualTo(generic));
        Assert.That(session.ConstructionLimitExceeded, Is.False);
        Assert.That(session.IsActive(invocation.TargetMethod), Is.False);
        if (!generic)
        {
            var program = new RoslynTotalProgramLowerer(subject.Context, default, session).Lower(subject.Graph);
            Assert.That(program.IsExact, Is.True);
            Assert.That(program.ConstructionLimitExceeded, Is.False);
            var replay = subject.Execute(program, []);
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(replay.ConsumedApproximation, Is.False);
            Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(17)));
        }
    }
}
