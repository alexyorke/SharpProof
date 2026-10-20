using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Frontend.Host;

namespace SharpProof.Frontend;

// Recognition is not a resource proof. A consumer must separately authenticate
// runtime evidence, lower every child, and establish the chosen completion model.
internal sealed class ResourceForkJoinPlan
{
    internal const int MaximumChildren = 16;
    internal IInvocationOperation Join { get; }
    internal ImmutableArray<IInvocationOperation> Children { get; }
    internal bool ConfiguredAwait { get; }

    private ResourceForkJoinPlan(IInvocationOperation join, ImmutableArray<IInvocationOperation> children, bool configuredAwait)
    {
        Join = RepositoryParameterGuard.NotNull(join, nameof(join));
        RepositoryParameterGuard.RequireInitialized(children.IsDefault, nameof(children), "Array must be initialized.");
        Children = children;
        ConfiguredAwait = configuredAwait;
    }

    internal static ResourceForkJoinPlan? TryCreate(IMethodSymbol method, Compilation? compilation, CancellationToken cancellationToken)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        cancellationToken.ThrowIfCancellationRequested();
        if (compilation == null || method is not { IsStatic: true, IsGenericMethod: false, MethodKind: MethodKind.Ordinary } ||
            method.IsExtern || method.IsAbstract || method.ContainingType.IsGenericType ||
            !method.ContainingType.StaticConstructors.IsEmpty || method.DeclaringSyntaxReferences.Length != 1 ||
            !ScalarParameters(method) || method.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) is not MethodDeclarationSyntax declaration ||
            !compilation.ContainsSyntaxTree(declaration.SyntaxTree))
        { return null; }
        var task = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task");
        var action = compilation.GetTypeByMetadataName("System.Action");
        var core = compilation.GetSpecialType(SpecialType.System_Object).ContainingAssembly;
        if (task == null || action == null || !SymbolEqualityComparer.Default.Equals(task.ContainingAssembly, core) ||
            !SymbolEqualityComparer.Default.Equals(action.ContainingAssembly, core) ||
            !SymbolEqualityComparer.Default.Equals(method.ReturnType, task))
        { return null; }
        var model = CompilationModelProvider.GetSemanticModel(compilation, declaration.SyntaxTree);
        IOperation? result = declaration.Body is { Statements.Count: 1 } body && body.Statements[0] is ReturnStatementSyntax statement
            ? (model.GetOperation(statement, cancellationToken) as IReturnOperation)?.ReturnedValue
            : declaration.Body == null && declaration.ExpressionBody is { } arrow ? model.GetOperation(arrow.Expression, cancellationToken) : null;
        if (method.IsAsync)
        {
            if (method.GetAttributes().Any(static attribute => attribute is null ? throw new ArgumentNullException(nameof(attribute)) :
                attribute.AttributeClass?.Name == "AsyncMethodBuilderAttribute"))
            { return null; }
            result = declaration.Body is { Statements.Count: 1 } asyncBody && asyncBody.Statements[0] is ExpressionStatementSyntax expression
                ? model.GetOperation(expression.Expression, cancellationToken) : result;
            var configuredType = compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.ConfiguredTaskAwaitable");
            if (configuredType == null || !SymbolEqualityComparer.Default.Equals(configuredType.ContainingAssembly, core) ||
                result is not IAwaitOperation { Operation: IInvocationOperation { Instance: { } awaited, Arguments.Length: 1 } configure } ||
                configure.TargetMethod is not { IsStatic: false, IsGenericMethod: false, Name: "ConfigureAwait", Parameters.Length: 1 } configureMethod ||
                !SymbolEqualityComparer.Default.Equals(configureMethod.ContainingType, task) ||
                !SymbolEqualityComparer.Default.Equals(configureMethod.ReturnType, configuredType) ||
                configureMethod.Parameters[0].Type.SpecialType != SpecialType.System_Boolean ||
                configure.Arguments[0].Value.ConstantValue is not { HasValue: true, Value: false })
            { return null; }
            result = awaited;
        }
        if (result is not IInvocationOperation { Instance: null, Arguments.Length: 1 } join ||
            !FrameworkMethod(join.TargetMethod, task, "WhenAll") || join.TargetMethod.Parameters.Length != 1 ||
            !join.TargetMethod.Parameters[0].IsParams || join.TargetMethod.Parameters[0].Type is not IArrayTypeSymbol { Rank: 1 } arrayType ||
            !SymbolEqualityComparer.Default.Equals(arrayType.ElementType, task) ||
            join.Arguments[0].Value is not IArrayCreationOperation { Initializer: { } initializer } ||
            initializer.ElementValues.Length is < 2 or > MaximumChildren)
        { return null; }
        var children = ImmutableArray.CreateBuilder<IInvocationOperation>(initializer.ElementValues.Length);
        foreach (var item in initializer.ElementValues)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item is not IInvocationOperation { Instance: null, Arguments.Length: 1 } run ||
                !FrameworkMethod(run.TargetMethod, task, "Run") || run.TargetMethod.Parameters.Length != 1 ||
                !SymbolEqualityComparer.Default.Equals(run.TargetMethod.Parameters[0].Type, action) ||
                run.Arguments[0].Value is not IDelegateCreationOperation { Target: IAnonymousFunctionOperation lambda } ||
                lambda.Symbol.IsAsync || !lambda.Symbol.Parameters.IsEmpty || ChildInvocation(lambda.Body) is not { } child ||
                !SourceChild(child, method, compilation, cancellationToken))
            { return null; }
            children.Add(child);
        }
        return new ResourceForkJoinPlan(join, children.MoveToImmutable(), method.IsAsync);
    }

    private static bool FrameworkMethod(IMethodSymbol method, INamedTypeSymbol task, string name)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        task = RepositoryParameterGuard.NotNull(task, nameof(task));
        name = RepositoryParameterGuard.NotNull(name, nameof(name));
        return method.Name == name && method.IsStatic && !method.IsGenericMethod && method.MethodKind == MethodKind.Ordinary &&
            SymbolEqualityComparer.Default.Equals(method.ContainingType, task) && SymbolEqualityComparer.Default.Equals(method.ReturnType, task);
    }

    private static bool ScalarParameters(IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        foreach (var parameter in method.Parameters)
        {
            if (parameter.RefKind != RefKind.None || parameter.Type.SpecialType != SpecialType.System_Int32)
            { return false; }
        }
        return true;
    }

    private static IInvocationOperation? ChildInvocation(IBlockOperation body)
    {
        body = RepositoryParameterGuard.NotNull(body, nameof(body));
        IInvocationOperation? invocation = null;
        foreach (var operation in body.Operations)
        {
            if (operation is IReturnOperation { IsImplicit: true, ReturnedValue: null })
            { continue; }
            if (invocation != null || operation is not IExpressionStatementOperation { Operation: IInvocationOperation child })
            { return null; }
            invocation = child;
        }
        return invocation;
    }

    private static bool SourceChild(IInvocationOperation invocation, IMethodSymbol root, Compilation compilation, CancellationToken cancellationToken)
    {
        invocation = RepositoryParameterGuard.NotNull(invocation, nameof(invocation));
        root = RepositoryParameterGuard.NotNull(root, nameof(root));
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        var target = invocation.TargetMethod;
        if (invocation.Instance != null || target is not { IsStatic: true, IsAsync: false, IsGenericMethod: false, ReturnsVoid: true, MethodKind: MethodKind.Ordinary } ||
            target.IsExtern || target.IsAbstract || target.IsConditional || target.ContainingType.IsGenericType ||
            !target.ContainingType.StaticConstructors.IsEmpty || !ScalarParameters(target) ||
            !SymbolEqualityComparer.Default.Equals(target.ContainingAssembly, root.ContainingAssembly) || target.DeclaringSyntaxReferences.Length != 1 ||
            target.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) is not MethodDeclarationSyntax declaration ||
            !compilation.ContainsSyntaxTree(declaration.SyntaxTree) || declaration.Body == null && declaration.ExpressionBody == null)
        { return false; }
        foreach (var argument in invocation.Arguments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (argument.Parameter == null || argument.Parameter.RefKind != RefKind.None || argument.ArgumentKind != ArgumentKind.Explicit)
            { return false; }
            if (argument.Value is IParameterReferenceOperation parameter && SymbolEqualityComparer.Default.Equals(parameter.Parameter.ContainingSymbol, root))
            { continue; }
            if (argument.Value.Type?.SpecialType != SpecialType.System_Int32 || argument.Value.ConstantValue is not { HasValue: true, Value: int })
            { return false; }
        }
        return true;
    }
}
