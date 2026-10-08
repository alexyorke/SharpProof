using System.Globalization;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
[NonParallelizable]
public sealed class FieldInitializerNestedCallableIntrinsicBoundaryAuditTests
{
    private const string LambdaResultSource = """
        using SharpProof.Attributes;
        public sealed class Subject {
            public System.Func<int> Value = () => Contract.Result<int>();
        }
        """;

    private const string LocalOldSource = """
        using SharpProof.Attributes;
        public sealed class Subject {
            public System.Func<int> Value = () => {
                int Read() => Contract.Old(1);
                return Read();
            };
        }
        """;

    private const string ValidOwnContractsSource = """
        using SharpProof.Attributes;
        public sealed class Subject {
            public System.Func<int> Value = () => {
                Contract.Ensures(Contract.Result<int>() == 1);
                Contract.Ensures(Contract.Old(1) == 1);
                int Read() {
                    Contract.Ensures(Contract.Result<int>() == 1);
                    Contract.Ensures(Contract.Old(1) == 1);
                    return 1;
                }
                return Read();
            };
        }
        """;

    [Test]
    public async Task ResultInFieldInitializerLambdaReportsOwnPlacement()
    {
        await AssertMisuseAsync(
            LambdaResultSource,
            "nested-field-lambda-result.cs",
            MethodKind.AnonymousFunction,
            "Result",
            "[101..123)",
            "SP0024|Error|nested-field-lambda-result.cs|[101..123)|SharpProof contract 'Contract.Result' has invalid argument '<placement>': expected use inside Contract.Ensures",
            "Contract.Result<T>() is valid only inside Contract.Ensures(...).");
    }

    [Test]
    public async Task OldInFieldInitializerLocalFunctionReportsOwnPlacement()
    {
        await AssertMisuseAsync(
            LocalOldSource,
            "nested-field-local-old.cs",
            MethodKind.LocalFunction,
            "Old",
            "[125..140)",
            "SP0024|Error|nested-field-local-old.cs|[125..140)|SharpProof contract 'Contract.Old' has invalid argument '<placement>': expected use inside Contract.Ensures",
            "Contract.Old(...) is valid only inside Contract.Ensures(...).");
    }

    [Test]
    public async Task InitializerLambdaAndLocalFunctionKeepTheirOwnValidEnsures()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            ValidOwnContractsSource, [], filePath: "nested-field-valid-contracts.cs");
        AssertNoCompilationErrors(compilation);
        var tree = compilation.SyntaxTrees.Single();
        var root = await tree.GetRootAsync();
        var model = compilation.GetSemanticModel(tree);
        var invocations = GetIntrinsicInvocations(root, model, compilation);
        Assert.That(invocations, Has.Length.EqualTo(4));
        var owners = new List<IMethodSymbol>();
        foreach (var invocation in invocations)
        {
            var operation = GetInvocationFromFieldInitializer(invocation, model);
            var owner = AssertIntrinsicCallableAndFieldOwner(
                invocation, operation!, model, compilation);
            owners.Add(owner);
            Assert.That(GetOwnEnsuresContexts(operation!, owner, model, compilation),
                Has.Length.EqualTo(1));
            var callable = GetNearestCallable(operation!);
            var body = callable switch
            {
                IAnonymousFunctionOperation anonymous => anonymous.Body,
                ILocalFunctionOperation local => local.Body,
                _ => null
            };
            Assert.That(body, Is.Not.Null);
            AssertDirectEnsuresPrologue(body!, owner, model, compilation);
        }
        using (Assert.EnterMultipleScope())
        {
            Assert.That(owners.Count(static owner =>
                owner.MethodKind == MethodKind.AnonymousFunction), Is.EqualTo(2));
            Assert.That(owners.Count(static owner =>
                owner.MethodKind == MethodKind.LocalFunction), Is.EqualTo(2));
            Assert.That(owners.Distinct<IMethodSymbol>(
                SymbolEqualityComparer.Default).Count(), Is.EqualTo(2));
        }

        var image = AnalyzerTestHost.EmitImage(compilation);
        var runtime = new AssemblyLoadContext("NestedFieldValidContracts", isCollectible: true);
        try
        {
            using var stream = new MemoryStream(image);
            var subject = runtime.LoadFromStream(stream).GetType("Subject")!;
            var instance = Activator.CreateInstance(subject);
            Assert.That(instance, Is.Not.Null);
            var field = subject.GetField("Value")!.GetValue(instance);
            Assert.That(field, Is.TypeOf<Func<int>>());
            Assert.That(((Func<int>)field!)(), Is.EqualTo(1));
        }
        finally
        {
            runtime.Unload();
        }
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, "contracts");
        var actual = DescribeDiagnostics(diagnostics);
        await TestContext.Out.WriteLineAsync(
            "Valid control: four real intrinsics have their own anonymous/local callable " +
            "and direct Ensures prologues; same-compilation CLR construction and delegate " +
            "invocation returned 1; complete diagnostics=[" + actual + "]");
        Assert.That(actual, Is.Empty);
    }

    private static async Task AssertMisuseAsync(
        string source,
        string sourcePath,
        MethodKind expectedOwnerKind,
        string expectedIntrinsicName,
        string expectedSpan,
        string expectedDiagnostics,
        string expectedRuntimeMessage)
    {
        var compilation = AnalyzerTestHost.CreateCompilation(source, [], filePath: sourcePath);
        AssertNoCompilationErrors(compilation);
        var tree = compilation.SyntaxTrees.Single();
        var root = await tree.GetRootAsync();
        var model = compilation.GetSemanticModel(tree);
        var invocation = GetIntrinsicInvocations(root, model, compilation).Single();
        var operation = GetInvocationFromFieldInitializer(invocation, model);
        var owner = AssertIntrinsicCallableAndFieldOwner(
            invocation, operation!, model, compilation);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(owner.MethodKind, Is.EqualTo(expectedOwnerKind));
            Assert.That(operation!.TargetMethod.Name, Is.EqualTo(expectedIntrinsicName));
            Assert.That(invocation.Span.ToString(), Is.EqualTo(expectedSpan));
            Assert.That(GetOwnEnsuresContexts(operation, owner, model, compilation), Is.Empty);
        }

        var image = AnalyzerTestHost.EmitImage(compilation);
        var runtime = new AssemblyLoadContext(
            "NestedFieldMisuse-" + expectedIntrinsicName, isCollectible: true);
        string runtimeFailure;
        try
        {
            using var stream = new MemoryStream(image);
            var subject = runtime.LoadFromStream(stream).GetType("Subject")!;
            var instance = Activator.CreateInstance(subject);
            Assert.That(instance, Is.Not.Null);
            var field = subject.GetField("Value")!.GetValue(instance);
            Assert.That(field, Is.TypeOf<Func<int>>());
            var value = (Func<int>)field!;
            var exception = Assert.Throws<InvalidOperationException>((Action)(() =>
            {
                _ = value();
            }))!;
            runtimeFailure = exception.Message;
            Assert.That(runtimeFailure, Is.EqualTo(expectedRuntimeMessage));
        }
        finally
        {
            runtime.Unload();
        }
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, "contracts");
        var actual = DescribeDiagnostics(diagnostics);
        var observation = "Compiler emit and construction succeeded; typed CLR delegate " +
            "invocation threw exact InvalidOperationException: " + runtimeFailure + "\n" +
            $"root=FieldInitializer;field=Subject.Value;callable={owner};" +
            $"callableKind={owner.MethodKind};returnType={owner.ReturnType};" +
            $"apiAssembly={operation!.TargetMethod.ContainingAssembly.Identity};" +
            $"tree={tree.FilePath};span={invocation.Span};ownEnsuresContexts=0\n" +
            "Expected complete diagnostics: " + expectedDiagnostics + "\n" +
            "Actual complete diagnostics: [" + actual + "]";
        await TestContext.Out.WriteLineAsync(observation);
        Assert.That(actual, Is.EqualTo(expectedDiagnostics), observation);
    }

    private static InvocationExpressionSyntax[] GetIntrinsicInvocations(
        SyntaxNode root, SemanticModel model, CSharpCompilation compilation)
    {
        var api = compilation.GetTypeByMetadataName("SharpProof.Attributes.Contract");
        Assert.That(api, Is.Not.Null);
        return root.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(invocation => model.GetSymbolInfo(invocation).Symbol is IMethodSymbol method &&
                (method.Name == "Result" || method.Name == "Old") &&
                SymbolEqualityComparer.Default.Equals(method.ContainingType, api))
            .ToArray();
    }

    private static IInvocationOperation GetInvocationFromFieldInitializer(
        InvocationExpressionSyntax invocation, SemanticModel model)
    {
        var fieldDeclaration = invocation.Ancestors().OfType<VariableDeclaratorSyntax>()
            .Single(variable => variable.Parent?.Parent is FieldDeclarationSyntax);
        var initializer = model.GetOperation(fieldDeclaration.Initializer!) as
            IFieldInitializerOperation;
        Assert.That(initializer, Is.Not.Null);
        return initializer!.DescendantsAndSelf().OfType<IInvocationOperation>()
            .Single(candidate => candidate.Syntax.SyntaxTree == invocation.SyntaxTree &&
                candidate.Syntax.Span == invocation.Span);
    }

    private static IMethodSymbol AssertIntrinsicCallableAndFieldOwner(
        InvocationExpressionSyntax invocation,
        IInvocationOperation operation,
        SemanticModel model,
        CSharpCompilation compilation)
    {
        var owner = model.GetEnclosingSymbol(invocation.SpanStart) as IMethodSymbol;
        Assert.That(owner, Is.Not.Null);
        var callable = GetNearestCallable(operation);
        var callableOwner = callable switch
        {
            IAnonymousFunctionOperation anonymous => anonymous.Symbol,
            ILocalFunctionOperation local => local.Symbol,
            _ => null
        };
        var fieldDeclaration = invocation.Ancestors().OfType<VariableDeclaratorSyntax>()
            .Single(variable => variable.Parent?.Parent is FieldDeclarationSyntax);
        var field = model.GetDeclaredSymbol(fieldDeclaration) as IFieldSymbol;
        IOperation operationRoot = operation;
        while (operationRoot.Parent != null)
        {
            operationRoot = operationRoot.Parent;
        }
        var initializer = operationRoot as IFieldInitializerOperation;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(field, Is.Not.Null);
            Assert.That(field!.IsImplicitlyDeclared, Is.False);
            Assert.That(owner!.MethodKind, Is.AnyOf(
                MethodKind.AnonymousFunction, MethodKind.LocalFunction));
            Assert.That(owner.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Int32));
            Assert.That(SymbolEqualityComparer.Default.Equals(owner, callableOwner), Is.True);
            Assert.That(SymbolEqualityComparer.Default.Equals(
                operation.TargetMethod.ContainingType,
                compilation.GetTypeByMetadataName("SharpProof.Attributes.Contract")), Is.True);
            Assert.That(initializer, Is.Not.Null);
        }
        Assert.That(initializer!.InitializedFields, Has.Length.EqualTo(1));
        Assert.That(SymbolEqualityComparer.Default.Equals(
            initializer.InitializedFields.Single(), field), Is.True);
        return owner!;
    }

    private static IOperation GetNearestCallable(IOperation operation)
    {
        var current = operation.Parent;
        while (current != null &&
               current is not (IAnonymousFunctionOperation or ILocalFunctionOperation))
        {
            current = current.Parent;
        }
        Assert.That(current, Is.Not.Null);
        return current!;
    }

    private static IInvocationOperation[] GetOwnEnsuresContexts(
        IOperation operation,
        IMethodSymbol owner,
        SemanticModel model,
        CSharpCompilation compilation)
    {
        var contexts = new List<IInvocationOperation>();
        var api = compilation.GetTypeByMetadataName("SharpProof.Attributes.Contract");
        for (var current = operation.Parent; current != null; current = current.Parent)
        {
            if (current is IAnonymousFunctionOperation or ILocalFunctionOperation)
            {
                break;
            }
            if (current is IInvocationOperation invocation &&
                invocation.TargetMethod.Name == "Ensures" &&
                SymbolEqualityComparer.Default.Equals(invocation.TargetMethod.ContainingType, api) &&
                SymbolEqualityComparer.Default.Equals(
                    model.GetEnclosingSymbol(invocation.Syntax.SpanStart), owner))
            {
                contexts.Add(invocation);
            }
        }
        return contexts.ToArray();
    }

    private static void AssertDirectEnsuresPrologue(
        IBlockOperation body,
        IMethodSymbol owner,
        SemanticModel model,
        CSharpCompilation compilation)
    {
        Assert.That(body.Operations.Length, Is.GreaterThanOrEqualTo(2));
        for (var index = 0; index < 2; index++)
        {
            var statement = body.Operations[index] as IExpressionStatementOperation;
            var clause = statement?.Operation as IInvocationOperation;
            Assert.That(clause, Is.Not.Null);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(clause!.TargetMethod.Name, Is.EqualTo("Ensures"));
                Assert.That(SymbolEqualityComparer.Default.Equals(
                    clause.TargetMethod.ContainingType,
                    compilation.GetTypeByMetadataName("SharpProof.Attributes.Contract")), Is.True);
                Assert.That(SymbolEqualityComparer.Default.Equals(
                    model.GetEnclosingSymbol(clause.Syntax.SpanStart), owner), Is.True);
            }
        }
    }

    private static void AssertNoCompilationErrors(CSharpCompilation compilation)
    {
        Assert.That(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        Assert.That(((CSharpParseOptions)compilation.SyntaxTrees.Single().Options)
            .PreprocessorSymbolNames, Does.Not.Contain("SHARPPROOF_CONTRACTS"));
    }

    private static string DescribeDiagnostics(IEnumerable<Diagnostic> diagnostics)
    {
        return string.Join("\n", diagnostics.Select(diagnostic =>
            $"{diagnostic.Id}|{diagnostic.Severity}|{diagnostic.Location.SourceTree?.FilePath}|" +
            $"{diagnostic.Location.SourceSpan}|" +
            diagnostic.GetMessage(CultureInfo.InvariantCulture)));
    }
}
