using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Frontend.Host;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ArrayReceiverModelOwnershipTests
{
    [TestCase("++((System.Collections.Generic.IList<int>)values)[0]", 6)]
    [TestCase("((System.Collections.Generic.IList<int>)values)[0] += 3", 8)]
    public void ApproximateArrayInterfaceUpdatesRetainTheirMutation(string expression, int expected)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var tree = CSharpSyntaxTree.ParseText("public static class Subject { public static int Target(int[] values) { return " + expression + "; } }");
        var compilation = CSharpCompilation.Create("ApproximateInterfaceUpdate", [tree], TestMetadataReferences.Platform, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, model.GetDeclaredSymbol(syntax)!);
        var lowered = new RoslynProgramLowerer(factory).LowerCandidate(
            Microsoft.CodeAnalysis.FlowAnalysis.ControlFlowGraph.Create(syntax, model)!, context, static _ => true, null,
            TestContext.CurrentContext.CancellationToken, opaqueCalls: true, approximateElementReads: true);
        Assert.That(lowered.IsExact, Is.True);
        var parameter = context.Parameters[0].Entry;
        var array = factory.CreateSequenceValue(factory.GetVariableInfo(parameter).Type, [factory.CreateIntegerValue(17)]);
        var writeBuilder = ImmutableArray.CreateBuilder<IrWriteInstruction>();
        var replay = new IrProgramInterpreter(factory).Execute(lowered.Program,
            ImmutableDictionary<ScopedIrId<IrVariableTag>, IrValue>.Empty.Add(parameter, array), 1000,
            new IrProgramReplayOptions(_ => factory.CreateIntegerValue(5)) { WriteObserver = writeBuilder.Add });
        var writes = writeBuilder.ToImmutable();
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ReturnValue!.Integer, Is.EqualTo(expected));
        Assert.That(replay.ConsumedApproximation, Is.True);
        Assert.That(writes.Count(write => write is null ? throw new ArgumentNullException(nameof(write)) : write.Region == IrWriteRegion.Element && write.IsStore), Is.EqualTo(1));
    }
    [TestCase(false)]
    [TestCase(true)]
    public void ArraySpecializationRequiresAnUnambiguousSourceModel(bool shared)
    {
        var tree = CSharpSyntaxTree.ParseText("public static class Subject { public static int Target(int[] values) => Helper(values); " + "public static int Helper(System.Collections.Generic.IList<int> values) => values.Count; }");
        var compilation = CSharpCompilation.Create("ArrayReceiverOwner", [tree], TestMetadataReferences.Platform, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        if (shared)
        {
            compilation = compilation.AddReferences(compilation.WithAssemblyName("ArrayReceiverDependency").ToMetadataReference());
        }

        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new global::System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        var model = CompilationModelProvider.GetRootSemanticModel(compilation, tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method is null ? throw new global::System.ArgumentNullException(nameof(method)) : (method.Identifier.ValueText == "Target"));
        var target = model.GetDeclaredSymbol(syntax)!;
        var invocation = (IInvocationOperation)model.GetOperation(syntax.ExpressionBody!.Expression)!;
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        var session = new TotalSourceCallSession(compilation, static _ => true, null, default);
        var admitted = session.TryPrepare(context, invocation.TargetMethod, null, invocation.Arguments, out var frame, out var graph);
        Assert.That(admitted, Is.EqualTo(!shared));
        if (shared)
        {
            Assert.That(frame, Is.Null);
            Assert.That(graph, Is.Null);
        }
        else
        {
            Assert.That(frame, Is.Not.Null);
            Assert.That(graph, Is.Not.Null);
            Assert.That(new RoslynProgramLowerer(factory).LowerCandidate(graph!, frame!).IsExact, Is.True);
        }
    }

    [TestCase("System.Func<int> read = () => values.Count; return read();")]
    [TestCase("int Read() => values.Count; return Read();")]
    public void CapturedParameterReadersKeepOpaqueInterfaceBindings(string body)
    {
        global::System.ArgumentNullException.ThrowIfNull(body);
        var tree = CSharpSyntaxTree.ParseText("public static class Subject { public static int Target(int[] values) => Helper(values); " + "public static int Helper(System.Collections.Generic.IList<int> values) { " + body + " } }");
        var compilation = CSharpCompilation.Create("CapturedReceiver", [tree], TestMetadataReferences.Platform, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new global::System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method is null ? throw new global::System.ArgumentNullException(nameof(method)) : (method.Identifier.ValueText == "Target"));
        var invocation = (IInvocationOperation)model.GetOperation(syntax.ExpressionBody!.Expression)!;
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, model.GetDeclaredSymbol(syntax)!);
        var session = new TotalSourceCallSession(compilation, static _ => true, null, default);
        Assert.That(session.TryPrepare(context, invocation.TargetMethod, null, invocation.Arguments, out var frame, out _), Is.True);
        Assert.That(factory.GetTypeInfo(factory.GetVariableInfo(frame!.Parameters[0].Entry).Type).Kind, Is.EqualTo(IrTypeKind.Reference));
    }
    [TestCase("return comparer.Compare(1, 2);", true)]
    [TestCase("ref var alias = ref comparer; return comparer.Compare(1, 2);", false)]
    public void DefaultComparerSpecializationRejectsRefAliasedParameterStorage(string body, bool expected)
    {
        System.ArgumentNullException.ThrowIfNull(body);
        var tree = CSharpSyntaxTree.ParseText("public static class Subject { public static int Target() => Helper(System.Collections.Generic.Comparer<int>.Default); public static int Helper(System.Collections.Generic.Comparer<int> comparer) { " + body + " } }");
        var compilation = CSharpCompilation.Create("ComparerReceiverOwner", [tree], TestMetadataReferences.Platform, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var model = compilation.GetSemanticModel(tree);
        var methods = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().ToImmutableArray();
        var invocation = (IInvocationOperation)model.GetOperation(methods[0].ExpressionBody!.Expression)!;
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, model.GetDeclaredSymbol(methods[0])!);
        var session = new TotalSourceCallSession(compilation, static _ => true, null, default);
        Assert.That(session.TryPrepare(context, invocation.TargetMethod, null, invocation.Arguments, out var frame, out _), Is.True);
        var comparison = (IInvocationOperation)model.GetOperation(methods[1].Body!.Statements.OfType<ReturnStatementSyntax>().Single().Expression!)!;
        Assert.That(frame!.IsDefaultIntegerComparison(comparison), Is.EqualTo(expected));
    }
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void GenericCollectionHelpersPreserveConcreteParameterRepresentation(bool local, bool cfg)
    {
        const string helper = "static int Read<T>(System.Collections.Generic.IList<T> list) => list.Count == 0 ? 0 : 17;";
        var tree = CSharpSyntaxTree.ParseText("public static class Subject { public static int Target(int[] values) { return Read<int>(values); " +
            (local ? helper + " }" : " } " + helper) + " }");
        var compilation = CSharpCompilation.Create("GenericCollectionOwner", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new ArgumentNullException(nameof(diagnostic)) :
            diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method =>
            method is null ? throw new ArgumentNullException(nameof(method)) : method.Identifier.ValueText == "Target");
        var rootGraph = Microsoft.CodeAnalysis.FlowAnalysis.ControlFlowGraph.Create(syntax, model)!;
        var invocation = cfg ? rootGraph.Blocks.SelectMany(block => block is null ? throw new ArgumentNullException(nameof(block)) :
            block.Operations.Concat(block.BranchValue == null ? [] : ImmutableArray.Create(block.BranchValue)))
            .SelectMany(operation => operation is null ? throw new ArgumentNullException(nameof(operation)) : operation.DescendantsAndSelf())
            .OfType<IInvocationOperation>().Single() :
            (IInvocationOperation)model.GetOperation(syntax.Body!.Statements.OfType<ReturnStatementSyntax>().Single().Expression!)!;
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, model.GetDeclaredSymbol(syntax)!);
        var session = new TotalSourceCallSession(compilation, static _ => true, null, default);
        Assert.That(session.TryPrepare(context, invocation.TargetMethod, null, invocation.Arguments, out var frame, out var graph), Is.True);
        Assert.That(factory.GetTypeInfo(factory.GetVariableInfo(frame!.Parameters[0].Entry).Type).Kind, Is.EqualTo(IrTypeKind.Sequence),
            "The helper's immutable array-backed parameter must be specialized before lowering its body.");
        frame.TrackArrayReceivers(graph!);
        var property = graph!.Blocks.SelectMany(block => block is null ? throw new ArgumentNullException(nameof(block)) :
            block.Operations.Concat(block.BranchValue == null ? [] : ImmutableArray.Create(block.BranchValue)))
            .SelectMany(operation => operation is null ? throw new ArgumentNullException(nameof(operation)) : operation.DescendantsAndSelf())
            .OfType<IPropertyReferenceOperation>().Single();
        Assert.That(frame.ConcreteArray(property.Instance!), Is.Not.Null,
            "A prepared helper's body must retain its specialized receiver binding.");
        Assert.That(frame.ArrayCollectionReceiver(property), Is.Not.Null,
            "property-element=" + property.Property.ContainingType.TypeArguments[0].ToDisplayString() +
            "; element-kind=" + factory.GetTypeInfo(frame.Type(property.Property.ContainingType.TypeArguments[0])).Kind +
            "; same-original=" + SymbolEqualityComparer.Default.Equals(property.Property.ContainingType.TypeArguments[0].OriginalDefinition,
                ((INamedTypeSymbol)frame.Parameters[0].Parameter.Type).TypeArguments[0].OriginalDefinition) +
            "; same-symbol=" + SymbolEqualityComparer.Default.Equals(property.Property.ContainingType.TypeArguments[0],
                ((INamedTypeSymbol)frame.Parameters[0].Parameter.Type).TypeArguments[0]));
        Assert.That(frame.HasScalarSignature, Is.True);
        Assert.That(frame.OwnsBody(graph.OriginalOperation), Is.True);
        var isolated = new RoslynProgramLowerer(factory).LowerCandidate(graph, frame);
        Assert.That(isolated.IsExact, Is.True, isolated.Classification.Abstention + "; " +
            "The prepared helper must lower exactly in isolation.");
        Assert.That(new RoslynProgramLowerer(factory).LowerCandidate(rootGraph, context,
            static prepared => { ArgumentNullException.ThrowIfNull(prepared); return true; }, TestContext.CurrentContext.CancellationToken).IsExact,
            Is.True, "Inlining the prepared helper must preserve its exact array receiver model.");
    }
    [Test]
    public void LocalReceiverElementSpecializationRequiresTheSameDeclaredParameterAndAssembly()
    {
        const string source = "public static class Subject { public static void Target() { " +
            "static int Read<T, U>(System.Collections.Generic.IList<T> list) => list.Count; " +
            "static int Other<T>(System.Collections.Generic.IList<T> list) => list.Count; } }";
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create("LocalElementOwner", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);
        var locals = tree.GetRoot().DescendantNodes().OfType<LocalFunctionStatementSyntax>().ToImmutableArray();
        var read = (IMethodSymbol)model.GetDeclaredSymbol(locals[0])!;
        var other = (IMethodSymbol)model.GetDeclaredSymbol(locals[1])!;
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var frame = new TotalLoweringContext(factory, read);
        frame.SpecializeArrayParameter(read.Parameters[0], compilation.CreateArrayTypeSymbol(compilation.GetSpecialType(SpecialType.System_Int32)));
        Assert.That(frame.Type(read.TypeParameters[0]), Is.EqualTo(factory.IntegerType));
        Assert.That(factory.GetTypeInfo(frame.Type(read.TypeParameters[1])).Kind, Is.EqualTo(IrTypeKind.Reference));
        Assert.That(factory.GetTypeInfo(frame.Type(other.TypeParameters[0])).Kind, Is.EqualTo(IrTypeKind.Reference));
        Assert.That(frame.Type(null), Is.EqualTo(factory.ObjectType));
        // Reusing a syntax tree does not establish the same compilation owner.
        var foreign = compilation.WithAssemblyName("ForeignLocalElementOwner").GetSemanticModel(tree);
        var foreignRead = (IMethodSymbol)foreign.GetDeclaredSymbol(locals[0])!;
        Assert.That(factory.GetTypeInfo(frame.Type(foreignRead.TypeParameters[0])).Kind, Is.EqualTo(IrTypeKind.Reference));
        var sameNameForeign = CSharpCompilation.Create(compilation.AssemblyName, [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)).GetSemanticModel(tree);
        var sameNameForeignRead = (IMethodSymbol)sameNameForeign.GetDeclaredSymbol(locals[0])!;
        Assert.That(factory.GetTypeInfo(frame.Type(sameNameForeignRead.TypeParameters[0])).Kind, Is.EqualTo(IrTypeKind.Reference));
    }
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void GenericArrayReturnStorageRequiresMatchingParameterRepresentation(bool local, bool directArray)
    {
        var helper = "static T Read<T>(" + (directArray ? "T[]" : "System.Collections.Generic.IList<T>") + " list) => list[0];";
        var tree = CSharpSyntaxTree.ParseText("public static class Subject { public static int Target(int[] values) { return Read<int>(values); " +
            (local ? helper + " }" : " } " + helper) + " }");
        var compilation = CSharpCompilation.Create("ArrayReturnOwner", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method =>
            method is null ? throw new ArgumentNullException(nameof(method)) : method.Identifier.ValueText == "Target");
        var invocation = (IInvocationOperation)model.GetOperation(syntax.Body!.Statements.OfType<ReturnStatementSyntax>().Single().Expression!)!;
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, model.GetDeclaredSymbol(syntax)!);
        var session = new TotalSourceCallSession(compilation, static _ => true, null, default);
        Assert.That(session.TryPrepare(context, invocation.TargetMethod, null, invocation.Arguments, out var frame, out var graph), Is.True);
        Assert.That(factory.GetVariableInfo(frame!.Result!.Value).Type, Is.EqualTo(factory.IntegerType));
        Assert.That(new RoslynProgramLowerer(factory).LowerCandidate(graph!, frame).IsExact, Is.True);
    }
    [Test]
    public void UnboundGenericArrayParametersDoNotAcquireScalarEvidence()
    {
        var tree = CSharpSyntaxTree.ParseText("public static class Subject { public static T Target<T>(T[] values) => Read<T>(values); " +
            "static T Read<T>(T[] list) => list[0]; }");
        var compilation = CSharpCompilation.Create("UnboundArrayOwner", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method =>
            method is null ? throw new ArgumentNullException(nameof(method)) : method.Identifier.ValueText == "Target");
        var invocation = (IInvocationOperation)model.GetOperation(syntax.ExpressionBody!.Expression)!;
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, model.GetDeclaredSymbol(syntax)!);
        var session = new TotalSourceCallSession(compilation, static _ => true, null, default);
        Assert.That(session.TryPrepare(context, invocation.TargetMethod, null, invocation.Arguments, out var frame, out var graph), Is.False);
        Assert.That(frame, Is.Null);
        Assert.That(graph, Is.Null);
    }
    [Test]
    public void ScalarParameterFactsDoNotMakeUnknownTypesPrimitive()
    {
        var tree = CSharpSyntaxTree.ParseText("public static class Subject { public static T Target<T>(T value) => Read<T>(value); static U Read<U>(U input) => input; }");
        var compilation = CSharpCompilation.Create("ScalarParameterOwner", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method =>
            method is null ? throw new ArgumentNullException(nameof(method)) : method.Identifier.ValueText == "Target");
        var target = model.GetDeclaredSymbol(syntax)!;
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        Assert.That(context.KnownScalarType(target.TypeParameters[0]), Is.Null);
        Assert.That(context.KnownScalarType(null), Is.Null);
        Assert.That(context.KnownScalarType(compilation.GetSpecialType(SpecialType.System_String)), Is.Null);
    }
}
