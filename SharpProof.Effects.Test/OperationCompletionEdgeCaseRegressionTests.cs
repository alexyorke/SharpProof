using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Specs;

namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class OperationCompletionEdgeCaseRegressionTests
{
    [Test]
    public void ReplayablePrefixUsesFlowFactsForSafeReceiversAndStores()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            #nullable enable
            public sealed class K {
                public int M() => 1;
            }
            public sealed class Box {
                public int Field;
                public int Property { get; set; }
            }
            public class VirtualBox {
                public virtual int M() => 1;
            }
            public sealed class FragileBox {
                public int Property {
                    get { while (true) { } }
                    set { }
                }
            }
            public static class StaticBox {
                static StaticBox() { throw new System.Exception(); }
                public static int Field;
            }
            public static class Sample {
                private static void Check() { }
                public static void LocalReceiver() {
                    var value = new K();
                    value.M();
                    Check();
                }
                public static void ArrayWrite() {
                    var values = new int[3];
                    values[0] = 1;
                    Check();
                }
                public static void InstanceFieldWrite() {
                    var box = new Box();
                    box.Field = 1;
                    Check();
                }
                public static void PropertyWrite() {
                    var box = new Box();
                    box.Property = 1;
                    Check();
                }
                public static void NullConditional() {
                    K? value = null;
                    value?.M();
                    Check();
                }
                public static void UnknownReceiver(K value) {
                    value.M();
                    Check();
                }
                public static void UnknownArrayIndex(int[] values, int index) {
                    values[index] = 1;
                    Check();
                }
                public static void VirtualReceiver() {
                    var value = new VirtualBox();
                    value.M();
                    Check();
                }
                public static void CheckedConversion(long value) {
                    _ = checked((int)value);
                    Check();
                }
                public static void DecimalConversion(decimal value) {
                    _ = unchecked((int)value);
                    Check();
                }
                public static void StaticConstructor() {
                    StaticBox.Field = 1;
                    Check();
                }
                public static void DivisionByUnknownValue(int divisor) {
                    _ = 1 / divisor;
                    Check();
                }
                public static void UnboundedLoop() {
                    for (var index = 0; index < 1;) { }
                    Check();
                }
                public static void CompoundAssignmentGetter() {
                    var value = new FragileBox();
                    value.Property += 1;
                    Check();
                }
            }
            """);

        foreach (var (methodName, expected) in new[]
                 {
                     ("LocalReceiver", true),
                     ("ArrayWrite", true),
                     ("InstanceFieldWrite", true),
                     ("PropertyWrite", true),
                     ("NullConditional", true),
                     ("UnknownReceiver", false),
                     ("UnknownArrayIndex", false),
                     ("VirtualReceiver", false),
                     ("CheckedConversion", false),
                     ("DecimalConversion", false),
                     ("StaticConstructor", false),
                     ("DivisionByUnknownValue", false),
                     ("UnboundedLoop", false),
                     ("CompoundAssignmentGetter", false)
                 })
        {
            var method = EffectTestHost.SampleMethod(compilation, methodName);
            var declaration = (MethodDeclarationSyntax)method
                .DeclaringSyntaxReferences.Single().GetSyntax();
            var model = compilation.GetSemanticModel(declaration.SyntaxTree);
            var root = (IMethodBodyOperation)model.GetOperation(declaration)!;
            var graph = ControlFlowGraph.Create(root);
            var flow = ManagedAbstractFlow.ForCompilation(compilation)
                .Analyze(method, graph, null, default);
            var target = root.DescendantsAndSelf()
                .OfType<IInvocationOperation>()
                .Single(static invocation =>
                    invocation.TargetMethod.Name == "Check");
            var targetStatement = target.Syntax.AncestorsAndSelf()
                .OfType<StatementSyntax>()
                .First(static statement =>
                    statement is ExpressionStatementSyntax);
            var priorStatements = declaration.Body!.Statements
                .TakeWhile(statement =>
                    !statement.Span.Contains(targetStatement.Span));
            var facts = new DefiniteOperationFacts(
                compilation,
                CancellationToken.None);
            var prefixCompletes = priorStatements.All(statement =>
                facts.CompletesNormally(
                    model.GetOperation(statement),
                    flow.Result,
                    target));

            Assert.That(prefixCompletes, Is.EqualTo(expected), methodName);
        }
    }

    [TestCase(false, true)]
    [TestCase(true, false)]
    public void StaticFieldInitializationCanReachCatchAndCallerSuffix(bool constant, bool expected)
    {
        var compilation = EffectTestHost.CreateCompilation("""
            public class Other {
                static Other() { throw new System.Exception(); }
            """ + (constant ? "public const int Value = 1;" : "public static int Value;") + """
            }
            public static class Sample {
                private static int state;
                public static void Helper() {
                    try { _ = Other.Value; while (true) {} } catch {}
                }
                public static void Run() { Helper(); state++; }
            }
            """);
        var helper = EffectTestHost.SampleMethod(compilation, "Helper");
        var run = EffectTestHost.SampleMethod(compilation, "Run");
        var facts = new DefiniteOperationFacts(compilation, System.Threading.CancellationToken.None);
        Assert.That(facts.MethodCanCompleteNormally(helper), Is.EqualTo(expected));
        Assert.That(EffectTestHost.CreateCompletionEvaluator(compilation, helper)
            .CanMethodCompleteNormally(helper), Is.EqualTo(expected));
        Assert.That(EffectTestHost.HasStaticWrite(compilation, run), Is.EqualTo(expected));
    }

    [Test]
    public void VirtualSelfNamedInvocationCanDispatchToReturningOverride()
    {
        var compilation = EffectTestHost.CreateCompilation("""
            public class Base {
                public Base Other;
                public virtual void M() => Other.M();
            }
            public class Returning : Base { public override void M() {} }
            public class Sample : Base {
                private static int state;
                public void Run() {
                    Other = new Returning();
                    base.M();
                    state++;
                }
            }
            """);
        var run = EffectTestHost.SampleMethod(compilation, "Run");
        var method = compilation.GetTypeByMetadataName("Base")!.GetMembers("M")
            .OfType<IMethodSymbol>().Single();
        Assert.That(new DefiniteOperationFacts(compilation, System.Threading.CancellationToken.None)
            .MethodCanCompleteNormally(method), Is.True);
        Assert.That(EffectTestHost.HasStaticWrite(compilation, run), Is.True);
    }

    [Test]
    public void NonexhaustiveSwitchExpressionWithReturningArmMayComplete()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                private static int state;

                private static int Choose(int value) =>
                    value switch { 0 => 1 };

                public static void Run(int value) {
                    _ = Choose(value);
                    state++;
                }
            }
            """);
        var choose = EffectTestHost.SampleMethod(compilation, "Choose");
        var run = EffectTestHost.SampleMethod(compilation, "Run");
        var facts = new DefiniteOperationFacts(
            compilation,
            System.Threading.CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(facts.MethodCanCompleteNormally(choose), Is.True);
            Assert.That(EffectTestHost.HasStaticWrite(compilation, run), Is.True);
        }
    }

    [Test]
    public void RecursiveSourceCallWithBaseCaseRetainsSuffixEffect()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                private static int state;

                private static int CountDown(int value) {
                    if (value <= 0) return 0;
                    return CountDown(value - 1);
                }

                public static void Run(int value) {
                    _ = CountDown(value);
                    state++;
                }
            }
            """);
        var countDown = EffectTestHost.SampleMethod(compilation, "CountDown");
        var run = EffectTestHost.SampleMethod(compilation, "Run");
        var facts = new DefiniteOperationFacts(
            compilation,
            System.Threading.CancellationToken.None);
        var apiSpecs = new ApiSpecResolver(
            ApiSpecTable.Default).Resolve(compilation);
        var localNode = new EffectMethodNodeBuilder(
                new EffectAnalysisSession(compilation, apiSpecs),
                compilation,
                ManagedAbstractFlow.Create(compilation, apiSpecs))
            .Build(run, System.Threading.CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(facts.MethodCanCompleteNormally(countDown), Is.True);
            Assert.That(localNode.LocalSummary.Writes.IsUnknown, Is.False);
            Assert.That(
                localNode.LocalSummary.Writes.Contains(
                    EffectRegionId.Static()),
                Is.True);
        }
    }

    [Test]
    public void ConstantNullConversionToNullableMayComplete()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                private static int state;

                private static void Observe(int? value) {
                }

                public static void Run() {
                    Observe((int?)null);
                    state++;
                }
            }
            """);
        var run = EffectTestHost.SampleMethod(compilation, "Run");
        var conversion = EffectTestHost.RootOperation(compilation, run)
            .DescendantsAndSelf()
            .OfType<IConversionOperation>()
            .Single(operation =>
                operation.Type is INamedTypeSymbol
                {
                    OriginalDefinition.SpecialType:
                        SpecialType.System_Nullable_T
                } &&
                operation.Operand.ConstantValue is
                { HasValue: true, Value: null });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                EffectTestHost.CreateCompletionEvaluator(compilation, run)
                    .CanCompleteNormally(conversion),
                Is.True);
            Assert.That(EffectTestHost.HasStaticWrite(compilation, run), Is.True);
        }
    }

    [Test]
    public void UserDefinedConstantNullConversionToStructMayComplete()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            #nullable enable

            public readonly struct Token {
                public static implicit operator Token(string? value) =>
                    default;
            }

            public static class Sample {
                private static int state;

                public static void Run() {
                    _ = (Token)(string?)null;
                    state++;
                }
            }
            """);
        var run = EffectTestHost.SampleMethod(compilation, "Run");
        var conversion = EffectTestHost.RootOperation(compilation, run)
            .DescendantsAndSelf()
            .OfType<IConversionOperation>()
            .Single(operation => operation.OperatorMethod != null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                conversion.Operand.ConstantValue,
                Is.EqualTo(new Optional<object?>(null)));
            Assert.That(
                EffectTestHost.CreateCompletionEvaluator(compilation, run)
                    .CanCompleteNormally(conversion),
                Is.True);
            Assert.That(EffectTestHost.HasStaticWrite(compilation, run), Is.True);
        }
    }

    [Test]
    public void LiftedNullDivisionByZeroCanReachFollowingWrites()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                private static int state;

                public static void Binary() {
                    int? left = null;
                    _ = left / 0;
                    state++;
                }

                public static void Compound() {
                    int? left = null;
                    left /= 0;
                    state++;
                }

                public static void Unknown(int? left) {
                    _ = left / 0;
                    state++;
                }
            }
            """);
        var binary = EffectTestHost.SampleMethod(compilation, "Binary");
        var compound = EffectTestHost.SampleMethod(compilation, "Compound");
        var unknown = EffectTestHost.SampleMethod(compilation, "Unknown");
        var binaryOperation = EffectTestHost.RootOperation(compilation, binary)
            .DescendantsAndSelf()
            .OfType<IBinaryOperation>()
            .Single();
        var compoundOperation = EffectTestHost.RootOperation(compilation, compound)
            .DescendantsAndSelf()
            .OfType<ICompoundAssignmentOperation>()
            .Single();
        var unknownOperation = EffectTestHost.RootOperation(compilation, unknown)
            .DescendantsAndSelf()
            .OfType<IBinaryOperation>()
            .Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(binaryOperation.IsLifted, Is.True);
            Assert.That(compoundOperation.IsLifted, Is.True);
            Assert.That(
                EffectTestHost.CreateCompletionEvaluator(compilation, binary)
                    .CanCompleteNormally(binaryOperation),
                Is.True);
            Assert.That(
                EffectTestHost.CreateCompletionEvaluator(compilation, compound)
                    .CanCompleteCompoundOperator(compoundOperation),
                Is.True);
            Assert.That(
                EffectTestHost.CreateCompletionEvaluator(compilation, unknown)
                    .CanCompleteNormally(unknownOperation),
                Is.True);
            Assert.That(EffectTestHost.HasStaticWrite(compilation, binary), Is.True);
            Assert.That(EffectTestHost.HasStaticWrite(compilation, compound), Is.True);
            Assert.That(EffectTestHost.HasStaticWrite(compilation, unknown), Is.True);
        }
    }

    [TestCase("_ = value switch { 0 => 0 };")]
    [TestCase("using (resource) { }")]
    [TestCase("{ using var lifetime = resource; }")]
    [TestCase("foreach (var item in sequence) { }")]
    public void ImplicitThrowBeforeInfiniteLoopCanCompleteThroughCatch(string statement)
    {
        var compilation = EffectTestHost.CreateCompilation(
            $$"""
            public static class Sample {
                private static int state;
                public static void Helper(int value, System.IDisposable resource,
                    System.Collections.Generic.IEnumerable<int> sequence) {
                    try { {{statement}} while (true) { } }
                    catch { }
                }
                public static void Run(int value, System.IDisposable resource,
                    System.Collections.Generic.IEnumerable<int> sequence) {
                    Helper(value, resource, sequence);
                    state++;
                }
            }
            """);
        var helper = EffectTestHost.SampleMethod(compilation, "Helper");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(EffectTestHost.CreateCompletionFacts(compilation)
                .MethodCanCompleteNormally(helper), Is.True);
            Assert.That(EffectTestHost.CreateCompletionEvaluator(compilation, helper)
                .CanMethodCompleteNormally(helper), Is.True);
            Assert.That(EffectTestHost.HasStaticWrite(compilation,
                EffectTestHost.SampleMethod(compilation, "Run")), Is.True);
        }
    }

    [Test]
    public void NonCompletingTryBodyDoesNotMakeCatchReachableForCompletion()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample
            {
                public static void Run()
                {
                    try
                    {
                        while (true)
                        {
                        }
                    }
                    catch
                    {
                    }
                }
            }
            """);
        var method = EffectTestHost.SampleMethod(compilation, "Run");
        var facts = EffectTestHost.CreateCompletionFacts(compilation);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(facts.MethodCanCompleteNormally(method), Is.False);
            Assert.That(
                EffectTestHost.CreateCompletionEvaluator(compilation, method)
                    .CanMethodCompleteNormally(method),
                Is.False);
        }
    }

}
