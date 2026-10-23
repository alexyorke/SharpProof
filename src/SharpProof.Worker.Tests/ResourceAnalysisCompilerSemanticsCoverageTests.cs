using System.Collections.Immutable;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Compiler.Semantics;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class ResourceAnalysisCompilerSemanticsCoverageTests
{
    [Test]
    public void ACallableWithoutABodyCannotBecomeAZeroCostProof()
    {
        var source = Source("public static void Target() {} ");
        AssertUnknown(ResourceBoundsAnalysis.Analyze(source.Method, null, TestContext.CurrentContext.CancellationToken), "MissingBody");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MissingInitializerOperationsCannotInventCostsOrReplaceAnEarlierGap(bool alreadyUnknown)
    {
        var source = Source("public static int Target() { var value = 1; return value; }");
        var declaration = (MethodDeclarationSyntax)source.Method.DeclaringSyntaxReferences.Single().GetSyntax();
        var model = source.Compilation.GetSemanticModel(declaration.SyntaxTree);
        Assert.That(model.GetOperation(declaration.ReturnType), Is.Null);
        var analysis = Analysis(source.Compilation);
        if (alreadyUnknown)
        { _ = Invoke<ResourceBoundsAnalysis.Cost>(analysis, "Unknown", "EarlierGap"); }
        var initializers = new List<IOperation>();
        _ = Invoke<object?>(analysis, "AddFieldInitializer", source.Compilation, declaration.ReturnType, initializers);
        Assert.That(initializers, Is.Empty);
        Assert.That(Unknown(analysis), Is.EqualTo(alreadyUnknown ? "EarlierGap" : "MissingFieldInitializer"));
        var initializer = declaration.Body!.DescendantNodes().OfType<VariableDeclaratorSyntax>().Single().Initializer!.Value;
        _ = Invoke<object?>(analysis, "AddFieldInitializer", source.Compilation, initializer, initializers);
        Assert.That(initializers.Count, Is.EqualTo(1));
        Assert.That(initializers[0].Syntax, Is.SameAs(initializer));
        Assert.That(Unknown(analysis), Is.EqualTo(alreadyUnknown ? "EarlierGap" : "MissingFieldInitializer"));
    }

    [Test]
    public void UnknownConditionalArmsDoNotBecomePartialCostsFromTheOtherArm()
    {
        var source = Source("public static int Target(bool value) { return value ? System.Environment.TickCount : 1; }");
        var analysis = ResourceBoundsAnalysis.Analyze(source.Method, source.Body, TestContext.CurrentContext.CancellationToken);
        Assert.That(analysis.Proven, Is.False);
        Assert.That(analysis.Bounds, Is.Null);
        Assert.That(analysis.UnknownReason, Does.Contain("Unsupported"));
    }

    [TestCase("uint value", "value")]
    [TestCase("int[,] value", "value.Length")]
    [TestCase("object value", "value.Length")]
    public void ResourceContractInputsMustHaveTheDeclaredScalarOrSequenceDomain(string parameter, string size)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(size);
        var source = Source("[SharpProof.Attributes.ResourceUpperBound(SharpProof.Attributes.ResourceMetric.ArrayAllocations, 0, \"" +
            size + "\", 1)] public static void Target(" + parameter + ") {}");
        Assert.That(ResourceUpperBoundContract.Decode(source.Method, source.Method.GetAttributes().Single()), Is.Null);
    }

    [TestCase("ref int value")]
    [TestCase("System.DateTime value")]
    public void UnsupportedParameterStorageCannotAcquireAnActivationWidth(string parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        var source = Source("public static void Target(" + parameter + ") {}");
        Assert.That(Invoke<ResourceActivationShape?>(Analysis(source.Compilation), "ActivationShape", source.Method, null), Is.Null);
    }

    [TestCase(ResourceActivationShape.MaximumVariables, true)]
    [TestCase(ResourceActivationShape.MaximumVariables + 1, false)]
    public void SignatureOnlyActivationInventoriesKeepTheCompleteVariableBudget(int count, bool admitted)
    {
        var parameters = string.Join(",", Enumerable.Range(0, count).Select(static index => "int p" + index));
        var source = Source("public static void Target(" + parameters + ") {}");
        var shape = Invoke<ResourceActivationShape?>(Analysis(source.Compilation), "ActivationShape", source.Method, null);
        Assert.That(shape is not null, Is.EqualTo(admitted));
        if (shape is not null)
        { Assert.That(shape.Variables.Length, Is.EqualTo(count)); }
    }

    [Test]
    public void NonPrimitiveArrayStorageCannotInventAPrimitiveWidth()
    {
        var source = Source("public static void Target() {}");
        var storage = typeof(ResourceBoundsAnalysis).GetMethod("Storage", BindingFlags.NonPublic | BindingFlags.Static)!;
        var exception = Assert.Throws<TargetInvocationException>((Action)(() => storage.Invoke(null,
            [source.Compilation.GetSpecialType(SpecialType.System_String)])));
        Assert.That(exception!.InnerException, Is.TypeOf<InvalidOperationException>());
        Assert.That(exception.InnerException!.Message, Is.EqualTo("Unsupported primitive array storage."));
    }

    [TestCase(false, "MissingCalleeBody")]
    [TestCase(true, "UnboundCallArgument")]
    public void DetachedInvocationFactsRequireOneOwnedBodyAndBoundArguments(bool unbound, string expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var source = Source("public static void Target() { Helper(1); } static void Helper(int value) {}");
        var invocation = (IInvocationOperation)((IExpressionStatementOperation)source.Body.Operations.Single()).Operation;
        if (unbound)
        {
            var argument = SemanticProjection.Wrap(invocation.Arguments.Single(),
                ("get_Parameter", null));
            invocation = SemanticProjection.Wrap(invocation, ("get_Arguments", ImmutableArray.Create(argument)));
        }
        else
        {
            var method = invocation.TargetMethod;
            var repeated = ImmutableArray.Create(method.DeclaringSyntaxReferences[0], method.DeclaringSyntaxReferences[0]);
            invocation = SemanticProjection.Wrap(invocation, ("get_TargetMethod",
                SemanticProjection.Wrap(method, ("get_DeclaringSyntaxReferences", repeated))));
        }
        var analysis = Analysis(source.Compilation);
        var cost = Invoke<ResourceBoundsAnalysis.Cost>(analysis, "Call", invocation, 0);
        Assert.That(cost, Is.SameAs(ResourceBoundsAnalysis.Cost.Zero));
        Assert.That(Unknown(analysis), Is.EqualTo(expected));
    }

    [TestCase("argument", "UnsupportedRecursiveArguments")]
    [TestCase("width", "UnprovedRecursiveProgress")]
    [TestCase("depth", "AnalysisBudgetExceeded")]
    public void TailRecurrenceRequiresBoundArgumentsMatchingWidthsAndRemainingDepth(string corruption, string expected)
    {
        ArgumentNullException.ThrowIfNull(corruption);
        ArgumentNullException.ThrowIfNull(expected);
        var source = Source("public static void Target(int n) { if (n <= 0) return; Target(n - 1); }");
        var tail = (IExpressionStatementOperation)source.Body.Operations.Last();
        var invocation = (IInvocationOperation)tail.Operation;
        var argument = invocation.Arguments.Single();
        if (corruption == "argument")
        { argument = SemanticProjection.Wrap(argument, ("get_Parameter", null)); }
        if (corruption == "width")
        {
            var progress = (IBinaryOperation)argument.Value;
            argument = SemanticProjection.Wrap(argument, ("get_Value", SemanticProjection.Wrap(progress,
                ("get_Type", source.Compilation.GetSpecialType(SpecialType.System_Int64)))));
        }
        invocation = SemanticProjection.Wrap(invocation, ("get_Arguments", ImmutableArray.Create(argument)));
        tail = SemanticProjection.Wrap(tail, ("get_Operation", invocation));
        var body = SemanticProjection.Wrap(source.Body, ("get_Operations", source.Body.Operations.SetItem(1, tail)));
        var analysis = Analysis(source.Compilation);
        var cost = Invoke<ResourceBoundsAnalysis.Cost>(analysis, "CallableBodyCore", source.Method, body, corruption == "depth" ? 129 : 0);
        Assert.That(cost, Is.SameAs(ResourceBoundsAnalysis.Cost.Zero));
        Assert.That(Unknown(analysis), Is.EqualTo(expected));
    }

    [Test]
    public void OversizedCompilerParameterTextFailsTheWholeCaseWithoutPartialBounds()
    {
        var source = Source("public static void Target(int n) { var a = new int[n]; }");
        var array = Array(source.Body);
        var size = NamedInput((IParameterReferenceOperation)array.DimensionSizes.Single(), new string('n', ResourceExpression.MaximumTextLength + 1));
        var projected = SemanticProjection.Wrap(array, ("get_DimensionSizes", ImmutableArray.Create<IOperation>(size)));
        var body = SemanticProjection.Wrap(source.Body, ("get_Operations", ImmutableArray.Create<IOperation>(projected)));
        var result = Invoke<ResourceBoundsAnalysis.Result>(null, "AnalyzeCase", source.Method, body,
            TestContext.CurrentContext.CancellationToken, null, false);
        AssertUnknown(result, "AnalysisBudgetExceeded");
    }

    [Test]
    public void IndependentResourceMetricsShareOneSummaryTextBudget()
    {
        var source = Source("public static void Target(int n, int m) { for (int i = 0; i < n; i++) {} var a = new int[m]; }");
        var loop = (IForLoopOperation)source.Body.Operations[0];
        var condition = (IBinaryOperation)loop.Condition!;
        var conditionInput = NamedInput((IParameterReferenceOperation)condition.RightOperand, new string('n', 9000));
        loop = SemanticProjection.Wrap(loop, ("get_Condition", SemanticProjection.Wrap(condition, ("get_RightOperand", conditionInput))));
        var array = Array(source.Body);
        var arrayInput = NamedInput((IParameterReferenceOperation)array.DimensionSizes.Single(), new string('m', 9000));
        array = SemanticProjection.Wrap(array, ("get_DimensionSizes", ImmutableArray.Create<IOperation>(arrayInput)));
        var analysis = Analysis(source.Compilation);
        var result = Invoke<ResourceBoundsAnalysis.Cost>(analysis, "Sequence", new IOperation[] { loop, array }, 0);
        Assert.That(result, Is.SameAs(ResourceBoundsAnalysis.Cost.Zero));
        Assert.That(Unknown(analysis), Is.EqualTo("AnalysisBudgetExceeded"));
    }

    [Test]
    public void IndividuallyBoundedBooleanCasesCannotExceedTheCombinedProgramBudget()
    {
        static ResourceProgram LargeProgram()
        {
            var program = ResourceProgram.Array(ResourceExpression.One);
            for (var depth = 0; depth < 10; depth++)
            { program = ResourceProgram.Sequence(program, program); }
            Assert.That(program.NodeCount, Is.LessThanOrEqualTo(ResourceExpression.MaximumNodes));
            return program;
        }
        static ResourceBoundsAnalysis.Result Case(ResourceProgram program)
        {
            ArgumentNullException.ThrowIfNull(program);
            var cost = ResourceBoundsAnalysis.Bound.Number(new BigInteger(1024));
            return new(new(ResourceBoundsAnalysis.Bound.Zero, cost, cost, program), null);
        }
        var first = Case(LargeProgram());
        var second = Case(LargeProgram());
        Assert.That(first.Proven && second.Proven, Is.True);
        var combined = Invoke<ResourceBoundsAnalysis.Result>(null, "CombineInputCases",
            new ResourceBooleanDistribution(0, "choice", 1, 1), first, second);
        AssertUnknown(combined, "AnalysisBudgetExceeded");
        Assert.That(combined.Assumptions, Is.Empty);
    }

    private static IParameterReferenceOperation NamedInput(IParameterReferenceOperation operation, string name)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(name);
        return SemanticProjection.Wrap(operation, ("get_Parameter", SemanticProjection.Wrap(operation.Parameter, ("get_Name", name))));
    }

    private static IArrayCreationOperation Array(IBlockOperation body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var declaration = body.Operations.OfType<IVariableDeclarationGroupOperation>().Last();
        return (IArrayCreationOperation)declaration.Declarations.Single().Declarators.Single().Initializer!.Value;
    }

    private static ResourceBoundsAnalysis Analysis(Compilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        return (ResourceBoundsAnalysis)typeof(ResourceBoundsAnalysis).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single().Invoke([compilation, TestContext.CurrentContext.CancellationToken]);
    }

    private static string? Unknown(ResourceBoundsAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        return (string?)typeof(ResourceBoundsAnalysis).GetField("_unknown", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(analysis);
    }

    private static T Invoke<T>(object? instance, string name, params object?[] arguments)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(arguments);
        return (T)typeof(ResourceBoundsAnalysis).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)!
            .Invoke(instance, arguments)!;
    }

    private static void AssertUnknown(ResourceBoundsAnalysis.Result result, string expected)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(expected);
        Assert.That(result.Proven, Is.False);
        Assert.That(result.Bounds, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo(expected));
    }

    private static SourceCase Source(string methods)
    {
        ArgumentNullException.ThrowIfNull(methods);
        var compilation = TestCompilation.Create("ResourceSemanticCoverage", "public static class C { " + methods + " }");
        TestCompilation.AssertNoErrors(compilation);
        var method = compilation.GetTypeByMetadataName("C")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
        var declaration = (MethodDeclarationSyntax)method.DeclaringSyntaxReferences.Single().GetSyntax(TestContext.CurrentContext.CancellationToken);
        var body = (IBlockOperation)compilation.GetSemanticModel(declaration.SyntaxTree).GetOperation(declaration.Body!)!;
        return new(compilation, method, body);
    }

    private sealed record SourceCase(CSharpCompilation Compilation, IMethodSymbol Method, IBlockOperation Body)
    {
        public CSharpCompilation Compilation { get; init; } = Compilation ?? throw new ArgumentNullException(nameof(Compilation));
        public IMethodSymbol Method { get; init; } = Method ?? throw new ArgumentNullException(nameof(Method));
        public IBlockOperation Body { get; init; } = Body ?? throw new ArgumentNullException(nameof(Body));
    }

    // A malformed detached compiler projection still delegates all unmodified
    // facts to a real Roslyn binding, keeping admission failures narrowly scoped.
    internal class SemanticProjection : DispatchProxy
    {
        private static readonly ConditionalWeakTable<SemanticProjection, ProjectionState> States = new();

        public SemanticProjection()
        {
        }

        internal static T Wrap<T>(T target, params (string Name, object? Value)[] replacements) where T : class
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(replacements);
            var proxy = Create<T, SemanticProjection>();
            States.Add((SemanticProjection)(object)proxy, new(target,
                replacements.ToImmutableDictionary(static pair => pair.Name, static pair => pair.Value, StringComparer.Ordinal)));
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            var state = States.GetValue(this, static _ => throw new InvalidOperationException("Compiler projection is uninitialized."));
            return state.Replacements.TryGetValue(targetMethod.Name, out var value) ? value : targetMethod.Invoke(state.Target, args);
        }

        private sealed record ProjectionState(object Target, ImmutableDictionary<string, object?> Replacements)
        {
            public object Target { get; init; } = Target ?? throw new ArgumentNullException(nameof(Target));
            public ImmutableDictionary<string, object?> Replacements { get; init; } =
                Replacements ?? throw new ArgumentNullException(nameof(Replacements));
        }
    }
}
