using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharpProof.DefensiveCodingAnalyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ParameterGuardAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "SPINT001";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Validate every input parameter",
        "Parameter '{0}' has no explicit guard",
        "DefensiveCoding",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Every input parameter of a concrete callable must occur in an explicit validation that rejects invalid input.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(Analyze,
            SyntaxKind.MethodDeclaration,
            SyntaxKind.ConstructorDeclaration,
            SyntaxKind.OperatorDeclaration,
            SyntaxKind.ConversionOperatorDeclaration,
            SyntaxKind.LocalFunctionStatement,
            SyntaxKind.SimpleLambdaExpression,
            SyntaxKind.ParenthesizedLambdaExpression,
            SyntaxKind.AnonymousMethodExpression,
            SyntaxKind.ClassDeclaration,
            SyntaxKind.StructDeclaration,
            SyntaxKind.RecordDeclaration,
            SyntaxKind.RecordStructDeclaration);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        if (!TryGetCallable(context.Node, out var parameters, out var body))
        {
            return;
        }

        foreach (var parameter in parameters)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            // An out parameter is written by the callable and cannot be read
            // before it is assigned, so it is not an input to validate.
            if (parameter.Modifiers.Any(static token => token.IsKind(SyntaxKind.OutKeyword)) ||
                context.SemanticModel.GetDeclaredSymbol(parameter, context.CancellationToken) is not IParameterSymbol symbol)
            {
                continue;
            }

            // Neither Boolean value nor a CancellationToken state is invalid
            // on its own. Rejecting one would change the callable's contract.
            if (symbol.Type.SpecialType == SpecialType.System_Boolean ||
                SymbolEqualityComparer.Default.Equals(
                    symbol.Type,
                    context.Compilation.GetTypeByMetadataName("System.Threading.CancellationToken")))
            {
                continue;
            }

            if (HasGuard(body, symbol, context.SemanticModel, context.CancellationToken))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                parameter.Identifier.GetLocation(),
                parameter.Identifier.ValueText));
        }
    }

    private static bool TryGetCallable(
        SyntaxNode node,
        out ImmutableArray<ParameterSyntax> parameters,
        out SyntaxNode body)
    {
        parameters = [];
        body = null!;
        switch (node)
        {
            case MethodDeclarationSyntax method when ((SyntaxNode?)method.Body ?? method.ExpressionBody) is { } methodBody:
                parameters = method.ParameterList.Parameters.ToImmutableArray();
                body = methodBody;
                return true;
            case ConstructorDeclarationSyntax constructor when ((SyntaxNode?)constructor.Body ?? constructor.ExpressionBody) is not null:
                parameters = constructor.ParameterList.Parameters.ToImmutableArray();
                body = constructor;
                return true;
            case OperatorDeclarationSyntax operation when ((SyntaxNode?)operation.Body ?? operation.ExpressionBody) is { } operationBody:
                parameters = operation.ParameterList.Parameters.ToImmutableArray();
                body = operationBody;
                return true;
            case ConversionOperatorDeclarationSyntax conversion
                when ((SyntaxNode?)conversion.Body ?? conversion.ExpressionBody) is { } conversionBody:
                parameters = conversion.ParameterList.Parameters.ToImmutableArray();
                body = conversionBody;
                return true;
            case LocalFunctionStatementSyntax local when ((SyntaxNode?)local.Body ?? local.ExpressionBody) is { } localBody:
                parameters = local.ParameterList.Parameters.ToImmutableArray();
                body = localBody;
                return true;
            case SimpleLambdaExpressionSyntax simpleLambda:
                parameters = [simpleLambda.Parameter];
                body = simpleLambda.Body;
                return true;
            case ParenthesizedLambdaExpressionSyntax lambda:
                parameters = lambda.ParameterList.Parameters.ToImmutableArray();
                body = lambda.Body;
                return true;
            case AnonymousMethodExpressionSyntax anonymous when anonymous.ParameterList is { } anonymousParameters:
                parameters = anonymousParameters.Parameters.ToImmutableArray();
                body = anonymous.Body;
                return true;
            case TypeDeclarationSyntax type when type.ParameterList is { } primary:
                parameters = primary.Parameters.ToImmutableArray();
                body = type;
                return true;
            default:
                return false;
        }
    }

    private static bool HasGuard(
        SyntaxNode body,
        IParameterSymbol parameter,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        foreach (var node in body.DescendantNodesAndSelf(node =>
            ReferenceEquals(node, body) || DescendIntoCallable(node)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node is IfStatementSyntax condition &&
                Mentions(condition.Condition, parameter, model, cancellationToken) &&
                (Rejects(condition.Statement) ||
                 condition.Else is { Statement: { } alternative } && Rejects(alternative)))
            {
                return true;
            }

            if (node is InvocationExpressionSyntax invocation &&
                IsValidationCall(invocation.Expression) &&
                invocation.ArgumentList.Arguments.Any(argument =>
                    Mentions(argument.Expression, parameter, model, cancellationToken)))
            {
                return true;
            }

            if (node is BinaryExpressionSyntax coalesce &&
                coalesce.IsKind(SyntaxKind.CoalesceExpression) &&
                coalesce.Right is ThrowExpressionSyntax &&
                Mentions(coalesce.Left, parameter, model, cancellationToken))
            {
                return true;
            }

            if (node is SwitchExpressionSyntax selection &&
                Mentions(selection.GoverningExpression, parameter, model, cancellationToken) &&
                selection.Arms.Any(static arm => arm.Expression is ThrowExpressionSyntax))
            {
                return true;
            }

            if (node is SwitchStatementSyntax switchStatement &&
                Mentions(switchStatement.Expression, parameter, model, cancellationToken) &&
                switchStatement.Sections.Any(static section =>
                    section.Statements.LastOrDefault() is { } last && Rejects(last)))
            {
                return true;
            }

            if (node is ConditionalExpressionSyntax conditional &&
                Mentions(conditional.Condition, parameter, model, cancellationToken) &&
                (conditional.WhenTrue is ThrowExpressionSyntax ||
                 conditional.WhenFalse is ThrowExpressionSyntax))
            {
                return true;
            }
        }

        return false;
    }

    private static bool DescendIntoCallable(SyntaxNode node)
    {
        return node is not AnonymousFunctionExpressionSyntax and not LocalFunctionStatementSyntax
            and not MethodDeclarationSyntax and not ConstructorDeclarationSyntax
            and not OperatorDeclarationSyntax and not ConversionOperatorDeclarationSyntax;
    }

    private static bool Rejects(StatementSyntax statement)
    {
        return statement is ThrowStatementSyntax or ReturnStatementSyntax ||
            statement is BlockSyntax block && block.Statements.LastOrDefault() is { } last && Rejects(last) ||
            statement is IfStatementSyntax conditional &&
                conditional.Else is { Statement: { } alternative } &&
                Rejects(conditional.Statement) && Rejects(alternative);
    }

    private static bool IsValidationCall(ExpressionSyntax expression)
    {
        var name = expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            GenericNameSyntax generic => generic.Identifier.ValueText,
            _ => string.Empty
        };
        return name.StartsWith("ThrowIf", StringComparison.Ordinal) ||
               name.StartsWith("Validate", StringComparison.Ordinal) ||
               name.StartsWith("Ensure", StringComparison.Ordinal) ||
               name.StartsWith("Check", StringComparison.Ordinal) ||
               name.StartsWith("Require", StringComparison.Ordinal) ||
               name is "Requires" or "NotNull";
    }

    private static bool Mentions(
        SyntaxNode expression,
        IParameterSymbol parameter,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        return expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Any(identifier =>
            SymbolEqualityComparer.Default.Equals(
                model.GetSymbolInfo(identifier, cancellationToken).Symbol,
                parameter));
    }
}
