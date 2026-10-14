using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Ir;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class ContractVariableTransferAdditionalFrameMatrixTests
{
    private const string SourceText = """
        using SharpProof.Attributes;
        public static class EmptyFrames {
            public static void Source() {}
            public static void Destination() {}
        }
        public static class ParameterFrames {
            public static bool WideSource(bool first, bool second, bool third) {
                Contract.Requires(first || second || third);
                return first;
            }
            public static bool NarrowDestination(bool left, bool right) { return left; }
        }
        public class InstanceFrames {
            public bool Source(bool condition) {
                Contract.Requires(condition);
                return condition;
            }
            public bool Destination(bool condition) { return condition; }
        }
        """;

    [Test]
    public void WholeInstanceFramesTransferAnUnusedReceiverSlot()
    {
        var compilation = CreateCompilation();
        var factory = new IrFactory();
        var source = Bind(compilation, factory, "InstanceFrames", "Source");
        var destination = Bind(compilation, factory, "InstanceFrames", "Destination");
        var sourceReceiver = RoleVariables(source, BoundContractVariableRole.Receiver).Single();
        var destinationReceiver = RoleVariables(destination, BoundContractVariableRole.Receiver).Single();
        var sourceParameter = Parameters(source).Single();
        var destinationParameter = Parameters(destination).Single();
        var sourceClauseVariables = IrTermAnalysis.CollectVariables(source.Contracts.Clauses.Single().Condition);

        Assert.That(sourceClauseVariables, Does.Contain(sourceParameter.Variable));
        Assert.That(sourceClauseVariables, Does.Not.Contain(sourceReceiver.Variable));
        var substitutions = Transfer(factory, source, destination, ParameterBindings(source));

        Assert.That(substitutions, Is.Not.Null);
        var actualSubstitutions = substitutions ?? throw new AssertionException("Expected a successful actual instance frame transfer.");
        Assert.That(
            VariableTarget(actualSubstitutions, sourceReceiver.Variable),
            Is.EqualTo(destinationReceiver.Variable));
        Assert.That(
            VariableTarget(actualSubstitutions, sourceParameter.Variable),
            Is.EqualTo(destinationParameter.Variable));
        Assert.That(
            VariableTarget(actualSubstitutions, RequiredResultVariable(source)),
            Is.EqualTo(RequiredResultVariable(destination)));
    }

    [Test]
    public void ActualSourceFrameWithMoreParametersThanDestinationFailsAtTheMissingOrdinal()
    {
        var compilation = CreateCompilation();
        var factory = new IrFactory();
        var source = Bind(compilation, factory, "ParameterFrames", "WideSource");
        var destination = Bind(compilation, factory, "ParameterFrames", "NarrowDestination");

        Assert.That(Parameters(source).Length, Is.EqualTo(3));
        Assert.That(Parameters(destination).Length, Is.EqualTo(2));
        var substitutions = Transfer(factory, source, destination, ParameterBindings(source));

        Assert.That(substitutions, Is.Null);
    }

    [Test]
    public void EmptyActualVoidFramesProduceAnEmptySuccessfulMap()
    {
        var compilation = CreateCompilation();
        var factory = new IrFactory();
        var source = Bind(compilation, factory, "EmptyFrames", "Source");
        var destination = Bind(compilation, factory, "EmptyFrames", "Destination");

        Assert.That(source.Contracts.Variables, Is.Empty);
        Assert.That(destination.Contracts.Variables, Is.Empty);
        Assert.That(source.Contracts.Clauses, Is.Empty);
        var substitutions = Transfer(factory, source, destination, ParameterBindings(source));

        Assert.That(substitutions, Is.Not.Null);
        var actualSubstitutions = substitutions ?? throw new AssertionException("Expected a successful actual instance frame transfer.");
        Assert.That(substitutions, Is.Empty);
    }

    private static ImmutableDictionary<IrVarId, IrTerm>? Transfer(
        IrFactory factory,
        BoundFrame source,
        BoundFrame destination,
        ImmutableArray<FrontendVariableBinding> sourceParameters)
    {
        return ContractVariableTransfer.CreateSubstitutions(
            factory,
            source.Contracts.Source,
            destination.Contracts.UsesCompanion,
            sourceParameters,
            OptionalResultVariable(source),
            RoleVariables(source, BoundContractVariableRole.Receiver)
                .Select(variable => variable.Variable)
                .ToImmutableArray(),
            [],
            destination.Contracts.Variables,
            current => CreatePreState(factory, current));
    }

    private static IrVarId CreatePreState(IrFactory factory, IrVarId current)
    {
        var type = factory.GetVariableInfo(current).Type;
        return factory.CreateVariable("additional-matrix-prestate", type);
    }

    private static ImmutableArray<FrontendVariableBinding> ParameterBindings(BoundFrame frame)
    {
        var modelParameters = Parameters(frame);
        return [.. frame.Method.Parameters
            .OrderBy(parameter => parameter.Ordinal)
            .Select(parameter =>
            {
                var modelSlot = modelParameters.Single(variable => variable.Ordinal == parameter.Ordinal);
                Assert.That(
                    SymbolEqualityComparer.Default.Equals(modelSlot.Symbol, parameter),
                    Is.True);
                return new FrontendVariableBinding(parameter, modelSlot.Variable);
            })];
    }

    private static ImmutableArray<BoundContractVariable> Parameters(BoundFrame frame)
    {
        return [.. frame.Contracts.Variables
            .Where(variable => variable.Role == BoundContractVariableRole.Parameter)
            .OrderBy(variable => variable.Ordinal)];
    }

    private static ImmutableArray<BoundContractVariable> RoleVariables(
        BoundFrame frame,
        BoundContractVariableRole role)
    {
        return [.. frame.Contracts.Variables.Where(variable => variable.Role == role)];
    }

    private static IrVarId? OptionalResultVariable(BoundFrame frame)
    {
        var results = RoleVariables(frame, BoundContractVariableRole.Result);
        Assert.That(results.Length, Is.LessThanOrEqualTo(1));
        return results.Length == 0 ? null : results[0].Variable;
    }

    private static IrVarId RequiredResultVariable(BoundFrame frame)
    {
        return OptionalResultVariable(frame) ?? throw new AssertionException(
            "The actual compiler-bound method frame must contain a result slot.");
    }

    private static IrVarId VariableTarget(
        ImmutableDictionary<IrVarId, IrTerm> substitutions,
        IrVarId source)
    {
        Assert.That(substitutions.TryGetValue(source, out var replacement), Is.True);
        Assert.That(replacement, Is.TypeOf<IrVariableTerm>());
        return ((IrVariableTerm)replacement!).Variable;
    }

    private static BoundFrame Bind(
        CSharpCompilation compilation,
        IrFactory factory,
        string typeName,
        string methodName)
    {
        var method = compilation.GetTypeByMetadataName(typeName)!
            .GetMembers(methodName)
            .OfType<IMethodSymbol>()
            .Single();
        Assert.That(
            SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, compilation.Assembly),
            Is.True);
        var result = new ContractBinder(compilation, factory).Bind(method);
        Assert.That(result.IsSuccess, Is.True, result.Failure.ToString());
        Assert.That(result.Contracts, Is.Not.Null);
        Assert.That(SymbolEqualityComparer.Default.Equals(result.Contracts!.Target, method), Is.True);
        return new BoundFrame(method, result.Contracts!);
    }

    private static CSharpCompilation CreateCompilation()
    {
        var compilation = TestCompilation.Create("ContractVariableTransferAdditionalFrameMatrix", SourceText);
        using var image = new MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True);
        return compilation;
    }

    private sealed record BoundFrame(IMethodSymbol Method, BoundMethodContracts Contracts);
}
