using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Frontend.Host;

namespace SharpProof.Frontend;

// One root frame and one immediately returned delegate. Nested deferred delegates
// may capture that same read-only root storage; intermediate capture scopes are rejected.
// Broader bodies must model their actual frame placement instead of reusing this rule.
internal sealed class RootParameterClosurePlan
{
    internal const int MaximumNestedFunctions = 16;
    internal IMethodSymbol Lambda { get; }
    internal ImmutableArray<ResourceStorageKind> Fields { get; }
    internal ImmutableArray<ISymbol> Captures { get; }
    internal bool HasLocals => Captures.Any(static capture => capture is null ? throw new ArgumentNullException(nameof(capture)) : capture is ILocalSymbol);

    private RootParameterClosurePlan(IMethodSymbol lambda, ImmutableArray<ResourceStorageKind> fields, ImmutableArray<ISymbol> captures)
    {
        Lambda = RepositoryParameterGuard.NotNull(lambda, nameof(lambda));
        RepositoryParameterGuard.RequireInitialized(fields.IsDefault, nameof(fields), "Array must be initialized.");
        Fields = fields;
        RepositoryParameterGuard.RequireInitialized(captures.IsDefault, nameof(captures), "Array must be initialized.");
        Captures = captures;
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
        var locals = new List<ILocalSymbol>();
        if (declaration.Body is { } block)
        {
            if (block.Statements.Count > 4096)
            { return null; }
            foreach (var prelude in block.Statements.Take(block.Statements.Count - 1))
            {
                if (prelude is not LocalDeclarationStatementSyntax { UsingKeyword.RawKind: 0, AwaitKeyword.RawKind: 0 } localDeclaration)
                { return null; }
                foreach (var variable in localDeclaration.Declaration.Variables)
                {
                    if (variable.Initializer == null || model.GetDeclaredSymbol(variable, cancellationToken) is not ILocalSymbol { IsConst: false, RefKind: RefKind.None } local)
                    { return null; }
                    locals.Add(local);
                    if (locals.Count > ResourceAllocationShape.MaximumFields)
                    { return null; }
                }
            }
        }
        IOperation? value = declaration.Body is { Statements.Count: > 0 } body && body.Statements[body.Statements.Count - 1] is ReturnStatementSyntax statement
            ? (model.GetOperation(statement, cancellationToken) as IReturnOperation)?.ReturnedValue
            : declaration.ExpressionBody is { } arrow ? model.GetOperation(arrow.Expression, cancellationToken) : null;
        var anonymous = value is IDelegateCreationOperation { Target: IAnonymousFunctionOperation lambda } ? lambda : value as IAnonymousFunctionOperation;
        if (anonymous == null || anonymous.Symbol.IsAsync)
        { return null; }
        var remaining = 4096;
        var functions = new List<IAnonymousFunctionOperation> { anonymous };
        foreach (var operation in anonymous.Body.Descendants())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (--remaining < 0 || operation is ILocalFunctionOperation)
            { return null; }
            if (operation is IAnonymousFunctionOperation nested)
            {
                if (nested.Symbol.IsAsync || functions.Count > MaximumNestedFunctions)
                { return null; }
                functions.Add(nested);
            }
        }
        var captures = ImmutableArray.CreateBuilder<ISymbol>();
        foreach (var function in functions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (function.Syntax is not ExpressionSyntax expression || model.AnalyzeDataFlow(expression) is not { Succeeded: true } flow ||
                flow.CapturedInside.IsEmpty || flow.CapturedInside.Length > ResourceAllocationShape.MaximumFields)
            { return null; }
            foreach (var capture in flow.CapturedInside)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!SymbolEqualityComparer.Default.Equals(capture.ContainingSymbol, method) ||
                    !(capture is IParameterSymbol || capture is ILocalSymbol local && locals.Contains(local, SymbolEqualityComparer.Default)))
                { return null; }
                if (!captures.Contains(capture, SymbolEqualityComparer.Default))
                { captures.Add(capture); }
                if (captures.Count > ResourceAllocationShape.MaximumFields)
                { return null; }
            }
        }
        // Entry parameter stores and local initializer stores represent every admitted
        // capture write. Reject later writes, including writes inside the lambda.
        var root = declaration.Body is { } methodBody ? model.GetOperation(methodBody, cancellationToken) : value;
        foreach (var operation in root!.DescendantsAndSelf())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (--remaining < 0)
            { return null; }
            var target = operation switch
            {
                IAssignmentOperation assignment => assignment.Target,
                IIncrementOrDecrementOperation increment => increment.Target,
                IArgumentOperation { Parameter.RefKind: RefKind.Ref or RefKind.Out } argument => argument.Value,
                _ => null
            };
            var written = target switch { ILocalReferenceOperation local => (ISymbol)local.Local, IParameterReferenceOperation parameter => parameter.Parameter, _ => null };
            if (written != null && captures.Contains(written, SymbolEqualityComparer.Default))
            { return null; }
        }
        var fields = ImmutableArray.CreateBuilder<ResourceStorageKind>();
        var orderedCaptures = captures.OrderBy(capture => capture is null ? throw new ArgumentNullException(nameof(capture)) : capture is IParameterSymbol parameter ? parameter.Ordinal : method.Parameters.Length + locals.IndexOf((ILocalSymbol)capture)).ToImmutableArray();
        foreach (var capture in orderedCaptures)
        {
            var type = capture is IParameterSymbol parameter ? parameter.Type : ((ILocalSymbol)capture).Type;
            var storage = type.IsReferenceType ? ResourceStorageKind.Reference : type.SpecialType switch
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
        // Roslyn caches each nested capturing delegate on the shared root frame.
        // The reference slot exists at factory entry, but starts null; the cached
        // delegate itself is created by a later invocation of its enclosing body.
        if (orderedCaptures.Length + functions.Count - 1 > ResourceAllocationShape.MaximumFields)
        { return null; }
        for (var index = 1; index < functions.Count; index++)
        { fields.Add(ResourceStorageKind.Reference); }
        return new(anonymous.Symbol, fields.ToImmutable(), orderedCaptures);
    }
}
