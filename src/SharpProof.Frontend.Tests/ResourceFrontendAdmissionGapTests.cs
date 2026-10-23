using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ResourceFrontendAdmissionGapTests
{
    [TestCase(8, true)]
    [TestCase(4096, false)]
    public void LargeMemberCatalogsCannotPublishTruncatedObjectStorage(int constants, bool admitted)
    {
        var members = string.Concat(Enumerable.Range(0, constants).Select(static index =>
            "public const int C" + index.ToString(CultureInfo.InvariantCulture) + "=0;"));
        var compilation = Compile("public sealed class Payload { public int Value; " + members + " }");
        var type = compilation.GetTypeByMetadataName("Payload")!;
        Assert.That(type.StaticConstructors, Is.Empty);
        Assert.That(type.GetMembers().Length > 4096, Is.EqualTo(!admitted));
        var storage = CSharpOperationSemantics.CompleteObjectStorage(type, TestContext.CurrentContext.CancellationToken);
        Assert.That(storage.HasValue, Is.EqualTo(admitted));
        if (storage.HasValue)
        { Assert.That(storage.Value, Is.EqualTo(new[] { ResourceStorageKind.Word32 })); }
    }

    [TestCase("public event System.Action Changed;")]
    [TestCase("~Payload() { }")]
    public void EventsAndFinalizersCannotBorrowThePlainObjectStorageModel(string member)
    {
        ArgumentNullException.ThrowIfNull(member);
        var compilation = Compile("public sealed class Payload { public int Value; " + member + " }");
        var type = compilation.GetTypeByMetadataName("Payload")!;
        Assert.That(type.GetMembers().Any(static candidate => candidate is null
            ? throw new ArgumentNullException(nameof(candidate))
            : candidate is IEventSymbol or IMethodSymbol { MethodKind: MethodKind.Destructor }), Is.True);
        Assert.That(CSharpOperationSemantics.CompleteObjectStorage(type, TestContext.CurrentContext.CancellationToken), Is.Null);
        var control = Compile("public sealed class Payload { public int Value; }").GetTypeByMetadataName("Payload")!;
        Assert.That(CSharpOperationSemantics.CompleteObjectStorage(control, TestContext.CurrentContext.CancellationToken)!.Value,
            Is.EqualTo(new[] { ResourceStorageKind.Word32 }));
    }

    [TestCase(0, true)]
    [TestCase(1, false)]
    public void ObjectFieldLimitIsAtomicAtTheLastCompleteStorageShape(int additionalFields, bool admitted)
    {
        var count = ResourceAllocationShape.MaximumFields + additionalFields;
        var members = string.Concat(Enumerable.Range(0, count).Select(static index =>
            "public int F" + index.ToString(CultureInfo.InvariantCulture) + ";"));
        var type = Compile("public sealed class Payload { " + members + " }").GetTypeByMetadataName("Payload")!;
        Assert.That(type.GetMembers().OfType<IFieldSymbol>().Count(), Is.EqualTo(count));
        var storage = CSharpOperationSemantics.CompleteObjectStorage(type, TestContext.CurrentContext.CancellationToken);
        Assert.That(storage.HasValue, Is.EqualTo(admitted));
        if (storage.HasValue)
        { Assert.That(storage.Value, Has.Length.EqualTo(count)); }
    }

    [TestCase(-1, true)]
    [TestCase(1, false)]
    public void OuterLambdaCaptureAnalysisIncludesTheNestedRootCaptureUnion(int delta, bool admitted)
    {
        var count = ResourceAllocationShape.MaximumFields + delta;
        var names = Enumerable.Range(0, count).Select(static index => "p" + index.ToString(CultureInfo.InvariantCulture)).ToArray();
        var parameters = string.Join(",", names.Select(static name => name is null
            ? throw new ArgumentNullException(nameof(name)) : "int " + name));
        var middle = count / 2;
        var source = "using System; public static class Subject { public static Func<Action> Target(" + parameters +
            ") => () => { GC.KeepAlive(new int[]{" + string.Join(",", names.Take(middle)) +
            "}); return () => GC.KeepAlive(new int[]{" + string.Join(",", names.Skip(middle)) + "}); }; }";
        var compilation = Compile(source);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var lambdas = tree.GetRoot().DescendantNodes().OfType<LambdaExpressionSyntax>().ToArray();
        Assert.That(lambdas, Has.Length.EqualTo(2));
        var outer = model.AnalyzeDataFlow(lambdas[0])!;
        var inner = model.AnalyzeDataFlow(lambdas[1])!;
        Assert.That(outer.Succeeded && inner.Succeeded, Is.True);
        Assert.That(outer.CapturedInside, Has.Length.EqualTo(count));
        Assert.That(inner.CapturedInside.Length, Is.LessThanOrEqualTo(ResourceAllocationShape.MaximumFields));
        Assert.That(inner.CapturedInside.All(capture => capture is null ? throw new ArgumentNullException(nameof(capture))
            : outer.CapturedInside.Contains(capture, SymbolEqualityComparer.Default)), Is.True);
        var method = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
        var plan = RootParameterClosurePlan.TryCreate(method, compilation, TestContext.CurrentContext.CancellationToken);
        Assert.That(plan != null, Is.EqualTo(admitted));
        if (plan != null)
        { Assert.That(plan.Fields, Has.Length.EqualTo(ResourceAllocationShape.MaximumFields)); }
    }

    [Test]
    public void RepeatedCharacterAllocationCannotHideAnUnresolvedLengthCall()
    {
        using var subject = TypedProgramSubject.Create(
            "int Target(int value) => new string('x', Count(ref value)).Length; " +
            "static int Count(ref int value) { value++; return value; }");
        Assert.That(subject.Invoke([3]), Is.EqualTo(4));
        var tree = subject.Compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<ObjectCreationExpressionSyntax>().Single();
        var creation = (IObjectCreationOperation)subject.Compilation.GetSemanticModel(tree).GetOperation(syntax)!;
        Assert.That(CSharpOperationSemantics.IsRepeatedCharacterStringCreation(creation), Is.True);
        var builder = new IrProgramBuilder(subject.Factory);
        var entry = builder.CreateBlock("entry");
        var lowered = new RoslynTotalExpressionLowerer(subject.Context, builder).LowerBodyValue(creation, entry);
        Assert.That(lowered.Classification.IsExact, Is.False);
        Assert.That(lowered.Classification.Abstention, Is.Not.EqualTo(FrontendAbstention.None));
        Assert.That(subject.LowerSourceCalls().IsExact, Is.False);

        using var control = TypedProgramSubject.Create("int Target(int value) => new string('x', value + 1).Length;");
        var exact = control.Lower();
        Assert.That(exact.IsExact, Is.True, exact.Classification.Abstention.ToString());
        Assert.That(control.Invoke([3]), Is.EqualTo(4));
        Assert.That(control.Execute(exact, [3]).ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(4)));
    }

    [TestCase("int[]", true)]
    [TestCase("int[][]", false)]
    public void CompleteReferenceStorageDoesNotAdmitAnUnmodeledZeroInitializedField(string fieldType, bool admitted)
    {
        ArgumentNullException.ThrowIfNull(fieldType);
        using var subject = TypedProgramSubject.Create("int Target() { var item = new Payload(); return 17; }",
            "public sealed class Payload { public " + fieldType + " Values; }");
        Assert.That(subject.Invoke([]), Is.EqualTo(17));
        var type = subject.Compilation.GetTypeByMetadataName("Payload")!;
        var field = type.GetMembers("Values").OfType<IFieldSymbol>().Single();
        Assert.That(field.Type.IsReferenceType, Is.True);
        Assert.That(CSharpOperationSemantics.IsReferenceDomain(field.Type), Is.EqualTo(admitted));
        Assert.That(CSharpOperationSemantics.CompleteObjectStorage(type, TestContext.CurrentContext.CancellationToken)!.Value,
            Is.EqualTo(new[] { ResourceStorageKind.Reference }));
        var lowered = subject.LowerSourceCalls();
        Assert.That(lowered.IsExact, Is.EqualTo(admitted), lowered.Classification.Abstention.ToString());
        Assert.That(lowered.ConstructionLimitExceeded, Is.False);
        if (admitted)
        {
            var replay = subject.Execute(lowered, []);
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(replay.ConsumedApproximation, Is.False);
            Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(17)));
        }
    }

    [Test]
    public void ObjectInitializerRejectsAnUnresolvedRefCalleeInsteadOfKeepingAPartialConstruction()
    {
        const string declaration = "public sealed class Payload { public int Value; }";
        using var subject = TypedProgramSubject.Create(
            "int Target(int value) { var item = new Payload { Value = Read(ref value) }; return item.Value; } " +
            "static int Read(ref int value) { value++; return value; }", declaration);
        Assert.That(subject.Invoke([5]), Is.EqualTo(6));
        var lowered = subject.LowerSourceCalls();
        Assert.That(lowered.IsExact, Is.False);
        Assert.That(lowered.Classification.Abstention, Is.Not.EqualTo(FrontendAbstention.None));
        Assert.That(lowered.ConstructionLimitExceeded, Is.False);
        using var control = TypedProgramSubject.Create(
            "int Target(int value) { var item = new Payload { Value = value + 1 }; return item.Value; }", declaration);
        var exact = control.LowerSourceCalls();
        Assert.That(exact.IsExact, Is.True, exact.Classification.Abstention.ToString());
        Assert.That(control.Invoke([5]), Is.EqualTo(6));
        var replay = control.Execute(exact, [5]);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(6)));
    }

    [Test]
    public void MultipleDeclaratorInitializersBindAndExecuteDistinctFieldSlots()
    {
        using var subject = TypedProgramSubject.Create(
            "int Target() { var item = new Payload(); return item.First * 10 + item.Second; }",
            "public sealed class Payload { public int First = Read(2), Second = Read(3); " +
            "private static int Read(int value) => value + 1; }");
        Assert.That(subject.Invoke([]), Is.EqualTo(34));
        var tree = subject.Compilation.SyntaxTrees.Single();
        var model = subject.Compilation.GetSemanticModel(tree);
        var variables = tree.GetRoot().DescendantNodes().OfType<FieldDeclarationSyntax>().Single().Declaration.Variables;
        foreach (var variable in variables)
        {
            var initializer = model.GetOperation(variable.Initializer!) as IFieldInitializerOperation;
            Assert.That(initializer, Is.Not.Null);
            Assert.That(initializer!.InitializedFields, Has.Length.EqualTo(1));
            Assert.That(SymbolEqualityComparer.Default.Equals(initializer.InitializedFields[0], model.GetDeclaredSymbol(variable)), Is.True);
        }
        var lowered = subject.LowerSourceCalls();
        Assert.That(lowered.IsExact, Is.True, lowered.Classification.Abstention.ToString());
        var replay = subject.Execute(lowered, []);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(34)));
    }

    [TestCase(-2, 0)]
    [TestCase(0, 0)]
    [TestCase(3, 6)]
    public void ParenthesizedDisjunctionCountsEachActualLoopBodyEntryOnce(int input, int expected)
    {
        using var subject = TypedProgramSubject.Create(
            "int Target(int n) { int i = 0, j = 0, entries = 0; " +
            "while (((i < n || j < n))) { if (i < n) i++; else j++; entries++; } return entries; }");
        Assert.That(subject.Invoke([input]), Is.EqualTo(expected));
        var context = new TotalLoweringContext(subject.Factory, subject.Context.Target) { CaptureResourceLoopEntries = true };
        var lowered = new RoslynProgramLowerer(subject.Factory).LowerCandidate(subject.Graph, context, static _ => true,
            TestContext.CurrentContext.CancellationToken);
        Assert.That(lowered.IsExact, Is.True, lowered.Classification.Abstention.ToString());
        var guards = lowered.Program.Blocks.Select(static block => block is null
            ? throw new ArgumentNullException(nameof(block)) : block.Terminator).OfType<IrBranchInstruction>()
            .Where(branch => branch is null ? throw new ArgumentNullException(nameof(branch)) : IrSourceLoopEntry.IsGuard(subject.Factory, branch.Operation)).ToArray();
        Assert.That(guards, Has.Length.EqualTo(2), "The body if must not become another source loop guard.");
        var observed = 0;
        var options = new IrProgramReplayOptions(static _ => null)
        {
            SourceLoopEntryObserver = (instruction, approximation) =>
            {
                ArgumentNullException.ThrowIfNull(instruction);
                Assert.That(approximation, Is.False);
                observed++;
            }
        };
        var replay = new IrProgramInterpreter(subject.Factory).Execute(lowered.Program,
            ImmutableDictionary<IrVarId, IrValue>.Empty.Add(context.Parameters[0].Entry, subject.Factory.CreateIntegerValue(input)),
            1000, options, TestContext.CurrentContext.CancellationToken);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(expected)));
        Assert.That(observed, Is.EqualTo(expected));
    }

    private static CSharpCompilation Compile(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12));
        var compilation = CSharpCompilation.Create("ResourceFrontendAdmission", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic is null
            ? throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        return compilation;
    }
}
