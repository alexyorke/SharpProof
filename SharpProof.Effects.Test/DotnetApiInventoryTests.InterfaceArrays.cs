using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.FlowAnalysis;
using SharpProof.Frontend;
using SharpProof.Ir;
using IrVarId = SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrVariableTag>;

namespace SharpProof.Effects.Test;
public sealed partial class DotnetApiInventoryTests
{
    [Test]
    public void InterfaceArrayAdmissionRejectsDerivedInterfacesAliasesAndReferenceCovariance()
    {
        var tree = CSharpSyntaxTree.ParseText("""
            public interface Derived : System.Collections.Generic.IList<int> { }
            public static class GuardProbe {
                public static int A(int[] values) => ((Derived)(System.Collections.Generic.IList<int>)values)[0];
                public static int B(System.Collections.Generic.IList<int> values) => values[0];
                public static int C(string[] values) => ((System.Collections.Generic.ICollection<object>)values).Count;
                public static int D(int[] values) => ((System.Collections.Generic.IList<int>)(object)values)[0];
                public static int E(int[] values) => ((System.Collections.Generic.IList<int>)(values))[0];
                public static int F(int[] values) => (values as System.Collections.Generic.IList<int>)[0];
            }
            """);
        var paths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? throw new InvalidOperationException("Runtime references unavailable.")).Split(Path.PathSeparator);
        var compilation = CSharpCompilation.Create("InterfaceArrayAdmission", [tree], paths.Select(path => path is null ? throw new global::System.ArgumentNullException(nameof(path)) : (MetadataReference.CreateFromFile(path))), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new global::System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        var model = compilation.GetSemanticModel(tree);
        foreach (var method in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            var property = (Microsoft.CodeAnalysis.Operations.IPropertyReferenceOperation)model.GetOperation(method.ExpressionBody!.Expression)!;
            Assert.That(CSharpOperationSemantics.ArrayCollectionReceiver(property) != null, Is.EqualTo(method.Identifier.ValueText == "E"));
        }
    }

    [Test]
    public void SourceDefinedInterfaceNamesDoNotAcquireFrameworkArraySemantics()
    {
        var tree = CSharpSyntaxTree.ParseText("""
            namespace System.Collections.Generic { public interface ICollection<T> { int Count { get; } } }
            public static class GuardProbe {
                public static int A(System.Collections.Generic.ICollection<int> values) => values.Count;
            }
            """);
        var paths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? throw new InvalidOperationException("Runtime references unavailable.")).Split(Path.PathSeparator);
        var compilation = CSharpCompilation.Create("InterfaceArraySpoof", [tree], paths.Select(path => path is null ? throw new global::System.ArgumentNullException(nameof(path)) : (MetadataReference.CreateFromFile(path))), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new global::System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var property = (Microsoft.CodeAnalysis.Operations.IPropertyReferenceOperation)compilation.GetSemanticModel(tree).GetOperation(syntax.ExpressionBody!.Expression)!;
        Assert.That(CSharpOperationSemantics.IsArrayCollectionDeclaration(property.Property), Is.False);
        Assert.That(CSharpOperationSemantics.ArrayCollectionReceiver(property), Is.Null);
    }

    [Test]
    public void InterfaceArraySourceSkeletonRetainsBoundsEffectsWithoutOpaqueCalls()
    {
        var tree = CSharpSyntaxTree.ParseText("public static class Probe { public static int Check(int index) { int[] values = new int[1]; return " + "((System.Collections.Generic.IList<int>)values)[index]; } }");
        var paths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? throw new InvalidOperationException("Runtime references unavailable.")).Split(Path.PathSeparator);
        var compilation = CSharpCompilation.Create("InterfaceArraySkeleton", [tree], paths.Select(path => path is null ? throw new global::System.ArgumentNullException(nameof(path)) : (MetadataReference.CreateFromFile(path))), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, model.GetDeclaredSymbol(syntax)!);
        var lowered = new RoslynProgramLowerer(factory).LowerShadowSourceBody(ControlFlowGraph.Create(syntax, model)!, context, static _ => true, TestContext.CurrentContext.CancellationToken);
        Assert.That(lowered.Classification.IsExact, Is.True);
        Assert.That(lowered.IsShadowCallSkeleton, Is.True);
        var instructions = lowered.Program.Blocks.SelectMany(block => block is null ? throw new global::System.ArgumentNullException(nameof(block)) : (block.Instructions)).ToArray();
        Assert.That(instructions.OfType<IrCallInstruction>(), Is.Empty);
        Assert.That(instructions.OfType<IrHavocInstruction>().Any(havoc => havoc is null ? throw new global::System.ArgumentNullException(nameof(havoc)) : (factory.GetOperationInfo(havoc.Operation).Description is { } description && factory.GetString(description).EndsWith(":interface-array:bounds", StringComparison.Ordinal))), Is.True);
        Assert.That(instructions.OfType<IrWriteInstruction>().Any(write => write is null ? throw new global::System.ArgumentNullException(nameof(write)) : (write.Region == IrWriteRegion.Static)), Is.True);
    }

    [TestCase("receiver")]
    [TestCase("index")]
    [TestCase("value")]
    [TestCase("comparison")]
    [TestCase("comparison-receiver")]
    public void UnmodeledOperandsRemainIncompleteThroughInterfaceAndComparisonIntrinsics(string kind)
    {
        global::System.ArgumentNullException.ThrowIfNull(kind);
        var body = kind switch
        {
            "receiver" => "return ((System.Collections.Generic.IList<int>)UnknownValues())[0];",
            "index" => "return ((System.Collections.Generic.IList<int>)values)[UnknownIndex()];",
            "value" => "((System.Collections.Generic.IList<int>)values)[0] = UnknownIndex(); return 0;",
            "comparison-receiver" => "return UnknownIndex().CompareTo(0);",
            _ => "return (0).CompareTo(UnknownIndex());"
        };
        var tree = CSharpSyntaxTree.ParseText("public static class Probe { " + "public static int Check(int[] values) { " + body + " } " + "private static int[] UnknownValues() => new int[1]; private static int UnknownIndex() => 0; }");
        var paths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? throw new InvalidOperationException("Runtime references unavailable.")).Split(Path.PathSeparator);
        var compilation = CSharpCompilation.Create("IncompleteIntrinsicOperands", [tree], paths.Select(path => path is null ? throw new global::System.ArgumentNullException(nameof(path)) : (MetadataReference.CreateFromFile(path))), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new global::System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method is null ? throw new global::System.ArgumentNullException(nameof(method)) : (method.Identifier.ValueText == "Check"));
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, model.GetDeclaredSymbol(syntax)!);
        // No callee preparation or opaque-call admission: each source helper
        // remains outside this lowering context and must retain its abstention.
        var lowered = new RoslynProgramLowerer(factory).LowerCandidate(ControlFlowGraph.Create(syntax, model)!, context);
        Assert.That(lowered.IsExact, Is.False);
        if (kind != "value")
        {
            var expression = syntax.Body!.Statements.OfType<ReturnStatementSyntax>().Single().Expression!;
            var clause = new RoslynTotalExpressionLowerer(context).LowerClause(model.GetOperation(expression)!);
            Assert.That(clause.Classification.IsExact, Is.False);
        }
    }

    [TestCase("System.Collections.Generic.IList<int> list = values; return list.Count;", true)]
    [TestCase("System.Collections.Generic.IList<int> list = values; list = choose ? values : unknown; return list.Count;", false)]
    [TestCase("System.Collections.Generic.IList<int> list = values; (list, choose) = (unknown, false); return list.Count;", false)]
    [TestCase("System.Collections.Generic.IList<int> list = values; Escape(ref list); System.Collections.Generic.IList<int> alias = list; return alias.Count; static void Escape(ref System.Collections.Generic.IList<int> item) { }", false)]
    [TestCase("System.Collections.Generic.IList<int> list = values; list = unknown; return list.Count;", false)]
    [TestCase("System.Collections.Generic.IList<int> list = values; System.Collections.Generic.IList<int> other = list; list = other; return list.Count;", false)]
    [TestCase("System.Collections.Generic.IList<int> list = values; Escape(ref list); return list.Count; static void Escape(ref System.Collections.Generic.IList<int> item) { }", false)]
    [TestCase("System.Collections.Generic.IList<int> list = values; System.Action action = () => list = unknown; return list.Count;", false)]
    [TestCase("return (choose ? (System.Collections.Generic.IList<int>)values : unknown).Count;", false)]
    [TestCase("return (choose ? (System.Collections.Generic.IList<int>)values : (System.Collections.Generic.IList<int>)values).Count;", true)]
    [TestCase("System.Collections.Generic.IList<int> list = values as System.Collections.Generic.IList<int>; return list.Count;", false)]
    [TestCase("System.Collections.Generic.IList<long> list = (System.Collections.Generic.IList<long>)(System.Collections.Generic.IList<int>)values; return list.Count;", false)]
    public void ReceiverProvenanceRequiresAllDefinitionsAndRejectsEscapes(string body, bool expected)
    {
        global::System.ArgumentNullException.ThrowIfNull(body);
        var tree = CSharpSyntaxTree.ParseText("public static class Probe { public static int Check(int[] values, System.Collections.Generic.IList<int> unknown, bool choose) { " + body + " } }");
        var paths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? throw new InvalidOperationException("Runtime references unavailable.")).Split(Path.PathSeparator);
        var compilation = CSharpCompilation.Create("ReceiverProvenance", [tree], paths.Select(path => path is null ? throw new global::System.ArgumentNullException(nameof(path)) : (MetadataReference.CreateFromFile(path))), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new global::System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var graph = ControlFlowGraph.Create(syntax, model)!;
        var context = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total), model.GetDeclaredSymbol(syntax)!);
        context.TrackArrayReceivers(graph);
        var property = graph.Blocks.SelectMany(block => block is null ? throw new global::System.ArgumentNullException(nameof(block)) : (block.Operations.Concat(block.BranchValue == null ? [] : new[] { block.BranchValue }))).SelectMany(operation => operation is null ? throw new global::System.ArgumentNullException(nameof(operation)) : (operation.DescendantsAndSelf())).OfType<Microsoft.CodeAnalysis.Operations.IPropertyReferenceOperation>().Single();
        Assert.That(context.ConcreteArray(property.Instance!) != null, Is.EqualTo(expected));
    }

    [Test]
    public void ReceiverProvenanceBoundsDeepInterfaceConversions()
    {
        ReceiverProvenanceRequiresAllDefinitionsAndRejectsEscapes("return (" + string.Concat(Enumerable.Repeat("(System.Collections.Generic.IList<int>)", 70)) + "values).Count;", false);
    }

    private static bool CheckInterfaceArrayIntrinsic(CSharpCompilation references, string api, bool wrongClaim)
    {
        global::System.ArgumentNullException.ThrowIfNull(api);
        var actions = api.Contains("ICollection", StringComparison.Ordinal) ? new[]
        {
            "count",
            "inherited-count",
            "aliased-count"
        }

        : ["read", "write", "aliased-read", "aliased-write"];
        foreach (var action in actions)
        {
            var body = action switch
            {
                "count" => "return ((System.Collections.Generic.ICollection<int>)values).Count;",
                "inherited-count" => "return ((System.Collections.Generic.IList<int>)values).Count;",
                "read" => "return ((System.Collections.Generic.IList<int>)values)[index];",
                "aliased-count" => "System.Collections.Generic.IList<int> list = values; System.Collections.Generic.ICollection<int> collection = list; return collection.Count;",
                "aliased-read" => "System.Collections.Generic.IList<int> list = values; return list[index];",
                "aliased-write" => "System.Collections.Generic.IList<int> list = values; list[index] = replacement; return values[index];",
                _ => "((System.Collections.Generic.IList<int>)values)[index] = replacement; return values[index];"
            };
            var tree = CSharpSyntaxTree.ParseText("public static class Probe { public static int Check(int[] values, int index, int replacement) { " + body + " } }");
            var compilation = references.AddSyntaxTrees(tree);
            if (compilation.GetDiagnostics().Any(diagnostic => diagnostic is null ? throw new global::System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)))
            {
                return false;
            }

            var model = compilation.GetSemanticModel(tree);
            var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
            var factory = new IrFactory(IrExecutionSemantics.Total);
            var context = new TotalLoweringContext(factory, model.GetDeclaredSymbol(syntax)!);
            var lowered = new RoslynProgramLowerer(factory).LowerCandidate(ControlFlowGraph.Create(syntax, model)!, context);
            if (!lowered.IsExact)
            {
                return false;
            }

            foreach (var values in new int[]?[]
            {
                null,
                [],
                [17],
                [int.MinValue, 0, int.MaxValue]
            }

            )
            {
                foreach (var index in new[]
                {
                    int.MinValue,
                    -1,
                    0,
                    1,
                    (values?.Length ?? 1) - 1,
                    values?.Length ?? 0,
                    int.MaxValue
                }

                )
                {
                    const int replacement = -37;
                    var runtime = values?.ToArray();
                    int expected = 0;
                    Exception? fault = null;
                    try
                    {
                        switch (action)
                        {
                            case "aliased-count":
                            case "count":
                                expected = ((ICollection<int>)runtime!).Count;
                                break;
                            case "inherited-count":
                                expected = ((IList<int>)runtime!).Count;
                                break;
                            case "aliased-read":
                            case "read":
                                expected = ((IList<int>)runtime!)[index];
                                break;
                            default:
                                ((IList<int>)runtime!)[index] = replacement;
                                expected = runtime![index];
                                break;
                        }
                    }
                    catch (Exception exception) when (exception is NullReferenceException or ArgumentOutOfRangeException)
                    {
                        fault = exception;
                    }

                    var parameters = context.Parameters;
                    var arrayType = factory.GetVariableInfo(parameters[0].Entry).Type;
                    var arrayValue = values == null ? factory.CreateNullValue(arrayType) : factory.CreateSequenceValue(arrayType, values.Select(value => factory.CreateIntegerValue(value)));
                    var replay = new IrProgramInterpreter(factory).Execute(lowered.Program, new Dictionary<IrVarId, IrValue> { [parameters[0].Entry] = arrayValue, [parameters[1].Entry] = factory.CreateIntegerValue(index), [parameters[2].Entry] = factory.CreateIntegerValue(replacement) }, 10000, new IrProgramReplayOptions(_ => factory.CreateBooleanValue(false)));
                    if (replay.ConsumedApproximation && fault is not ArgumentOutOfRangeException)
                    {
                        return false;
                    }

                    if (fault != null)
                    {
                        if (values != null && !runtime!.SequenceEqual(values))
                        {
                            return false;
                        }

                        if (replay.Status != IrProgramExecutionStatus.Exception || replay.Exception?.Kind != (fault is NullReferenceException ? IrExceptionKind.NullReference : IrExceptionKind.Unknown))
                        {
                            return false;
                        }
                    }
                    else if (replay.Status != IrProgramExecutionStatus.Returned || replay.ReturnValue?.Integer != expected + (wrongClaim ? 1L : 0L))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }
}
