using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.Analyzer.Configuration;

namespace SharpProof.Analyzer;

internal static partial class CommonBugAnalyzer
{
    internal static void AnalyzeCallable(
        MethodBodyAnalysisContext context,
        AnalyzerSession session)
    {
        AnalyzeAsyncCorrectness(context, session);
        AnalyzeCollectionAndConcurrencyCorrectness(context, session);
        AnalyzeNullLinqSerializationAndDeployment(context, session);
        AnalyzeAdditionalCommonBugs(context, session);
    }

    internal static void AnalyzeNamedType(
        SymbolAnalysisContext context,
        AnalyzerSession session)
    {
        if (context.Symbol is INamedTypeSymbol type)
            AnalyzeNamedTypeCore(context, session, type);
    }

    private static void Report(
        MethodBodyAnalysisContext context,
        AnalyzerSession session,
        DiagnosticDescriptor descriptor,
        Location location,
        string kind,
        params object[] messageArguments)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        var symbol = context.MethodSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(SharpProofDiagnostics.CommonBugKindProperty, kind)
            .Add(SharpProofDiagnostics.CommonBugSymbolProperty, symbol);
        properties = BaselineDiagnosticProperties.Add(
            properties,
            context.MethodSymbol,
            context.Node.SyntaxTree,
            "CommonBug",
            kind,
            descriptor.Id + "@" + location.SourceSpan.Start.ToString(CultureInfo.InvariantCulture));
        properties = ExplainDiagnosticProperties.Add(
            properties,
            location,
            kind,
            "detected",
            impliedConditionText: kind);

        var diagnostic = Diagnostic.Create(
            descriptor,
            location,
            null,
            properties,
            messageArguments);
        if (!session.Baseline.IsSuppressed(diagnostic)) context.ReportDiagnostic(diagnostic);
    }

    private static void Report(
        SymbolAnalysisContext context,
        AnalyzerSession session,
        DiagnosticDescriptor descriptor,
        ISymbol symbol,
        Location location,
        string kind,
        params object[] messageArguments)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (location.SourceTree == null) return;

        var symbolDisplay = symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(SharpProofDiagnostics.CommonBugKindProperty, kind)
            .Add(SharpProofDiagnostics.CommonBugSymbolProperty, symbolDisplay);
        properties = BaselineDiagnosticProperties.Add(
            properties,
            symbol,
            location.SourceTree,
            "CommonBug",
            kind,
            descriptor.Id + "@" + location.SourceSpan.Start.ToString(CultureInfo.InvariantCulture));
        properties = ExplainDiagnosticProperties.Add(
            properties,
            location,
            kind,
            "detected",
            impliedConditionText: kind);

        var diagnostic = Diagnostic.Create(
            descriptor,
            location,
            null,
            properties,
            messageArguments);
        if (!session.Baseline.IsSuppressed(diagnostic)) context.ReportDiagnostic(diagnostic);
    }

    private static void Report(
        SyntaxNodeAnalysisContext context,
        AnalyzerSession session,
        DiagnosticDescriptor descriptor,
        Location location,
        string kind,
        params object[] messageArguments)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        var symbol = context.SemanticModel.GetEnclosingSymbol(location.SourceSpan.Start, context.CancellationToken);
        if (symbol == null || location.SourceTree == null) return;

        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(SharpProofDiagnostics.CommonBugKindProperty, kind)
            .Add(SharpProofDiagnostics.CommonBugSymbolProperty,
                symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
        properties = BaselineDiagnosticProperties.Add(
            properties,
            symbol,
            location.SourceTree,
            "CommonBug",
            kind,
            descriptor.Id + "@" + location.SourceSpan.Start.ToString(CultureInfo.InvariantCulture));
        properties = ExplainDiagnosticProperties.Add(properties, location, kind, "detected", impliedConditionText: kind);
        var diagnostic = Diagnostic.Create(descriptor, location, null, properties, messageArguments);
        if (!session.Baseline.IsSuppressed(diagnostic)) context.ReportDiagnostic(diagnostic);
    }

    private static void Report(
        SyntaxTreeAnalysisContext context,
        AnalyzerSession session,
        DiagnosticDescriptor descriptor,
        Location location,
        string kind,
        params object[] messageArguments)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        var path = context.Tree.FilePath ?? string.Empty;
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(SharpProofDiagnostics.CommonBugKindProperty, kind)
            .Add(SharpProofDiagnostics.CommonBugSymbolProperty, "<syntax-tree>");
        properties = BaselineDiagnosticProperties.Add(
            properties,
            "<syntax-tree>",
            path,
            "CommonBug",
            kind,
            descriptor.Id + "@" + location.SourceSpan.Start.ToString(CultureInfo.InvariantCulture));
        properties = ExplainDiagnosticProperties.Add(properties, location, kind, "detected", impliedConditionText: kind);
        var diagnostic = Diagnostic.Create(descriptor, location, null, properties, messageArguments);
        if (!session.Baseline.IsSuppressed(diagnostic)) context.ReportDiagnostic(diagnostic);
    }

    private static IOperation? Unwrap(IOperation? operation)
    {
        while (operation is IConversionOperation conversion && conversion.OperatorMethod == null)
            operation = conversion.Operand;

        while (operation is IParenthesizedOperation parenthesized)
            operation = parenthesized.Operand;

        return operation;
    }

    private static bool IsTaskType(ITypeSymbol? type)
    {
        for (var current = type as INamedTypeSymbol; current != null; current = current.BaseType)
            if (string.Equals(current.Name, "Task", StringComparison.Ordinal) &&
                string.Equals(
                    current.ContainingNamespace?.ToDisplayString(),
                    "System.Threading.Tasks",
                    StringComparison.Ordinal))
                return true;

        return false;
    }

    private static bool IsTaskCompletionSourceType(ITypeSymbol? type)
    {
        return type is INamedTypeSymbol namedType &&
               string.Equals(namedType.Name, "TaskCompletionSource", StringComparison.Ordinal) &&
               string.Equals(
                   namedType.ContainingNamespace?.ToDisplayString(),
                   "System.Threading.Tasks",
                   StringComparison.Ordinal);
    }

    private static bool IsOrDerivesFrom(ITypeSymbol? type, string metadataName)
    {
        for (var current = type as INamedTypeSymbol; current != null; current = current.BaseType)
        {
            var namespaceName = current.ContainingNamespace?.ToDisplayString();
            var candidate = string.IsNullOrEmpty(namespaceName)
                ? current.MetadataName
                : namespaceName + "." + current.MetadataName;
            if (string.Equals(candidate, metadataName, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    private static AttributeData? FindAttribute(ISymbol symbol, INamedTypeSymbol? attributeType)
    {
        if (attributeType == null) return null;

        return symbol.GetAttributes().FirstOrDefault(attribute =>
            SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType));
    }
}
