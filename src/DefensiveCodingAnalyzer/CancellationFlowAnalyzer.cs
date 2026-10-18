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

    private static void AnalyzeCallable(SyntaxNodeAnalysisContext context, IMethodSymbol symbol,
        SyntaxTokenList modifiers, SyntaxNode body, Location identifierLocation,
        SyntaxNode declaration, bool isLocalFunction)
    {
        var tokenType = context.Compilation.GetTypeByMetadataName("System.Threading.CancellationToken");
        if (tokenType == null || !IsAsyncApi(symbol, modifiers, context.Compilation))
        {
            return;
        }

        var tokens = symbol.Parameters.Where(parameter =>
            SymbolEqualityComparer.Default.Equals(parameter.Type, tokenType)).ToImmutableArray();
        if (isLocalFunction && !modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.StaticKeyword)))
        {
            foreach (var ancestor in declaration.Ancestors())
            {
                if (ancestor is not MethodDeclarationSyntax and not LocalFunctionStatementSyntax)
                {
                    continue;
                }

                if (context.SemanticModel.GetDeclaredSymbol(ancestor, context.CancellationToken) is IMethodSymbol enclosing)
                {
                    tokens = tokens.AddRange(enclosing.Parameters.Where(parameter =>
                        SymbolEqualityComparer.Default.Equals(parameter.Type, tokenType)));
                }
                if (ancestor is MethodDeclarationSyntax ||
                    ancestor is LocalFunctionStatementSyntax parentLocal &&
                    parentLocal.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.StaticKeyword)))
                {
                    break;
                }
            }
        }

        if (tokens.IsEmpty)
        {
            if (isLocalFunction || !IsFixedSignature(symbol, context.Compilation, context.CancellationToken))
            {
                context.ReportDiagnostic(Diagnostic.Create(MissingToken,
                    identifierLocation, symbol.Name));
            }
            return;
        }

        foreach (var node in body.DescendantNodesAndSelf(DescendIntoCurrentMethod))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (node is not InvocationExpressionSyntax invocation ||
                context.SemanticModel.GetOperation(invocation, context.CancellationToken) is not IInvocationOperation operation ||
                !HasDroppedToken(operation, tokenType, tokens, context.SemanticModel,
                    context.CancellationToken))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(DroppedToken,
                invocation.GetLocation(), operation.TargetMethod.Name));
        }
    }

    private static bool IsAsyncApi(IMethodSymbol symbol, SyntaxTokenList modifiers,
        Compilation compilation)
    {
        if (modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.AsyncKeyword)))
        {
            return true;
        }

        if (!symbol.Name.EndsWith("Async", StringComparison.Ordinal))
        {
            return false;
        }

        if (symbol.ReturnType is not INamedTypeSymbol named)
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
            SymbolEqualityComparer.Default.Equals(method, compilation.GetEntryPoint(cancellationToken));
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
                    new HashSet<ISymbol>(SymbolEqualityComparer.Default), 0, cancellationToken));
        }

        // The selected overload omitted cancellation entirely. Match only an
        // overload with the same leading parameter types and a trailing token.
        return target.ContainingType.GetMembers(target.Name).OfType<IMethodSymbol>().Any(candidate =>
            candidate.Parameters.Length == target.Parameters.Length + 1 &&
            SymbolEqualityComparer.Default.Equals(candidate.Parameters[candidate.Parameters.Length - 1].Type, tokenType) &&
            target.Parameters.Zip(candidate.Parameters, (selected, cancellable) =>
                SymbolEqualityComparer.Default.Equals(selected.Type, cancellable.Type)).All(static same => same));
    }

    private static bool FlowsFromAvailableToken(IOperation value,
        ImmutableArray<IParameterSymbol> availableTokens, SemanticModel semanticModel,
        HashSet<ISymbol> visited, int depth, CancellationToken cancellationToken)
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
                    semanticModel, visited, depth + 1, cancellationToken);
            // C# operation trees omit parentheses; conversions are handled above.
            case IParameterReferenceOperation parameter:
                return availableTokens.Any(candidate =>
                    SymbolEqualityComparer.Default.Equals(candidate, parameter.Parameter));
            case ILocalReferenceOperation local:
                if (!visited.Add(local.Local))
                {
                    return false;
                }
                try
                {
                    if (IsReassigned(local.Local, semanticModel, cancellationToken) ||
                        local.Local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken)
                            is not VariableDeclaratorSyntax { Initializer: { Value: var initializer } })
                    {
                        return false;
                    }
                    var initialValue = semanticModel.GetOperation(initializer, cancellationToken);
                    return initialValue != null && FlowsFromAvailableToken(initialValue, availableTokens,
                        semanticModel, visited, depth + 1, cancellationToken);
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
            case IInvocationOperation invocation when
                invocation.TargetMethod.IsStatic &&
                invocation.TargetMethod.Name == "CreateLinkedTokenSource" &&
                SymbolEqualityComparer.Default.Equals(invocation.TargetMethod.ContainingType,
                    semanticModel.Compilation.GetTypeByMetadataName("System.Threading.CancellationTokenSource")):
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
                        semanticModel, visited, depth + 1, cancellationToken) &&
                    FlowsFromAvailableToken(whenFalse, availableTokens,
                        semanticModel, visited, depth + 1, cancellationToken);
            default:
                return false;
        }
    }

    private static bool IsReassigned(ILocalSymbol local, SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var declaration = local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken);
        var scope = declaration?.Ancestors().FirstOrDefault(node =>
            node is MethodDeclarationSyntax or LocalFunctionStatementSyntax);
        if (scope == null)
        {
            return true;
        }

        foreach (var node in scope.DescendantNodes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node is AssignmentExpressionSyntax assignment &&
                semanticModel.GetOperation(assignment.Left, cancellationToken) is ILocalReferenceOperation target &&
                SymbolEqualityComparer.Default.Equals(target.Local, local))
            {
                return true;
            }
            if (node is ArgumentSyntax argument && !argument.RefKindKeyword.IsKind(SyntaxKind.None) &&
                semanticModel.GetOperation(argument.Expression, cancellationToken) is ILocalReferenceOperation reference &&
                SymbolEqualityComparer.Default.Equals(reference.Local, local))
            {
                return true;
            }
        }
        return false;
    }
}
