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

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [MissingToken, DroppedToken, OptionalToken];

    public override void Initialize(AnalysisContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeParameter, SyntaxKind.Parameter);
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

        var tokenType = context.Compilation.GetTypeByMetadataName("System.Threading.CancellationToken");
        if (tokenType == null || !IsAsyncApi(symbol, method, context.Compilation))
        {
            return;
        }

        var token = symbol.Parameters.FirstOrDefault(parameter =>
            SymbolEqualityComparer.Default.Equals(parameter.Type, tokenType));
        if (token == null)
        {
            if (!IsFixedSignature(symbol, context.Compilation, context.CancellationToken))
            {
                context.ReportDiagnostic(Diagnostic.Create(MissingToken,
                    method.Identifier.GetLocation(), method.Identifier.ValueText));
            }
            return;
        }

        var body = (SyntaxNode?)method.Body ?? method.ExpressionBody!;
        foreach (var node in body.DescendantNodesAndSelf(DescendIntoCurrentMethod))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (node is not InvocationExpressionSyntax invocation ||
                context.SemanticModel.GetOperation(invocation, context.CancellationToken) is not IInvocationOperation operation ||
                !HasCancellableTarget(operation.TargetMethod, tokenType, operation.Arguments))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(DroppedToken,
                invocation.GetLocation(), operation.TargetMethod.Name));
        }
    }

    private static bool IsAsyncApi(IMethodSymbol symbol, MethodDeclarationSyntax syntax,
        Compilation compilation)
    {
        if (syntax.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.AsyncKeyword)))
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

    private static bool HasCancellableTarget(IMethodSymbol target, ITypeSymbol tokenType,
        ImmutableArray<IArgumentOperation> arguments)
    {
        var tokenParameter = target.Parameters.FirstOrDefault(parameter =>
            SymbolEqualityComparer.Default.Equals(parameter.Type, tokenType));
        if (tokenParameter != null)
        {
            return arguments.Any(argument =>
                SymbolEqualityComparer.Default.Equals(argument.Parameter, tokenParameter) &&
                (argument.ArgumentKind == ArgumentKind.DefaultValue || IsNoCancellation(argument.Value, tokenType)));
        }

        // The selected overload omitted cancellation entirely. Match only an
        // overload with the same leading parameter types and a trailing token.
        return target.ContainingType.GetMembers(target.Name).OfType<IMethodSymbol>().Any(candidate =>
            candidate.Parameters.Length == target.Parameters.Length + 1 &&
            SymbolEqualityComparer.Default.Equals(candidate.Parameters[candidate.Parameters.Length - 1].Type, tokenType) &&
            target.Parameters.Zip(candidate.Parameters, (selected, cancellable) =>
                SymbolEqualityComparer.Default.Equals(selected.Type, cancellable.Type)).All(static same => same));
    }

    private static bool IsNoCancellation(IOperation value, ITypeSymbol tokenType)
    {
        return value is IDefaultValueOperation ||
            value is IPropertyReferenceOperation property &&
                property.Property.Name == "None" &&
                SymbolEqualityComparer.Default.Equals(property.Property.ContainingType, tokenType) ||
            value is IConversionOperation conversion && IsNoCancellation(conversion.Operand, tokenType);
    }
}
