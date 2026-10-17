using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Fuzz;
using SharpProof.Ir;
using SharpProof.Verify;

namespace SharpProof.Fuzz.Test;

public sealed class FuzzEmissionLifecycleTests
{
    private static readonly string[] ParameterBindingMethodNames = ["Supported", "Extra"];

    [Test]
    public void RealCompilationEmissionReturnsItsSuccessfulReadableImage()
    {
        var compilation = CreateValidCompilation();
        var emission = GeneratedCompilationEmitter.Emit(
            compilation, CancellationToken.None);
        using var image = emission.Image;

        Assert.That(emission.Emit.Success, Is.True,
            string.Join(Environment.NewLine, emission.Emit.Diagnostics));
        Assert.That(image.CanRead, Is.True);
        Assert.That(image.Length, Is.GreaterThan(0));
    }

    [Test]
    public async Task RealCompilationEmissionPropagatesTheActualPrecanceledTokenException()
    {
        var compilation = CreateValidCompilation();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        Action emit = () =>
        {
            using var image = GeneratedCompilationEmitter.Emit(
                compilation, cancellation.Token).Image;
        };

        Assert.Throws<OperationCanceledException>(emit);
    }

    [Test]
    public void MetadataEmissionReportsAnActualCompilerReturnTypeError()
    {
        const string source = "public static class MetadataEmissionControl { " +
            "public static int Target() => \"bad\"; }";
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12),
            "metadata-emission-control.cs",
            cancellationToken: CancellationToken.None);
        var compilation = CSharpCompilation.Create("MetadataEmissionInvalidReturn", [tree],
            TestMetadataReferences.SortedDistinctPlatform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.Id), Does.Contain("CS0029"));
        Action emit = () =>
        {
            using var image = MetadataProgramDifferentialOracle.Emit(
                compilation, CancellationToken.None);
        };

        var exception = Assert.Throws<InvalidOperationException>(emit);

        Assert.That(exception!.Message, Does.Contain("CS0029"));
        Assert.That(exception.Message, Does.Contain("metadata-emission-control.cs"));
    }

    [Test]
    public void CompiledMethodShapesExposeOnlyTheirOwnedExpressionOperations()
    {
        const string source = "public static class ExpressionOperationControl { " +
            "public static int Block(int value) { return value + 1; } " +
            "public static int Expression(int value) => value + 1; }";
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12),
            "expression-operation-control.cs",
            cancellationToken: CancellationToken.None);
        var compilation = CSharpCompilation.Create("ExpressionOperationControl", [tree],
            TestMetadataReferences.SortedDistinctPlatform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var emission = GeneratedCompilationEmitter.Emit(
            compilation, CancellationToken.None);
        using var image = emission.Image;
        Assert.That(emission.Emit.Success, Is.True,
            string.Join(Environment.NewLine, emission.Emit.Diagnostics));
        Assert.That(image.CanRead, Is.True);
        Assert.That(image.Length, Is.GreaterThan(0));
        var model = compilation.GetSemanticModel(tree);
        Assert.That(model.SyntaxTree, Is.SameAs(tree));
        var methods = tree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .ToDictionary(method => method.Identifier.ValueText);
        Assert.That(methods.Count, Is.EqualTo(2));
        foreach (var method in methods.Values)
        {
            var symbol = model.GetDeclaredSymbol(method);
            Assert.That(symbol, Is.Not.Null);
            Assert.That(symbol!.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Int32));
            var declaration = symbol.DeclaringSyntaxReferences.Single();
            Assert.That(declaration.SyntaxTree, Is.SameAs(tree));
            Assert.That(declaration.Span, Is.EqualTo(method.Span));
        }
        var block = methods["Block"];
        Assert.That(block.ExpressionBody, Is.Null);
        Assert.That(FrontendDifferentialOracle.GetExpressionOperation(
            model, block.ExpressionBody?.Expression, CancellationToken.None), Is.Null);
        var expression = methods["Expression"].ExpressionBody!.Expression;
        var direct = model.GetOperation(expression, CancellationToken.None);
        Assert.That(direct, Is.Not.Null);
        Assert.That(direct!.Syntax.SyntaxTree, Is.SameAs(tree));
        var accessed = FrontendDifferentialOracle.GetExpressionOperation(
            model, expression, CancellationToken.None);
        Assert.That(accessed, Is.Not.Null);
        Assert.That(accessed!.Syntax, Is.SameAs(expression));
        Assert.That(accessed.Kind, Is.EqualTo(direct.Kind));
    }

    [Test]
    public void OmittedArraySizeSyntaxHasNoExpressionOperation()
    {
        const string source = "public static class OmittedSizeControl { " +
            "public static int[] Target() => new int[] { 1 }; }";
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12),
            "omitted-size-control.cs",
            cancellationToken: CancellationToken.None);
        var compilation = CSharpCompilation.Create("OmittedSizeControl", [tree],
            TestMetadataReferences.SortedDistinctPlatform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var emission = GeneratedCompilationEmitter.Emit(
            compilation, CancellationToken.None);
        using var image = emission.Image;
        Assert.That(emission.Emit.Success, Is.True,
            string.Join(Environment.NewLine, emission.Emit.Diagnostics));
        var model = compilation.GetSemanticModel(tree);
        Assert.That(model.SyntaxTree, Is.SameAs(tree));
        var method = tree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>().Single();
        var symbol = model.GetDeclaredSymbol(method);
        Assert.That(symbol, Is.Not.Null);
        Assert.That(symbol!.ReturnType.TypeKind, Is.EqualTo(TypeKind.Array));
        Assert.That(symbol.DeclaringSyntaxReferences.Single().SyntaxTree, Is.SameAs(tree));
        var creation = (ArrayCreationExpressionSyntax)method.ExpressionBody!.Expression;
        Assert.That(model.GetOperation(creation, CancellationToken.None), Is.Not.Null);
        var omitted = creation.Type.RankSpecifiers.Single().Sizes.Single();
        Assert.That(omitted, Is.TypeOf<OmittedArraySizeExpressionSyntax>());
        Assert.That(omitted.SyntaxTree, Is.SameAs(tree));
        Assert.That(model.GetOperation(omitted, CancellationToken.None), Is.Null);
        Assert.That(FrontendDifferentialOracle.GetExpressionOperation(
            model, omitted, CancellationToken.None), Is.Null);
    }

    [Test]
    public void OwnedParameterBindingAcceptsItsBooleanSlotAndRejectsASeventhSlot()
    {
        const string source = "public static class ParameterBindingControl { " +
            "public static bool Supported(long left, long right, bool condition, " +
            "string text, long[] values, object reference) => condition; " +
            "public static bool Extra(long left, long right, bool condition, " +
            "string text, long[] values, object reference, bool extra) => extra; }";
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12),
            "parameter-binding-control.cs",
            cancellationToken: CancellationToken.None);
        var compilation = CSharpCompilation.Create("ParameterBindingControl", [tree],
            TestMetadataReferences.SortedDistinctPlatform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var emission = GeneratedCompilationEmitter.Emit(
            compilation, CancellationToken.None);
        using var image = emission.Image;
        Assert.That(emission.Emit.Success, Is.True,
            string.Join(Environment.NewLine, emission.Emit.Diagnostics));
        var model = compilation.GetSemanticModel(tree);
        Assert.That(model.SyntaxTree, Is.SameAs(tree));
        var methods = tree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>().ToArray();
        Assert.That(methods.Select(method => method.Identifier.ValueText),
            Is.EquivalentTo(ParameterBindingMethodNames));
        var generated = new GeneratedCSharpCase(
            GeneratedCSharpExpression.Condition(), 0L, 0L, true);
        foreach (var method in methods)
        {
            var supported = method.Identifier.ValueText == "Supported";
            var symbol = model.GetDeclaredSymbol(method);
            Assert.That(symbol, Is.Not.Null);
            Assert.That(symbol!.Parameters.Length, Is.EqualTo(supported ? 6 : 7));
            Assert.That(symbol.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Boolean));
            Assert.That(symbol.DeclaringSyntaxReferences.Single().SyntaxTree, Is.SameAs(tree));
            var operation = model.GetOperation(method.ExpressionBody!.Expression,
                CancellationToken.None);
            Assert.That(operation, Is.Not.Null);
            Assert.That(operation!.Syntax.SyntaxTree, Is.SameAs(tree));
            var factory = new IrFactory();
            var lowering = new RoslynOperationLowerer(factory).Lower(operation);
            Assert.That(lowering.IsExact, Is.True);
            var binding = lowering.Variables.Single();
            Assert.That(binding.Symbol, Is.AssignableTo<IParameterSymbol>());
            var parameter = (IParameterSymbol)binding.Symbol;
            Assert.That(parameter.Ordinal, Is.EqualTo(supported ? 2 : 6));
            Assert.That(SymbolEqualityComparer.Default.Equals(
                parameter.ContainingSymbol, symbol), Is.True);
            Assert.That(factory.GetVariableInfo(binding.Variable).Type,
                Is.EqualTo(factory.BooleanType));
            if (supported)
            {
                var environment = FrontendDifferentialOracle.CreateEnvironment(
                    factory, symbol, lowering, generated);
                Assert.That(environment.Variables.Single().Key, Is.EqualTo(binding.Variable));
                Assert.That(environment.Variables.Single().Value.Boolean, Is.True);
                Assert.That(environment.SequenceOrigins, Is.Empty);
            }
            else
            {
                Action bind = () => FrontendDifferentialOracle.CreateEnvironment(
                    factory, symbol, lowering, generated);
                var exception = Assert.Throws<InvalidOperationException>(bind);
                Assert.That(exception!.Message,
                    Is.EqualTo("The generated method has an unexpected parameter."));
            }
        }
    }

    [Test]
    public void FrontendRejectsARealLoneSurrogateLiteralAtClosedSubsetAdmission()
    {
        var loneSurrogate = new string('\uD800', 1);
        var positive = new GeneratedCSharpCase(GeneratedCSharpExpression.Binary(
            GeneratedExpressionKind.Equal, GeneratedCSharpExpression.String("a"),
            GeneratedCSharpExpression.Text()), 0L, 0L, false)
        {
            Text = "a"
        };
        var negative = new GeneratedCSharpCase(GeneratedCSharpExpression.Binary(
            GeneratedExpressionKind.Equal, GeneratedCSharpExpression.String(loneSurrogate),
            GeneratedCSharpExpression.Text()), 0L, 0L, false)
        {
            Text = "a"
        };
        Assert.That(negative.Expression.Type, Is.EqualTo(GeneratedExpressionType.Boolean));
        var tree = CSharpSyntaxTree.ParseText(negative.Source,
            new CSharpParseOptions(LanguageVersion.CSharp12),
            "surrogate-admission-control.cs",
            cancellationToken: CancellationToken.None);
        var compilation = CSharpCompilation.Create("SurrogateAdmissionControl", [tree],
            TestMetadataReferences.SortedDistinctPlatform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                checkOverflow: true, nullableContextOptions: NullableContextOptions.Enable));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var emission = GeneratedCompilationEmitter.Emit(
            compilation, CancellationToken.None);
        using var image = emission.Image;
        Assert.That(emission.Emit.Success, Is.True,
            string.Join(Environment.NewLine, emission.Emit.Diagnostics));
        var model = compilation.GetSemanticModel(tree);
        Assert.That(model.SyntaxTree, Is.SameAs(tree));
        var method = tree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>().Single();
        var symbol = model.GetDeclaredSymbol(method);
        Assert.That(symbol, Is.Not.Null);
        Assert.That(symbol!.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Boolean));
        Assert.That(symbol.Parameters.Length, Is.EqualTo(6));
        Assert.That(symbol.DeclaringSyntaxReferences.Single().SyntaxTree, Is.SameAs(tree));
        var literal = method.ExpressionBody!.Expression.DescendantNodesAndSelf()
            .OfType<LiteralExpressionSyntax>()
            .Single(node => node.IsKind(SyntaxKind.StringLiteralExpression));
        var constant = model.GetConstantValue(literal, CancellationToken.None);
        Assert.That(constant.HasValue, Is.True);
        Assert.That(constant.Value, Is.EqualTo(loneSurrogate));
        Assert.That(((string)constant.Value!)[0], Is.EqualTo('\uD800'));
        var context = new AssemblyLoadContext("SurrogateAdmissionControl", isCollectible: true);
        try
        {
            image.Position = 0;
            var runtime = context.LoadFromStream(image).GetType("SharpProofGeneratedFrontend");
            Assert.That(runtime, Is.Not.Null);
            var target = runtime!.GetMethod("Target", BindingFlags.Public | BindingFlags.Static);
            Assert.That(target, Is.Not.Null);
            object?[] preservationArguments = [0L, 0L, false, loneSurrogate, null, null];
            Assert.That(target!.Invoke(null, preservationArguments), Is.True);
        }
        finally
        {
            context.Unload();
        }
        var operation = FrontendDifferentialOracle.GetExpressionOperation(
            model, method.ExpressionBody.Expression, CancellationToken.None);
        Assert.That(operation, Is.Not.Null);
        Assert.That(operation!.Syntax.SyntaxTree, Is.SameAs(tree));
        Assert.That(operation.ConstantValue.HasValue, Is.False);
        var lowering = new RoslynOperationLowerer(new IrFactory()).Lower(operation);
        Assert.That(lowering.IsExact, Is.False);
        Assert.That(lowering.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedType));
        Assert.That(lowering.Term, Is.TypeOf<IrOpaqueTerm>());
        var results = FrontendDifferentialOracle.CompareBatch([positive, negative], cancellationToken: CancellationToken.None);
        Assert.That(results.Length, Is.EqualTo(2));
        Assert.That(results[0].Status, Is.EqualTo(FuzzOracleStatus.Agreement), results[0].Detail);
        Assert.That(results[1].Status, Is.EqualTo(FuzzOracleStatus.Mismatch));
        Assert.That(results[1].Detail,
            Is.EqualTo("Generated supported C# closed the frontend subset: UnsupportedType."));
    }

    [Test]
    public void OwnedSemanticEdgeMethodShapesKeepTheirOperationFailureContract()
    {
        const string source = "public static class SharpProofGeneratedFrontendEdges { " +
            "public static bool EdgeTarget0(bool value) => value; " +
            "public static bool EdgeTarget1(bool value) { return value; } }";
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12),
            "semantic-edge-operation-control.cs",
            cancellationToken: CancellationToken.None);
        var compilation = CSharpCompilation.Create("SemanticEdgeOperationControl", [tree],
            TestMetadataReferences.SortedDistinctPlatform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var emission = GeneratedCompilationEmitter.Emit(
            compilation, CancellationToken.None);
        using var image = emission.Image;
        Assert.That(emission.Emit.Success, Is.True,
            string.Join(Environment.NewLine, emission.Emit.Diagnostics));
        Assert.That(image.CanRead, Is.True);
        Assert.That(image.Length, Is.GreaterThan(0));
        var model = compilation.GetSemanticModel(tree);
        Assert.That(model.SyntaxTree, Is.SameAs(tree));
        var methods = tree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .ToDictionary(method => method.Identifier.ValueText);
        Assert.That(methods.Count, Is.EqualTo(2));
        foreach (var method in methods.Values)
        {
            var symbol = model.GetDeclaredSymbol(method);
            Assert.That(symbol, Is.Not.Null);
            Assert.That(symbol!.Parameters.Length, Is.EqualTo(1));
            Assert.That(symbol.Parameters[0].Type.SpecialType, Is.EqualTo(SpecialType.System_Boolean));
            Assert.That(symbol.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Boolean));
            var declaration = symbol.DeclaringSyntaxReferences.Single();
            Assert.That(declaration.SyntaxTree, Is.SameAs(tree));
            Assert.That(declaration.Span, Is.EqualTo(method.Span));
        }
        var expression = methods["EdgeTarget0"];
        var block = methods["EdgeTarget1"];
        Assert.That(expression.ExpressionBody, Is.Not.Null);
        Assert.That(block.ExpressionBody, Is.Null);
        Assert.That(FrontendDifferentialOracle.GetExpressionOperation(
            model, expression.ExpressionBody!.Expression, CancellationToken.None), Is.Not.Null);
        Assert.That(FrontendDifferentialOracle.GetExpressionOperation(
            model, block.ExpressionBody?.Expression, CancellationToken.None), Is.Null);
        var context = new AssemblyLoadContext("SemanticEdgeOperationControl", isCollectible: true);
        try
        {
            image.Position = 0;
            var runtime = context.LoadFromStream(image).GetType("SharpProofGeneratedFrontendEdges");
            Assert.That(runtime, Is.Not.Null);
            Assert.That(runtime!.Assembly.GetName().Name, Is.EqualTo(compilation.AssemblyName));
            object?[] arguments = [true];
            foreach (var method in methods.Values)
            {
                var compiled = runtime.GetMethod(method.Identifier.ValueText,
                    BindingFlags.Public | BindingFlags.Static);
                Assert.That(compiled, Is.Not.Null);
                Assert.That(compiled!.Invoke(null, arguments), Is.True);
            }
            var generated = new FrontendSemanticEdgeCase("bool", "bool value", "value",
                arguments, FrontendSubsetDecision.Exact, FrontendAbstention.None);
            var positive = FrontendDifferentialOracle.CompareSemanticEdge(
                generated, expression, model, runtime, 0, CancellationToken.None);
            Assert.That(positive.Status, Is.EqualTo(FuzzOracleStatus.Agreement));
            Assert.That(positive.ActualDecision, Is.EqualTo(FrontendSubsetDecision.Exact));
            Assert.That(positive.ActualAbstention, Is.EqualTo(FrontendAbstention.None));
            Assert.That(positive.Detail, Is.Empty);
            Assert.That(positive.ExceptionKind, Is.Null);
            var negative = FrontendDifferentialOracle.CompareSemanticEdge(
                generated, block, model, runtime, 1, CancellationToken.None);
            Assert.That(negative.Status, Is.EqualTo(FuzzOracleStatus.Mismatch));
            Assert.That(negative.ActualDecision, Is.Null);
            Assert.That(negative.ActualAbstention, Is.Null);
            Assert.That(negative.Detail,
                Is.EqualTo("Roslyn did not expose the semantic-edge method operation."));
            Assert.That(negative.ExceptionKind, Is.Null);
        }
        finally
        {
            context.Unload();
        }
    }

    [TestCase(2)]
    [TestCase(258)]
    public async Task PartialOracleKeepsNativeAdmissionForRealLazyBooleanReference(int unaryCount)
    {
        var guardExpression = "guard || (" + new string('!', unaryCount) + "value)";
        var source = "public static class BooleanDepthReference { " +
            "public static bool Guard(bool guard, bool value) => " + guardExpression + "; " +
            "public static bool Reference(bool guard, bool value) { if (Guard(guard, value)) return value; " +
            "throw new System.DivideByZeroException(); } }";
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12), "BooleanDepthReference.cs");
        var compilation = CSharpCompilation.Create("BooleanDepthReference",
            [tree], TestMetadataReferences.SortedDistinctPlatform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var emission = GeneratedCompilationEmitter.Emit(compilation, CancellationToken.None);
        using var image = emission.Image;
        Assert.That(emission.Emit.Success, Is.True,
            string.Join(Environment.NewLine, emission.Emit.Diagnostics));
        Assert.That(image.CanRead, Is.True);
        Assert.That(image.Length, Is.GreaterThan(0));
        var model = compilation.GetSemanticModel(tree);
        var guardSyntax = (await tree.GetRootAsync()).DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Guard");
        var guardSymbol = model.GetDeclaredSymbol(guardSyntax);
        Assert.That(guardSymbol, Is.Not.Null);
        Assert.That(guardSymbol!.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Boolean));
        Assert.That(guardSymbol.Parameters.Length, Is.EqualTo(2));
        Assert.That(guardSymbol.Parameters[0].Type.SpecialType, Is.EqualTo(SpecialType.System_Boolean));
        Assert.That(guardSymbol.Parameters[1].Type.SpecialType, Is.EqualTo(SpecialType.System_Boolean));
        Assert.That(guardSymbol.DeclaringSyntaxReferences.Single().SyntaxTree, Is.SameAs(tree));
        Assert.That(guardSymbol.DeclaringSyntaxReferences.Single().Span, Is.EqualTo(guardSyntax.Span));
        var sourceOperators = guardSyntax.ExpressionBody!.Expression.DescendantNodesAndSelf()
            .OfType<PrefixUnaryExpressionSyntax>().ToArray();
        Assert.That(sourceOperators.Length, Is.EqualTo(unaryCount));
        Assert.That(sourceOperators.All(operation => operation.IsKind(SyntaxKind.LogicalNotExpression)), Is.True);
        Assert.That(model.GetOperation(guardSyntax.ExpressionBody.Expression), Is.Not.Null);

        var factory = new IrFactory(IrExecutionSemantics.Total);
        var variable = factory.CreateVariable("value", factory.BooleanType);
        var formula = factory.Variable(variable);
        var guardVariable = factory.CreateVariable("guard", factory.BooleanType);
        IrTerm completion = formula;
        for (var index = 0; index < unaryCount; index++)
        {
            completion = factory.Unary(IrUnaryOperator.Not, completion);
        }
        var observedDepth = 1;
        var cursor = completion;
        while (cursor is IrUnaryTerm unary)
        {
            Assert.That(unary.Operator, Is.EqualTo(IrUnaryOperator.Not));
            Assert.That(unary.Type, Is.EqualTo(factory.BooleanType));
            observedDepth++;
            cursor = unary.Operand;
        }
        Assert.That(cursor, Is.SameAs(formula));
        Assert.That(observedDepth, Is.EqualTo(unaryCount + 1));
        completion = factory.Binary(IrBinaryOperator.OrElse, factory.Variable(guardVariable), completion);
        observedDepth++;
        Assert.That(observedDepth, Is.EqualTo(unaryCount + 2));
        Assert.That(IrStructuralShrinker.StructuralSize(completion), Is.EqualTo(unaryCount + 3));
        var scenarios = ImmutableArray.CreateBuilder<ImmutableDictionary<ScopedIrId<IrVariableTag>, IrValue>>(3);
        var references = ImmutableArray.CreateBuilder<PartialTermSemanticOutcome>(3);
        bool[] inputs = unaryCount == 2 ? [true, false, false] : [true, false];
        bool[] guards = unaryCount == 2 ? [true, true, false] : [true, true];
        var context = new AssemblyLoadContext("BooleanDepthReferenceControl", isCollectible: true);
        try
        {
            image.Position = 0;
            var runtime = context.LoadFromStream(image).GetType("BooleanDepthReference");
            Assert.That(runtime, Is.Not.Null);
            Assert.That(runtime!.Assembly.GetName().Name, Is.EqualTo(compilation.AssemblyName));
            var guard = runtime.GetMethod("Guard", BindingFlags.Public | BindingFlags.Static);
            var reference = runtime.GetMethod("Reference", BindingFlags.Public | BindingFlags.Static);
            Assert.That(guard, Is.Not.Null);
            Assert.That(reference, Is.Not.Null);
            for (var index = 0; index < inputs.Length; index++)
            {
                var input = inputs[index];
                var guardInput = guards[index];
                object?[] arguments = [guardInput, input];
                var scenario = ImmutableDictionary<ScopedIrId<IrVariableTag>, IrValue>.Empty
                    .Add(variable, factory.CreateBooleanValue(input))
                    .Add(guardVariable, factory.CreateBooleanValue(guardInput));
                scenarios.Add(scenario);
                var runtimeCompletion = guard!.Invoke(null, arguments);
                Assert.That(runtimeCompletion, Is.EqualTo(guardInput || input));
                var evaluated = new IrInterpreter(factory).Evaluate(completion, scenario);
                Assert.That(evaluated.Status, Is.EqualTo(IrEvaluationStatus.Value));
                Assert.That(evaluated.Value!.Kind, Is.EqualTo(IrValueKind.Boolean));
                Assert.That(evaluated.Value!.Boolean, Is.EqualTo(runtimeCompletion));
                if (guardInput)
                {
                    var returned = reference!.Invoke(null, arguments);
                    Assert.That(returned, Is.EqualTo(input));
                    var interpreted = new IrInterpreter(factory).Evaluate(formula, scenario);
                    Assert.That(interpreted.Status, Is.EqualTo(IrEvaluationStatus.Value));
                    Assert.That(interpreted.Value!.Boolean, Is.EqualTo(returned));
                    references.Add((bool)returned!
                        ? PartialTermSemanticOutcome.DefinedTrue : PartialTermSemanticOutcome.DefinedFalse);
                }
                else
                {
                    var thrown = Assert.Throws<TargetInvocationException>((Action)(() => reference!.Invoke(null, arguments)));
                    Assert.That(thrown!.InnerException, Is.TypeOf<DivideByZeroException>());
                    references.Add(PartialTermSemanticOutcome.Undefined);
                }
            }
        }
        finally
        {
            context.Unload();
        }
        using var native = FuzzSmtSession.Create(factory);
        var valueAssumption = new Assumption(factory,
            factory.Binary(IrBinaryOperator.Equal, formula, factory.Boolean(true)),
            new LoweredJustification(factory.CreateOperation("boolean-depth-reference-input")));
        var guardAssumption = new Assumption(factory,
            factory.Binary(IrBinaryOperator.Equal, factory.Variable(guardVariable), factory.Boolean(true)),
            new LoweredJustification(factory.CreateOperation("boolean-depth-reference-guard")));
        var proof = await new ProofKernel(native).VerifyAsync(new VerificationQuery(factory,
            [valueAssumption, guardAssumption], new Goal(factory, completion,
                ProofDiagnosticKind.InternalConsistency, new SourceLocationId(0))), cancellationToken: CancellationToken.None);
        if (unaryCount == 2)
        {
            Assert.That(proof, Is.TypeOf<ProvenOutcome>());
        }
        else
        {
            Assert.That(observedDepth, Is.EqualTo(260));
            Assert.That(proof, Is.TypeOf<UnknownOutcome>());
            Assert.That(((UnknownOutcome)proof).Reason, Is.EqualTo(AbstentionReason.UnsupportedEncoding));
        }
        var generated = new PartialTermSmtCase(formula, completion,
            scenarios.ToImmutable(), references.ToImmutable());
        var result = await PartialTermSmtDifferentialOracle.CompareAsync(factory, generated, cancellationToken: CancellationToken.None);
        Assert.That(result.DefinedTrueCount, Is.EqualTo(1));

        if (unaryCount == 2)
        {
            Assert.That(result.Status, Is.EqualTo(FuzzOracleStatus.Agreement));
            Assert.That(result.ScenarioCount, Is.EqualTo(3));
            Assert.That(result.DefinedFalseCount, Is.EqualTo(1));
            Assert.That(result.UndefinedCount, Is.EqualTo(1));
            Assert.That(result.Detail, Is.Empty);
        }
        else
        {
            Assert.That(result.Status, Is.EqualTo(FuzzOracleStatus.Abstained));
            Assert.That(result.ScenarioCount, Is.EqualTo(1));
            Assert.That(result.UndefinedCount, Is.Zero);
            Assert.That(result.DefinedFalseCount, Is.Zero);
            Assert.That(result.Detail,
                Is.EqualTo("The backend returned Unknown(UnsupportedEncoding) for partial-term scenario 0."));
            var retained = await SmtFailureRetention.RetainPartialTermAsync(
                factory, generated, 8, 12346, cancellationToken: CancellationToken.None);
            var printed = new IrPrinter(factory).Print(generated.Formula);
            Assert.That(retained, Is.EqualTo(new FuzzFailure(8, 12346,
                "partial-term-smt", printed, printed, result.Detail)));
            var replay = await PartialTermSmtDifferentialOracle.CompareAsync(factory, generated, cancellationToken: CancellationToken.None);
            Assert.That(replay, Is.EqualTo(result));
        }
    }

    private static CSharpCompilation CreateValidCompilation()
    {
        const string source = "public static class EmittedControl { " +
            "public static bool Target(bool value) => !value; }";
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12),
            cancellationToken: CancellationToken.None);
        var compilation = CSharpCompilation.Create("FuzzEmissionControl", [tree],
            TestMetadataReferences.SortedDistinctPlatform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                checkOverflow: true, nullableContextOptions: NullableContextOptions.Enable));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        return compilation;
    }
}
