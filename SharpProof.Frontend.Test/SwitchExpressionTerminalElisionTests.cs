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
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class SwitchExpressionTerminalElisionTests
{
    private static readonly ImmutableArray<string> EnabledSymbols = ["ABSENT"];
    private static readonly ImmutableArray<(int Value, int Other)> Inputs = [(0, 0), (0, 1), (1, 0), (2, 1)];

    [TestCase("exhaustive", false, TestName = "SwitchTerminal_Exhaustive_Omitted")]
    [TestCase("exhaustive", true, TestName = "SwitchTerminal_Exhaustive_Enabled")]
    [TestCase("guarded", false, TestName = "SwitchTerminal_Guarded_Omitted")]
    [TestCase("guarded", true, TestName = "SwitchTerminal_Guarded_Enabled")]
    [TestCase("nested", false, TestName = "SwitchTerminal_Nested_Omitted")]
    [TestCase("nested", true, TestName = "SwitchTerminal_Nested_Enabled")]
    [TestCase("local", false, TestName = "SwitchTerminal_Local_Omitted")]
    [TestCase("local", true, TestName = "SwitchTerminal_Local_Enabled")]
    public void SwitchArgumentsPreserveOmissionAndEnabledFaults(string shape, bool enabled)
    {
        System.ArgumentNullException.ThrowIfNull(shape);
        var expression = Expression(shape);
        var body = "int result = 11; Log(" + expression + "); result += 6; return result;";
        var targetSource = shape == "local"
            ? "public static int Target(int value, int other) { static int Local(int value, int other) { " +
                body + " } return Local(value, other); }"
            : "public static int Target(int value, int other) { " + body + " }";
        var source = "public static class Subject { " + targetSource +
            " [System.Diagnostics.Conditional(\"ABSENT\")] private static void Log(bool argument) { } }";
        var sourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
        Assert.That(sourceHash, Is.EqualTo(SourceHash(shape)).IgnoreCase);
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12, preprocessorSymbols: enabled ? EnabledSymbols : []),
            "switch-terminal-elision.cs");
        var compilation = CSharpCompilation.Create("SwitchTerminalElision", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));

        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method.Identifier.ValueText == "Target"));
        var model = compilation.GetSemanticModel(tree);
        var parentGraph = ControlFlowGraph.Create(syntax, model)!;
        var method = shape == "local" ? parentGraph.LocalFunctions.Single() : (IMethodSymbol)model.GetDeclaredSymbol(syntax)!;
        var graph = shape == "local" ? parentGraph.GetLocalFunctionControlFlowGraph(method) : parentGraph;
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, method);
        Assert.That(context.OwnsBody(graph.OriginalOperation), Is.True);
        Assert.That(context.HasScalarSignature, Is.True);
        Assert.That(graph.OriginalOperation.SemanticModel!.Compilation, Is.SameAs(compilation));

        var ownSyntax = graph.OriginalOperation.Syntax;
        var switches = ownSyntax.DescendantNodesAndSelf().OfType<SwitchExpressionSyntax>().ToArray();
        Assert.That(switches, Has.Length.EqualTo(shape == "nested" ? 2 : 1));
        var owner = ownSyntax.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Select(invocation => invocation is null ? throw new System.ArgumentNullException(nameof(invocation)) : (model.GetOperation(invocation))).OfType<IInvocationOperation>()
            .Single(invocation => invocation is null ? throw new System.ArgumentNullException(nameof(invocation)) : (invocation.TargetMethod.Name == "Log"));
        var emission = new CSharpInvocationEmissionPolicy(compilation);
        Assert.That(emission.IsElided(owner, requireOwningInvocation: true), Is.EqualTo(!enabled));
        var conditions = graph.Blocks.Where(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.ConditionKind != ControlFlowConditionKind.None &&
            block.BranchValue is IIsPatternOperation)).ToArray();
        Assert.That(conditions.Length, Is.GreaterThanOrEqualTo(switches.Length));
        var throws = graph.Blocks.Where(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.FallThroughSuccessor?.Semantics == ControlFlowBranchSemantics.Throw)).ToArray();
        Assert.That(throws.Length, Is.GreaterThanOrEqualTo(2));
        foreach (var block in conditions.Concat(throws))
        {
            Assert.That(block.BranchValue, Is.Not.Null);
            Assert.That(emission.IsElided(block.BranchValue!), Is.EqualTo(!enabled));
            AssertSameOwner(emission.OwningInvocation(block.BranchValue!), owner);
        }
        var ownerBlocks = graph.Blocks.Where(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Operations.Any(operation =>
            operation is null ? throw new System.ArgumentNullException(nameof(operation)) : ((operation is IExpressionStatementOperation statement ? statement.Operation : operation) is IInvocationOperation invocation &&
            invocation.Syntax.SyntaxTree == owner.Syntax.SyntaxTree && invocation.Syntax.Span == owner.Syntax.Span)))).ToArray();
        Assert.That(ownerBlocks, Has.Length.EqualTo(1));
        Assert.That(ownerBlocks[0].IsReachable, Is.False);
        if (shape == "guarded")
        {
            var implicitThrows = throws.Where(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.BranchValue is
                IObjectCreationOperation { IsImplicit: true, Syntax: SwitchExpressionSyntax })).ToArray();
            Assert.That(implicitThrows, Has.Length.EqualTo(1));
            Assert.That(implicitThrows[0].IsReachable, Is.True);
            var creation = (IObjectCreationOperation)implicitThrows[0].BranchValue!;
            var expected = compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.SwitchExpressionException");
            var core = compilation.GetSpecialType(SpecialType.System_Object).ContainingAssembly;
            var coreException = core.GetTypeByMetadataName("System.Exception")!;
            Assert.That(expected, Is.Not.Null);
            Assert.That(SymbolEqualityComparer.Default.Equals(creation.Type, expected), Is.True);
            Assert.That(DerivesFrom(creation.Type, coreException), Is.True);
            Assert.That(creation.Constructor, Is.Not.Null);
            Assert.That(creation.Constructor!.Parameters, Is.Empty);
            Assert.That(creation.Arguments, Is.Empty);
            Assert.That(creation.Initializer, Is.Null);
            Assert.That(SymbolEqualityComparer.Default.Equals(creation.Constructor.ContainingAssembly, core), Is.True);
            Assert.That(SymbolEqualityComparer.Default.Equals(creation.Type!.ContainingAssembly, core), Is.True);
            Assert.That(creation.Syntax.SyntaxTree, Is.SameAs(tree));
            Assert.That(creation.Syntax.Span, Is.EqualTo(switches.Single().Span));
            var guard = switches.Single().Arms[0].WhenClause!.Condition;
            Assert.That(graph.Blocks.Any(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.ConditionKind != ControlFlowConditionKind.None &&
                block.BranchValue?.Syntax.SyntaxTree == tree && block.BranchValue.Syntax.Span == guard.Span)), Is.True);
        }
        TestContext.WriteLine(JsonSerializer.Serialize(new
        {
            shape,
            enabled,
            SourceSha256 = sourceHash,
            Source = source,
            OwnedMethod = method.ToDisplayString(),
            OriginalKind = graph.OriginalOperation.Kind.ToString(),
            Switches = switches.Length,
            OwnerOrdinal = ownerBlocks[0].Ordinal,
            OwnerReachable = ownerBlocks[0].IsReachable,
            Conditions = conditions.Select(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (new { block.Ordinal, Syntax = block.BranchValue!.Syntax.ToString() })).ToArray(),
            Throws = throws.Select(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (new
            {
                block.Ordinal,
                block.IsReachable,
                Implicit = block.BranchValue!.IsImplicit,
                Syntax = block.BranchValue.Syntax.ToString()
            })).ToArray()
        }));

        var runtime = new AssemblyLoadContext("SwitchTerminalElision", isCollectible: true);
        try
        {
            image.Position = 0;
            var native = runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Target")!;
            foreach (var input in Inputs)
            {
                if (enabled)
                {
                    Action invoke = () => { _ = native.Invoke(null, [input.Value, input.Other]); };
                    var fault = Assert.Throws<TargetInvocationException>(invoke);
                    Assert.That(fault!.InnerException, Is.TypeOf(ExpectedFault(shape, input.Value, input.Other)));
                }
                else
                { Assert.That(native.Invoke(null, [input.Value, input.Other]), Is.EqualTo(17)); }
            }

            var lowered = new RoslynProgramLowerer(factory).LowerCandidate(graph, context);
            Assert.That(lowered.IsExact, Is.True, lowered.Classification.Abstention.ToString());
            Assert.That(lowered.ConstructionLimitExceeded, Is.False);
            foreach (var input in Inputs)
            {
                var environment = context.Parameters.ToDictionary(binding => binding is null ? throw new System.ArgumentNullException(nameof(binding)) : (binding.Entry),
                    binding => binding is null ? throw new System.ArgumentNullException(nameof(binding)) : (factory.CreateIntegerValue(factory.GetVariableInfo(binding.Entry).Type,
                        binding.Parameter.Ordinal == 0 ? input.Value : input.Other)));
                var replay = new IrProgramInterpreter(factory).Execute(lowered.Program, environment);
                Assert.That(replay.ConsumedApproximation, Is.False);
                Assert.That(replay.ApproximationVariables, Is.Empty);
                if (enabled)
                {
                    Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Exception));
                    Assert.That(replay.Exception!.Kind, Is.EqualTo(IrExceptionKind.Explicit));
                    var span = factory.GetOperationInfo(replay.Instruction!.Operation).SourceSpan!;
                    Assert.That(source.Substring(span.Start, span.Length),
                        Is.EqualTo(ExpectedThrowSource(shape, input.Value, input.Other)));
                }
                else
                {
                    Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
                    Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(17)));
                }
            }
        }
        finally
        { runtime.Unload(); }
    }

    private static void AssertSameOwner(IInvocationOperation? actual, IInvocationOperation expected)
    {
        System.ArgumentNullException.ThrowIfNull(expected);
        System.ArgumentNullException.ThrowIfNull(actual);
        Assert.That(actual, Is.Not.Null);
        Assert.That(actual!.Syntax.SyntaxTree, Is.SameAs(expected.Syntax.SyntaxTree));
        Assert.That(actual.Syntax.Span, Is.EqualTo(expected.Syntax.Span));
        Assert.That(SymbolEqualityComparer.Default.Equals(actual.TargetMethod, expected.TargetMethod), Is.True);
    }

    private static bool DerivesFrom(ITypeSymbol? type, INamedTypeSymbol expected)
    {
        for (var current = type as INamedTypeSymbol; current != null; current = current.BaseType)
        { if (SymbolEqualityComparer.Default.Equals(current, expected)) { return true; } }
        return false;
    }

    private static string Expression(string shape)
    {
        return shape switch
        {
            "exhaustive" or "local" =>
                "value switch { 0 => throw new System.NullReferenceException(), _ => throw new System.InvalidOperationException() }",
            "guarded" =>
                "value switch { 0 when other == 0 => throw new System.NullReferenceException(), 1 => throw new System.InvalidOperationException() }",
            "nested" =>
                "value switch { 0 => other switch { 0 => throw new System.NullReferenceException(), _ => throw new System.InvalidOperationException() }, _ => throw new System.ArgumentException() }",
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
    }

    private static string SourceHash(string shape)
    {
        return shape switch
        {
            "exhaustive" => "a862469b72e3914c7c0a7b9a3f6cd71d4e581c111c317be5c31065922d3827b3",
            "guarded" => "2363014d661a351919a2eb4d08b0238b4fb4fa0775129157b825b5e6302126bf",
            "nested" => "51270173b96f0ef7017bf4815a48af22552a99a8cdc7c013aa131b4fa8509b34",
            "local" => "2968ab1d0c08c10bbb899df9e392bc0a8c04a1c7e35638e9b1ba58e0f90586cc",
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
    }

    private static Type ExpectedFault(string shape, int value, int other)
    {
        if (shape == "guarded")
        {
            if (value == 0 && other == 0)
            { return typeof(NullReferenceException); }
            return value == 1 ? typeof(InvalidOperationException) : typeof(System.Runtime.CompilerServices.SwitchExpressionException);
        }
        if (shape == "nested" && value != 0)
        { return typeof(ArgumentException); }
        return value == 0 && (shape != "nested" || other == 0) ? typeof(NullReferenceException) : typeof(InvalidOperationException);
    }

    private static string ExpectedThrowSource(string shape, int value, int other)
    {
        var fault = ExpectedFault(shape, value, other);
        if (fault == typeof(System.Runtime.CompilerServices.SwitchExpressionException))
        { return Expression(shape); }
        return "new " + fault.FullName + "()";
    }
}
