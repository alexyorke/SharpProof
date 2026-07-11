using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Analyzer.Configuration;
using SharpProof.Analyzer.Engine;

namespace SharpProof.Analyzer;

internal static class TrustedBoundaryReviewAnalyzer
{
    private const int DirectImpureRank = 10;
    private const int DirectPureExternalRank = 11;
    private const int AssemblyImpureRank = 12;
    private const int RecognizedExternalPureRank = 13;
    private const int AssemblyPureExternalRank = 14;
    private const int ConfiguredImpureBoundaryRank = 20;
    private const int ConfiguredImpureMemberRank = 30;
    private const int GeneratedSummaryRank = 40;
    private const int BuiltInImpureRank = 50;
    private const int KnownPureRank = 60;

    private const string ImpureAttributeName = "SharpProof.Attributes.ImpureAttribute";
    private const string PureExternalAttributeName = "SharpProof.Attributes.PureExternalAttribute";
    private const string JetBrainsPureAttributeName = "JetBrains.Annotations.PureAttribute";
    private const string CodeContractsPureAttributeName = "System.Diagnostics.Contracts.PureAttribute";

    internal static void Analyze(MethodBodyAnalysisContext context, AnalyzerSession session)
    {
        var mode = session.Configuration.TrustedBoundaryReviewMode;
        if (mode == TrustedBoundaryReviewMode.Off) return;

        foreach (var operation in context.State.VisibleOperations)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var symbol = GetReferencedBoundarySymbol(operation);
            if (symbol == null) continue;

            foreach (var finding in Evaluate(
                         symbol,
                         operation.Syntax.GetLocation(),
                         context.SemanticModel.Compilation))
            {
                if (mode == TrustedBoundaryReviewMode.Used &&
                    !string.Equals(finding.Disposition, "applied", StringComparison.Ordinal))
                    continue;

                session.RecordTrustedBoundaryFinding(finding);
            }
        }
    }

    internal static void ReportDiagnostics(CompilationAnalysisContext context, AnalyzerSession session)
    {
        foreach (var finding in session.GetTrustedBoundaryFindings())
        {
            var properties = ImmutableDictionary<string, string?>.Empty
                .Add(SharpProofDiagnostics.TrustedBoundarySymbolProperty, finding.SymbolDisplay)
                .Add(SharpProofDiagnostics.TrustedBoundarySourceProperty, finding.Source)
                .Add(SharpProofDiagnostics.TrustedBoundaryValueProperty, finding.Value)
                .Add(SharpProofDiagnostics.TrustedBoundaryDispositionProperty, finding.Disposition)
                .Add(SharpProofDiagnostics.TrustedBoundaryOverriddenByProperty, finding.OverriddenBy)
                .Add(SharpProofDiagnostics.TrustedBoundaryOverrideValueProperty, finding.OverrideValue)
                .Add(SharpProofDiagnostics.TrustedBoundaryClassificationProperty, finding.Classification);
            if (finding.Location.SourceTree != null)
                properties = BaselineDiagnosticProperties.Add(
                    properties,
                    finding.Symbol,
                    finding.Location.SourceTree,
                    "TrustedBoundaryReview",
                    evidenceKey: finding.Source + ":" + finding.Value + ":" + finding.Disposition);

            var overrideSuffix = string.IsNullOrWhiteSpace(finding.OverriddenBy)
                ? string.Empty
                : $" by '{finding.OverriddenBy}'";
            var diagnostic = Diagnostic.Create(
                SharpProofDiagnostics.TrustedBoundaryReviewRule,
                finding.Location,
                null,
                properties,
                finding.Source,
                finding.SymbolDisplay,
                finding.Disposition,
                overrideSuffix);
            if (!session.Baseline.IsSuppressed(diagnostic)) context.ReportDiagnostic(diagnostic);
        }
    }

    private static ImmutableArray<TrustedBoundaryReviewFinding> Evaluate(
        ISymbol referencedSymbol,
        Location location,
        Compilation compilation)
    {
        var symbol = referencedSymbol.OriginalDefinition;
        var method = symbol as IMethodSymbol ?? (symbol as IPropertySymbol)?.GetMethod?.OriginalDefinition;
        var symbolDisplay = symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        var candidates = ImmutableArray.CreateBuilder<TrustCandidate>();

        var directAttributes = GetDirectAttributes(symbol).ToImmutableArray();
        var directImpureAttribute = FindAttribute(directAttributes, ImpureAttributeName);
        var directPureExternalAttribute = FindAttribute(directAttributes, PureExternalAttributeName);
        if (directPureExternalAttribute != null)
            candidates.Add(new TrustCandidate(
                "member_pure_external_attribute",
                GetAttributeValue(directPureExternalAttribute),
                "pure",
                DirectPureExternalRank));

        foreach (var attribute in directAttributes
                     .Where(static attribute =>
                         IsAttribute(attribute, JetBrainsPureAttributeName) ||
                         IsAttribute(attribute, CodeContractsPureAttributeName))
                     .OrderBy(static attribute => GetAttributeValue(attribute), StringComparer.Ordinal))
            candidates.Add(new TrustCandidate(
                "recognized_external_pure_attribute",
                GetAttributeValue(attribute),
                "pure",
                RecognizedExternalPureRank));

        var assemblyAttributes = symbol.ContainingAssembly?.GetAttributes() ?? ImmutableArray<AttributeData>.Empty;
        var assemblyImpureAttribute = FindAttribute(assemblyAttributes, ImpureAttributeName);
        var assemblyPureExternalAttribute = FindAttribute(assemblyAttributes, PureExternalAttributeName);
        if (assemblyPureExternalAttribute != null)
            candidates.Add(new TrustCandidate(
                "assembly_pure_external_attribute",
                GetAttributeValue(assemblyPureExternalAttribute),
                "pure",
                AssemblyPureExternalRank));

        var hasConfiguredPure = TryGetConfiguredKnownPureMember(symbol, method, out var configuredPureValue);
        if (hasConfiguredPure)
            candidates.Add(new TrustCandidate(
                "config_known_pure_method",
                configuredPureValue,
                "pure",
                KnownPureRank));

        var generatedEntries = method == null
            ? ImmutableArray<GeneratedPurityCatalog.TrustedPurityEntry>.Empty
            : GeneratedPurityCatalog.Current.GetTrustedPurityEntries(method, compilation);
        foreach (var entry in generatedEntries.Where(static entry => entry.Classification.IsPure))
            candidates.Add(new TrustCandidate(
                entry.Source,
                entry.Value,
                entry.Classification.Classification,
                GeneratedSummaryRank,
                entry.IsSelected));

        if (TryGetBuiltInKnownPureMember(symbol, method, out var builtInPureValue))
            candidates.Add(new TrustCandidate(
                "built_in_purity_catalog",
                builtInPureValue,
                "pure",
                KnownPureRank));

        if (candidates.Count == 0) return ImmutableArray<TrustedBoundaryReviewFinding>.Empty;

        var winner = ResolveWinner(
            symbol,
            method,
            directImpureAttribute,
            directPureExternalAttribute,
            assemblyImpureAttribute,
            assemblyPureExternalAttribute,
            directAttributes,
            hasConfiguredPure,
            generatedEntries,
            candidates);
        var findings = ImmutableArray.CreateBuilder<TrustedBoundaryReviewFinding>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var applied = IsApplied(candidate, winner);
            findings.Add(new TrustedBoundaryReviewFinding(
                symbol,
                symbolDisplay,
                candidate.Source,
                candidate.Value,
                applied ? "applied" : "overridden",
                applied ? string.Empty : winner.Source,
                applied ? string.Empty : winner.Value,
                candidate.Classification,
                location));
        }

        return findings.ToImmutable();
    }

    private static TrustWinner ResolveWinner(
        ISymbol symbol,
        IMethodSymbol? method,
        AttributeData? directImpureAttribute,
        AttributeData? directPureExternalAttribute,
        AttributeData? assemblyImpureAttribute,
        AttributeData? assemblyPureExternalAttribute,
        ImmutableArray<AttributeData> directAttributes,
        bool hasConfiguredPure,
        ImmutableArray<GeneratedPurityCatalog.TrustedPurityEntry> generatedEntries,
        ImmutableArray<TrustCandidate>.Builder candidates)
    {
        if (directImpureAttribute != null)
            return new TrustWinner(
                "member_impure_attribute",
                GetAttributeValue(directImpureAttribute),
                "impure",
                DirectImpureRank);

        if (directPureExternalAttribute != null)
            return new TrustWinner(
                "member_pure_external_attribute",
                GetAttributeValue(directPureExternalAttribute),
                "pure",
                DirectPureExternalRank);

        if (assemblyImpureAttribute != null)
            return new TrustWinner(
                "assembly_impure_attribute",
                GetAttributeValue(assemblyImpureAttribute),
                "impure",
                AssemblyImpureRank);

        var recognizedExternalAttribute = directAttributes
            .Where(static attribute =>
                IsAttribute(attribute, JetBrainsPureAttributeName) ||
                IsAttribute(attribute, CodeContractsPureAttributeName))
            .OrderBy(static attribute => GetAttributeValue(attribute), StringComparer.Ordinal)
            .FirstOrDefault();
        if (recognizedExternalAttribute != null)
            return new TrustWinner(
                "recognized_external_pure_attribute",
                GetAttributeValue(recognizedExternalAttribute),
                "pure",
                RecognizedExternalPureRank);

        if (assemblyPureExternalAttribute != null)
            return new TrustWinner(
                "assembly_pure_external_attribute",
                GetAttributeValue(assemblyPureExternalAttribute),
                "pure",
                AssemblyPureExternalRank);

        if (!hasConfiguredPure &&
            TryGetConfiguredImpureBoundary(symbol, method, out var boundarySource, out var boundaryValue))
            return new TrustWinner(
                boundarySource,
                boundaryValue,
                "impure",
                ConfiguredImpureBoundaryRank);

        if (TryGetConfiguredKnownImpureMember(symbol, method, out var configuredImpureValue))
            return new TrustWinner(
                "config_known_impure_method",
                configuredImpureValue,
                "impure",
                ConfiguredImpureMemberRank);

        var selectedGeneratedEntry = generatedEntries.FirstOrDefault(static entry => entry.IsSelected);
        if (!string.IsNullOrWhiteSpace(selectedGeneratedEntry.Source))
            return new TrustWinner(
                selectedGeneratedEntry.Source,
                selectedGeneratedEntry.Value,
                selectedGeneratedEntry.Classification.Classification,
                GeneratedSummaryRank);

        if (TryGetBuiltInImpureMember(symbol, method, out var builtInImpureValue))
            return new TrustWinner(
                "built_in_purity_catalog",
                builtInImpureValue,
                "impure",
                BuiltInImpureRank);

        var knownPureCandidate = candidates.First(static candidate => candidate.Rank == KnownPureRank);
        return new TrustWinner(
            knownPureCandidate.Source,
            knownPureCandidate.Value,
            "pure",
            KnownPureRank);
    }

    private static bool IsApplied(TrustCandidate candidate, TrustWinner winner)
    {
        if (candidate.Rank < winner.Rank) return true;
        if (candidate.Rank > winner.Rank) return false;

        if (candidate.Rank == GeneratedSummaryRank)
            return candidate.IsSelected && string.Equals(winner.Classification, "pure", StringComparison.Ordinal);

        if (candidate.Rank == KnownPureRank) return true;

        return string.Equals(candidate.Source, winner.Source, StringComparison.Ordinal);
    }

    private static ISymbol? GetReferencedBoundarySymbol(IOperation operation)
    {
        return operation switch
        {
            IInvocationOperation invocation => invocation.TargetMethod,
            IObjectCreationOperation creation => creation.Constructor,
            IPropertyReferenceOperation property => property.Property,
            IFieldReferenceOperation field => field.Field,
            IBinaryOperation binary => binary.OperatorMethod,
            IUnaryOperation unary => unary.OperatorMethod,
            IConversionOperation conversion => conversion.OperatorMethod,
            IIncrementOrDecrementOperation increment => increment.OperatorMethod,
            ICompoundAssignmentOperation compoundAssignment => compoundAssignment.OperatorMethod,
            _ => null
        };
    }

    private static bool TryGetConfiguredKnownPureMember(
        ISymbol symbol,
        IMethodSymbol? method,
        out string configuredValue)
    {
        if (ImpurityCatalog.TryGetConfiguredKnownPureMember(symbol, out configuredValue)) return true;
        return method != null &&
               !SymbolEqualityComparer.Default.Equals(symbol, method) &&
               ImpurityCatalog.TryGetConfiguredKnownPureMember(method, out configuredValue);
    }

    private static bool TryGetConfiguredKnownImpureMember(
        ISymbol symbol,
        IMethodSymbol? method,
        out string configuredValue)
    {
        if (ImpurityCatalog.TryGetConfiguredKnownImpureMember(symbol, out configuredValue)) return true;
        return method != null &&
               !SymbolEqualityComparer.Default.Equals(symbol, method) &&
               ImpurityCatalog.TryGetConfiguredKnownImpureMember(method, out configuredValue);
    }

    private static bool TryGetConfiguredImpureBoundary(
        ISymbol symbol,
        IMethodSymbol? method,
        out string source,
        out string configuredValue)
    {
        if (ImpurityCatalog.TryGetConfiguredImpureBoundary(symbol, out source, out configuredValue)) return true;
        return method != null &&
               !SymbolEqualityComparer.Default.Equals(symbol, method) &&
               ImpurityCatalog.TryGetConfiguredImpureBoundary(method, out source, out configuredValue);
    }

    private static bool TryGetBuiltInKnownPureMember(
        ISymbol symbol,
        IMethodSymbol? method,
        out string catalogValue)
    {
        if (ImpurityCatalog.TryGetBuiltInKnownPureMember(symbol, out catalogValue)) return true;
        return method != null &&
               !SymbolEqualityComparer.Default.Equals(symbol, method) &&
               ImpurityCatalog.TryGetBuiltInKnownPureMember(method, out catalogValue);
    }

    private static bool TryGetBuiltInImpureMember(
        ISymbol symbol,
        IMethodSymbol? method,
        out string catalogValue)
    {
        var source = ImpurityCatalog.GetKnownImpureMemberSource(symbol);
        if (!string.IsNullOrWhiteSpace(source) &&
            !string.Equals(source, "config_known_impure", StringComparison.Ordinal))
        {
            catalogValue = source!;
            return true;
        }

        if (method != null && !SymbolEqualityComparer.Default.Equals(symbol, method))
        {
            source = ImpurityCatalog.GetKnownImpureMemberSource(method);
            if (!string.IsNullOrWhiteSpace(source) &&
                !string.Equals(source, "config_known_impure", StringComparison.Ordinal))
            {
                catalogValue = source!;
                return true;
            }
        }

        if (ImpurityCatalog.IsInImpureNamespaceOrType(symbol) ||
            (method != null && ImpurityCatalog.IsInImpureNamespaceOrType(method)))
        {
            catalogValue = "known_impure_namespace_or_type";
            return true;
        }

        catalogValue = string.Empty;
        return false;
    }

    private static IEnumerable<AttributeData> GetDirectAttributes(ISymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes()) yield return attribute;

        if (symbol is IMethodSymbol { AssociatedSymbol: { } associatedSymbol })
            foreach (var attribute in associatedSymbol.GetAttributes())
                yield return attribute;

        if (symbol is IPropertySymbol { GetMethod: { } getMethod } &&
            getMethod.DeclaringSyntaxReferences.Length == 0)
            foreach (var attribute in getMethod.GetAttributes())
                yield return attribute;
    }

    private static AttributeData? FindAttribute(
        IEnumerable<AttributeData> attributes,
        string metadataName)
    {
        return attributes.FirstOrDefault(attribute => IsAttribute(attribute, metadataName));
    }

    private static bool IsAttribute(AttributeData attribute, string metadataName)
    {
        return string.Equals(attribute.AttributeClass?.ToDisplayString(), metadataName, StringComparison.Ordinal) ||
               string.Equals(
                   attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                   "global::" + metadataName,
                   StringComparison.Ordinal);
    }

    private static string GetAttributeValue(AttributeData attribute)
    {
        return attribute.AttributeClass?.ToDisplayString() ?? "<unknown attribute>";
    }

    private readonly record struct TrustCandidate(
        string Source,
        string Value,
        string Classification,
        int Rank,
        bool IsSelected = false);

    private readonly record struct TrustWinner(
        string Source,
        string Value,
        string Classification,
        int Rank);
}

internal sealed record TrustedBoundaryReviewFinding(
    ISymbol Symbol,
    string SymbolDisplay,
    string Source,
    string Value,
    string Disposition,
    string OverriddenBy,
    string OverrideValue,
    string Classification,
    Location Location)
{
    internal string Key =>
        SymbolDisplay + "\u001f" + Source + "\u001f" + Value + "\u001f" + Disposition + "\u001f" + OverriddenBy;
}
