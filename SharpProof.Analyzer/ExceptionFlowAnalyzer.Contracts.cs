using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.Analyzer.Configuration;

namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowAnalyzer
{
    private static ImmutableArray<ExceptionContract> CollectExceptionContracts(
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel,
        SharpProofAttributeIdentityPolicy attributePolicy,
        CancellationToken cancellationToken)
    {
        var builder = ImmutableArray.CreateBuilder<ExceptionContract>();
        foreach (var attribute in attributePolicy.GetAcceptedAttributes(methodSymbol, "DoesNotThrowAttribute"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            builder.Add(new ExceptionContract(
                ExceptionContractKind.DoesNotThrow,
                ImmutableArray<ITypeSymbol>.Empty,
                "[DoesNotThrow]",
                GetAttributeArgumentText(attribute, cancellationToken),
                GetAttributeLocation(attribute, cancellationToken),
                null));
        }

        var exceptionBase = semanticModel.Compilation.GetTypeByMetadataName("System.Exception");
        foreach (var attribute in attributePolicy.GetAcceptedAttributes(methodSymbol, "AllowedExceptionsAttribute"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var allowedTypes = CollectAllowedExceptionTypes(
                attribute,
                exceptionBase,
                out var invalidReason);
            builder.Add(new ExceptionContract(
                ExceptionContractKind.AllowedExceptions,
                allowedTypes,
                "[AllowedExceptions]",
                GetAttributeArgumentText(attribute, cancellationToken),
                GetAttributeLocation(attribute, cancellationToken),
                invalidReason));
        }

        return builder.ToImmutable();
    }

    private static void AnalyzeExceptionContracts(
        MethodBodyAnalysisContext context,
        IMethodSymbol methodSymbol,
        ImmutableArray<ExceptionContract> contracts,
        ExceptionFlowQuery.MethodExceptionQueryResult? queryResult,
        DiagnosticBaseline baseline)
    {
        if (contracts.Length == 0) return;

        var validContracts = ReportAndFilterInvalidExceptionContracts(
            contracts,
            context,
            methodSymbol,
            baseline);
        if (validContracts.Length == 0 || queryResult == null) return;

        var effectiveContracts = CreateEffectiveExceptionContracts(validContracts);
        foreach (var contract in effectiveContracts)
            AnalyzeExceptionContract(context, methodSymbol, contract, queryResult.SiteEntries, baseline);
    }

    private static void AnalyzeExceptionContract(
        MethodBodyAnalysisContext context,
        IMethodSymbol methodSymbol,
        EffectiveExceptionContract contract,
        ImmutableArray<ExceptionFlowQuery.UncaughtExceptionSiteEntry> siteEntries,
        DiagnosticBaseline baseline)
    {
        foreach (var siteGroup in siteEntries.GroupBy(entry => CreateExceptionSiteKey(entry.Site),
                     StringComparer.Ordinal))
        {
            var firstEntry = siteGroup.First();
            var disallowedEvidence = new ExceptionFlowQuery.ExceptionEvidenceSet();
            foreach (var siteEntry in siteGroup)
                if (!IsAllowedByExceptionContract(contract, siteEntry.Exception))
                    disallowedEvidence.Add(siteEntry.Exception);

            if (disallowedEvidence.Count == 0) continue;

            var siteLocation = GetExceptionSiteLocation(firstEntry.Site);
            if (siteLocation == null) continue;

            var operationDisplay = GetExceptionSiteDisplay(firstEntry.Site, firstEntry.Method);
            var exceptionList = string.Join(", ", disallowedEvidence.Types);
            var properties = CreateExceptionProperties(disallowedEvidence)
                .Add(SharpProofDiagnostics.ExceptionContractAttributeProperty, contract.AttributeDisplay)
                .Add(SharpProofDiagnostics.ExceptionContractAllowedTypesProperty, FormatAllowedTypes(contract))
                .Add(SharpProofDiagnostics.ExceptionContractDisallowedTypesProperty, exceptionList);
            properties = BaselineDiagnosticProperties.Add(
                properties,
                methodSymbol,
                context.Node.SyntaxTree,
                "ExceptionContract",
                operationDisplay,
                CreateExceptionEvidenceKey(
                    contract.AttributeDisplay + ":" + CreateExceptionSiteKey(firstEntry.Site),
                    disallowedEvidence));
            properties = ExplainDiagnosticProperties.Add(
                properties,
                siteLocation,
                operationDisplay,
                "exception_contract_violation",
                exceptionList);

            var diagnostic = Diagnostic.Create(
                SharpProofDiagnostics.ExceptionContractViolationRule,
                siteLocation,
                AdditionalLocations(contract.Location),
                properties,
                methodSymbol.Name,
                contract.AttributeDisplay,
                operationDisplay,
                exceptionList);
            if (!baseline.IsSuppressed(diagnostic)) context.ReportDiagnostic(diagnostic);
        }
    }

    private static ImmutableArray<ExceptionContract> ReportAndFilterInvalidExceptionContracts(
        ImmutableArray<ExceptionContract> contracts,
        MethodBodyAnalysisContext context,
        IMethodSymbol methodSymbol,
        DiagnosticBaseline baseline)
    {
        var validContracts = ImmutableArray.CreateBuilder<ExceptionContract>(contracts.Length);
        foreach (var contract in contracts)
        {
            if (contract.InvalidReason == null)
            {
                validContracts.Add(contract);
                continue;
            }

            var diagnostic = InvalidContractArgumentDiagnostics.Create(
                contract.AttributeDisplay,
                contract.Argument,
                contract.InvalidReason,
                contract.Location ?? AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(context.Node),
                methodSymbol,
                context.Node.SyntaxTree);
            if (!baseline.IsSuppressed(diagnostic)) context.ReportDiagnostic(diagnostic);
        }

        return validContracts.ToImmutable();
    }

    private static ImmutableArray<EffectiveExceptionContract> CreateEffectiveExceptionContracts(
        ImmutableArray<ExceptionContract> validContracts)
    {
        var builder = ImmutableArray.CreateBuilder<EffectiveExceptionContract>();
        var doesNotThrow = validContracts
            .Where(static contract => contract.Kind == ExceptionContractKind.DoesNotThrow)
            .ToArray();
        if (doesNotThrow.Length > 0)
            builder.Add(new EffectiveExceptionContract(
                ExceptionContractKind.DoesNotThrow,
                ImmutableArray<ITypeSymbol>.Empty,
                "[DoesNotThrow]",
                doesNotThrow[0].Location));

        var allowedExceptions = validContracts
            .Where(static contract => contract.Kind == ExceptionContractKind.AllowedExceptions)
            .ToArray();
        if (allowedExceptions.Length > 0)
        {
            var allowedTypes = ImmutableArray.CreateBuilder<ITypeSymbol>();
            foreach (var contract in allowedExceptions)
                foreach (var allowedType in contract.AllowedTypes)
                    if (!ContainsSymbol(allowedTypes, allowedType))
                        allowedTypes.Add(allowedType);

            builder.Add(new EffectiveExceptionContract(
                ExceptionContractKind.AllowedExceptions,
                allowedTypes.ToImmutable(),
                "[AllowedExceptions]",
                allowedExceptions[0].Location));
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<ITypeSymbol> CollectAllowedExceptionTypes(
        AttributeData attribute,
        INamedTypeSymbol? exceptionBase,
        out string? invalidReason)
    {
        invalidReason = null;
        if (exceptionBase == null)
        {
            invalidReason = "could not resolve System.Exception";
            return ImmutableArray<ITypeSymbol>.Empty;
        }

        if (attribute.ConstructorArguments.Length != 1 ||
            attribute.ConstructorArguments[0].Kind != TypedConstantKind.Array)
        {
            invalidReason = "expected exception type list";
            return ImmutableArray<ITypeSymbol>.Empty;
        }

        var values = attribute.ConstructorArguments[0].Values;
        var builder = ImmutableArray.CreateBuilder<ITypeSymbol>(values.Length);
        foreach (var value in values)
        {
            if (value.Kind != TypedConstantKind.Type ||
                value.Value is not ITypeSymbol allowedType)
            {
                invalidReason = "expected System.Type arguments";
                continue;
            }

            if (!IsExceptionTypeOrSubclass(allowedType, exceptionBase))
            {
                invalidReason = "type '" + FormatType(allowedType) + "' must derive from System.Exception";
                continue;
            }

            if (!ContainsSymbol(builder, allowedType)) builder.Add(allowedType);
        }

        return builder.ToImmutable();
    }

    private static bool IsAllowedByExceptionContract(
        EffectiveExceptionContract contract,
        ExceptionFlowQuery.ExceptionCandidate exception)
    {
        if (contract.Kind == ExceptionContractKind.DoesNotThrow) return false;

        if (exception.Type == null) return false;

        foreach (var allowedType in contract.AllowedTypes)
            if (IsExceptionTypeOrSubclass(exception.Type, allowedType))
                return true;

        return false;
    }

    private static bool IsExceptionTypeOrSubclass(ITypeSymbol candidate, ITypeSymbol allowedBase)
    {
        for (var current = candidate; current != null; current = current.BaseType)
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, allowedBase.OriginalDefinition) ||
                SymbolEqualityComparer.Default.Equals(current, allowedBase))
                return true;

        return false;
    }

    private static bool ContainsSymbol(
        IEnumerable<ITypeSymbol> symbols,
        ITypeSymbol candidate)
    {
        return symbols.Any(symbol =>
            SymbolEqualityComparer.Default.Equals(symbol.OriginalDefinition, candidate.OriginalDefinition));
    }

    private static string FormatAllowedTypes(EffectiveExceptionContract contract)
    {
        return contract.Kind == ExceptionContractKind.DoesNotThrow
            ? string.Empty
            : string.Join(";", contract.AllowedTypes.Select(FormatType).OrderBy(type => type, StringComparer.Ordinal));
    }

    private static string FormatType(ITypeSymbol type)
    {
        var display = type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        return string.IsNullOrWhiteSpace(display)
            ? type.Name
            : display;
    }

    private static Location? GetAttributeLocation(
        AttributeData attribute,
        CancellationToken cancellationToken)
    {
        return attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation();
    }

    private static string GetAttributeArgumentText(
        AttributeData attribute,
        CancellationToken cancellationToken)
    {
        if (attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken) is AttributeSyntax attributeSyntax)
            return attributeSyntax.ArgumentList == null
                ? "<missing>"
                : string.Join(", ",
                    attributeSyntax.ArgumentList.Arguments.Select(static argument => argument.ToString()));

        return "<missing>";
    }

    private static IEnumerable<Location>? AdditionalLocations(Location? location)
    {
        return location == null ? null : new[] { location };
    }

    private enum ExceptionContractKind
    {
        DoesNotThrow,
        AllowedExceptions
    }

    private readonly record struct ExceptionContract(
        ExceptionContractKind Kind,
        ImmutableArray<ITypeSymbol> AllowedTypes,
        string AttributeDisplay,
        string Argument,
        Location? Location,
        string? InvalidReason);

    private readonly record struct EffectiveExceptionContract(
        ExceptionContractKind Kind,
        ImmutableArray<ITypeSymbol> AllowedTypes,
        string AttributeDisplay,
        Location? Location);
}
