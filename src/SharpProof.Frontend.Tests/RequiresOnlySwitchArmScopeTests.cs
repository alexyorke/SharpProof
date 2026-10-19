using System.Collections.Immutable;
using System.Numerics;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Contracts;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class RequiresOnlySwitchArmScopeTests
{
    private static readonly ImmutableArray<int> expectedClrReturns = [7, 7];

    [TestCase(true)]
    [TestCase(false)]
    public void RequiresOnlySwitchArmCrossingAbstainsWithinAnActualTryRegion(bool hasSwitch)
    {
        Observe(hasSwitch);
    }

    private static void Observe(bool hasSwitch)
    {
        var predicate = hasSwitch
            ? "value switch { int matched => matched > 0, _ => true }"
            : "value != null";
        var source = "using System; using SharpProof.Attributes; public static class Subject { " +
            "public static int Target(object value) { Contract.Requires(value != null); " +
            "Contract.Ensures(" + predicate + "); try { return 7; } catch (Exception) { return 9; } } }";
        using var subject = Subject.Create(source, hasSwitch);
        Assert.That(((CSharpParseOptions)subject.Syntax.SyntaxTree.Options).PreprocessorSymbolNames,
            Does.Not.Contain(SharpProof.Attributes.Contract.ConditionalSymbol));
        var model = subject.Compilation.GetSemanticModel(subject.Syntax.SyntaxTree);
        var calls = subject.Syntax.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Select(syntax => syntax is null ? throw new System.ArgumentNullException(nameof(syntax)) : ((IInvocationOperation)model.GetOperation(syntax)!)).ToImmutableArray();
        Assert.That(calls, Has.Length.EqualTo(2));
        var requiresCall = calls.Single(call => call is null ? throw new System.ArgumentNullException(nameof(call)) : (call.TargetMethod.Name == "Requires"));
        var ensuresCall = calls.Single(call => call is null ? throw new System.ArgumentNullException(nameof(call)) : (call.TargetMethod.Name == "Ensures"));
        foreach (var call in calls)
        {
            Assert.That(call.TargetMethod.ContainingType.ToDisplayString(),
                Is.EqualTo("SharpProof.Attributes.Contract"));
            var conditional = call.TargetMethod.GetAttributes().Single(attribute =>
                attribute is null ? throw new System.ArgumentNullException(nameof(attribute)) : (attribute.AttributeClass!.ToDisplayString() == "System.Diagnostics.ConditionalAttribute"));
            Assert.That(conditional.ConstructorArguments.Single().Value,
                Is.EqualTo(SharpProof.Attributes.Contract.ConditionalSymbol));
        }

        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, subject.Target);
        Assert.That(context.HasScalarSignature, Is.True);
        Assert.That(context.OwnsBody(subject.Graph.OriginalOperation), Is.True);
        var binding = new ContractBinder(subject.Compilation, factory).BindTotalRequires(context);
        Assert.That(binding.IsSuccess, Is.True, binding.Failure.ToString());
        Assert.That(binding.Clauses, Has.Length.EqualTo(1));
        Assert.That(binding.Clauses.Single().Kind, Is.EqualTo(BoundContractKind.Requires));
        Assert.That(context.IsExcludedSpecificationCall(requiresCall), Is.True);
        Assert.That(context.IsExcludedSpecificationCall(ensuresCall), Is.True);
        Assert.That(context.IsSpecificationOperation(ensuresCall.Arguments.Single().Value), Is.True);

        var completeFactory = new IrFactory(IrExecutionSemantics.Total);
        var completeContext = new TotalLoweringContext(completeFactory, subject.Target);
        var completeBinding = new ContractBinder(subject.Compilation, completeFactory).BindTotal(completeContext);
        Assert.That(completeBinding.Failure, Is.EqualTo(hasSwitch
            ? ContractBindingFailure.UnsupportedExpression
            : ContractBindingFailure.None));
        Assert.That(completeBinding.IsSuccess, Is.EqualTo(!hasSwitch));
        Assert.That(Regions(subject.Graph.Root).Any(region => region is null ? throw new System.ArgumentNullException(nameof(region)) : (region.Kind == ControlFlowRegionKind.Try)), Is.True);
        Assert.That(Regions(subject.Graph.Root).Any(region => region is null ? throw new System.ArgumentNullException(nameof(region)) : (region.Kind == ControlFlowRegionKind.Catch)), Is.True);

        object? witness = null;
        if (hasSwitch)
        {
            Assert.That(ensuresCall.Arguments.Single().Value, Is.InstanceOf<ISwitchExpressionOperation>());
            witness = ObserveActualArmScope(subject, model, ensuresCall, context);
        }

        ImmutableArray<object> inputs = [7, "ordinary input"];
        foreach (var input in inputs)
        {
            Assert.That(subject.Invoke(input), Is.EqualTo(7));
        }
        var lowered = new RoslynProgramLowerer(factory).LowerCandidate(subject.Graph, context);
        Assert.That(lowered.ConstructionLimitExceeded, Is.False);
        Assert.That(lowered.IsExact, Is.EqualTo(!hasSwitch), lowered.Classification.Abstention.ToString());
        if (hasSwitch)
        {
            Assert.That(lowered.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedControlFlow));
            Assert.That(lowered.Abstentions, Is.Not.Empty);
        }
        else
        {
            foreach (var input in inputs)
            {
                var parameter = context.Parameters.Single();
                var type = factory.GetVariableInfo(parameter.Entry).Type;
                var execution = new IrProgramInterpreter(factory).Execute(lowered.Program,
                    global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(parameter.Entry, factory.CreateReferenceValue(type, input)));
                Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
                Assert.That(execution.ConsumedApproximation, Is.False);
                Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(7)));
            }
        }

        WriteObservation(new
        {
            Scenario = hasSwitch ? "try-switch-arm-scope" : "try-simple-predicate-control",
            SourceSha256HexUpper = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))),
            CompilerErrors = 0,
            EmitSucceeded = true,
            ClrReturns = expectedClrReturns,
            RequiresOnlyFailure = binding.Failure.ToString(),
            FullBindingFailure = completeBinding.Failure.ToString(),
            ExcludedRequires = context.IsExcludedSpecificationCall(requiresCall),
            ExcludedEnsures = context.IsExcludedSpecificationCall(ensuresCall),
            EnsuresSpan = new { ensuresCall.Syntax.Span.Start, ensuresCall.Syntax.Span.Length },
            ScopeWitness = witness,
            LoweringIsExact = lowered.IsExact,
            LoweringAbstention = lowered.Classification.Abstention.ToString(),
            lowered.ConstructionLimitExceeded
        });
    }

    private static object ObserveActualArmScope(Subject subject, SemanticModel model,
        IInvocationOperation ensuresCall, TotalLoweringContext context)
    {
        System.ArgumentNullException.ThrowIfNull(context);
        System.ArgumentNullException.ThrowIfNull(ensuresCall);
        System.ArgumentNullException.ThrowIfNull(model);
        System.ArgumentNullException.ThrowIfNull(subject);
        var declaration = subject.Syntax.DescendantNodes().OfType<DeclarationPatternSyntax>().Single();
        var designation = (SingleVariableDesignationSyntax)declaration.Designation;
        var local = (ILocalSymbol)model.GetDeclaredSymbol(designation)!;
        Assert.That(local.Name, Is.EqualTo("matched"));
        Assert.That(SymbolEqualityComparer.Default.Equals(local.ContainingSymbol, subject.Target), Is.True);
        Assert.That(local.DeclaringSyntaxReferences.Single().SyntaxTree, Is.SameAs(subject.Syntax.SyntaxTree));
        var owner = Regions(subject.Graph.Root).Single(region =>
            region is null ? throw new System.ArgumentNullException(nameof(region)) : (region.Kind == ControlFlowRegionKind.LocalLifetime &&
            region.Locals.Any(candidate => candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (SymbolEqualityComparer.Default.Equals(candidate, local)))));
        var condition = subject.Graph.Blocks.Single(block => block.IsReachable &&
            block.ConditionKind != ControlFlowConditionKind.None && block.BranchValue != null &&
            block.BranchValue.DescendantsAndSelf().OfType<IIsPatternOperation>().Any(pattern =>
                pattern is null ? throw new System.ArgumentNullException(nameof(pattern)) : (pattern.Pattern.Syntax.SyntaxTree == declaration.SyntaxTree &&
                pattern.Pattern.Syntax.Span == declaration.Span)));
        Assert.That(Regions(owner).Contains(condition.EnclosingRegion), Is.True);
        var conditionalValue = condition.BranchValue!;
        Assert.That(ensuresCall.Syntax.Span.Contains(conditionalValue.Syntax.Span), Is.True);
        Assert.That(context.IsSpecificationOperation(conditionalValue), Is.True);
        var edges = ImmutableArray.Create(condition.FallThroughSuccessor, condition.ConditionalSuccessor);
        Assert.That(edges.All(edge => edge is { Destination: not null, Semantics: ControlFlowBranchSemantics.Regular } &&
            edge.FinallyRegions.IsEmpty), Is.True);
        var crossings = edges.Where(edge => edge is null ? throw new System.ArgumentNullException(nameof(edge)) : (edge!.Destination!.IsReachable &&
            !ReferenceEquals(condition.EnclosingRegion, edge.Destination.EnclosingRegion))).ToImmutableArray();
        Assert.That(crossings, Is.Not.Empty);
        return new
        {
            LocalName = local.Name,
            LocalDeclarationSpan = new { designation.Span.Start, designation.Span.Length },
            OwningLocalLifetime = DescribeRegion(owner),
            ConditionalBlock = condition.Ordinal,
            ConditionalRegion = DescribeRegion(condition.EnclosingRegion),
            Crossings = crossings.Select(edge => edge is null ? throw new System.ArgumentNullException(nameof(edge)) : (new
            {
                DestinationBlock = edge!.Destination!.Ordinal,
                DestinationRegion = DescribeRegion(edge.Destination.EnclosingRegion)
            })).ToImmutableArray()
        };
    }

    private static IEnumerable<ControlFlowRegion> Regions(ControlFlowRegion region)
    {
        System.ArgumentNullException.ThrowIfNull(region);
        yield return region;
        foreach (var nested in region.NestedRegions)
        {
            foreach (var descendant in Regions(nested))
            {
                yield return descendant;
            }
        }
    }

    private static object DescribeRegion(ControlFlowRegion region)
    {
        System.ArgumentNullException.ThrowIfNull(region);
        return new
        {
            Kind = region.Kind.ToString(),
            region.FirstBlockOrdinal,
            region.LastBlockOrdinal,
            Locals = region.Locals.Select(local => local is null ? throw new System.ArgumentNullException(nameof(local)) : (local.Name)).ToImmutableArray()
        };
    }

    private static void WriteObservation(object observation)
    {
        System.ArgumentNullException.ThrowIfNull(observation);
        TestContext.Out.WriteLine("SP_REQUIRES_SWITCH_SCOPE_V1 " + JsonSerializer.Serialize(observation));
    }

    private sealed class Subject(CSharpCompilation compilation, MethodDeclarationSyntax syntax,
        IMethodSymbol target, ControlFlowGraph graph, AssemblyLoadContext runtime, MethodInfo entry) : IDisposable
    {
        private readonly AssemblyLoadContext _runtime = runtime ?? throw new System.ArgumentNullException(nameof(runtime));
        private readonly MethodInfo _entry = entry ?? throw new System.ArgumentNullException(nameof(entry));
        internal CSharpCompilation Compilation { get; } = compilation ?? throw new System.ArgumentNullException(nameof(compilation));
        internal MethodDeclarationSyntax Syntax { get; } = syntax ?? throw new System.ArgumentNullException(nameof(syntax));
        internal IMethodSymbol Target { get; } = target ?? throw new System.ArgumentNullException(nameof(target));
        internal ControlFlowGraph Graph { get; } = graph ?? throw new System.ArgumentNullException(nameof(graph));

        internal static Subject Create(string source, bool hasSwitch)
        {
            System.ArgumentNullException.ThrowIfNull(source);
            var tree = CSharpSyntaxTree.ParseText(source,
                new CSharpParseOptions(LanguageVersion.CSharp12), "requires-switch.cs");
            var compilation = CSharpCompilation.Create(
                hasSwitch ? "RequiresSwitchScope" : "RequiresSimpleScope", [tree],
                TestMetadataReferences.WithSharpProof,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release));
            Assert.That(compilation.GetDiagnostics().Where(diagnostic =>
                diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
            var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
            var model = compilation.GetSemanticModel(tree);
            var target = (IMethodSymbol)model.GetDeclaredSymbol(syntax)!;
            var graph = ControlFlowGraph.Create(syntax, model)!;
            Assert.That(graph, Is.Not.Null);
            using var image = new MemoryStream();
            var emitted = compilation.Emit(image);
            Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
            var runtime = new AssemblyLoadContext("RequiresSwitchScope", isCollectible: true);
            image.Position = 0;
            var entry = runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Target")!;
            return new Subject(compilation, syntax, target, graph, runtime, entry);
        }

        internal object? Invoke(object value)
        {
            System.ArgumentNullException.ThrowIfNull(value);
            return _entry.Invoke(null, [value]);
        }

        public void Dispose()
        {
            _runtime.Unload();
        }
    }
}
