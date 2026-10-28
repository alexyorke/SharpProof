using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.DefensiveCodingAnalyzer;

// Repository-only rule: asynchronous APIs accept a token and pass it to
// cancellable asynchronous operations whenever the callee offers one.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CancellationFlowAnalyzer : DiagnosticAnalyzer
{
    public const string MissingTokenId = "SPINT002";
    public const string DroppedTokenId = "SPINT003";
    public const string OptionalTokenId = "SPINT004";
    public const string NoTokenId = "SPINT005";

    private static readonly DiagnosticDescriptor MissingToken = new(
        MissingTokenId,
        "Accept a cancellation token in asynchronous methods",
        "Asynchronous method '{0}' must accept a CancellationToken",
        "Cancellation",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Repository asynchronous methods must expose cancellation to their callers.");

    private static readonly DiagnosticDescriptor DroppedToken = new(
        DroppedTokenId,
        "Forward the available cancellation token",
        "Pass the available CancellationToken to '{0}'",
        "Cancellation",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "An asynchronous method with a token must use a cancellable overload when one is available.");

    private static readonly DiagnosticDescriptor OptionalToken = new(
        OptionalTokenId,
        "Require explicit cancellation tokens",
        "CancellationToken parameter '{0}' must not be optional",
        "Cancellation",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Repository methods must require callers to supply a cancellation token explicitly.");

    private static readonly DiagnosticDescriptor NoToken = new(
        NoTokenId,
        "Use a cancellable token",
        "CancellationToken.None prevents the operation from being canceled",
        "Cancellation",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Repository code, including tests, must pass a cancellable token.");
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [MissingToken, DroppedToken, OptionalToken, NoToken];

    public override void Initialize(AnalysisContext context)
    {
        context = RepositoryParameterGuard.NotNull(context, nameof(context));
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeLocalFunction, SyntaxKind.LocalFunctionStatement);
        context.RegisterSyntaxNodeAction(AnalyzeParameter, SyntaxKind.Parameter);
        context.RegisterSyntaxNodeAction(AnalyzeAnonymousFunction, SyntaxKind.SimpleLambdaExpression,
            SyntaxKind.ParenthesizedLambdaExpression, SyntaxKind.AnonymousMethodExpression);
        context.RegisterOperationAction(AnalyzeNoToken, OperationKind.PropertyReference);
    }

    private static void AnalyzeNoToken(OperationAnalysisContext context)
    {
        if (context.Operation is not IPropertyReferenceOperation { Property: var property } reference ||
            !property.IsStatic || property.Name != "None" ||
            !SymbolEqualityComparer.Default.Equals(property.ContainingType,
                context.Compilation.GetTypeByMetadataName("System.Threading.CancellationToken")))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(NoToken, reference.Syntax.GetLocation()));
    }
    private static void AnalyzeParameter(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not ParameterSyntax { Default: not null } parameter ||
            context.SemanticModel.GetDeclaredSymbol(parameter, context.CancellationToken) is not IParameterSymbol symbol ||
            !SymbolEqualityComparer.Default.Equals(symbol.Type,
                context.Compilation.GetTypeByMetadataName("System.Threading.CancellationToken")))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(OptionalToken,
            parameter.Identifier.GetLocation(), parameter.Identifier.ValueText));
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        // Test runner signatures are framework-owned; test helpers are not
        // production cancellation boundaries.
        if (context.Compilation.AssemblyName?.EndsWith(".Test", StringComparison.Ordinal) == true ||
            context.Node is not MethodDeclarationSyntax method ||
            method.Body == null && method.ExpressionBody == null ||
            context.SemanticModel.GetDeclaredSymbol(method, context.CancellationToken) is not IMethodSymbol symbol)
        {
            return;
        }

        AnalyzeCallable(context, symbol, method.Modifiers,
            (SyntaxNode?)method.Body ?? method.ExpressionBody!, method.Identifier.GetLocation(),
            method, isLocalFunction: false);
    }

    private static void AnalyzeLocalFunction(SyntaxNodeAnalysisContext context)
    {
        if (context.Compilation.AssemblyName?.EndsWith(".Test", StringComparison.Ordinal) == true ||
            context.Node is not LocalFunctionStatementSyntax local ||
            local.Body == null && local.ExpressionBody == null ||
            context.SemanticModel.GetDeclaredSymbol(local, context.CancellationToken) is not IMethodSymbol symbol)
        {
            return;
        }

        AnalyzeCallable(context, symbol, local.Modifiers,
            (SyntaxNode?)local.Body ?? local.ExpressionBody!, local.Identifier.GetLocation(),
            local, isLocalFunction: true);
    }

    private static void AnalyzeAnonymousFunction(SyntaxNodeAnalysisContext context)
    {
        if (context.Compilation.AssemblyName?.EndsWith(".Test", StringComparison.Ordinal) == true ||
            context.SemanticModel.GetOperation(context.Node, context.CancellationToken) is not IAnonymousFunctionOperation operation)
        {
            return;
        }
        var body = context.Node switch
        {
            SimpleLambdaExpressionSyntax lambda => lambda.Body,
            ParenthesizedLambdaExpressionSyntax lambda => lambda.Body,
            AnonymousMethodExpressionSyntax anonymous => (CSharpSyntaxNode)anonymous.Body,
            _ => null
        };
        if (body != null)
        {
            AnalyzeCallable(context, operation.Symbol, default, body, context.Node.GetLocation(),
                context.Node, isLocalFunction: false, isAnonymous: true);
        }
    }

    private static void AnalyzeCallable(SyntaxNodeAnalysisContext context, IMethodSymbol symbol,
        SyntaxTokenList modifiers, SyntaxNode body, Location identifierLocation,
        SyntaxNode declaration, bool isLocalFunction, bool isAnonymous = false)
    {
        var tokenType = context.Compilation.GetTypeByMetadataName("System.Threading.CancellationToken");
        if (tokenType == null || !IsAsyncApi(symbol, modifiers, context.Compilation, isAnonymous))
        {
            return;
        }

        var tokenBuilder = ImmutableArray.CreateBuilder<IParameterSymbol>();
        tokenBuilder.AddRange(symbol.Parameters.Where(parameter =>
            SymbolEqualityComparer.Default.Equals(parameter.Type, tokenType)));
        if ((isLocalFunction || isAnonymous) && !symbol.IsStatic)
        {
            foreach (var ancestor in declaration.Ancestors())
            {
                var enclosing = GetCallableSymbol(ancestor, context.SemanticModel, context.CancellationToken);
                if (enclosing == null)
                {
                    continue;
                }
                tokenBuilder.AddRange(enclosing.Parameters.Where(parameter =>
                    SymbolEqualityComparer.Default.Equals(parameter.Type, tokenType)));
                if (ancestor is BaseMethodDeclarationSyntax || enclosing.IsStatic)
                {
                    break;
                }
            }
        }

        var tokens = tokenBuilder.ToImmutable();
        if (tokens.IsEmpty)
        {
            // Delegate signatures are owned by the caller; only forwarding is
            // checked inside anonymous functions with available tokens.
            if (!isAnonymous && (isLocalFunction || !IsFixedSignature(symbol, context.Compilation, context.CancellationToken)))
            {
                context.ReportDiagnostic(Diagnostic.Create(MissingToken,
                    identifierLocation, symbol.Name));
            }
            return;
        }

        foreach (var node in body.DescendantNodesAndSelf(DescendIntoCurrentMethod))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (node is InvocationExpressionSyntax invocation &&
                context.SemanticModel.GetOperation(invocation, context.CancellationToken) is IInvocationOperation operation &&
                HasDroppedToken(operation, tokenType, tokens, context.SemanticModel, context.CancellationToken))
            {
                context.ReportDiagnostic(Diagnostic.Create(DroppedToken,
                    invocation.GetLocation(), operation.TargetMethod.Name));
            }
            else if (node is CommonForEachStatementSyntax loop && !loop.AwaitKeyword.IsKind(SyntaxKind.None) &&
                RequiresEnumerationToken(loop, tokenType, tokens, context.SemanticModel, context.CancellationToken))
            {
                context.ReportDiagnostic(Diagnostic.Create(DroppedToken,
                    loop.Expression.GetLocation(), "GetAsyncEnumerator"));
            }
        }
    }

    private static IMethodSymbol? GetCallableSymbol(SyntaxNode node, SemanticModel model, CancellationToken cancellationToken)
    {
        return node switch
        {
            BaseMethodDeclarationSyntax or LocalFunctionStatementSyntax =>
                model.GetDeclaredSymbol(node, cancellationToken) as IMethodSymbol,
            AnonymousFunctionExpressionSyntax =>
                (model.GetOperation(node, cancellationToken) as IAnonymousFunctionOperation)?.Symbol,
            _ => null
        };
    }

    private static bool IsAsyncApi(IMethodSymbol symbol, SyntaxTokenList modifiers,
        Compilation compilation, bool isAnonymous)
    {
        if (symbol.IsAsync || modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.AsyncKeyword)))
        {
            return true;
        }
        return (isAnonymous || symbol.Name.EndsWith("Async", StringComparison.Ordinal)) &&
            IsTaskLike(symbol.ReturnType, compilation);
    }

    private static bool IsTaskLike(ITypeSymbol type, Compilation compilation)
    {
        if (type is not INamedTypeSymbol named)
        {
            return false;
        }
        var definition = named.OriginalDefinition;
        return SymbolEqualityComparer.Default.Equals(definition,
                compilation.GetTypeByMetadataName("System.Threading.Tasks.Task")) ||
            SymbolEqualityComparer.Default.Equals(definition,
                compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1")) ||
            SymbolEqualityComparer.Default.Equals(definition,
                compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask")) ||
            SymbolEqualityComparer.Default.Equals(definition,
                compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask`1")) ||
            SymbolEqualityComparer.Default.Equals(definition,
                compilation.GetTypeByMetadataName("System.Collections.Generic.IAsyncEnumerable`1"));
    }

    private static bool IsFixedSignature(IMethodSymbol method, Compilation compilation,
        CancellationToken cancellationToken)
    {
        return method.IsOverride || method.ExplicitInterfaceImplementations.Length != 0 ||
            SymbolEqualityComparer.Default.Equals(method, compilation.GetEntryPoint(cancellationToken)) ||
            method.ContainingType.AllInterfaces.SelectMany(static contract => contract.GetMembers())
                .OfType<IMethodSymbol>().Any(member => SymbolEqualityComparer.Default.Equals(
                    method.ContainingType.FindImplementationForInterfaceMember(member), method));
    }

    private static bool DescendIntoCurrentMethod(SyntaxNode node)
    {
        return node is not AnonymousFunctionExpressionSyntax and not LocalFunctionStatementSyntax;
    }

    private static bool HasDroppedToken(IInvocationOperation invocation, ITypeSymbol tokenType,
        ImmutableArray<IParameterSymbol> availableTokens, SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var target = invocation.TargetMethod;
        if (target.Parameters.Any(parameter =>
                SymbolEqualityComparer.Default.Equals(parameter.Type, tokenType)))
        {
            return !invocation.Arguments.Any(argument =>
                argument.Parameter is { } parameter &&
                SymbolEqualityComparer.Default.Equals(parameter.Type, tokenType) &&
                argument.ArgumentKind != ArgumentKind.DefaultValue &&
                FlowsFromAvailableToken(argument.Value, availableTokens, semanticModel,
                    ImmutableHashSet.CreateBuilder<ISymbol>(SymbolEqualityComparer.Default), 0, cancellationToken));
        }

        if (invocation.Syntax is not InvocationExpressionSyntax syntax)
        {
            return false;
        }

        // Ask the compiler to bind the actual call with an added named token.
        // This preserves inference, ref kinds, accessibility and receiver rules.
        var tokenNames = semanticModel.GetMemberGroup(syntax.Expression, cancellationToken)
            .OfType<IMethodSymbol>().SelectMany(static method => method.Parameters)
            .Where(parameter => SymbolEqualityComparer.Default.Equals(parameter.Type, tokenType))
            .Select(static parameter => parameter.Name).Distinct(StringComparer.Ordinal);
        foreach (var name in tokenNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tokenName = SyntaxFactory.QualifiedName(
                SyntaxFactory.QualifiedName(
                    SyntaxFactory.AliasQualifiedName(SyntaxFactory.IdentifierName(SyntaxFactory.Token(SyntaxKind.GlobalKeyword)),
                        SyntaxFactory.IdentifierName("System")),
                    SyntaxFactory.IdentifierName("Threading")),
                SyntaxFactory.IdentifierName("CancellationToken"));
            var parameterName = SyntaxFactory.IdentifierName(
                SyntaxFactory.Identifier(default, SyntaxKind.IdentifierToken, "@" + name, name, default));
            var argument = SyntaxFactory.Argument(SyntaxFactory.DefaultExpression(tokenName))
                .WithNameColon(SyntaxFactory.NameColon(parameterName));
            var replacement = syntax.WithArgumentList(syntax.ArgumentList.AddArguments(argument));
            var binding = semanticModel.GetSpeculativeSymbolInfo(syntax.SpanStart, replacement, SpeculativeBindingOption.BindAsExpression);
            if (binding.Symbol is IMethodSymbol bound &&
                semanticModel.IsAccessible(syntax.SpanStart, bound) &&
                bound.Parameters.Any(parameter => parameter.Name == name &&
                    SymbolEqualityComparer.Default.Equals(parameter.Type, tokenType)))
            {
                return true;
            }
        }
        return false;
    }

    private static bool RequiresEnumerationToken(CommonForEachStatementSyntax loop, ITypeSymbol tokenType,
        ImmutableArray<IParameterSymbol> availableTokens, SemanticModel model, CancellationToken cancellationToken)
    {
        var enumerator = model.GetForEachStatementInfo(loop).GetEnumeratorMethod;
        if (enumerator == null)
        {
            return false;
        }
        var collection = model.GetOperation(loop.Expression, cancellationToken);
        var configured = model.Compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.ConfiguredCancelableAsyncEnumerable`1");
        if (!enumerator.Parameters.Any(parameter => SymbolEqualityComparer.Default.Equals(parameter.Type, tokenType)) &&
            !SymbolEqualityComparer.Default.Equals(collection?.Type?.OriginalDefinition, configured))
        {
            return false;
        }
        return !HasEnumerationCancellation(collection, tokenType, availableTokens, model,
            ImmutableHashSet.CreateBuilder<ISymbol>(SymbolEqualityComparer.Default), 0, cancellationToken);
    }

    private static bool HasEnumerationCancellation(IOperation? collection, ITypeSymbol tokenType,
        ImmutableArray<IParameterSymbol> availableTokens, SemanticModel model,
        ImmutableHashSet<ISymbol>.Builder visited, int depth, CancellationToken cancellationToken)
    {
        if (collection == null || depth >= 16)
        {
            return false;
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (collection is IConversionOperation conversion)
        {
            return HasEnumerationCancellation(conversion.Operand, tokenType, availableTokens, model, visited, depth + 1, cancellationToken);
        }
        if (collection is ILocalReferenceOperation local && visited.Add(local.Local) &&
            !IsReassigned(local.Local, local.Syntax, model, cancellationToken) &&
            local.Local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken) is
                VariableDeclaratorSyntax { Initializer.Value: { } initializer })
        {
            return HasEnumerationCancellation(model.GetOperation(initializer, cancellationToken), tokenType,
                availableTokens, model, visited, depth + 1, cancellationToken);
        }
        if (collection is not IInvocationOperation invocation)
        {
            return false;
        }

        var owner = invocation.TargetMethod.ContainingType.OriginalDefinition;
        var isEnumerableWrapper = SymbolEqualityComparer.Default.Equals(owner,
                model.Compilation.GetTypeByMetadataName("System.Threading.Tasks.TaskAsyncEnumerableExtensions")) ||
            SymbolEqualityComparer.Default.Equals(owner,
                model.Compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.ConfiguredCancelableAsyncEnumerable`1"));
        if (isEnumerableWrapper && invocation.TargetMethod.Name == "ConfigureAwait")
        {
            return HasEnumerationCancellation(invocation.Instance ?? invocation.Arguments.FirstOrDefault()?.Value,
                tokenType, availableTokens, model, visited, depth + 1, cancellationToken);
        }

        // Explicit token-bearing producer/WithCancellation calls are already
        // checked by HasDroppedToken. Do not duplicate their diagnostics here.
        return invocation.TargetMethod.Parameters.Any(parameter =>
                SymbolEqualityComparer.Default.Equals(parameter.Type, tokenType)) ||
            HasDroppedToken(invocation, tokenType, availableTokens, model, cancellationToken);
    }

    private static bool FlowsFromAvailableToken(IOperation value,
        ImmutableArray<IParameterSymbol> availableTokens, SemanticModel semanticModel,
        ImmutableHashSet<ISymbol>.Builder visited, int depth, CancellationToken cancellationToken,
        SyntaxNode? arrayObservation = null)
    {
        if (depth >= 16)
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        switch (value)
        {
            case IConversionOperation conversion:
                return FlowsFromAvailableToken(conversion.Operand, availableTokens,
                    semanticModel, visited, depth + 1, cancellationToken, arrayObservation);
            case IParameterReferenceOperation parameter:
                return availableTokens.Any(candidate =>
                    SymbolEqualityComparer.Default.Equals(candidate, parameter.Parameter)) &&
                    !IsReassigned(parameter.Parameter, parameter.Syntax, semanticModel, cancellationToken);
            case ILocalReferenceOperation local:
                if (!visited.Add(local.Local))
                {
                    return false;
                }
                try
                {
                    if (IsReassigned(local.Local, local.Syntax, semanticModel, cancellationToken) ||
                        local.Local.Type is IArrayTypeSymbol && HasArrayMutationOrEscape(local.Local, arrayObservation ?? local.Syntax,
                            semanticModel, ImmutableHashSet.CreateBuilder<ISymbol>(SymbolEqualityComparer.Default), cancellationToken) ||
                        local.Local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken)
                            is not VariableDeclaratorSyntax { Initializer: { Value: var initializer } })
                    {
                        return false;
                    }
                    var initialValue = semanticModel.GetOperation(initializer, cancellationToken);
                    return initialValue != null && FlowsFromAvailableToken(initialValue, availableTokens,
                        semanticModel, visited, depth + 1, cancellationToken,
                        local.Local.Type is IArrayTypeSymbol ? arrayObservation ?? local.Syntax : null);
                }
                finally
                {
                    visited.Remove(local.Local);
                }
            case IPropertyReferenceOperation property when
                property.Property.Name == "Token" &&
                SymbolEqualityComparer.Default.Equals(property.Property.ContainingType,
                    semanticModel.Compilation.GetTypeByMetadataName("System.Threading.CancellationTokenSource")) &&
                property.Instance != null:
                return FlowsFromAvailableToken(property.Instance, availableTokens,
                    semanticModel, visited, depth + 1, cancellationToken);
            case IInvocationOperation invocation when IsLinkedTokenSource(invocation.TargetMethod, semanticModel.Compilation):
                return invocation.Arguments.Any(argument =>
                    FlowsFromAvailableToken(argument.Value, availableTokens,
                        semanticModel, visited, depth + 1, cancellationToken));
            case IArrayCreationOperation array when array.Initializer != null:
                return array.Initializer.ElementValues.Any(element =>
                    FlowsFromAvailableToken(element, availableTokens,
                        semanticModel, visited, depth + 1, cancellationToken));
            case IConditionalOperation conditional:
                return conditional.WhenFalse is { } whenFalse &&
                    FlowsFromAvailableToken(conditional.WhenTrue, availableTokens,
                        semanticModel, visited, depth + 1, cancellationToken, arrayObservation) &&
                    FlowsFromAvailableToken(whenFalse, availableTokens,
                        semanticModel, visited, depth + 1, cancellationToken, arrayObservation);
            default:
                return false;
        }
    }

    private static bool IsLinkedTokenSource(IMethodSymbol method, Compilation compilation)
    {
        return method.IsStatic && method.Name == "CreateLinkedTokenSource" &&
            SymbolEqualityComparer.Default.Equals(method.ContainingType,
                compilation.GetTypeByMetadataName("System.Threading.CancellationTokenSource"));
    }

    private static SyntaxNode? GetCallableScope(SyntaxNode node)
    {
        return node.Ancestors().FirstOrDefault(static ancestor =>
            ancestor is BaseMethodDeclarationSyntax or LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax);
    }

    private static bool MayRunBefore(SyntaxNode mutation, SyntaxNode use, SyntaxNode scope)
    {
        if (mutation.SpanStart < use.SpanStart ||
            !ReferenceEquals(GetCallableScope(use), scope) ||
            !ReferenceEquals(GetCallableScope(mutation), scope))
        {
            return true;
        }
        // A later write in the same loop can precede the next iteration's read.
        return use.Ancestors().TakeWhile(ancestor => !ReferenceEquals(ancestor, scope)).Any(ancestor =>
            ancestor is ForStatementSyntax or CommonForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax &&
            ancestor.Span.Contains(mutation.Span));
    }

    private static bool RefersToSymbol(SyntaxNode expression, ISymbol symbol, SemanticModel model, CancellationToken cancellationToken)
    {
        return TargetsSymbol(model.GetOperation(expression, cancellationToken), symbol);
    }

    private static bool TargetsSymbol(IOperation? target, ISymbol symbol)
    {
        return target switch
        {
            ILocalReferenceOperation local => SymbolEqualityComparer.Default.Equals(local.Local, symbol),
            IParameterReferenceOperation parameter => SymbolEqualityComparer.Default.Equals(parameter.Parameter, symbol),
            ITupleOperation tuple => tuple.Elements.Any(element => TargetsSymbol(element, symbol)),
            IDeclarationExpressionOperation declaration => TargetsSymbol(declaration.Expression, symbol),
            IConversionOperation conversion => TargetsSymbol(conversion.Operand, symbol),
            IParenthesizedOperation parentheses => TargetsSymbol(parentheses.Operand, symbol),
            // Element stores invalidate array contents, but evaluating an index
            // or a property receiver does not write the symbols read there.
            IArrayElementReferenceOperation element => TargetsSymbol(element.ArrayReference, symbol),
            _ => false
        };
    }

    private static bool IsReassigned(ISymbol symbol, SyntaxNode use, SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var declaration = symbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken);
        var scope = declaration == null ? null : GetCallableScope(declaration);
        if (scope == null)
        {
            return true;
        }

        foreach (var node in scope.DescendantNodes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node is not AssignmentExpressionSyntax and not RefExpressionSyntax &&
                !(node is ArgumentSyntax candidateArgument &&
                    candidateArgument.RefKindKeyword.Kind() is SyntaxKind.RefKeyword or SyntaxKind.OutKeyword))
            {
                continue;
            }
            if (!MayRunBefore(node, use, scope))
            {
                continue;
            }
            if (node is AssignmentExpressionSyntax assignment &&
                RefersToSymbol(assignment.Left, symbol, semanticModel, cancellationToken) ||
                node is ArgumentSyntax argument && argument.RefKindKeyword.Kind() is SyntaxKind.RefKeyword or SyntaxKind.OutKeyword &&
                RefersToSymbol(argument.Expression, symbol, semanticModel, cancellationToken) ||
                node is RefExpressionSyntax reference &&
                RefersToSymbol(reference.Expression, symbol, semanticModel, cancellationToken))
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasArrayMutationOrEscape(ILocalSymbol array, SyntaxNode use, SemanticModel model,
        ImmutableHashSet<ISymbol>.Builder visited, CancellationToken cancellationToken)
    {
        if (!visited.Add(array))
        {
            return false;
        }
        var declaration = array.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken);
        var scope = declaration == null ? null : GetCallableScope(declaration);
        if (scope == null)
        {
            return true;
        }
        foreach (var identifier in scope.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(identifier, cancellationToken).Symbol, array) ||
                !MayRunBefore(identifier, use, scope))
            {
                continue;
            }
            SyntaxNode value = identifier;
            while (value.Parent is ParenthesizedExpressionSyntax or CastExpressionSyntax)
            {
                value = value.Parent;
            }
            if (value.Parent is EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax aliasDeclaration } &&
                model.GetDeclaredSymbol(aliasDeclaration, cancellationToken) is ILocalSymbol alias &&
                alias.Type is IArrayTypeSymbol)
            {
                if (IsReassigned(alias, use, model, cancellationToken) ||
                    HasArrayMutationOrEscape(alias, use, model, visited, cancellationToken))
                {
                    return true;
                }
                continue;
            }
            if (value.Parent is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Length" or "LongLength" })
            {
                continue;
            }
            if (value.Parent is ElementAccessExpressionSyntax element &&
                element.Parent is not AssignmentExpressionSyntax and not RefExpressionSyntax)
            {
                continue;
            }
            if (value.Parent is ArgumentSyntax { Parent.Parent: InvocationExpressionSyntax call } &&
                model.GetSymbolInfo(call, cancellationToken).Symbol is IMethodSymbol method &&
                IsLinkedTokenSource(method, model.Compilation))
            {
                continue;
            }
            return true;
        }
        return false;
    }
}
