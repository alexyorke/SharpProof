using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.Analyzer.Configuration;
using SharpProof.Analyzer.Engine;

namespace SharpProof.Analyzer;

internal static class MethodPurityAnalyzer
{
    internal static void AnalyzeSymbolForPurity(
        SyntaxNodeAnalysisContext context,
        CompilationPurityService purityService,
        MissingPuritySuggestionOptions missingPuritySuggestions,
        bool emitExplanations,
        bool reportBclFallbackGuesses,
        DiagnosticBaseline baseline,
        SharpProofAttributeIdentityPolicy attributePolicy)
    {
        var declaredSymbol = context.SemanticModel.GetDeclaredSymbol(context.Node, context.CancellationToken);
        var methodSymbol = declaredSymbol as IMethodSymbol;
        if (methodSymbol == null &&
            declaredSymbol is IPropertySymbol propertySymbol &&
            context.Node is PropertyDeclarationSyntax { ExpressionBody: not null } or IndexerDeclarationSyntax
            {
                ExpressionBody: not null
            })
            methodSymbol = propertySymbol.GetMethod;

        if (methodSymbol == null) return;


        if (methodSymbol.Locations.FirstOrDefault() == null || methodSymbol.Locations.First().IsInMetadata) return;


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
        var hasPureExternalAttribute = hasDirectPureExternalAttribute
                                       || PurityAnalysisEngine.HasPureExternalAttribute(methodSymbol);
        var hasImpureAttribute = hasDirectImpureAttribute
                                 || PurityAnalysisEngine.HasImpureAttribute(methodSymbol);

        if (HasConflictingPurityAttributes(
                hasEnforcePureAttribute,
                hasPureAttribute,
                hasDirectPureExternalAttribute,
                hasDirectImpureAttribute))
        {
            var conflictingDiagnosticLocation = GetIdentifierLocation(context.Node);
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
                if (!baseline.IsSuppressed(conflicting)) context.ReportDiagnostic(conflicting);
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
            var allowSyncLocation = GetIdentifierLocation(context.Node);
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
                if (!baseline.IsSuppressed(diag)) context.ReportDiagnostic(diag);
            }
        }

        // Report redundant [AllowSynchronization] if present but no synchronization constructs exist in the body
        if (hasAllowSynchronization && hasPurityEnforcementAttribute)
        {
            var containsLock = context.Node.DescendantNodes().OfType<LockStatementSyntax>().Any();
            if (!containsLock)
            {
                var redundantLoc = GetIdentifierLocation(context.Node);
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
                    if (!baseline.IsSuppressed(redundant)) context.ReportDiagnostic(redundant);
                }
            }
        }


        var effectiveMissingPuritySuggestions = AnalyzerConfiguration.GetMissingPuritySuggestionOptions(
            context.Options,
            context.Node.SyntaxTree,
            missingPuritySuggestions);

        if (!hasPurityEnforcementAttribute &&
            (hasImpureAttribute || !effectiveMissingPuritySuggestions.IsEnabled))
            return;

        var enforceOrPureAttributeSymbol =
            GetEffectivePurityAttributeSymbol(enforcePureAttributeSymbol, pureAttributeSymbol);
        var purityResult = purityService.GetPurity(
            methodSymbol,
            context.SemanticModel,
            enforceOrPureAttributeSymbol,
            allowSynchronizationAttributeSymbol,
            context.CancellationToken);
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
            var diagnosticLocation = GetIdentifierLocation(context.Node);

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
                    if (!baseline.IsSuppressed(explanation)) context.ReportDiagnostic(explanation);
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
                    if (!baseline.IsSuppressed(fallbackDiagnostic)) context.ReportDiagnostic(fallbackDiagnostic);
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
                var diagnosticLocation = GetIdentifierLocation(context.Node);

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
                    if (!baseline.IsSuppressed(diagnostic)) context.ReportDiagnostic(diagnostic);
                }
            }
        }
    }

    private static bool ShouldReportMissingEnforcePure(
        SyntaxNodeAnalysisContext context,
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
            AccessorDeclarationSyntax a => (SyntaxNode?)a.Body ?? a.ExpressionBody?.Expression,
            LocalFunctionStatementSyntax l => (SyntaxNode?)l.Body ?? l.ExpressionBody?.Expression,
            _ => node
        };

        if (body == null) return 0;

        var complexity = 0;
        foreach (var descendant in body.DescendantNodesAndSelf())
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

        if (!methodSymbol.IsStatic &&
            (methodSymbol.ContainingType?.TypeKind == TypeKind.Interface ||
             ImplementsInstanceInterfaceMember(methodSymbol) ||
             methodSymbol.IsVirtual ||
             methodSymbol.IsAbstract ||
             methodSymbol.IsOverride))
            return false;

        return true;
    }

    private static bool ImplementsInstanceInterfaceMember(IMethodSymbol methodSymbol)
    {
        if (methodSymbol.IsStatic || methodSymbol.ContainingType == null) return false;

        if (methodSymbol.ExplicitInterfaceImplementations.Length > 0) return true;

        foreach (var interfaceType in methodSymbol.ContainingType.AllInterfaces)
            foreach (var interfaceMember in interfaceType.GetMembers(methodSymbol.Name).OfType<IMethodSymbol>())
                if (methodSymbol.ContainingType.FindImplementationForInterfaceMember(interfaceMember) is IMethodSymbol
                        implementationMethod &&
                    SymbolEqualityComparer.Default.Equals(implementationMethod.OriginalDefinition,
                        methodSymbol.OriginalDefinition))
                    return true;

        return false;
    }

    private static bool HasPurityEnforcement(IMethodSymbol methodSymbol, INamedTypeSymbol? enforcePureAttributeSymbol,
        INamedTypeSymbol? pureAttributeSymbol)
    {
        return HasPurityEnforcement(methodSymbol, enforcePureAttributeSymbol, pureAttributeSymbol,
            new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default));
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

        if (methodSymbol.ContainingType != null)
            foreach (var interfaceType in methodSymbol.ContainingType.AllInterfaces)
                foreach (var interfaceMember in interfaceType.GetMembers(methodSymbol.Name).OfType<IMethodSymbol>())
                {
                    if (!HasPurityEnforcement(interfaceMember, enforcePureAttributeSymbol, pureAttributeSymbol,
                            visitedMethods)) continue;

                    if (methodSymbol.ContainingType.FindImplementationForInterfaceMember(interfaceMember) is IMethodSymbol
                            implementationMethod &&
                        SymbolEqualityComparer.Default.Equals(implementationMethod.OriginalDefinition, methodSymbol))
                        return true;
                }

        return false;
    }

    private static bool HasAttributeByName(IMethodSymbol methodSymbol, string attributeTypeName)
    {
        foreach (var attributeData in GetMethodAndAssociatedAttributes(methodSymbol))
        {
            var attributeClass = attributeData.AttributeClass;
            if (attributeClass != null &&
                string.Equals(attributeClass.Name, attributeTypeName, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    private static IEnumerable<AttributeData> GetMethodAndAssociatedAttributes(IMethodSymbol methodSymbol)
    {
        foreach (var attribute in methodSymbol.GetAttributes()) yield return attribute;

        if (methodSymbol.AssociatedSymbol != null)
            foreach (var attribute in methodSymbol.AssociatedSymbol.GetAttributes())
                yield return attribute;
    }

    private static INamedTypeSymbol GetEffectivePurityAttributeSymbol(INamedTypeSymbol? enforcePureAttributeSymbol,
        INamedTypeSymbol? pureAttributeSymbol)
    {
        return enforcePureAttributeSymbol ?? pureAttributeSymbol!;
    }

    private static Location? GetIdentifierLocation(SyntaxNode node)
    {
        return node switch
        {
            MethodDeclarationSyntax m => m.Identifier.GetLocation(),
            PropertyDeclarationSyntax p => p.Identifier.GetLocation(),
            IndexerDeclarationSyntax i => i.ThisKeyword.GetLocation(),

            AccessorDeclarationSyntax a =>
                a.Parent?.Parent switch
                {
                    PropertyDeclarationSyntax p => p.Identifier.GetLocation(),
                    IndexerDeclarationSyntax i => i.ThisKeyword.GetLocation(),
                    _ => a.Keyword.GetLocation()
                } ?? a.Keyword.GetLocation(),
            ConstructorDeclarationSyntax c => c.Identifier.GetLocation(),
            ConversionOperatorDeclarationSyntax c => c.Type.GetLocation(),
            OperatorDeclarationSyntax o => o.OperatorToken.GetLocation(),
            LocalFunctionStatementSyntax l => l.Identifier.GetLocation(),

            _ => node.GetLocation()
        };
    }
}
