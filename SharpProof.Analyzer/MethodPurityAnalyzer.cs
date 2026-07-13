using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Analyzer.Configuration;
using SharpProof.Analyzer.Engine;
using SharpProof.Symbolic;

namespace SharpProof.Analyzer;

internal static class MethodPurityAnalyzer
{
    internal static void AnalyzeSymbolForPurity(
        MethodBodyAnalysisContext context,
        CompilationPurityService purityService,
        MissingPuritySuggestionOptions missingPuritySuggestions,
        bool emitExplanations,
        bool reportBclFallbackGuesses,
        DiagnosticBaseline baseline,
        SharpProofAttributeIdentityPolicy attributePolicy)
    {
        var methodSymbol = context.MethodSymbol;

        void Report(Diagnostic diagnostic)
        {
            AnalyzerDiagnosticReporter.ReportIfNotSuppressed(context, baseline, diagnostic);
        }


        if (!methodSymbol.Locations.Any(static location => location.IsInSource)) return;


        var enforcePureAttributeSymbol =
            attributePolicy.GetAppliedAttributeSymbol(methodSymbol, "EnforcePureAttribute") ??
            attributePolicy.ResolveAttributeSymbol(context.SemanticModel.Compilation, "EnforcePureAttribute");
        var pureAttributeSymbol =
            attributePolicy.GetAppliedAttributeSymbol(methodSymbol, "PureAttribute") ??
            attributePolicy.ResolveAttributeSymbol(context.SemanticModel.Compilation, "PureAttribute");

        if (enforcePureAttributeSymbol == null && pureAttributeSymbol == null) return;


        var allowSynchronizationAttributeSymbol =
            attributePolicy.GetAppliedAttributeSymbol(methodSymbol, "AllowSynchronizationAttribute") ??
            attributePolicy.ResolveAttributeSymbol(context.SemanticModel.Compilation, "AllowSynchronizationAttribute");

        var hasEnforcePureAttribute = attributePolicy.HasAttribute(methodSymbol, "EnforcePureAttribute");
        var hasPureAttribute = attributePolicy.HasAttribute(methodSymbol, "PureAttribute");
        var hasDirectPureExternalAttribute = attributePolicy.HasAttribute(methodSymbol, "PureExternalAttribute");
        var hasDirectImpureAttribute = attributePolicy.HasAttribute(methodSymbol, "ImpureAttribute");
        var policy = PurityPolicyResolver.Resolve(methodSymbol, context.SemanticModel.Compilation, attributePolicy);
        var hasPureExternalAttribute = policy.Decision == PurityPolicyDecision.Pure &&
                                       policy.Winner?.Source is
                                           "member_pure_external_attribute" or
                                           "recognized_external_pure_attribute" or
                                           "assembly_pure_external_attribute";
        var hasImpureAttribute = policy.Decision == PurityPolicyDecision.Impure &&
                                 policy.Winner?.Source is
                                     "member_impure_attribute" or
                                     "assembly_impure_attribute";
        var hasInheritedPurityEnforcement =
            HasInheritedPurityEnforcement(methodSymbol, enforcePureAttributeSymbol, pureAttributeSymbol);
        var hasInheritedImpureAttribute = MethodContractHierarchy.EnumerateSources(
                methodSymbol,
                context.CancellationToken)
            .Skip(1)
            .Any(candidate => attributePolicy.HasAttribute(candidate, "ImpureAttribute"));

        if (HasConflictingPurityAttributes(
                hasEnforcePureAttribute,
                hasPureAttribute,
                hasDirectPureExternalAttribute,
                hasDirectImpureAttribute) ||
            hasDirectImpureAttribute && hasInheritedPurityEnforcement ||
            hasInheritedImpureAttribute &&
            (hasEnforcePureAttribute || hasPureAttribute || hasDirectPureExternalAttribute))
        {
            var conflictingDiagnosticLocation = AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(context.Node);
            if (conflictingDiagnosticLocation != null)
            {
                var properties = BaselineDiagnosticProperties.Add(
                    ImmutableDictionary<string, string?>.Empty,
                    methodSymbol,
                    context.Node.SyntaxTree,
                    "PurityAttributeConflict",
                    evidenceKey: "conflicting_purity_attributes");
                properties = ExplainDiagnosticProperties.Add(
                    properties,
                    conflictingDiagnosticLocation,
                    "purity attributes",
                    "invalid",
                    "conflicting_purity_attributes");
                var conflicting = Diagnostic.Create(
                    SharpProofDiagnostics.ConflictingPurityAttributesRule,
                    conflictingDiagnosticLocation,
                    null,
                    properties, methodSymbol.Name);
                Report(conflicting);
            }
        }


        var hasPurityEnforcementAttribute = hasEnforcePureAttribute || hasPureAttribute || hasPureExternalAttribute ||
                                            HasPurityEnforcement(methodSymbol, enforcePureAttributeSymbol,
                                                pureAttributeSymbol);
        var hasAllowSynchronization =
            attributePolicy.HasAttribute(methodSymbol, "AllowSynchronizationAttribute");

        // Report if [AllowSynchronization] is present without [EnforcePure]/[Pure]
        if (hasAllowSynchronization && !hasPurityEnforcementAttribute)
        {
            var allowSyncLocation = AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(context.Node);
            if (allowSyncLocation != null)
            {
                var properties = BaselineDiagnosticProperties.Add(
                    ImmutableDictionary<string, string?>.Empty,
                    methodSymbol,
                    context.Node.SyntaxTree,
                    "AllowSynchronizationContract",
                    evidenceKey: "allow_synchronization_without_purity");
                properties = ExplainDiagnosticProperties.Add(
                    properties,
                    allowSyncLocation,
                    "[AllowSynchronization]",
                    "invalid",
                    "missing_purity_attribute");
                var diag = Diagnostic.Create(
                    SharpProofDiagnostics.AllowSynchronizationWithoutPurityAttributeRule,
                    allowSyncLocation,
                    null,
                    properties, methodSymbol.Name);
                Report(diag);
            }
        }

        // Report redundant [AllowSynchronization] if present but no synchronization constructs exist in the body
        if (hasAllowSynchronization && hasPurityEnforcementAttribute)
        {
            var containsLock = context.Node.DescendantNodes().OfType<LockStatementSyntax>().Any();
            if (!containsLock)
            {
                var redundantLoc = AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(context.Node);
                if (redundantLoc != null)
                {
                    var properties = BaselineDiagnosticProperties.Add(
                        ImmutableDictionary<string, string?>.Empty,
                        methodSymbol,
                        context.Node.SyntaxTree,
                        "AllowSynchronizationContract",
                        evidenceKey: "redundant_allow_synchronization");
                    properties = ExplainDiagnosticProperties.Add(
                        properties,
                        redundantLoc,
                        "[AllowSynchronization]",
                        "redundant");
                    var redundant = Diagnostic.Create(
                        SharpProofDiagnostics.RedundantAllowSynchronizationRule,
                        redundantLoc,
                        null,
                        properties, methodSymbol.Name);
                    Report(redundant);
                }
            }
        }

        // Abstract declarations define contracts but have no implementation body to verify.
        // Their call sites remain conservative through dispatch-target analysis.
        if (methodSymbol.IsAbstract) return;


        var effectiveMissingPuritySuggestions = AnalyzerConfiguration.GetMissingPuritySuggestionOptions(
            context.Options,
            context.Node.SyntaxTree,
            missingPuritySuggestions);

        if (!hasPurityEnforcementAttribute &&
            (hasImpureAttribute || !effectiveMissingPuritySuggestions.IsEnabled))
            return;

        var enforceOrPureAttributeSymbol =
            GetEffectivePurityAttributeSymbol(enforcePureAttributeSymbol, pureAttributeSymbol);
        PurityAnalysisEngine.PurityAnalysisResult purityResult;
        try
        {
            purityResult = purityService.GetPurity(
                methodSymbol,
                context.SemanticModel,
                enforceOrPureAttributeSymbol,
                allowSynchronizationAttributeSymbol,
                context.CancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && !SymbolicErrorClassifier.IsFatal(ex))
        {
            var error = SymbolicErrorClassifier.FromException(ex);
            purityResult = PurityAnalysisEngine.PurityAnalysisResult.ImpureUnknownLocation.WithEvidence(
                PurityAnalysisEngine.PurityEvidence.Create(
                    "analysis_failure",
                    nameof(CompilationPurityService),
                    symbol: methodSymbol,
                    catalogSource: error.Code));
        }
        var isPure = purityResult.IsPure;

        var effectiveEmitExplanations = AnalyzerConfiguration.GetEmitExplanations(
            context.Options,
            context.Node.SyntaxTree,
            emitExplanations);
        var effectiveReportBclFallbackGuesses = AnalyzerConfiguration.GetReportBclFallbackGuesses(
            context.Options,
            context.Node.SyntaxTree,
            reportBclFallbackGuesses);

        if (!isPure && hasPurityEnforcementAttribute)
        {
            var diagnosticLocation = AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(context.Node);

            if (diagnosticLocation != null)
            {
                var properties = BaselineDiagnosticProperties.Add(
                    AnalysisTruncationDiagnosticProperties.Add(
                        purityResult.Evidence.ToDiagnosticProperties(),
                        purityResult.AnalysisTruncation),
                    methodSymbol,
                    context.Node.SyntaxTree,
                    purityResult.Evidence.OperationKind,
                    evidenceKey: CreatePurityEvidenceKey(purityResult.Evidence));
                properties = ExplainDiagnosticProperties.Add(
                    properties,
                    diagnosticLocation,
                    hasEnforcePureAttribute ? "[EnforcePure]" : "[Pure]",
                    "not_proven",
                    GetPurityUnknownReason(purityResult.Evidence));
                var diagnostic = Diagnostic.Create(
                    SharpProofDiagnostics.PurityNotVerifiedRule,
                    diagnosticLocation,
                    null,
                    properties, methodSymbol.Name);
                if (baseline.IsSuppressed(diagnostic)) return;

                context.ReportDiagnostic(diagnostic);
                if (effectiveEmitExplanations)
                {
                    var explanation = Diagnostic.Create(
                        SharpProofDiagnostics.PurityExplanationRule,
                        diagnosticLocation,
                        null,
                        properties, methodSymbol.Name, purityResult.Evidence.ToSummary());
                    Report(explanation);
                }

                if ((effectiveEmitExplanations || effectiveReportBclFallbackGuesses) &&
                    !string.IsNullOrEmpty(purityResult.Evidence.BclFallbackGuess))
                {
                    var fallbackDiagnostic = Diagnostic.Create(
                        SharpProofDiagnostics.BclFallbackGuessRule,
                        diagnosticLocation,
                        null,
                        properties, methodSymbol.Name, purityResult.Evidence.BclFallbackGuess,
                        BclPurityFallbackHeuristics.GetDisplayReason(purityResult.Evidence.BclFallbackReason));
                    Report(fallbackDiagnostic);
                }
            }
        }

        else if (effectiveMissingPuritySuggestions.IsEnabled && isPure && !hasPurityEnforcementAttribute &&
                 !hasAllowSynchronization && !hasImpureAttribute)
        {
            if (context.Node is LocalFunctionStatementSyntax or PropertyDeclarationSyntax
                or IndexerDeclarationSyntax) return;

            if (!ShouldReportMissingEnforcePure(context, methodSymbol, effectiveMissingPuritySuggestions)) return;

            var isCompilerGeneratedSetter = false;
            if (methodSymbol.MethodKind == MethodKind.PropertySet &&
                context.Node is AccessorDeclarationSyntax setterNode)
                if (setterNode.Body == null && setterNode.ExpressionBody == null)
                    isCompilerGeneratedSetter = true;

            if (!isCompilerGeneratedSetter)
            {
                var diagnosticLocation = AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(context.Node);

                if (diagnosticLocation != null)
                {
                    var properties = BaselineDiagnosticProperties.Add(
                        ImmutableDictionary<string, string?>.Empty,
                        methodSymbol,
                        context.Node.SyntaxTree,
                        "MissingEnforcePureAttribute",
                        evidenceKey: "missing_enforce_pure");
                    properties = ExplainDiagnosticProperties.Add(
                        properties,
                        diagnosticLocation,
                        "[EnforcePure]",
                        "suggested");
                    var diagnostic = Diagnostic.Create(
                        SharpProofDiagnostics.MissingEnforcePureAttributeRule,
                        diagnosticLocation,
                        null,
                        properties, methodSymbol.Name);
                    Report(diagnostic);
                }
            }
        }
    }

    private static bool ShouldReportMissingEnforcePure(
        MethodBodyAnalysisContext context,
        IMethodSymbol methodSymbol,
        MissingPuritySuggestionOptions options)
    {
        if (!ShouldSuggestMissingEnforcePure(methodSymbol)) return false;

        if (!MatchesSuggestionScope(methodSymbol, options.Scope)) return false;

        if (options.ExcludeGeneratedFiles && IsGeneratedCode(context.Node)) return false;

        if (options.ExcludeTestFiles && IsTestCode(methodSymbol, context.Node.SyntaxTree.FilePath)) return false;

        if (options.NamespaceFilters.Count > 0 &&
            !MatchesNamespaceFilter(methodSymbol, options.NamespaceFilters)) return false;

        if (options.MinimumComplexity > 0 && GetMethodComplexity(context.Node) < options.MinimumComplexity)
            return false;

        return true;
    }

    private static string CreatePurityEvidenceKey(PurityAnalysisEngine.PurityEvidence evidence)
    {
        return evidence.Category +
               "|" +
               evidence.RuleName +
               "|" +
               evidence.OperationKind +
               "|" +
               evidence.Symbol +
               "|" +
               evidence.CatalogSource +
               "|" +
               evidence.CalleeChain +
               "|" +
               evidence.BclFallbackGuess +
               "|" +
               evidence.BclFallbackReason;
    }

    private static string? GetPurityUnknownReason(PurityAnalysisEngine.PurityEvidence evidence)
    {
        if (evidence.UnknownReasonInfo.IsUnknown) return evidence.UnknownReasonInfo.Code;

        return string.IsNullOrWhiteSpace(evidence.Category) ? null : evidence.Category;
    }

    private static bool HasConflictingPurityAttributes(
        bool hasEnforcePureAttribute,
        bool hasPureAttribute,
        bool hasPureExternalAttribute,
        bool hasImpureAttribute)
    {
        if (hasImpureAttribute && (hasEnforcePureAttribute || hasPureAttribute || hasPureExternalAttribute))
            return true;

        if (hasPureExternalAttribute && (hasEnforcePureAttribute || hasPureAttribute)) return true;

        return hasEnforcePureAttribute && hasPureAttribute;
    }

    private static bool MatchesSuggestionScope(IMethodSymbol methodSymbol, MissingPuritySuggestionScope scope)
    {
        switch (scope)
        {
            case MissingPuritySuggestionScope.All:
                return true;
            case MissingPuritySuggestionScope.Public:
                return methodSymbol.DeclaredAccessibility == Accessibility.Public ||
                       methodSymbol.DeclaredAccessibility == Accessibility.Protected ||
                       methodSymbol.DeclaredAccessibility == Accessibility.ProtectedOrInternal;
            case MissingPuritySuggestionScope.Internal:
                return methodSymbol.DeclaredAccessibility == Accessibility.Internal ||
                       methodSymbol.DeclaredAccessibility == Accessibility.ProtectedAndInternal ||
                       methodSymbol.DeclaredAccessibility == Accessibility.ProtectedOrInternal;
            default:
                return false;
        }
    }

    private static bool IsGeneratedCode(SyntaxNode node)
    {
        var filePath = node.SyntaxTree.FilePath;
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            var fileName = Path.GetFileName(filePath);
            if (fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase))
                return true;

            var normalized = filePath.Replace('/', Path.DirectorySeparatorChar);
            if (normalized.IndexOf(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }

        var root = node.SyntaxTree.GetRoot();
        return root.GetLeadingTrivia().ToString().IndexOf("<auto-generated", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsTestCode(IMethodSymbol methodSymbol, string filePath)
    {
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            var normalized = filePath.Replace('/', Path.DirectorySeparatorChar);
            var fileName = Path.GetFileNameWithoutExtension(filePath) ?? string.Empty;
            if (normalized.StartsWith("test" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("tests" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                normalized.IndexOf(Path.DirectorySeparatorChar + "test" + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized.IndexOf(Path.DirectorySeparatorChar + "tests" + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) >= 0 ||
                fileName.EndsWith("Test", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith("Tests", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        var containingTypeName = methodSymbol.ContainingType?.Name;
        if (!string.IsNullOrWhiteSpace(containingTypeName))
        {
            var typeName = containingTypeName!;
            if (typeName.EndsWith("Test", StringComparison.OrdinalIgnoreCase) ||
                typeName.EndsWith("Tests", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        var namespaceName = methodSymbol.ContainingNamespace?.ToDisplayString();
        return IsTestLikeName(namespaceName);
    }

    private static bool IsTestLikeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        var name = value!;
        return name.Equals("Test", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Tests", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".Test", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase) ||
               name.IndexOf(".Test.", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf(".Tests.", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool MatchesNamespaceFilter(IMethodSymbol methodSymbol, ImmutableHashSet<string> namespaceFilters)
    {
        var namespaceName = methodSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        foreach (var filter in namespaceFilters)
        {
            if (filter.Length == 0) continue;

            if (namespaceName.Equals(filter, StringComparison.Ordinal) ||
                namespaceName.StartsWith(filter + ".", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static int GetMethodComplexity(SyntaxNode node)
    {
        var body = node switch
        {
            MethodDeclarationSyntax m => (SyntaxNode?)m.Body ?? m.ExpressionBody?.Expression,
            ConstructorDeclarationSyntax c => (SyntaxNode?)c.Body ?? c.ExpressionBody?.Expression,
            OperatorDeclarationSyntax o => (SyntaxNode?)o.Body ?? o.ExpressionBody?.Expression,
            ConversionOperatorDeclarationSyntax c => (SyntaxNode?)c.Body ?? c.ExpressionBody?.Expression,
            AccessorDeclarationSyntax a => (SyntaxNode?)a.Body ?? a.ExpressionBody?.Expression,
            LocalFunctionStatementSyntax l => (SyntaxNode?)l.Body ?? l.ExpressionBody?.Expression,
            _ => node
        };

        if (body == null) return 0;

        var complexity = 0;
        foreach (var descendant in CSharpSyntaxFacts.DescendantNodesInExecution(body))
            if (descendant is StatementSyntax ||
                descendant is BinaryExpressionSyntax ||
                descendant is ConditionalExpressionSyntax ||
                descendant is SwitchExpressionSyntax ||
                descendant is InvocationExpressionSyntax ||
                descendant is ObjectCreationExpressionSyntax)
                complexity++;

        return complexity;
    }

    private static bool ShouldSuggestMissingEnforcePure(IMethodSymbol methodSymbol)
    {
        if (methodSymbol.MethodKind == MethodKind.Conversion) return false;

        if (methodSymbol.MethodKind == MethodKind.Constructor &&
            HasAttributeByName(methodSymbol, "SetsRequiredMembersAttribute"))
            return false;

        if (methodSymbol.ContainingType?.TypeKind == TypeKind.Interface ||
            (!methodSymbol.IsStatic && ImplementsInstanceInterfaceMember(methodSymbol)) ||
            methodSymbol.IsVirtual ||
            methodSymbol.IsAbstract ||
            methodSymbol.IsOverride)
            return false;

        return true;
    }

    private static bool ImplementsInstanceInterfaceMember(IMethodSymbol methodSymbol)
    {
        if (methodSymbol.IsStatic || methodSymbol.ContainingType == null) return false;

        if (methodSymbol.ExplicitInterfaceImplementations.Length > 0) return true;

        return EnumerateImplementedInterfaceMethods(methodSymbol).Any();
    }

    private static bool HasPurityEnforcement(IMethodSymbol methodSymbol, INamedTypeSymbol? enforcePureAttributeSymbol,
        INamedTypeSymbol? pureAttributeSymbol)
    {
        return HasPurityEnforcement(methodSymbol, enforcePureAttributeSymbol, pureAttributeSymbol,
            new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default));
    }

    private static bool HasInheritedPurityEnforcement(
        IMethodSymbol methodSymbol,
        INamedTypeSymbol? enforcePureAttributeSymbol,
        INamedTypeSymbol? pureAttributeSymbol)
    {
        var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default)
        {
            methodSymbol.OriginalDefinition
        };

        if (methodSymbol.OverriddenMethod != null &&
            HasPurityEnforcement(
                methodSymbol.OverriddenMethod,
                enforcePureAttributeSymbol,
                pureAttributeSymbol,
                visited))
            return true;

        if (methodSymbol.ContainingType == null) return false;

        foreach (var interfaceMember in EnumerateImplementedInterfaceMethods(methodSymbol))
            if (HasPurityEnforcement(
                    interfaceMember,
                    enforcePureAttributeSymbol,
                    pureAttributeSymbol,
                    visited))
                return true;

        return false;
    }

    private static bool HasPurityEnforcement(
        IMethodSymbol methodSymbol,
        INamedTypeSymbol? enforcePureAttributeSymbol,
        INamedTypeSymbol? pureAttributeSymbol,
        HashSet<IMethodSymbol> visitedMethods)
    {
        methodSymbol = methodSymbol.OriginalDefinition;
        if (!visitedMethods.Add(methodSymbol)) return false;

        foreach (var attributeData in methodSymbol.GetAttributes())
        {
            var attributeClass = attributeData.AttributeClass?.OriginalDefinition;
            if (enforcePureAttributeSymbol != null &&
                SymbolEqualityComparer.Default.Equals(attributeClass, enforcePureAttributeSymbol)) return true;
            if (pureAttributeSymbol != null &&
                SymbolEqualityComparer.Default.Equals(attributeClass, pureAttributeSymbol)) return true;
        }

        if (methodSymbol.OverriddenMethod != null &&
            HasPurityEnforcement(methodSymbol.OverriddenMethod, enforcePureAttributeSymbol, pureAttributeSymbol,
                visitedMethods))
            return true;

        foreach (var interfaceMember in EnumerateImplementedInterfaceMethods(methodSymbol))
            if (HasPurityEnforcement(
                    interfaceMember,
                    enforcePureAttributeSymbol,
                    pureAttributeSymbol,
                    visitedMethods))
                return true;

        return false;
    }

    private static IEnumerable<IMethodSymbol> EnumerateImplementedInterfaceMethods(IMethodSymbol methodSymbol)
    {
        var containingType = methodSymbol.ContainingType;
        if (containingType == null) yield break;

        foreach (var interfaceType in containingType.AllInterfaces)
            foreach (var interfaceMember in interfaceType.GetMembers(methodSymbol.Name).OfType<IMethodSymbol>())
                if (containingType.FindImplementationForInterfaceMember(interfaceMember) is IMethodSymbol
                        implementationMethod &&
                    SymbolEqualityComparer.Default.Equals(
                        implementationMethod.OriginalDefinition,
                        methodSymbol.OriginalDefinition))
                    yield return interfaceMember;
    }

    private static bool HasAttributeByName(IMethodSymbol methodSymbol, string attributeTypeName)
    {
        foreach (var attributeData in SymbolAttributeTraversal.GetAttributes(
                     methodSymbol,
                     AssociatedAttributePolicy.AnyAssociatedSymbol))
        {
            var attributeClass = attributeData.AttributeClass;
            if (attributeClass != null &&
                string.Equals(attributeClass.Name, attributeTypeName, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    private static INamedTypeSymbol GetEffectivePurityAttributeSymbol(INamedTypeSymbol? enforcePureAttributeSymbol,
        INamedTypeSymbol? pureAttributeSymbol)
    {
        return enforcePureAttributeSymbol ?? pureAttributeSymbol!;
    }

}
