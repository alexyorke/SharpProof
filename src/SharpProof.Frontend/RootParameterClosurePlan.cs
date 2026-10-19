using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Frontend.Host;

namespace SharpProof.Frontend;

// One root frame, one returned delegate, no intermediate capture scopes.
// Broader bodies must model their actual frame placement instead of reusing this rule.
internal sealed class RootParameterClosurePlan
{
    internal IMethodSymbol Lambda { get; }
    internal ImmutableArray<ResourceStorageKind> Fields { get; }

    private RootParameterClosurePlan(IMethodSymbol lambda, ImmutableArray<ResourceStorageKind> fields)
    {
        Lambda = RepositoryParameterGuard.NotNull(lambda, nameof(lambda));
        RepositoryParameterGuard.RequireInitialized(fields.IsDefault, nameof(fields), "Array must be initialized.");
        Fields = fields;
    }

    internal bool Matches(IDelegateCreationOperation creation)
    {
        creation = RepositoryParameterGuard.NotNull(creation, nameof(creation));
        var symbol = creation.Target switch
        {
            IAnonymousFunctionOperation anonymous => anonymous.Symbol,
            IFlowAnonymousFunctionOperation anonymous => anonymous.Symbol,
            _ => null
        };
        return SymbolEqualityComparer.Default.Equals(Lambda, symbol);
    }

    internal static RootParameterClosurePlan? TryCreate(IMethodSymbol method, Compilation? compilation, CancellationToken cancellationToken)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        cancellationToken.ThrowIfCancellationRequested();
        if (compilation == null || method is not { IsStatic: true, IsAsync: false, IsGenericMethod: false, MethodKind: MethodKind.Ordinary } ||
            method.ContainingType.IsGenericType || method.ReturnType.TypeKind != TypeKind.Delegate ||
            method.Parameters.Any(static parameter => parameter is null ? throw new ArgumentNullException(nameof(parameter)) : parameter.RefKind != RefKind.None) ||
            method.DeclaringSyntaxReferences.Length != 1 ||
            method.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) is not MethodDeclarationSyntax declaration ||
            !compilation.ContainsSyntaxTree(declaration.SyntaxTree))
        { return null; }
        var model = CompilationModelProvider.GetSemanticModel(compilation, declaration.SyntaxTree);
        IOperation? value = declaration.Body is { Statements.Count: 1 } body && body.Statements[0] is ReturnStatementSyntax statement
            ? (model.GetOperation(statement, cancellationToken) as IReturnOperation)?.ReturnedValue
            : declaration.ExpressionBody is { } arrow ? model.GetOperation(arrow.Expression, cancellationToken) : null;
        var anonymous = value is IDelegateCreationOperation { Target: IAnonymousFunctionOperation lambda } ? lambda : value as IAnonymousFunctionOperation;
        if (anonymous == null || anonymous.Symbol.IsAsync)
        { return null; }
        var remaining = 4096;
        foreach (var operation in anonymous.Body.Descendants())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (--remaining < 0 || operation is IAnonymousFunctionOperation or ILocalFunctionOperation)
            { return null; }
        }
        if (anonymous.Syntax is not ExpressionSyntax expression || model.AnalyzeDataFlow(expression) is not { Succeeded: true } flow ||
            flow.CapturedInside.IsEmpty || flow.CapturedInside.Length > ResourceAllocationShape.MaximumFields)
        { return null; }
        var parameters = ImmutableArray.CreateBuilder<IParameterSymbol>();
        foreach (var capture in flow.CapturedInside)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (capture is not IParameterSymbol parameter || !SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, method))
            { return null; }
            parameters.Add(parameter);
        }
        var fields = ImmutableArray.CreateBuilder<ResourceStorageKind>();
        foreach (var parameter in parameters.OrderBy(static parameter => parameter is null ? throw new ArgumentNullException(nameof(parameter)) : parameter.Ordinal))
        {
            var storage = parameter.Type.IsReferenceType ? ResourceStorageKind.Reference : parameter.Type.SpecialType switch
            {
                SpecialType.System_Boolean or SpecialType.System_Byte or SpecialType.System_SByte => ResourceStorageKind.Byte,
                SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Char => ResourceStorageKind.Word16,
                SpecialType.System_Int32 or SpecialType.System_UInt32 => ResourceStorageKind.Word32,
                SpecialType.System_Int64 or SpecialType.System_UInt64 => ResourceStorageKind.Word64,
                _ => ResourceStorageKind.Unspecified
            };
            if (storage == ResourceStorageKind.Unspecified)
            { return null; }
            fields.Add(storage);
        }
        return new(anonymous.Symbol, fields.ToImmutable());
    }
}
