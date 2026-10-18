using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Ir;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class ContractVariableTransferBoundaryTests
{
    private static readonly ImmutableArray<bool> BooleanValues = [false, true];
    private const string SourceText = """
        using SharpProof.Attributes;
        public static class Frames {
            public static bool Source(bool first, bool second) {
                Contract.Requires(first || second);
                Contract.Ensures(Contract.Result<bool>() == (Contract.Old(first) && Contract.Old(second)));
                return first && second;
            }
            public static bool Destination(bool left, bool right) { return false; }
            public static void Sink() {}
        }
        public static class ResultOnly {
            public static bool Read() {
                Contract.Ensures(Contract.Result<bool>());
                return true;
            }
        }
        public class InstanceFrames {
            public bool Source(bool condition) {
                Contract.Requires(condition);
                return condition;
            }
            public static bool Destination(bool condition) { return condition; }
        }
        public interface ApiTarget { bool Read(bool value); }
        [ContractFor(typeof(ApiTarget))]
        public static class ApiTargetContracts {
            public static bool Read(ApiTarget receiver, bool value) {
                Contract.Requires(value);
                Contract.Ensures(Contract.Result<bool>() && Contract.Old(value));
                return true;
            }
        }
        """;

    [Test]
    public void TransfersTwoBooleanParametersResultAndOldValuesWithTruthPreserved()
    {
        var compilation = CreateCompilation();
        var factory = new IrFactory();
        var source = Bind(compilation, factory, "Frames", "Source");
        var destination = Bind(compilation, factory, "Frames", "Destination");
        var callbackInputs = new List<IrVarId>();
        var callbackOutputs = new List<IrVarId>();

        var substitutions = CreateMap(
            factory,
            source,
            destination,
            ParameterBindings(source),
            callbackInputs,
            callbackOutputs);

        Assert.That(substitutions, Is.Not.Null);
        var completeSubstitutions = substitutions!;
        var sourceParameters = ParameterBindings(source);
        var destinationParameters = Parameters(destination);
        Assert.That(
            sourceParameters.Select(binding => ((binding.Variable.IsDefault) ? throw new System.ArgumentException("Value must be initialized.", nameof(binding)) : (completeSubstitutions[binding.Variable].Variable))),
            Is.EqualTo(destinationParameters.Select(variable => variable is null ? throw new System.ArgumentNullException(nameof(variable)) : (variable.Variable))));
        Assert.That(
            completeSubstitutions[RequiredResultVariable(source)].Variable,
            Is.EqualTo(RequiredResultVariable(destination)));
        var oldPairs = PreStatePairs(source);
        Assert.That(oldPairs, Has.Length.EqualTo(2));
        Assert.That(callbackInputs, Is.EqualTo(destinationParameters.Select(variable => variable is null ? throw new System.ArgumentNullException(nameof(variable)) : (variable.Variable))));
        Assert.That(callbackOutputs, Has.Count.EqualTo(2));
        Assert.That(
            oldPairs.Select(pair => completeSubstitutions[pair.Value].Variable),
            Is.EqualTo(callbackOutputs));

        var rebound = ContractClauseSubstitution.Substitute(
            factory,
            source.Contracts.Clauses,
            completeSubstitutions.ToImmutableDictionary(pair => pair.Key, pair => (IrTerm)pair.Value));
        Assert.That(rebound.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(rebound.Clauses, Has.Length.EqualTo(2));

        var left = destinationParameters[0].Variable;
        var right = destinationParameters[1].Variable;
        var result = RequiredResultVariable(destination);
        var interpreter = new IrInterpreter(factory);
        foreach (var leftValue in BooleanValues)
        {
            foreach (var rightValue in BooleanValues)
            {
                foreach (var resultValue in BooleanValues)
                {
                    var values = global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(left, factory.CreateBooleanValue(leftValue)).SetItem(right, factory.CreateBooleanValue(rightValue)).SetItem(result, factory.CreateBooleanValue(resultValue)).SetItem(callbackOutputs[0], factory.CreateBooleanValue(leftValue)).SetItem(callbackOutputs[1], factory.CreateBooleanValue(rightValue));

                    Assert.That(
                        interpreter.Evaluate(rebound.Clauses[0].Condition, values).Value!.Boolean,
                        Is.EqualTo(leftValue || rightValue));
                    Assert.That(
                        interpreter.Evaluate(rebound.Clauses[1].Condition, values).Value!.Boolean,
                        Is.EqualTo(resultValue == (leftValue && rightValue)));
                }
            }
        }
    }

    [Test]
    public void CompanionParameterOffsetMapsTheActualReceiverAndValueSlots()
    {
        var compilation = CreateCompilation();
        var factory = new IrFactory();
        var companionSource = Bind(compilation, factory, "ApiTargetContracts", "Read");
        var apiTarget = Bind(compilation, factory, "ApiTarget", "Read");
        var callbackInputs = new List<IrVarId>();
        var callbackOutputs = new List<IrVarId>();

        Assert.That(apiTarget.Contracts.UsesCompanion, Is.True);
        Assert.That(
            SymbolEqualityComparer.Default.Equals(apiTarget.Contracts.Source, companionSource.Method),
            Is.True);
        var sourceParameters = ParameterBindings(companionSource);
        var destinationReceiver = RoleVariables(apiTarget, BoundContractVariableRole.Receiver).Single();
        var destinationParameter = Parameters(apiTarget).Single();
        var substitutions = CreateMap(
            factory,
            companionSource,
            apiTarget,
            sourceParameters,
            callbackInputs,
            callbackOutputs);

        Assert.That(substitutions, Is.Not.Null);
        var completeSubstitutions = substitutions!;
        Assert.That(sourceParameters, Has.Length.EqualTo(2));
        Assert.That(
            completeSubstitutions[sourceParameters[0].Variable].Variable,
            Is.EqualTo(destinationReceiver.Variable));
        Assert.That(
            completeSubstitutions[sourceParameters[1].Variable].Variable,
            Is.EqualTo(destinationParameter.Variable));
        Assert.That(
            completeSubstitutions[RequiredResultVariable(companionSource)].Variable,
            Is.EqualTo(RequiredResultVariable(apiTarget)));
        Assert.That(callbackInputs, Has.Count.EqualTo(1));
        Assert.That(callbackInputs.Single(), Is.EqualTo(destinationParameter.Variable));
        Assert.That(callbackOutputs, Has.Count.EqualTo(1));
        Assert.That(
            completeSubstitutions[PreStatePairs(companionSource).Single().Value].Variable,
            Is.EqualTo(callbackOutputs.Single()));
    }

    [Test]
    public void UnusedRealReceiverSlotIsRejectedByTheWholeFrameTransferContract()
    {
        var compilation = CreateCompilation();
        var factory = new IrFactory();
        var source = Bind(compilation, factory, "InstanceFrames", "Source");
        var destination = Bind(compilation, factory, "InstanceFrames", "Destination");
        var sourceReceiver = RoleVariables(source, BoundContractVariableRole.Receiver).Single();
        var sourceParameter = Parameters(source).Single();
        var clauseVariables = IrTermAnalysis.CollectVariables(source.Contracts.Clauses.Single().Condition);
        var callbackInputs = new List<IrVarId>();
        var callbackOutputs = new List<IrVarId>();

        Assert.That(clauseVariables, Does.Contain(sourceParameter.Variable));
        Assert.That(clauseVariables, Does.Not.Contain(sourceReceiver.Variable));
        Assert.That(RoleVariables(destination, BoundContractVariableRole.Receiver), Is.Empty);
        var substitutions = CreateMap(
            factory,
            source,
            destination,
            ParameterBindings(source),
            callbackInputs,
            callbackOutputs);

        Assert.That(substitutions, Is.Null);
        Assert.That(callbackInputs, Is.Empty);
        Assert.That(callbackOutputs, Is.Empty);
    }

    [Test]
    public void ResultSlotCannotTransferIntoARealVoidDestinationFrame()
    {
        var compilation = CreateCompilation();
        var factory = new IrFactory();
        var source = Bind(compilation, factory, "ResultOnly", "Read");
        var destination = Bind(compilation, factory, "Frames", "Sink");
        var callbackInputs = new List<IrVarId>();
        var callbackOutputs = new List<IrVarId>();

        Assert.That(ResultVariable(source).HasValue, Is.True);
        Assert.That(ResultVariable(destination).HasValue, Is.False);
        var substitutions = CreateMap(
            factory,
            source,
            destination,
            ParameterBindings(source),
            callbackInputs,
            callbackOutputs);

        Assert.That(substitutions, Is.Null);
        Assert.That(callbackInputs, Is.Empty);
        Assert.That(callbackOutputs, Is.Empty);
    }

    [Test]
    public void MissingCurrentParameterRejectsAfterPreservingEarlierPreStateCallbackEffects()
    {
        var compilation = CreateCompilation();
        var factory = new IrFactory();
        var source = Bind(compilation, factory, "Frames", "Source");
        var destination = Bind(compilation, factory, "Frames", "Destination");
        var sourceBindings = ParameterBindings(source);
        var oldPairs = PreStatePairs(source);
        Assert.That(oldPairs, Has.Length.EqualTo(2));
        var missingCurrent = oldPairs[^1].Key;
        var incompleteBindings = sourceBindings
            .Where(binding => binding.Variable != missingCurrent)
            .ToImmutableArray();
        Assert.That(incompleteBindings, Has.Length.EqualTo(1));
        var callbackInputs = new List<IrVarId>();
        var callbackOutputs = new List<IrVarId>();

        var substitutions = ContractVariableTransfer.CreateSubstitutions(
            factory,
            source.Contracts.Source,
            destination.Contracts.UsesCompanion,
            incompleteBindings,
            ResultVariable(source),
            RoleVariables(source, BoundContractVariableRole.Receiver)
                .Select(variable => variable is null ? throw new System.ArgumentNullException(nameof(variable)) : (variable.Variable))
                .ToImmutableArray(),
            oldPairs,
            destination.Contracts.Variables,
            current => current.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(current)) : (CreatePreState(factory, current, callbackInputs, callbackOutputs)));

        Assert.That(substitutions, Is.Null);
        Assert.That(callbackInputs, Has.Count.EqualTo(1));
        Assert.That(callbackOutputs, Has.Count.EqualTo(1));
        var firstPairParameter = sourceBindings.Single(binding => binding.Variable == oldPairs[0].Key);
        var firstPairOrdinal = ((IParameterSymbol)firstPairParameter.Symbol).Ordinal;
        var expectedDestinationCurrent = Parameters(destination)
            .Single(variable => variable is null ? throw new System.ArgumentNullException(nameof(variable)) : (variable.Ordinal == firstPairOrdinal))
            .Variable;
        Assert.That(callbackInputs.Single(), Is.EqualTo(expectedDestinationCurrent));
        Assert.That(
            factory.GetVariableInfo(callbackOutputs.Single()).Type,
            Is.EqualTo(factory.BooleanType));
    }

    private static ImmutableDictionary<IrVarId, IrVariableTerm>? CreateMap(
        IrFactory factory,
        BoundFrame source,
        BoundFrame destination,
        ImmutableArray<FrontendVariableBinding> sourceParameters,
        List<IrVarId> callbackInputs,
        List<IrVarId> callbackOutputs)
    {

        System.ArgumentNullException.ThrowIfNull(callbackInputs);
        System.ArgumentNullException.ThrowIfNull(callbackOutputs);
        System.ArgumentNullException.ThrowIfNull(destination);
        System.ArgumentNullException.ThrowIfNull(factory);
        System.ArgumentNullException.ThrowIfNull(source);
        if (sourceParameters.IsDefault)
        {
            throw new System.ArgumentException("Array must be initialized.", nameof(sourceParameters));
        }
        var map = ContractVariableTransfer.CreateSubstitutions(
            factory,
            source.Contracts.Source,
            destination.Contracts.UsesCompanion,
            sourceParameters,
            ResultVariable(source),
            RoleVariables(source, BoundContractVariableRole.Receiver)
                .Select(variable => variable is null ? throw new System.ArgumentNullException(nameof(variable)) : (variable.Variable))
                .ToImmutableArray(),
            PreStatePairs(source),
            destination.Contracts.Variables,
            current => current.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(current)) : (CreatePreState(factory, current, callbackInputs, callbackOutputs)));
        if (map == null)
        {
            return null;
        }
        Assert.That(map.Values, Is.All.InstanceOf<IrVariableTerm>());
        return map.ToImmutableDictionary(pair => pair.Key, pair => (IrVariableTerm)pair.Value);
    }

    private static IrVarId CreatePreState(
        IrFactory factory,
        IrVarId current,
        List<IrVarId> callbackInputs,
        List<IrVarId> callbackOutputs)
    {
        if (current.IsDefault)
        {
            throw new System.ArgumentOutOfRangeException(nameof(current));
        }
        System.ArgumentNullException.ThrowIfNull(callbackInputs);
        System.ArgumentNullException.ThrowIfNull(callbackOutputs);
        System.ArgumentNullException.ThrowIfNull(factory);
        callbackInputs.Add(current);
        var type = factory.GetVariableInfo(current).Type;
        var preState = factory.CreateVariable(
            "held-prestate:" + callbackOutputs.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            type);
        callbackOutputs.Add(preState);
        return preState;
    }

    private static ImmutableArray<KeyValuePair<IrVarId, IrVarId>> PreStatePairs(BoundFrame frame)
    {
        System.ArgumentNullException.ThrowIfNull(frame);
        return [.. frame.Contracts.Variables
            .Where(variable => variable is null ? throw new System.ArgumentNullException(nameof(variable)) : (variable.Role == BoundContractVariableRole.PreState))
            .Select(variable => new KeyValuePair<IrVarId, IrVarId>(
                variable.CurrentStateVariable ?? throw new AssertionException(
                    "A public pre-state slot must name its current slot."),
                variable.Variable))];
    }

    private static ImmutableArray<BoundContractVariable> Parameters(BoundFrame frame)
    {
        System.ArgumentNullException.ThrowIfNull(frame);
        return [.. frame.Contracts.Variables
            .Where(variable => variable is null ? throw new System.ArgumentNullException(nameof(variable)) : (variable.Role == BoundContractVariableRole.Parameter))
            .OrderBy(variable => variable is null ? throw new System.ArgumentNullException(nameof(variable)) : (variable.Ordinal))];
    }

    private static ImmutableArray<BoundContractVariable> RoleVariables(
        BoundFrame frame,
        BoundContractVariableRole role)
    {
        System.ArgumentNullException.ThrowIfNull(frame);
        return [.. frame.Contracts.Variables.Where(variable => variable is null ? throw new System.ArgumentNullException(nameof(variable)) : (variable.Role == role))];
    }

    private static ImmutableArray<FrontendVariableBinding> ParameterBindings(BoundFrame frame)
    {
        System.ArgumentNullException.ThrowIfNull(frame);
        return [.. frame.Method.Parameters
            .OrderBy(parameter => parameter is null ? throw new System.ArgumentNullException(nameof(parameter)) : (parameter.Ordinal))
            .Select(parameter =>
            {
                System.ArgumentNullException.ThrowIfNull(parameter);
                var modelSlot = Parameters(frame).Single(variable => variable is null ? throw new System.ArgumentNullException(nameof(variable)) : (variable.Ordinal == parameter.Ordinal));
                Assert.That(
                    SymbolEqualityComparer.Default.Equals(modelSlot.Symbol, parameter),
                    Is.True);
                return new FrontendVariableBinding(parameter, modelSlot.Variable);
            })];
    }

    private static IrVarId RequiredResultVariable(BoundFrame frame)
    {
        return ResultVariable(frame) ?? throw new AssertionException("The actual public frame must contain a result slot.");
    }

    private static IrVarId? ResultVariable(BoundFrame frame)
    {
        System.ArgumentNullException.ThrowIfNull(frame);
        var results = RoleVariables(frame, BoundContractVariableRole.Result);
        Assert.That(results.Length, Is.LessThanOrEqualTo(1));
        return results.Length == 0 ? null : results[0].Variable;
    }

    private static BoundFrame Bind(
        CSharpCompilation compilation,
        IrFactory factory,
        string typeName,
        string methodName)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        System.ArgumentNullException.ThrowIfNull(factory);
        System.ArgumentNullException.ThrowIfNull(methodName);
        System.ArgumentNullException.ThrowIfNull(typeName);
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
        var compilation = TestCompilation.Create("ContractVariableTransferBoundary", SourceText);
        using var image = new MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True);
        return compilation;
    }

    private sealed record BoundFrame(IMethodSymbol Method, BoundMethodContracts Contracts)
    {
        public IMethodSymbol Method { get; init; } = Method ?? throw new System.ArgumentNullException(nameof(Method));
        public BoundMethodContracts Contracts { get; init; } = Contracts ?? throw new System.ArgumentNullException(nameof(Contracts));
    }
}
