using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class OwnedBoundaryFrontendCoverageTests
{
    [TestCase(4095, true)]
    [TestCase(4096, false)]
    public void CompleteObjectStorageBoundsTheWholeMemberInventory(int fields, bool admitted)
    {
        var declarations = string.Concat(Enumerable.Range(0, fields).Select(static ordinal =>
            $"public static int Value{ordinal};"));
        var compilation = Compile("public sealed class Box { " + declarations + " }");
        var type = compilation.GetTypeByMetadataName("Box")!;
        Assert.That(type.GetMembers().Length, Is.EqualTo(fields + 1));
        var storage = CSharpOperationSemantics.CompleteObjectStorage(type, TestContext.CurrentContext.CancellationToken);
        Assert.That(storage.HasValue, Is.EqualTo(admitted));
        if (storage.HasValue)
        { Assert.That(storage.Value, Is.Empty); }
    }

    [TestCase("public event System.Action Changed;")]
    [TestCase("~Box() { }")]
    public void EventAndFinalizerObjectsDoNotPublishACompleteStorageCertificate(string member)
    {
        ArgumentNullException.ThrowIfNull(member);
        var compilation = Compile("public sealed class Box { public int Value; " + member + " }");
        var type = compilation.GetTypeByMetadataName("Box")!;
        Assert.That(CSharpOperationSemantics.CompleteObjectStorage(type, TestContext.CurrentContext.CancellationToken), Is.Null);
    }

    [TestCase(ResourceAllocationShape.MaximumFields, true)]
    [TestCase(ResourceAllocationShape.MaximumFields + 1, false)]
    public void FieldInventoryIsAtomicAtTheResourceShapeLimit(int fields, bool admitted)
    {
        var declarations = string.Concat(Enumerable.Range(0, fields).Select(static ordinal =>
            $"public int Value{ordinal};"));
        var compilation = Compile("public sealed class Box { " + declarations + " }");
        var storage = CSharpOperationSemantics.CompleteObjectStorage(
            compilation.GetTypeByMetadataName("Box")!, TestContext.CurrentContext.CancellationToken);
        Assert.That(storage.HasValue, Is.EqualTo(admitted));
        if (storage.HasValue)
        {
            Assert.That(storage.Value.Length, Is.EqualTo(fields));
            Assert.That(storage.Value.Distinct(), Is.EqualTo(new[] { ResourceStorageKind.Word32 }));
        }
    }

    [TestCase("Static", false)]
    [TestCase("Unsupported", false)]
    [TestCase("Value", true)]
    public void InitializerStoreRejectsAnUnmodeledFieldOrWrongValueTypeWithoutWriting(string name, bool wrongType)
    {
        ArgumentNullException.ThrowIfNull(name);
        using var subject = TypedProgramSubject.Create("int Target() => 7;",
            "public sealed class Box { public int Value; public float Unsupported; public static int Static; }");
        var field = subject.Compilation.GetTypeByMetadataName("Box")!.GetMembers(name).OfType<IFieldSymbol>().Single();
        var syntax = subject.Compilation.SyntaxTrees.Single().GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>().Single().ExpressionBody!.Expression;
        var initializer = subject.Compilation.GetSemanticModel(syntax.SyntaxTree).GetOperation(syntax)!;
        var builder = new IrProgramBuilder(subject.Factory);
        var entry = builder.CreateBlock();
        var expressions = new RoslynTotalExpressionLowerer(subject.Context, builder);
        Assert.That(expressions.StoreInitializer(field, subject.Factory.Null(subject.Factory.ObjectType), initializer,
            wrongType ? subject.Factory.Boolean(true) : subject.Factory.Integer(1), entry), Is.False);
        builder.Return(entry, subject.Factory.CreateOperation(), subject.Factory.Integer(7));
        var program = builder.Build();
        Assert.That(program.Blocks.SelectMany(static block =>
            block is null ? throw new ArgumentNullException(nameof(block)) : block.Instructions)
            .OfType<IrWriteInstruction>().Where(static write => write is null
                ? throw new ArgumentNullException(nameof(write)) : write.IsStore && write.Region == IrWriteRegion.Field), Is.Empty);
        var replay = new IrProgramInterpreter(subject.Factory).Execute(program);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ReturnValue!.Integer, Is.EqualTo(7));
    }

    [Test]
    public void RepeatedCharacterAllocationDoesNotHideAnUnmodeledLengthCall()
    {
        using var subject = TypedProgramSubject.Create(
            "string Target(int count) => new string('x', Count(count)); static int Count(int value) => value;");
        Assert.That(subject.Invoke([3]), Is.EqualTo("xxx"));
        var tree = subject.Compilation.SyntaxTrees.Single();
        var creation = (IObjectCreationOperation)subject.Compilation.GetSemanticModel(tree).GetOperation(
            tree.GetRoot().DescendantNodes().OfType<ObjectCreationExpressionSyntax>().Single())!;
        Assert.That(CSharpOperationSemantics.IsRepeatedCharacterStringCreation(creation), Is.True);
        var builder = new IrProgramBuilder(subject.Factory);
        var entry = builder.CreateBlock();
        var result = new RoslynTotalExpressionLowerer(subject.Context, builder).LowerBodyValue(creation, entry);
        Assert.That(result.Classification.IsExact, Is.False);
        Assert.That(result.Classification.Abstention, Is.Not.EqualTo(FrontendAbstention.None));
        builder.Return(result.Continuation, subject.Factory.CreateOperation(), result.Value);
        Assert.That(builder.Build().Blocks.SelectMany(static block =>
            block is null ? throw new ArgumentNullException(nameof(block)) : block.Instructions)
            .OfType<IrAllocationInstruction>(), Is.Empty);
    }

    [Test]
    public void ParenthesizedDisjunctiveLoopGuardsRetainBothResourceEntrySites()
    {
        using var subject = TypedProgramSubject.Create(
            "int Target(int first, int second) { int left=0, right=0, total=0; " +
            "while (((left < first || right < second))) { if (left < first) left++; else right++; total++; } return total; }");
        Assert.That(subject.Invoke([2, 3]), Is.EqualTo(5));
        var session = new TotalSourceCallSession(subject.Compilation, static _ => true, null,
            TestContext.CurrentContext.CancellationToken, captureResourceLoopEntries: true);
        var lowering = new RoslynTotalProgramLowerer(subject.Context, TestContext.CurrentContext.CancellationToken, session)
            .Lower(subject.Graph);
        Assert.That(lowering.IsExact, Is.True, lowering.Classification.Abstention.ToString());
        Assert.That(lowering.Program.Blocks.Count(block => block is null ? throw new ArgumentNullException(nameof(block)) :
            IrSourceLoopEntry.IsGuard(subject.Factory, block.Terminator.Operation)), Is.EqualTo(2));
        var replay = subject.Execute(lowering, [2, 3]);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.Integer, Is.EqualTo(5));
    }

    [Test]
    public void AnUnresolvedLayoutAttributeCannotAcquireDefaultFieldStoreEvidence()
    {
        var compilation = Compile(
            "[Missing] public sealed class Box { public int Value; } " +
            "public static class Subject { public static int Target() { var value=new Box(); return value.Value; } }",
            allowErrors: true);
        var field = compilation.GetTypeByMetadataName("Box")!.GetMembers("Value").OfType<IFieldSymbol>().Single();
        Assert.That(CSharpOperationSemantics.CompleteObjectStorage(field.ContainingType,
            TestContext.CurrentContext.CancellationToken), Is.Not.Null);
        Assert.That(CSharpOperationSemantics.HasIndependentFieldStorage(field), Is.False);
        Assert.That(Lower(compilation).IsExact, Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ObjectInitializersDoNotPublishAnExactObjectAfterAnOpaquePropertyRead(bool opaque)
    {
        var value = opaque ? "System.Environment.TickCount" : "7";
        using var subject = TypedProgramSubject.Create(
            "int Target() { var item=new Outer(); return item.Inner.Value; }",
            "public sealed class Box { public int Value; } public sealed class Outer { " +
            "public Box Inner=new Box { Value=" + value + " }; }");
        var actual = subject.Invoke([]);
        Assert.That(actual, Is.TypeOf<int>());
        var lowering = subject.LowerSourceCalls();
        Assert.That(lowering.IsExact, Is.EqualTo(!opaque), lowering.Classification.Abstention.ToString());
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        if (opaque)
        {
            Assert.That(lowering.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedControlFlow));
            return;
        }
        Assert.That(actual, Is.EqualTo(7));
        var replay = subject.Execute(lowering, []);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.Integer, Is.EqualTo(7));
    }

    [Test]
    public void ConstructorHarvestWorkAllowancesPublishOnlyCompleteReplayablePrograms()
    {
        // Use the ordinary scalar-returning constructor input already established
        // by SourceInitializerNaturalBudgetCoverageTests, rather than an upstream
        // unsupported object-returning signature or a speculative compiler shape.
        using var subject = TypedProgramSubject.Create(
            "int Target() { var item=new Box { Second=7 }; return item.First+item.Second; }",
            "public sealed class Box { public int First=2, Second=3; }");
        Assert.That(subject.Invoke([]), Is.EqualTo(9));
        var constructor = subject.Compilation.GetTypeByMetadataName("Box")!.InstanceConstructors.Single();
        int? firstExact = null;
        var limited = 0;
        for (var available = 0; available <= 256; available++)
        {
            var factory = new IrFactory(IrExecutionSemantics.Total);
            var context = new TotalLoweringContext(factory, subject.Context.Target);
            var session = new TotalSourceCallSession(subject.Compilation, static _ => true, null,
                TestContext.CurrentContext.CancellationToken);
            Assert.That(session.Spend(RoslynTotalProgramLowerer.MaximumRegionSteps - available), Is.True);
            var lowering = new RoslynTotalProgramLowerer(context, TestContext.CurrentContext.CancellationToken, session).Lower(subject.Graph);
            Assert.That(session.IsActive(subject.Context.Target), Is.False);
            Assert.That(session.IsActive(constructor), Is.False);
            Assert.That(lowering.ConstructionLimitExceeded, Is.EqualTo(session.ConstructionLimitExceeded));
            if (lowering.ConstructionLimitExceeded)
            {
                limited++;
                Assert.That(lowering.IsExact, Is.False);
                Assert.That(firstExact, Is.Null, "Increasing the allowance must not invalidate an earlier exact import.");
                continue;
            }
            firstExact ??= available;
            Assert.That(lowering.IsExact, Is.True, lowering.Classification.Abstention.ToString());
            var replay = new IrProgramInterpreter(factory).Execute(lowering.Program,
                ImmutableDictionary<IrVarId, IrValue>.Empty, 1000,
                new IrProgramReplayOptions(static _ => null), TestContext.CurrentContext.CancellationToken);
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(replay.ConsumedApproximation, Is.False);
            Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(9)));
        }
        Assert.That(limited, Is.GreaterThan(0));
        Assert.That(firstExact, Is.Not.Null);
        Assert.That(firstExact, Is.LessThan(256));
    }

    [Test]
    public void SourceEventInitializerHarvestRejectsBeforePublishingConstructorEvidence()
    {
        using var control = TypedProgramSubject.Create(
            "int Target() { var item=new Box(); return item.Before; }",
            "public sealed class Box { public int Before=2; }");
        var admitted = control.LowerSourceCalls();
        Assert.That(admitted.IsExact, Is.True, admitted.Classification.Abstention.ToString());
        var replay = control.Execute(admitted, []);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.Integer, Is.EqualTo(2));
        using var subject = TypedProgramSubject.Create(
            "int Target() { var item=new Box(); return item.Before; }",
            "public sealed class Box { public int Before=2; public event System.Action Changed=()=>{}; }");
        Assert.That(subject.Invoke([]), Is.EqualTo(2));
        var lowering = subject.LowerSourceCalls();
        Assert.That(lowering.IsExact, Is.False);
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
    }

    [Test]
    public void LazyInitializerValidationRejectsBeforeAdvancingToAnInvalidSuffix()
    {
        var compilation = Compile("public class Box { public int Before=2; }");
        var type = compilation.GetTypeByMetadataName("Box")!;
        var tree = compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<VariableDeclaratorSyntax>().Single();
        var field = (IFieldInitializerOperation)compilation.GetSemanticModel(tree).GetOperation(syntax.Initializer!)!;
        var suffixVisited = false;
        var disposed = false;
        Assert.That(RoslynTotalProgramLowerer.TryConsumeConstructionInitializerValues(type, Candidates(),
            TestContext.CurrentContext.CancellationToken, out var initializers), Is.False);
        Assert.That(initializers, Is.Empty);
        Assert.That(suffixVisited, Is.False);
        Assert.That(disposed, Is.True);

        IEnumerable<(bool IsField, IOperation? Operation, IPropertySymbol? Property)> Candidates()
        {
            try
            {
                yield return (true, field, null);
                yield return (true, field.Value, null);
                suffixVisited = true;
                throw new AssertionException("Validation advanced after rejecting the previous compiler operation.");
            }
            finally
            { disposed = true; }
        }
    }

    [Test]
    public void LazyInitializerCancellationStopsTheCurrentEnumerationAndPreventsTheNextOne()
    {
        var compilation = Compile("public class Box { public int Before=2; }");
        var type = compilation.GetTypeByMetadataName("Box")!;
        var tree = compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<VariableDeclaratorSyntax>().Single();
        var field = (IFieldInitializerOperation)compilation.GetSemanticModel(tree).GetOperation(syntax.Initializer!)!;
        using var cancellation = new CancellationTokenSource();
        var visited = 0;
        Assert.Throws<OperationCanceledException>((Action)(() =>
            RoslynTotalProgramLowerer.TryConsumeConstructionInitializerValues(type, Candidates(), cancellation.Token, out _)));
        Assert.That(visited, Is.EqualTo(2));
        Assert.Throws<OperationCanceledException>((Action)(() =>
            RoslynTotalProgramLowerer.TryConsumeConstructionInitializerValues(type, Candidates(), cancellation.Token, out _)));
        Assert.That(visited, Is.EqualTo(2), "An already canceled consumer must not start a new source harvest.");

        IEnumerable<(bool IsField, IOperation? Operation, IPropertySymbol? Property)> Candidates()
        {
            visited++;
            yield return (true, field, null);
            cancellation.Cancel();
            visited++;
            yield return (true, field, null);
            visited++;
            throw new AssertionException("Cancellation did not stop the initializer harvest.");
        }
    }

    [Test]
    public void CompilerInitializerOperationsPublishInOrderOrRejectTheWholeInventory()
    {
        var compilation = Compile("public class Box { public int Before=2; public int Value { get; }=3; public int Computed => 4; }");
        var type = compilation.GetTypeByMetadataName("Box")!;
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var fieldSyntax = tree.GetRoot().DescendantNodes().OfType<VariableDeclaratorSyntax>().Single();
        var propertySyntax = tree.GetRoot().DescendantNodes().OfType<PropertyDeclarationSyntax>()
            .Single(static property => property is null ? throw new ArgumentNullException(nameof(property)) : property.Identifier.ValueText == "Value");
        var field = (IFieldInitializerOperation)model.GetOperation(fieldSyntax.Initializer!)!;
        var property = (IPropertyInitializerOperation)model.GetOperation(propertySyntax.Initializer!)!;
        var symbol = (IPropertySymbol)model.GetDeclaredSymbol(propertySyntax)!;
        var computed = type.GetMembers("Computed").OfType<IPropertySymbol>().Single();
        ImmutableArray<(bool IsField, IOperation? Operation, IPropertySymbol? Property)> candidates =
            [(true, field, null), (false, property, symbol)];
        var token = TestContext.CurrentContext.CancellationToken;
        Assert.That(RoslynTotalProgramLowerer.TryConstructionInitializerValues(type, candidates, token, out var initializers), Is.True);
        Assert.That(initializers, Has.Length.EqualTo(2));
        Assert.That(SymbolEqualityComparer.Default.Equals(initializers[0].Field, field.InitializedFields.Single()), Is.True);
        Assert.That(initializers[0].Value, Is.SameAs(field.Value));
        Assert.That(SymbolEqualityComparer.Default.Equals(initializers[1].Field.AssociatedSymbol, symbol), Is.True);
        Assert.That(initializers[1].Value, Is.SameAs(property.Value));

        ImmutableArray<(bool IsField, IOperation? Operation, IPropertySymbol? Property)> invalidSuffixes =
        [
            (true, field.Value, null), (true, null, null),
            (false, field, symbol), (false, null, symbol),
            (false, property, null), (false, property, computed)
        ];
        foreach (var suffix in invalidSuffixes)
        {
            Assert.That(RoslynTotalProgramLowerer.TryConstructionInitializerValues(type, candidates.Add(suffix), token, out var rejected), Is.False);
            Assert.That(rejected, Is.Empty, "An invalid suffix must not expose earlier accepted initializers.");
        }
        Assert.That(RoslynTotalProgramLowerer.TryConstructionInitializerValues(type, [], token, out var empty), Is.True);
        Assert.That(empty, Is.Empty);
    }

    [Test]
    public void CompilerInitializerValidationRequiresInitializedContextAndHonorsCancellation()
    {
        var compilation = Compile("public class Box { }");
        var type = compilation.GetTypeByMetadataName("Box")!;
        Assert.Throws<ArgumentNullException>((Action)(() =>
            RoslynTotalProgramLowerer.TryConstructionInitializerValues(null!, [], default, out _)));
        Assert.Throws<ArgumentNullException>((Action)(() =>
            RoslynTotalProgramLowerer.TryConsumeConstructionInitializerValues(type, null!, default, out _)));
        Assert.Throws<ArgumentException>((Action)(() =>
            RoslynTotalProgramLowerer.TryConstructionInitializerValues(type, default, default, out _)));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>((Action)(() =>
            RoslynTotalProgramLowerer.TryConstructionInitializerValues(type, [], cancellation.Token, out _)));
    }

    [TestCase(ResourceAllocationShape.MaximumFields, true)]
    [TestCase(ResourceAllocationShape.MaximumFields + 1, false)]
    public void RootCaptureInventoryRejectsOversizedCompilerFramesBeforePublishingAPlan(int count, bool admitted)
    {
        var names = Enumerable.Range(0, count).Select(static ordinal => "p" + ordinal).ToArray();
        var parameters = string.Join(",", names.Select(static name => name is null
            ? throw new ArgumentNullException(nameof(name)) : "int " + name));
        var compilation = Compile("public static class Subject { public static System.Action Target(" + parameters +
            ") => () => System.GC.KeepAlive(new int[] {" + string.Join(",", names) + "}); }");
        var method = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
        var tree = compilation.SyntaxTrees.Single();
        var lambda = tree.GetRoot().DescendantNodes().OfType<LambdaExpressionSyntax>().Single();
        var flow = compilation.GetSemanticModel(tree).AnalyzeDataFlow(lambda)!;
        Assert.That(flow.Succeeded, Is.True);
        Assert.That(flow.CapturedInside.Length, Is.EqualTo(count));
        var plan = RootParameterClosurePlan.TryCreate(method, compilation, TestContext.CurrentContext.CancellationToken);
        Assert.That(plan != null, Is.EqualTo(admitted));
        if (plan != null)
        { Assert.That(plan.Fields.Length, Is.EqualTo(count)); }
    }

    [Test]
    public void NestedRootCapturesBelongToTheOuterCompilerDataFlowInventory()
    {
        var compilation = Compile("public static class Subject { public static System.Func<System.Action> Target(int first, int second) " +
            "=> () => { System.Action left=()=>System.GC.KeepAlive(first); " +
            "System.Action right=()=>System.GC.KeepAlive(second); return left; }; }");
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var method = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
        var lambdas = tree.GetRoot().DescendantNodes().OfType<LambdaExpressionSyntax>().ToArray();
        Assert.That(lambdas, Has.Length.EqualTo(3));
        var rootCaptures = model.AnalyzeDataFlow(lambdas[0])!.CapturedInside;
        Assert.That(rootCaptures.Length, Is.EqualTo(2));
        foreach (var lambda in lambdas.Skip(1))
        {
            foreach (var capture in model.AnalyzeDataFlow(lambda)!.CapturedInside)
            {
                Assert.That(rootCaptures.Contains(capture, SymbolEqualityComparer.Default), Is.True);
            }
        }
        Assert.That(RootParameterClosurePlan.TryCreate(method, compilation,
            TestContext.CurrentContext.CancellationToken), Is.Not.Null);
    }

    [Test]
    public void OversizedNestedBodyIsRejectedByTheSharedOperationInventory()
    {
        var statements = string.Concat(Enumerable.Repeat("value = value;", 1500));
        var compilation = Compile("public static class Subject { public static System.Func<System.Action> Target(int root) " +
            "=> () => { int value=0; " + statements + " return () => System.GC.KeepAlive(root+value); }; }");
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var method = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
        var lambda = tree.GetRoot().DescendantNodes().OfType<LambdaExpressionSyntax>().First();
        var operation = (IAnonymousFunctionOperation)model.GetOperation(lambda)!;
        Assert.That(operation.Body.Descendants().Count(), Is.GreaterThan(4096));
        Assert.That(RootParameterClosurePlan.TryCreate(method, compilation,
            TestContext.CurrentContext.CancellationToken), Is.Null);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>((Action)(() => RootParameterClosurePlan.TryCreate(method, compilation, cancellation.Token)));
    }

    private static FrontendProgramLoweringResult Lower(CSharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        var tree = compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var model = compilation.GetSemanticModel(tree);
        var context = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total),
            (IMethodSymbol)model.GetDeclaredSymbol(syntax)!)
        { Compilation = compilation };
        var session = new TotalSourceCallSession(compilation, static _ => true, null, TestContext.CurrentContext.CancellationToken);
        return new RoslynTotalProgramLowerer(context, TestContext.CurrentContext.CancellationToken, session)
            .Lower(ControlFlowGraph.Create(syntax, model)!);
    }

    private static CSharpCompilation Compile(string source, bool allowErrors = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        var compilation = CSharpCompilation.Create("OwnedFrontendBoundary",
            [CSharpSyntaxTree.ParseText(source, path: "owned-boundary.cs")], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics().Where(static diagnostic =>
            diagnostic is null ? throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Severity == DiagnosticSeverity.Error);
        if (allowErrors)
        { Assert.That(errors, Is.Not.Empty); }
        else
        { Assert.That(errors, Is.Empty); }
        return compilation;
    }
}
