using System;
using System.Linq;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Generic;
using System.Collections.Immutable;
using SharpProof.Analyzer.Configuration;
using SharpProof.Analyzer.Engine;

namespace SharpProof.Analyzer
{
    internal static class MethodPurityAnalyzer
    {

        internal static void AnalyzeSymbolForPurity(
            SyntaxNodeAnalysisContext context,
            Engine.CompilationPurityService purityService,
            MissingPuritySuggestionOptions missingPuritySuggestions,
            bool emitExplanations,
            bool reportBclFallbackGuesses,
            DiagnosticBaseline baseline)
        {

            ISymbol? declaredSymbol = context.SemanticModel.GetDeclaredSymbol(context.Node, context.CancellationToken);
            IMethodSymbol? methodSymbol = declaredSymbol as IMethodSymbol;
            if (methodSymbol == null &&
                declaredSymbol is IPropertySymbol propertySymbol &&
                context.Node is PropertyDeclarationSyntax { ExpressionBody: not null } or IndexerDeclarationSyntax { ExpressionBody: not null })
            {
                methodSymbol = propertySymbol.GetMethod;
            }

            if (methodSymbol == null)
            {
                return;
            }


            if (methodSymbol.Locations.FirstOrDefault() == null || methodSymbol.Locations.First().IsInMetadata)
            {
                return;
            }


            var enforcePureAttributeSymbol =
                ResolveAttributeSymbol(context.SemanticModel.Compilation, "SharpProof.Attributes.EnforcePureAttribute", "EnforcePureAttribute")
                ?? GetAppliedAttributeSymbol(methodSymbol, "EnforcePureAttribute");
            var pureAttributeSymbol =
                ResolveAttributeSymbol(context.SemanticModel.Compilation, "SharpProof.Attributes.PureAttribute", "PureAttribute")
                ?? GetAppliedAttributeSymbol(methodSymbol, "PureAttribute");
            var pureExternalAttributeSymbol =
                ResolveAttributeSymbol(context.SemanticModel.Compilation, "SharpProof.Attributes.PureExternalAttribute", "PureExternalAttribute")
                ?? GetAppliedAttributeSymbol(methodSymbol, "PureExternalAttribute");
            var impureAttributeSymbol =
                ResolveAttributeSymbol(context.SemanticModel.Compilation, "SharpProof.Attributes.ImpureAttribute", "ImpureAttribute")
                ?? GetAppliedAttributeSymbol(methodSymbol, "ImpureAttribute");

            if (enforcePureAttributeSymbol == null && pureAttributeSymbol == null)
            {
                return;
            }


            var allowSynchronizationAttributeSymbol =
                ResolveAttributeSymbol(context.SemanticModel.Compilation, "SharpProof.Attributes.AllowSynchronizationAttribute", "AllowSynchronizationAttribute")
                ?? GetAppliedAttributeSymbol(methodSymbol, "AllowSynchronizationAttribute");

            bool hasEnforcePureAttribute = (enforcePureAttributeSymbol != null && HasAttribute(methodSymbol, enforcePureAttributeSymbol))
                || HasAttributeByName(methodSymbol, "EnforcePureAttribute");
            bool hasPureAttribute = (pureAttributeSymbol != null && HasAttribute(methodSymbol, pureAttributeSymbol))
                || HasAttributeByName(methodSymbol, "PureAttribute");
            bool hasDirectPureExternalAttribute = (pureExternalAttributeSymbol != null && HasAttribute(methodSymbol, pureExternalAttributeSymbol))
                || HasAttributeByName(methodSymbol, "PureExternalAttribute");
            bool hasDirectImpureAttribute = (impureAttributeSymbol != null && HasAttribute(methodSymbol, impureAttributeSymbol))
                || HasAttributeByName(methodSymbol, "ImpureAttribute");
            bool hasPureExternalAttribute = hasDirectPureExternalAttribute
                || PurityAnalysisEngine.HasPureExternalAttribute(methodSymbol);
            bool hasImpureAttribute = hasDirectImpureAttribute
                || PurityAnalysisEngine.HasImpureAttribute(methodSymbol);

            if (HasConflictingPurityAttributes(
                hasEnforcePureAttribute,
                hasPureAttribute,
                hasDirectPureExternalAttribute,
                hasDirectImpureAttribute))
            {
                Location? conflictingDiagnosticLocation = GetIdentifierLocation(context.Node);
                if (conflictingDiagnosticLocation != null)
                {
                    var conflicting = Diagnostic.Create(
                        SharpProofDiagnostics.ConflictingPurityAttributesRule,
                        conflictingDiagnosticLocation,
                        methodSymbol.Name);
                    context.ReportDiagnostic(conflicting);
                }
            }


            bool hasPurityEnforcementAttribute = hasEnforcePureAttribute || hasPureAttribute || hasPureExternalAttribute || HasPurityEnforcement(methodSymbol, enforcePureAttributeSymbol, pureAttributeSymbol);
            bool hasAllowSynchronization =
                (allowSynchronizationAttributeSymbol != null && HasAttribute(methodSymbol, allowSynchronizationAttributeSymbol))
                || HasAttributeByName(methodSymbol, "AllowSynchronizationAttribute");

            // Report if [AllowSynchronization] is present without [EnforcePure]/[Pure]
            if (hasAllowSynchronization && !hasPurityEnforcementAttribute)
            {
                Location? allowSyncLocation = GetIdentifierLocation(context.Node);
                if (allowSyncLocation != null)
                {
                    var diag = Diagnostic.Create(SharpProofDiagnostics.AllowSynchronizationWithoutPurityAttributeRule, allowSyncLocation, methodSymbol.Name);
                    context.ReportDiagnostic(diag);
                }
            }

            // Report redundant [AllowSynchronization] if present but no synchronization constructs exist in the body
            if (hasAllowSynchronization && hasPurityEnforcementAttribute)
            {
                bool containsLock = context.Node.DescendantNodes().OfType<LockStatementSyntax>().Any();
                if (!containsLock)
                {
                    Location? redundantLoc = GetIdentifierLocation(context.Node);
                    if (redundantLoc != null)
                    {
                        var redundant = Diagnostic.Create(SharpProofDiagnostics.RedundantAllowSynchronizationRule, redundantLoc, methodSymbol.Name);
                        context.ReportDiagnostic(redundant);
                    }
                }
            }


            var enforceOrPureAttributeSymbol = GetEffectivePurityAttributeSymbol(enforcePureAttributeSymbol, pureAttributeSymbol);
            PurityAnalysisEngine.PurityAnalysisResult purityResult = purityService.GetPurity(
                methodSymbol,
                context.SemanticModel,
                enforceOrPureAttributeSymbol,
                allowSynchronizationAttributeSymbol);
            bool isPure = purityResult.IsPure;

            var effectiveMissingPuritySuggestions = AnalyzerConfiguration.GetMissingPuritySuggestionOptions(
                context.Options,
                context.Node.SyntaxTree,
                missingPuritySuggestions);
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

                Location? diagnosticLocation = GetIdentifierLocation(context.Node);
                PurityAnalysisEngine.LogDebug($"[MPA] Method '{methodSymbol.Name}' determined impure. Reporting SP0002 on identifier.");

                if (diagnosticLocation != null)
                {
                    if (baseline.IsSuppressed(SharpProofDiagnostics.PurityNotVerifiedId, methodSymbol, context.Node.SyntaxTree))
                    {
                        return;
                    }

                    var properties = purityResult.Evidence.ToDiagnosticProperties();
                    var diagnostic = Diagnostic.Create(
                        SharpProofDiagnostics.PurityNotVerifiedRule,
                        diagnosticLocation,
                        additionalLocations: null,
                        properties: properties,
                        messageArgs: new object[] { methodSymbol.Name }
                    );
                    context.ReportDiagnostic(diagnostic);
                    if (effectiveEmitExplanations)
                    {
                        var explanation = Diagnostic.Create(
                            SharpProofDiagnostics.PurityExplanationRule,
                            diagnosticLocation,
                            additionalLocations: null,
                            properties: properties,
                            messageArgs: new object[] { methodSymbol.Name, purityResult.Evidence.ToSummary() });
                        context.ReportDiagnostic(explanation);
                    }

                    if ((effectiveEmitExplanations || effectiveReportBclFallbackGuesses) &&
                        !string.IsNullOrEmpty(purityResult.Evidence.BclFallbackGuess))
                    {
                        var fallbackDiagnostic = Diagnostic.Create(
                            SharpProofDiagnostics.BclFallbackGuessRule,
                            diagnosticLocation,
                            additionalLocations: null,
                            properties: properties,
                            messageArgs: new object[]
                            {
                                methodSymbol.Name,
                                purityResult.Evidence.BclFallbackGuess,
                                BclPurityFallbackHeuristics.GetDisplayReason(purityResult.Evidence.BclFallbackReason)
                            });
                        context.ReportDiagnostic(fallbackDiagnostic);
                    }

                    PurityAnalysisEngine.LogDebug($"[MPA] Reported diagnostic SP0002 for {methodSymbol.Name} at {diagnosticLocation}.");
                }
                else
                {

                    PurityAnalysisEngine.LogDebug($"[MPA] Could not get identifier location for diagnostic on impure method {methodSymbol.Name}.");
                }
            }

            else if (effectiveMissingPuritySuggestions.IsEnabled && isPure && !hasPurityEnforcementAttribute && !hasAllowSynchronization && !hasImpureAttribute)
            {
                if (context.Node is LocalFunctionStatementSyntax or PropertyDeclarationSyntax or IndexerDeclarationSyntax)
                {
                    return;
                }

                if (!ShouldReportMissingEnforcePure(context, methodSymbol, effectiveMissingPuritySuggestions))
                {
                    return;
                }

                bool isCompilerGeneratedSetter = false;
                if (methodSymbol.MethodKind == MethodKind.PropertySet && context.Node is AccessorDeclarationSyntax setterNode)
                {
                    if (setterNode.Body == null && setterNode.ExpressionBody == null)
                    {
                        isCompilerGeneratedSetter = true;
                        PurityAnalysisEngine.LogDebug($"[MPA] Method '{methodSymbol.Name}' is an auto-property setter. Not a candidate for SP0004.");
                    }
                }

                if (!isCompilerGeneratedSetter)
                {

                    Location? diagnosticLocation = GetIdentifierLocation(context.Node);
                    PurityAnalysisEngine.LogDebug($"[MPA] Method '{methodSymbol.Name}' determined pure but lacks [EnforcePure]. Reporting SP0004 on identifier.");

                    if (diagnosticLocation != null)
                    {
                        if (baseline.IsSuppressed(SharpProofDiagnostics.MissingEnforcePureAttributeId, methodSymbol, context.Node.SyntaxTree))
                        {
                            return;
                        }

                        var diagnostic = Diagnostic.Create(
                            SharpProofDiagnostics.MissingEnforcePureAttributeRule,
                            diagnosticLocation,
                            methodSymbol.Name
                        );
                        context.ReportDiagnostic(diagnostic);
                        PurityAnalysisEngine.LogDebug($"[MPA] Reported diagnostic SP0004 for {methodSymbol.Name} at {diagnosticLocation}.");
                    }
                    else
                    {
                        PurityAnalysisEngine.LogDebug($"[MPA] Could not get identifier location for diagnostic SP0004 on pure method {methodSymbol.Name}.");
                    }
                }
            }
        }

        private static bool ShouldReportMissingEnforcePure(
            SyntaxNodeAnalysisContext context,
            IMethodSymbol methodSymbol,
            MissingPuritySuggestionOptions options)
        {
            if (!ShouldSuggestMissingEnforcePure(methodSymbol))
            {
                PurityAnalysisEngine.LogDebug($"[MPA] Method '{methodSymbol.Name}' participates in instance dispatch. Skipping SP0004 suggestion.");
                return false;
            }

            if (!MatchesSuggestionScope(methodSymbol, options.Scope))
            {
                return false;
            }

            if (options.ExcludeGeneratedFiles && IsGeneratedCode(context.Node))
            {
                return false;
            }

            if (options.ExcludeTestFiles && IsTestCode(methodSymbol, context.Node.SyntaxTree.FilePath))
            {
                return false;
            }

            if (options.NamespaceFilters.Count > 0 && !MatchesNamespaceFilter(methodSymbol, options.NamespaceFilters))
            {
                return false;
            }

            if (options.MinimumComplexity > 0 && GetMethodComplexity(context.Node) < options.MinimumComplexity)
            {
                return false;
            }

            return true;
        }

        private static bool HasConflictingPurityAttributes(
            bool hasEnforcePureAttribute,
            bool hasPureAttribute,
            bool hasPureExternalAttribute,
            bool hasImpureAttribute)
        {
            if (hasImpureAttribute && (hasEnforcePureAttribute || hasPureAttribute || hasPureExternalAttribute))
            {
                return true;
            }

            if (hasPureExternalAttribute && (hasEnforcePureAttribute || hasPureAttribute))
            {
                return true;
            }

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
                {
                    return true;
                }

                var normalized = filePath.Replace('/', Path.DirectorySeparatorChar);
                if (normalized.IndexOf(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
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
                    normalized.IndexOf(Path.DirectorySeparatorChar + "test" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    normalized.IndexOf(Path.DirectorySeparatorChar + "tests" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    fileName.EndsWith("Test", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith("Tests", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            var containingTypeName = methodSymbol.ContainingType?.Name;
            if (!string.IsNullOrWhiteSpace(containingTypeName))
            {
                var typeName = containingTypeName!;
                if (typeName.EndsWith("Test", StringComparison.OrdinalIgnoreCase) ||
                    typeName.EndsWith("Tests", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            var namespaceName = methodSymbol.ContainingNamespace?.ToDisplayString();
            return IsTestLikeName(namespaceName);
        }

        private static bool IsTestLikeName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

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
                if (filter.Length == 0)
                {
                    continue;
                }

                if (namespaceName.Equals(filter, StringComparison.Ordinal) ||
                    namespaceName.StartsWith(filter + ".", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static int GetMethodComplexity(SyntaxNode node)
        {
            SyntaxNode? body = node switch
            {
                MethodDeclarationSyntax m => (SyntaxNode?)m.Body ?? m.ExpressionBody?.Expression,
                ConstructorDeclarationSyntax c => (SyntaxNode?)c.Body ?? c.ExpressionBody?.Expression,
                OperatorDeclarationSyntax o => (SyntaxNode?)o.Body ?? o.ExpressionBody?.Expression,
                AccessorDeclarationSyntax a => (SyntaxNode?)a.Body ?? a.ExpressionBody?.Expression,
                LocalFunctionStatementSyntax l => (SyntaxNode?)l.Body ?? l.ExpressionBody?.Expression,
                _ => node
            };

            if (body == null)
            {
                return 0;
            }

            int complexity = 0;
            foreach (var descendant in body.DescendantNodesAndSelf())
            {
                if (descendant is StatementSyntax ||
                    descendant is BinaryExpressionSyntax ||
                    descendant is ConditionalExpressionSyntax ||
                    descendant is SwitchExpressionSyntax ||
                    descendant is InvocationExpressionSyntax ||
                    descendant is ObjectCreationExpressionSyntax)
                {
                    complexity++;
                }
            }

            return complexity;
        }

        private static bool ShouldSuggestMissingEnforcePure(IMethodSymbol methodSymbol)
        {
            if (methodSymbol.MethodKind == MethodKind.Conversion)
            {
                return false;
            }

            if (methodSymbol.MethodKind == MethodKind.Constructor &&
                HasAttributeByName(methodSymbol, "SetsRequiredMembersAttribute"))
            {
                return false;
            }

            if (!methodSymbol.IsStatic &&
                (methodSymbol.ContainingType?.TypeKind == TypeKind.Interface ||
                 ImplementsInstanceInterfaceMember(methodSymbol) ||
                 methodSymbol.IsVirtual ||
                 methodSymbol.IsAbstract ||
                 methodSymbol.IsOverride))
            {
                return false;
            }

            return true;
        }

        private static bool ImplementsInstanceInterfaceMember(IMethodSymbol methodSymbol)
        {
            if (methodSymbol.IsStatic || methodSymbol.ContainingType == null)
            {
                return false;
            }

            if (methodSymbol.ExplicitInterfaceImplementations.Length > 0)
            {
                return true;
            }

            foreach (var interfaceType in methodSymbol.ContainingType.AllInterfaces)
            {
                foreach (var interfaceMember in interfaceType.GetMembers(methodSymbol.Name).OfType<IMethodSymbol>())
                {
                    if (methodSymbol.ContainingType.FindImplementationForInterfaceMember(interfaceMember) is IMethodSymbol implementationMethod &&
                        SymbolEqualityComparer.Default.Equals(implementationMethod.OriginalDefinition, methodSymbol.OriginalDefinition))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool HasPurityEnforcement(IMethodSymbol methodSymbol, INamedTypeSymbol? enforcePureAttributeSymbol, INamedTypeSymbol? pureAttributeSymbol)
        {
            return HasPurityEnforcement(methodSymbol, enforcePureAttributeSymbol, pureAttributeSymbol, new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default));
        }

        private static bool HasPurityEnforcement(
            IMethodSymbol methodSymbol,
            INamedTypeSymbol? enforcePureAttributeSymbol,
            INamedTypeSymbol? pureAttributeSymbol,
            HashSet<IMethodSymbol> visitedMethods)
        {
            methodSymbol = methodSymbol.OriginalDefinition;
            if (!visitedMethods.Add(methodSymbol))
            {
                return false;
            }

            foreach (var attributeData in methodSymbol.GetAttributes())
            {
                var attributeClass = attributeData.AttributeClass?.OriginalDefinition;
                if (enforcePureAttributeSymbol != null && SymbolEqualityComparer.Default.Equals(attributeClass, enforcePureAttributeSymbol))
                {
                    return true;
                }
                if (pureAttributeSymbol != null && SymbolEqualityComparer.Default.Equals(attributeClass, pureAttributeSymbol))
                {
                    return true;
                }
            }

            if (methodSymbol.OverriddenMethod != null &&
                HasPurityEnforcement(methodSymbol.OverriddenMethod, enforcePureAttributeSymbol, pureAttributeSymbol, visitedMethods))
            {
                return true;
            }

            if (methodSymbol.ContainingType != null)
            {
                foreach (var interfaceType in methodSymbol.ContainingType.AllInterfaces)
                {
                    foreach (var interfaceMember in interfaceType.GetMembers(methodSymbol.Name).OfType<IMethodSymbol>())
                    {
                        if (!HasPurityEnforcement(interfaceMember, enforcePureAttributeSymbol, pureAttributeSymbol, visitedMethods))
                        {
                            continue;
                        }

                        if (methodSymbol.ContainingType.FindImplementationForInterfaceMember(interfaceMember) is IMethodSymbol implementationMethod &&
                            SymbolEqualityComparer.Default.Equals(implementationMethod.OriginalDefinition, methodSymbol))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static INamedTypeSymbol? GetAppliedAttributeSymbol(IMethodSymbol methodSymbol, string attributeTypeName)
        {
            foreach (var attributeData in methodSymbol.GetAttributes())
            {
                var attributeClass = attributeData.AttributeClass;
                if (attributeClass != null && string.Equals(attributeClass.Name, attributeTypeName, StringComparison.Ordinal))
                {
                    return attributeClass;
                }
            }

            return null;
        }

        private static bool HasAttribute(IMethodSymbol methodSymbol, INamedTypeSymbol attributeType)
        {
            foreach (var attributeData in GetMethodAndAssociatedAttributes(methodSymbol))
            {
                var attributeClass = attributeData.AttributeClass?.OriginalDefinition;
                if (SymbolEqualityComparer.Default.Equals(attributeClass, attributeType))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasAttributeByName(IMethodSymbol methodSymbol, string attributeTypeName)
        {
            foreach (var attributeData in GetMethodAndAssociatedAttributes(methodSymbol))
            {
                var attributeClass = attributeData.AttributeClass;
                if (attributeClass != null && string.Equals(attributeClass.Name, attributeTypeName, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static IEnumerable<AttributeData> GetMethodAndAssociatedAttributes(IMethodSymbol methodSymbol)
        {
            foreach (var attribute in methodSymbol.GetAttributes())
            {
                yield return attribute;
            }

            if (methodSymbol.AssociatedSymbol != null)
            {
                foreach (var attribute in methodSymbol.AssociatedSymbol.GetAttributes())
                {
                    yield return attribute;
                }
            }
        }

        private static INamedTypeSymbol GetEffectivePurityAttributeSymbol(INamedTypeSymbol? enforcePureAttributeSymbol, INamedTypeSymbol? pureAttributeSymbol)
        {
            return enforcePureAttributeSymbol ?? pureAttributeSymbol!;
        }

        private static INamedTypeSymbol? ResolveAttributeSymbol(Compilation compilation, string qualifiedMetadataName, string fallbackMetadataName)
        {
            return compilation.GetTypeByMetadataName(qualifiedMetadataName)
                ?? compilation.GetTypeByMetadataName(fallbackMetadataName)
                ?? FindTypeByName(compilation.Assembly.GlobalNamespace, fallbackMetadataName);
        }

        private static INamedTypeSymbol? FindTypeByName(INamespaceSymbol namespaceSymbol, string typeName)
        {
            var directMatch = namespaceSymbol.GetTypeMembers(typeName).FirstOrDefault();
            if (directMatch != null)
            {
                return directMatch;
            }

            foreach (var nestedNamespace in namespaceSymbol.GetNamespaceMembers())
            {
                var nestedMatch = FindTypeByName(nestedNamespace, typeName);
                if (nestedMatch != null)
                {
                    return nestedMatch;
                }
            }

            return null;
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
}
