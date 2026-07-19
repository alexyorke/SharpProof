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
    private const int ConfiguredPureRank = 30;
    private const int BuiltInPureRank = 50;
    private const int DirectImpureWinnerPriority = 10;
    private const int DirectPureExternalWinnerPriority = 20;
    private const int AssemblyImpureWinnerPriority = 30;
    private const int RecognizedExternalPureWinnerPriority = 40;
    private const int AssemblyPureExternalWinnerPriority = 50;
    private const int ConfiguredImpureBoundaryWinnerPriority = 60;
    private const int ConfiguredImpureMemberWinnerPriority = 70;
    private const int SelectedGeneratedOverrideWinnerPriority = 80;
    private const int ConfiguredPureWinnerPriority = 90;
    private const int SelectedGeneratedWinnerPriority = 100;
    private const int BuiltInImpureWinnerPriority = 110;
    private const int BuiltInPureWinnerPriority = 120;

    private const string ImpureAttributeName = "SharpProof.Attributes.ImpureAttribute";
    private const string PureExternalAttributeName = "SharpProof.Attributes.PureExternalAttribute";
    private const string JetBrainsPureAttributeName = "JetBrains.Annotations.PureAttribute";
    private const string CodeContractsPureAttributeName = "System.Diagnostics.Contracts.PureAttribute";

    internal static void Analyze(MethodBodyAnalysisContext context, AnalyzerSession session)
    {
        var mode = session.Configuration.TrustedBoundaryReviewMode;
        if (mode == TrustedBoundaryReviewMode.Off) return;

        foreach (var operation in context.Snapshot.VisibleOperations)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            foreach (var symbol in GetReferencedBoundarySymbols(operation))
            {
                foreach (var finding in Evaluate(
                             symbol,
                             operation.Syntax.GetLocation(),
                             context.SemanticModel.Compilation,
                             session.Configuration))
                {
                    if (mode == TrustedBoundaryReviewMode.Used &&
                        !string.Equals(finding.Disposition, "applied", StringComparison.Ordinal))
                        continue;

                    session.RecordTrustedBoundaryFinding(finding);
                }
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
        Compilation compilation,
        AnalyzerConfiguration configuration)
    {
        var symbol = referencedSymbol.OriginalDefinition;
        var method = symbol as IMethodSymbol ?? (symbol as IPropertySymbol)?.GetMethod?.OriginalDefinition;
        var symbolDisplay = symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        var candidates = ImmutableArray.CreateBuilder<TrustCandidate>();

        var directAttributes = GetDirectAttributes(symbol).ToImmutableArray();
        var directImpureAttribute = FindAttribute(directAttributes, ImpureAttributeName);
        var directPureExternalAttribute = FindAttribute(directAttributes, PureExternalAttributeName);
        if (directImpureAttribute != null)
            candidates.Add(new TrustCandidate(
                "member_impure_attribute",
                GetAttributeValue(directImpureAttribute),
                "impure",
                DirectImpureRank,
                DirectImpureWinnerPriority,
                false));
        if (directPureExternalAttribute != null)
            candidates.Add(new TrustCandidate(
                "member_pure_external_attribute",
                GetAttributeValue(directPureExternalAttribute),
                "pure",
                DirectPureExternalRank,
                DirectPureExternalWinnerPriority,
                true));

        foreach (var attribute in directAttributes
                     .Where(static attribute =>
                         IsAttribute(attribute, JetBrainsPureAttributeName) ||
                         IsAttribute(attribute, CodeContractsPureAttributeName))
                     .OrderBy(static attribute => GetAttributeValue(attribute), StringComparer.Ordinal))
            candidates.Add(new TrustCandidate(
                "recognized_external_pure_attribute",
                GetAttributeValue(attribute),
                "pure",
                RecognizedExternalPureRank,
                RecognizedExternalPureWinnerPriority,
                true));

        var assemblyAttributes = symbol.ContainingAssembly?.GetAttributes() ?? ImmutableArray<AttributeData>.Empty;
        var assemblyImpureAttribute = FindAttribute(assemblyAttributes, ImpureAttributeName);
        var assemblyPureExternalAttribute = FindAttribute(assemblyAttributes, PureExternalAttributeName);
        if (assemblyImpureAttribute != null)
            candidates.Add(new TrustCandidate(
                "assembly_impure_attribute",
                GetAttributeValue(assemblyImpureAttribute),
                "impure",
                AssemblyImpureRank,
                AssemblyImpureWinnerPriority,
                false));
        if (assemblyPureExternalAttribute != null)
            candidates.Add(new TrustCandidate(
                "assembly_pure_external_attribute",
                GetAttributeValue(assemblyPureExternalAttribute),
                "pure",
                AssemblyPureExternalRank,
                AssemblyPureExternalWinnerPriority,
                true));

        var hasConfiguredPure = TryGetConfiguredKnownPureMember(
            symbol,
            method,
            configuration,
            out var configuredPureValue);
        if (hasConfiguredPure)
            candidates.Add(new TrustCandidate(
                "config_known_pure_method",
                configuredPureValue,
                "pure",
                ConfiguredPureRank,
                ConfiguredPureWinnerPriority,
                true));

        if (!hasConfiguredPure &&
            TryGetConfiguredImpureBoundary(
                symbol,
                method,
                configuration,
                out var boundarySource,
                out var boundaryValue))
            candidates.Add(new TrustCandidate(
                boundarySource,
                boundaryValue,
                "impure",
                ConfiguredImpureBoundaryRank,
                ConfiguredImpureBoundaryWinnerPriority,
                false));

        if (TryGetConfiguredKnownImpureMember(
                symbol,
                method,
                configuration,
                out var configuredImpureValue))
            candidates.Add(new TrustCandidate(
                "config_known_impure_method",
                configuredImpureValue,
                "impure",
                ConfiguredImpureMemberRank,
                ConfiguredImpureMemberWinnerPriority,
                false));

        var generatedEntries = method == null
            ? ImmutableArray<EffectSummaryCatalog.TrustedPurityEntry>.Empty
            : EffectSummaryCatalog.Current.GetTrustedPurityEntries(method, compilation);
        foreach (var entry in generatedEntries)
            candidates.Add(new TrustCandidate(
                entry.Source,
                entry.Value,
                entry.Classification.Classification,
                GeneratedSummaryRank,
                entry.IsSelected && hasConfiguredPure && entry.Classification.IsNonPure
                    ? SelectedGeneratedOverrideWinnerPriority
                    : entry.IsSelected
                        ? SelectedGeneratedWinnerPriority
                        : int.MaxValue,
                entry.Classification.IsPure));

        if (TryGetBuiltInImpureMember(symbol, method, out var builtInImpureValue))
            candidates.Add(new TrustCandidate(
                "built_in_purity_catalog",
                builtInImpureValue,
                "impure",
                BuiltInImpureRank,
                BuiltInImpureWinnerPriority,
                false));

        if (TryGetBuiltInKnownPureMember(symbol, method, out var builtInPureValue))
            candidates.Add(new TrustCandidate(
                "built_in_purity_catalog",
                builtInPureValue,
                "pure",
                BuiltInPureRank,
                BuiltInPureWinnerPriority,
                true));

        var reviewCandidates = candidates.Where(static candidate => candidate.IsReviewable).ToImmutableArray();
        if (reviewCandidates.IsEmpty) return ImmutableArray<TrustedBoundaryReviewFinding>.Empty;

        var winner = candidates
            .Where(static candidate => candidate.WinnerPriority != int.MaxValue)
            .OrderBy(static candidate => candidate.WinnerPriority)
            .ThenBy(static candidate => candidate.Rank)
            .ThenBy(static candidate => candidate.Value, StringComparer.Ordinal)
            .First();
        var findings = ImmutableArray.CreateBuilder<TrustedBoundaryReviewFinding>(reviewCandidates.Length);
        foreach (var candidate in reviewCandidates)
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

    private static bool IsApplied(TrustCandidate candidate, TrustCandidate winner)
    {
        return string.Equals(candidate.Source, winner.Source, StringComparison.Ordinal) &&
               string.Equals(candidate.Value, winner.Value, StringComparison.Ordinal) &&
               string.Equals(candidate.Classification, winner.Classification, StringComparison.Ordinal);
    }

    private static IEnumerable<ISymbol> GetReferencedBoundarySymbols(IOperation operation)
    {
        switch (operation)
        {
            case IInvocationOperation invocation:
                yield return invocation.TargetMethod;
                break;
            case IObjectCreationOperation { Constructor: { } constructor }:
                yield return constructor;
                break;
            case IPropertyReferenceOperation property:
                var isSimpleWrite = property.Parent is ISimpleAssignmentOperation simpleAssignment &&
                                    ReferenceEquals(simpleAssignment.Target, property);
                var isReadWrite = property.Parent is ICompoundAssignmentOperation compoundAssignment &&
                                      ReferenceEquals(compoundAssignment.Target, property) ||
                                  property.Parent is IIncrementOrDecrementOperation increment &&
                                      ReferenceEquals(increment.Target, property) ||
                                  property.Parent is ICoalesceAssignmentOperation coalesceAssignment &&
                                      ReferenceEquals(coalesceAssignment.Target, property);

                if (!isSimpleWrite && property.Property.GetMethod is { } getter) yield return getter;
                if ((isSimpleWrite || isReadWrite) && property.Property.SetMethod is { } setter) yield return setter;
                break;
            case IFieldReferenceOperation field:
                yield return field.Field;
                break;
            case IBinaryOperation { OperatorMethod: { } binaryOperator }:
                yield return binaryOperator;
                break;
            case IUnaryOperation { OperatorMethod: { } unaryOperator }:
                yield return unaryOperator;
                break;
            case IConversionOperation { OperatorMethod: { } conversionOperator }:
                yield return conversionOperator;
                break;
            case IIncrementOrDecrementOperation { OperatorMethod: { } incrementOperator }:
                yield return incrementOperator;
                break;
            case ICompoundAssignmentOperation { OperatorMethod: { } compoundOperator }:
                yield return compoundOperator;
                break;
        }
    }

    private static bool TryGetConfiguredKnownPureMember(
        ISymbol symbol,
        IMethodSymbol? method,
        AnalyzerConfiguration configuration,
        out string configuredValue)
    {
        if (ImpurityCatalog.TryGetConfiguredKnownPureMember(symbol, configuration, out configuredValue)) return true;
        return method != null &&
               !SymbolEq.AreEqual(symbol, method) &&
               ImpurityCatalog.TryGetConfiguredKnownPureMember(method, configuration, out configuredValue);
    }

    private static bool TryGetConfiguredKnownImpureMember(
        ISymbol symbol,
        IMethodSymbol? method,
        AnalyzerConfiguration configuration,
        out string configuredValue)
    {
        if (ImpurityCatalog.TryGetConfiguredKnownImpureMember(symbol, configuration, out configuredValue)) return true;
        return method != null &&
               !SymbolEq.AreEqual(symbol, method) &&
               ImpurityCatalog.TryGetConfiguredKnownImpureMember(method, configuration, out configuredValue);
    }

    private static bool TryGetConfiguredImpureBoundary(
        ISymbol symbol,
        IMethodSymbol? method,
        AnalyzerConfiguration configuration,
        out string source,
        out string configuredValue)
    {
        if (ImpurityCatalog.TryGetConfiguredImpureBoundary(
                symbol,
                configuration,
                out source,
                out configuredValue))
            return true;
        return method != null &&
               !SymbolEq.AreEqual(symbol, method) &&
               ImpurityCatalog.TryGetConfiguredImpureBoundary(
                   method,
                   configuration,
                   out source,
                   out configuredValue);
    }

    private static bool TryGetBuiltInKnownPureMember(
        ISymbol symbol,
        IMethodSymbol? method,
        out string catalogValue)
    {
        if (ImpurityCatalog.TryGetBuiltInKnownPureMember(symbol, out catalogValue)) return true;
        return method != null &&
               !SymbolEq.AreEqual(symbol, method) &&
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

        if (method != null && !SymbolEq.AreEqual(symbol, method))
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
        var associatedAttributePolicy = symbol is IMethodSymbol
            ? AssociatedAttributePolicy.AnyAssociatedSymbol
            : AssociatedAttributePolicy.None;
        foreach (var attribute in SymbolAttributeTraversal.GetAttributes(symbol, associatedAttributePolicy))
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
        int WinnerPriority,
        bool IsReviewable);
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
