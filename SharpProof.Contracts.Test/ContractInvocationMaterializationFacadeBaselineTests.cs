using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class ContractInvocationMaterializationFacadeBaselineTests
{
    private static readonly ImmutableArray<bool> BooleanValues = [false, true];
    private static readonly ImmutableArray<BoundContractKind> FullKinds =
    [
        BoundContractKind.Requires,
        BoundContractKind.Assume,
        BoundContractKind.Ensures
    ];
    private static readonly ImmutableArray<BoundContractKind> RequiresOnlyKinds = [BoundContractKind.Requires];
    private const string ContractSource = """
        using SharpProof.Attributes;
        public static class ContractFrames {
            public static bool Evaluate(bool input) {
                Contract.Requires(input);
                Contract.Assume(!input || input);
                Contract.Ensures(Contract.Result<bool>() == Contract.Old(input));
                return input;
            }
        }
        """;

    [Test]
    public void PublicFullAndRequiresOnlyBindingsKeepKindsOrderAndTruth()
    {
        var compilation = CreateCompilation("InvocationMaterializationPublicBaseline", ContractSource);
        var method = GetMethod(compilation, "ContractFrames", "Evaluate");
        var factory = new IrFactory();
        var binder = new ContractBinder(compilation, factory);

        var full = binder.Bind(method);
        Assert.That(full.IsSuccess, Is.True, full.Failure.ToString());
        var fullContracts = full.Contracts!;
        Assert.That(
            fullContracts.Clauses.Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind)),
            Is.EqualTo(FullKinds));
        Assert.That(
            fullContracts.Clauses.All(clause =>
                clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Evidence == BoundContractEvidence.CompilerBoundInvocation)),
            Is.True);
        Assert.That(fullContracts.Clauses.Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.SourceSyntax)),
            Has.All.Not.Null);

        var parameter = fullContracts.Variables.Single(
            variable => variable is null ? throw new System.ArgumentNullException(nameof(variable)) : (variable.Role == BoundContractVariableRole.Parameter)).Variable;
        var result = fullContracts.Variables.Single(
            variable => variable is null ? throw new System.ArgumentNullException(nameof(variable)) : (variable.Role == BoundContractVariableRole.Result)).Variable;
        var preState = fullContracts.Variables.Single(
            variable => variable is null ? throw new System.ArgumentNullException(nameof(variable)) : (variable.Role == BoundContractVariableRole.PreState));
        Assert.That(preState.CurrentStateVariable, Is.EqualTo(parameter));

        var interpreter = new IrInterpreter(factory);
        foreach (var input in BooleanValues)
        {
            foreach (var oldInput in BooleanValues)
            {
                foreach (var resultValue in BooleanValues)
                {
                    var values = new Dictionary<IrVarId, IrValue>
                    {
                        [parameter] = factory.CreateBooleanValue(input),
                        [preState.Variable] = factory.CreateBooleanValue(oldInput),
                        [result] = factory.CreateBooleanValue(resultValue)
                    };
                    Assert.That(
                        interpreter.Evaluate(fullContracts.Clauses[0].Condition, values).Value!.Boolean,
                        Is.EqualTo(input));
                    Assert.That(
                        interpreter.Evaluate(fullContracts.Clauses[1].Condition, values).Value!.Boolean,
                        Is.True);
                    Assert.That(
                        interpreter.Evaluate(fullContracts.Clauses[2].Condition, values).Value!.Boolean,
                        Is.EqualTo(resultValue == oldInput));
                }
            }
        }

        var requiresOnly = binder.BindRequires(method);
        Assert.That(requiresOnly.IsSuccess, Is.True, requiresOnly.Failure.ToString());
        var requiresContracts = requiresOnly.Contracts!;
        Assert.That(
            requiresContracts.Clauses.Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind)),
            Is.EqualTo(RequiresOnlyKinds));
        Assert.That(
            requiresContracts.Variables.Any(variable =>
                variable is null ? throw new System.ArgumentNullException(nameof(variable)) : (variable.Role is BoundContractVariableRole.Result or BoundContractVariableRole.PreState)),
            Is.False);
        var requiresParameter = requiresContracts.Variables.Single(
            variable => variable is null ? throw new System.ArgumentNullException(nameof(variable)) : (variable.Role == BoundContractVariableRole.Parameter)).Variable;
        foreach (var input in BooleanValues)
        {
            var values = new Dictionary<IrVarId, IrValue>
            {
                [requiresParameter] = factory.CreateBooleanValue(input)
            };
            Assert.That(
                interpreter.Evaluate(requiresContracts.Clauses.Single().Condition, values).Value!.Boolean,
                Is.EqualTo(input));
        }
    }

    [Test]
    public void PublicBinderIgnoresOrdinaryCallsAsClauseCandidates()
    {
        var compilation = CreateCompilation(
            "InvocationMaterializationOrdinaryCallControl",
            """
            public static class OrdinaryCalls {
                public static void Zero() {}
                public static void Two(bool first, bool second) {}
                public static int One(int value) => value;
            }
            public static class Caller {
                public static bool Read(bool input) {
                    OrdinaryCalls.Zero();
                    OrdinaryCalls.Two(input, !input);
                    _ = OrdinaryCalls.One(input ? 1 : 0);
                    return input;
                }
            }
            """);
        var method = GetMethod(compilation, "Caller", "Read");
        var result = new ContractBinder(compilation, new IrFactory()).Bind(method);

        Assert.That(result.IsSuccess, Is.True, result.Failure.ToString());
        Assert.That(result.Contracts!.Clauses, Is.Empty);
    }

    private static CSharpCompilation CreateCompilation(string assemblyName, string source)
    {
        System.ArgumentNullException.ThrowIfNull(assemblyName);
        System.ArgumentNullException.ThrowIfNull(source);
        var compilation = TestCompilation.Create(assemblyName, source);
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        return compilation;
    }

    private static IMethodSymbol GetMethod(
        CSharpCompilation compilation,
        string typeName,
        string methodName)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        System.ArgumentNullException.ThrowIfNull(methodName);
        System.ArgumentNullException.ThrowIfNull(typeName);
        var method = compilation.GetTypeByMetadataName(typeName)!
            .GetMembers(methodName)
            .OfType<IMethodSymbol>()
            .Single();
        Assert.That(
            SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, compilation.Assembly),
            Is.True);
        return method;
    }
}
