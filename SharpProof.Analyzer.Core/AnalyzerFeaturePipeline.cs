namespace SharpProof.Analyzer;

internal static partial class AnalyzerFeaturePipeline
{
    internal static void ValidateNestedCallableDeclaration(
        SyntaxNodeAnalysisContext context,
        AnalyzerSession session)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (AnalyzerGeneratedCodePolicy.IsGenerated(
                context.Node.SyntaxTree,
                context.Compilation,
                context.CancellationToken))
        {
            return;
        }

        SharpProofControlAttributePolicy.ValidateNestedCallableDeclaration(
            context.Node,
            context.SemanticModel,
            session,
            context.ReportDiagnostic,
            context.CancellationToken);

        var method = context.Node switch
        {
            LocalFunctionStatementSyntax localFunction =>
                context.SemanticModel.GetDeclaredSymbol(
                    localFunction,
                    context.CancellationToken),
            LambdaExpressionSyntax lambda =>
                (context.SemanticModel.GetOperation(
                    lambda,
                    context.CancellationToken) as IAnonymousFunctionOperation)?.Symbol,
            _ => null
        };
        if (method == null)
        {
            return;
        }

        var selection = GetSelection(
            method,
            session,
            context.ReportDiagnostic,
            context.CancellationToken);
        if (!selection.Any)
        {
            return;
        }

        if (selection.IsSuppressed)
        {
            session.RecordSemanticOutcome(
                method,
                AnalyzerSemanticOutcome.Suppressed);
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            GeneratedDiagnosticDescriptors.SelectedAnalysisIncompleteRule,
            AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(context.Node),
            method.Name,
            LanguageSubsetAbstentionReason.UnsupportedCallable));
        session.RecordSemanticOutcome(
            method,
            AnalyzerSemanticOutcome.Abstained);
    }

    internal static void AnalyzeUnselectedOperationBlock(
        OperationBlockAnalysisContext context,
        AnalyzerSession session)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (context.OwningSymbol is not IMethodSymbol method ||
            method.DeclaringSyntaxReferences.IsDefaultOrEmpty ||
            IsNestedCallable(method) ||
            session.IsContractCompanion(method))
        {
            return;
        }

        var declaration = FindDeclaration(
            method,
            context.OperationBlocks,
            context.CancellationToken);
        if (declaration == null)
        {
            session.RecordSemanticOutcome(
                method,
                AnalyzerSemanticOutcome.Abstained);
            return;
        }

        if (AnalyzerGeneratedCodePolicy.IsGenerated(
                method,
                declaration.SyntaxTree,
                context.Compilation,
                context.CancellationToken))
        {
            return;
        }

        var semanticModel = SharpProof.Frontend.Host.CompilationModelProvider
            .GetSemanticModel(context.Compilation, declaration.SyntaxTree);
        if (method.MethodKind == MethodKind.Constructor &&
            !CanReachConstructorEntry(
                method,
                semanticModel,
                context.CancellationToken))
        {
            session.RecordSemanticOutcome(
                method,
                AnalyzerSemanticOutcome.NotApplicable);
            return;
        }
        var outcome = RequiresCallSiteAnalyzer.Analyze(
            method,
            declaration,
            semanticModel,
            session,
            context.ReportDiagnostic,
            context.CancellationToken);
        session.RecordSemanticOutcome(method, outcome);
    }

    internal static void ValidateMethodAttributes(
        SymbolAnalysisContext context,
        AnalyzerSession session)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (context.Symbol is not IMethodSymbol method ||
            method.DeclaringSyntaxReferences.IsDefaultOrEmpty)
        {
            return;
        }
        if (method.PartialImplementationPart != null)
        {
            return;
        }

        var declaration = method.DeclaringSyntaxReferences[0]
            .GetSyntax(context.CancellationToken);
        var generated = AnalyzerGeneratedCodePolicy.IsGenerated(
            method,
            declaration.SyntaxTree,
            context.Compilation,
            context.CancellationToken);
        if (generated && !GetFeatureSelection(method, session).Any)
        {
            return;
        }

        EffectContractDiagnostics.ValidateArguments(method, session, context.ReportDiagnostic);
        ClosedContractDiagnostics.Validate(method, session, context.ReportDiagnostic);
        var rejectedContractApi =
            session.Attributes.GetRejectedSelectionFeatures(method) !=
            ContractSelectionFeatures.None;
        var rejectedCallableApi =
            session.Attributes.GetRejectedCallableSelectionFeatures(method) !=
            ContractSelectionFeatures.None;
        if (rejectedCallableApi &&
            session.TryMarkRejectedContractApiReported(method))
        {
            ReportRejectedContractApi(
                method,
                context.ReportDiagnostic,
                context.CancellationToken);
        }
        var selection = GetSelection(
            method, session, context.ReportDiagnostic, context.CancellationToken);
        var resolution = session.ResolveContractSource(method);
        var explicitSelection = session.Attributes.Select(
            method,
            session.Configuration.ContractsEnabled &&
            resolution.HasSelectedContractIntent);
        var explicitContractsSelected =
            session.Configuration.ContractsEnabled &&
            (explicitSelection & ContractSelectionFeatures.Contracts) != 0;
        var rejectedCompanionContractApi =
            resolution.UsesCompanion &&
            resolution.Inventory.HasRejectedContractApiUsage;
        if (IsConcreteSemicolonAccessor(method, context.CancellationToken) &&
            selection.Any)
        {
            if (AnalyzerGeneratedCodePolicy.IsGenerated(
                    method,
                    declaration.SyntaxTree,
                    context.Compilation,
                    context.CancellationToken))
            {
                return;
            }
            if (selection.IsSuppressed)
            {
                session.RecordSemanticOutcome(
                    method,
                    AnalyzerSemanticOutcome.Suppressed);
                return;
            }
            session.RegisterSelectedSemicolonAccessor(method);
        }
        if ((!method.IsAbstract &&
             !method.IsExtern &&
             !InvocationEmissionPolicy.IsUnimplementedPartial(method)) ||
            !selection.Any ||
            (InvocationEmissionPolicy.IsUnimplementedPartial(method) &&
             !selection.Effects))
        {
            return;
        }

        if (selection.IsSuppressed)
        {
            session.RecordSemanticOutcome(method, AnalyzerSemanticOutcome.Suppressed);
            return;
        }
        if (rejectedContractApi || rejectedCompanionContractApi)
        {
            session.RecordSemanticOutcome(
                method,
                AnalyzerSemanticOutcome.Abstained);
            return;
        }
        if (!explicitContractsSelected &&
            selection.Effects &&
            session.ResolveEffectContract(method) is
            { Kind: EffectContractResolutionKind.Valid })
        {
            var outcome = EffectContractDiagnostics.Analyze(
                method,
                method.DeclaringSyntaxReferences[0].GetSyntax(context.CancellationToken),
                session,
                context.ReportDiagnostic,
                context.CancellationToken);
            session.RecordSemanticOutcome(
                method,
                outcome == AnalyzerSemanticOutcome.NotApplicable
                    ? AnalyzerSemanticOutcome.Proven
                    : outcome);
            return;
        }
        context.ReportDiagnostic(Diagnostic.Create(
            GeneratedDiagnosticDescriptors.SelectedAnalysisIncompleteRule,
            AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(method, context.CancellationToken),
            method.Name,
            LanguageSubsetAbstentionReason.MissingOperationRoot));
        session.RecordSemanticOutcome(method, AnalyzerSemanticOutcome.Abstained);
    }

    internal static void AnalyzeOperationBlock(
        OperationBlockAnalysisContext context,
        AnalyzerSession session)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (context.OwningSymbol is not IMethodSymbol method)
        {
            return;
        }

        if (IsConcreteSemicolonAccessor(method, context.CancellationToken))
        {
            return;
        }

        if (session.IsContractCompanion(method))
        {
            return;
        }
        if (InvocationEmissionPolicy.IsUnimplementedPartial(method))
        {
            return;
        }

        if (method.PartialImplementationPart != null)
        {
            return;
        }
        if (method.PartialDefinitionPart != null &&
            !session.TryBeginExecutableAnalysis(method))
        {
            return;
        }
        method = EffectAnalysisSession.NormalizeMethod(method);

        if (method.DeclaringSyntaxReferences.IsDefaultOrEmpty)
        {
            session.RecordSemanticOutcome(method, AnalyzerSemanticOutcome.Abstained);
            return;
        }

        var declaration = FindDeclaration(
            method, context.OperationBlocks, context.CancellationToken);
        if (declaration == null)
        {
            session.RecordSemanticOutcome(method, AnalyzerSemanticOutcome.Abstained);
            return;
        }
        var rejectedContractApi =
            session.Attributes.GetRejectedSelectionFeatures(method) !=
                ContractSelectionFeatures.None ||
            session.GetContractClauses(method)
                .HasRejectedContractApiUsage;
        if (AnalyzerGeneratedCodePolicy.IsGenerated(
                method,
                declaration.SyntaxTree,
                context.Compilation,
                context.CancellationToken) &&
            !GetFeatureSelection(method, session).Any &&
            !rejectedContractApi)
        {
            return;
        }
        var selection = GetSelection(
            method, session, context.ReportDiagnostic, context.CancellationToken);
        if (!selection.Any &&
            !rejectedContractApi &&
            AnalyzerGeneratedCodePolicy.IsGenerated(
                method,
                declaration.SyntaxTree,
                context.Compilation,
                context.CancellationToken))
        {
            return;
        }

        var hasInvalidContractClauses =
            ValidateContractClauses(
                method,
                session,
                context.ReportDiagnostic,
                context.CancellationToken);
        if (rejectedContractApi)
        {
            session.RecordSemanticOutcome(
                method,
                AnalyzerSemanticOutcome.Abstained);
            return;
        }
        if (selection.IsSuppressed)
        {
            session.RecordSemanticOutcome(method, AnalyzerSemanticOutcome.Suppressed);
            return;
        }

        var semanticModel = SharpProof.Frontend.Host.CompilationModelProvider.GetSemanticModel(
            context.Compilation, declaration.SyntaxTree);
        var constructorEntryReachable =
            method.MethodKind != MethodKind.Constructor ||
            CanReachConstructorEntry(
                method,
                semanticModel,
                context.CancellationToken);
        var outcome = AnalyzerSemanticOutcome.NotApplicable;
        var subsetIncompleteReported = false;
        var classifySubset = selection.Any;
        var subset = classifySubset
            ? LanguageSubsetGate.ClassifyEffects(
                method,
                declaration,
                semanticModel,
                context.OperationBlocks,
                session.HasResolvedApiSpec,
                context.CancellationToken)
            : LanguageSubsetDecision.Supported;
        if (!subset.IsSupported)
        {
            if (selection.Any)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    GeneratedDiagnosticDescriptors.SelectedAnalysisIncompleteRule,
                    AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(declaration),
                    method.Name,
                    subset.OperationKind is { } operation
                        ? subset.Reason + " (" + operation + ")"
                        : subset.Reason.ToString()));
                subsetIncompleteReported = true;
            }

            outcome = AnalyzerSemanticOutcomes.Combine(
                outcome, AnalyzerSemanticOutcome.Abstained);
        }
        else if (selection.Effects)
        {
            outcome = AnalyzerSemanticOutcomes.Combine(
                outcome,
                EffectContractDiagnostics.Analyze(
                    method,
                    declaration,
                    session,
                    context.ReportDiagnostic,
                    context.CancellationToken));
        }

        if (session.Configuration.ContractsEnabled &&
            !IsNestedCallable(method) &&
            constructorEntryReachable)
        {
            var requiresOutcome =
                RequiresCallSiteAnalyzer.Analyze(
                    method,
                    declaration,
                    semanticModel,
                    session,
                    context.ReportDiagnostic,
                    context.CancellationToken,
                    out var requiresUnknown);
            outcome = AnalyzerSemanticOutcomes.Combine(
                outcome,
                requiresOutcome);
            if (selection.Contracts &&
                requiresUnknown &&
                !subsetIncompleteReported &&
                !hasInvalidContractClauses &&
                !method.IsAbstract &&
                !method.IsExtern)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    GeneratedDiagnosticDescriptors.SelectedAnalysisIncompleteRule,
                    AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(declaration),
                    method.Name,
                    "RequiresCallSiteAnalysisUnknown"));
            }
        }

        session.RecordSemanticOutcome(method, outcome);
    }

    internal static void ReconcileSelectedSemicolonAccessors(
        CompilationAnalysisContext context,
        AnalyzerSession session)
    {
        foreach (var method in session.GetUnrecordedSelectedSemicolonAccessors())
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            context.ReportDiagnostic(Diagnostic.Create(
                GeneratedDiagnosticDescriptors.SelectedAnalysisIncompleteRule,
                AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(
                    method,
                    context.CancellationToken),
                method.Name,
                LanguageSubsetAbstentionReason.MissingOperationRoot));
            session.RecordSemanticOutcome(
                method,
                AnalyzerSemanticOutcome.Abstained);
        }
    }

    private static bool IsNestedCallable(IMethodSymbol method)
    {
        return method.MethodKind is
            MethodKind.LocalFunction or MethodKind.AnonymousFunction;
    }

    private static bool IsConcreteSemicolonAccessor(
        IMethodSymbol method,
        CancellationToken cancellationToken)
    {
        return !method.IsAbstract &&
            !method.IsExtern &&
            method.MethodKind is
                MethodKind.PropertyGet or
                MethodKind.PropertySet or
                MethodKind.EventAdd or
                MethodKind.EventRemove &&
            method.DeclaringSyntaxReferences.Any(reference =>
                reference.GetSyntax(cancellationToken) is AccessorDeclarationSyntax
                {
                    Body: null,
                    ExpressionBody: null,
                    SemicolonToken.RawKind: not 0
                });
    }

    internal static void AnalyzePrimaryConstructor(
        SyntaxNodeAnalysisContext context,
        AnalyzerSession session)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (context.Node is not TypeDeclarationSyntax declaration ||
            !PrimaryConstructorCallableInventory.TryGet(
                declaration,
                context.SemanticModel,
                context.CancellationToken,
                out var constructor) ||
            AnalyzerGeneratedCodePolicy.IsGenerated(
                constructor,
                declaration.SyntaxTree,
                context.Compilation,
                context.CancellationToken) ||
            !session.TryBeginRequiresCallSiteAnalysis(constructor))
        {
            return;
        }

        if (!CanReachConstructorEntry(
                constructor,
                context.SemanticModel,
                context.CancellationToken))
        {
            session.RecordSemanticOutcome(
                constructor,
                AnalyzerSemanticOutcome.NotApplicable);
            return;
        }

        var outcome = SharpProofControlAttributePolicy
            .ValidateAndShouldSuppress(
                constructor,
                session,
                context.ReportDiagnostic,
                context.CancellationToken)
            ? AnalyzerSemanticOutcome.Suppressed
            : RequiresCallSiteAnalyzer.AnalyzePrimaryConstructorInitializer(
                constructor,
                declaration,
                context.SemanticModel,
                session,
                context.ReportDiagnostic,
                context.CancellationToken);
        session.RecordSemanticOutcome(constructor, outcome);
    }

    internal static void AnalyzeMemberInitializer(
        SyntaxNodeAnalysisContext context,
        AnalyzerSession session)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (context.Node is not EqualsValueClauseSyntax initializer ||
            initializer.Parent is not VariableDeclaratorSyntax and not PropertyDeclarationSyntax)
        {
            return;
        }
        var symbol = initializer.Parent switch
        {
            VariableDeclaratorSyntax variable => context.SemanticModel.GetDeclaredSymbol(
                variable, context.CancellationToken),
            PropertyDeclarationSyntax property => context.SemanticModel.GetDeclaredSymbol(
                property, context.CancellationToken),
            _ => null
        };
        if (symbol is not IFieldSymbol and
            not IPropertySymbol and
            not IEventSymbol ||
            symbol.ContainingType is not { } type)
        {
            return;
        }
        var isStatic = symbol.IsStatic;
        var constructors = (isStatic
                ? type.StaticConstructors
                : type.InstanceConstructors)
            .OrderBy(static candidate => candidate.DeclaringSyntaxReferences
                .FirstOrDefault()?.SyntaxTree.FilePath, StringComparer.Ordinal)
            .ThenBy(static candidate => candidate.DeclaringSyntaxReferences
                .FirstOrDefault()?.Span.Start ?? int.MaxValue)
            .Where(candidate =>
                !RequiresCallSiteDiscovery.IsRecordCopyConstructor(candidate) &&
                !AnalyzerGeneratedCodePolicy.IsGenerated(
                    candidate,
                    candidate.DeclaringSyntaxReferences.FirstOrDefault()?.SyntaxTree ??
                        initializer.SyntaxTree,
                    context.Compilation,
                    context.CancellationToken))
            .ToArray();
        var root = context.SemanticModel.GetOperation(
            initializer.Value, context.CancellationToken);
        if (constructors.Length == 0 || root == null ||
            AnalyzerGeneratedCodePolicy.IsGenerated(
                symbol,
                initializer.SyntaxTree,
                context.Compilation,
                context.CancellationToken))
        {
            return;
        }
        var outcome = AnalyzerSemanticOutcome.NotApplicable;
        var operationFacts = new DefiniteOperationFacts(
            context.Compilation,
            context.CancellationToken);
        if (!CanReachMemberInitializer(
                initializer,
                isStatic,
                context.SemanticModel,
                operationFacts,
                context.CancellationToken))
        {
            return;
        }
        var reportedDiagnostics = new HashSet<string>(StringComparer.Ordinal);
        void ReportInitializerDiagnostic(Diagnostic diagnostic)
        {
            var key = diagnostic.Id + "|" +
                diagnostic.Location.SourceSpan.Start + "|" +
                diagnostic.Location.SourceSpan.Length + "|" +
                diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
            if (reportedDiagnostics.Add(key))
            {
                context.ReportDiagnostic(diagnostic);
            }
        }
        foreach (var candidate in constructors)
        {
            if (SharpProofControlAttributePolicy.ValidateAndShouldSuppress(
                    candidate,
                    session,
                    context.ReportDiagnostic,
                    context.CancellationToken))
            {
                session.RecordSemanticOutcome(
                    candidate,
                    AnalyzerSemanticOutcome.Suppressed);
                continue;
            }

            outcome = AnalyzerSemanticOutcome.NotApplicable;
            foreach (var operation in RequiresCallSiteDiscovery
                         .ExecutableUnflowedDescendantsAndSelf(
                             root,
                             operationFacts))
            {
                outcome = AnalyzerSemanticOutcomes.Combine(
                    outcome,
                    RequiresCallSiteAnalyzer.AnalyzeInitializerCall(
                        candidate, initializer, operation,
                        context.SemanticModel, session,
                        ReportInitializerDiagnostic, context.CancellationToken));
            }
            session.RecordSemanticOutcome(candidate, outcome);
        }
    }

    private static bool CanReachMemberInitializer(
        EqualsValueClauseSyntax target,
        bool isStatic,
        SemanticModel semanticModel,
        DefiniteOperationFacts operationFacts,
        CancellationToken cancellationToken)
    {
        var containingType = target.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        var targetMember = target.FirstAncestorOrSelf<MemberDeclarationSyntax>();
        if (containingType == null || targetMember == null)
        {
            return true;
        }

        var containingTypeSymbol = semanticModel.GetDeclaredSymbol(
            containingType, cancellationToken) as INamedTypeSymbol;
        if (containingTypeSymbol == null)
        {
            return true;
        }

        var syntaxTrees = semanticModel.Compilation.SyntaxTrees.ToImmutableArray();
        var declarations = containingTypeSymbol.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(cancellationToken))
            .OfType<TypeDeclarationSyntax>()
            .OrderBy(declaration => Array.IndexOf(
                syntaxTrees.ToArray(), declaration.SyntaxTree))
            .ThenBy(static declaration => declaration.SpanStart);
        foreach (var declaration in declarations)
        {
            foreach (var member in declaration.Members)
            {
                foreach (var initializer in GetMemberInitializers(member))
                {
                    if (initializer.SyntaxTree == target.SyntaxTree &&
                        initializer.Span == target.Span)
                    {
                        return true;
                    }
                    var initializerModel = initializer.SyntaxTree ==
                        semanticModel.SyntaxTree
                        ? semanticModel
                        : SharpProof.Frontend.Host.CompilationModelProvider
                            .GetSemanticModel(
                                semanticModel.Compilation,
                                initializer.SyntaxTree);
                    if (!HasMatchingInitializationKind(
                            initializer,
                            isStatic,
                            initializerModel,
                            cancellationToken))
                    {
                        continue;
                    }
                    var operation = initializerModel.GetOperation(
                        initializer.Value,
                        cancellationToken);
                    if (operation != null &&
                        !operationFacts.MayCompleteNormally(operation))
                    {
                        return false;
                    }
                }

                if (member.SyntaxTree == targetMember.SyntaxTree &&
                    member.Span == targetMember.Span)
                {
                    return true;
                }
            }
        }
        return true;
    }

    private static bool CanReachConstructorEntry(
        IMethodSymbol constructor,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (constructor.MethodKind != MethodKind.Constructor ||
            constructor.ContainingType is not { } containingType)
        {
            return true;
        }

        var syntaxTrees = semanticModel.Compilation.SyntaxTrees.ToImmutableArray();
        var operationFacts = new DefiniteOperationFacts(
            semanticModel.Compilation,
            cancellationToken);
        var declarations = containingType.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(cancellationToken))
            .OfType<TypeDeclarationSyntax>()
            .OrderBy(typeDeclaration => Array.IndexOf(
                syntaxTrees.ToArray(), typeDeclaration.SyntaxTree))
            .ThenBy(static typeDeclaration => typeDeclaration.SpanStart);
        foreach (var typeDeclaration in declarations)
        {
            foreach (var member in typeDeclaration.Members)
            {
                foreach (var initializer in GetMemberInitializers(member))
                {
                    var initializerModel = initializer.SyntaxTree ==
                        semanticModel.SyntaxTree
                        ? semanticModel
                        : SharpProof.Frontend.Host.CompilationModelProvider
                            .GetSemanticModel(
                                semanticModel.Compilation,
                                initializer.SyntaxTree);
                    if (!HasMatchingInitializationKind(
                            initializer,
                            constructor.IsStatic,
                            initializerModel,
                            cancellationToken))
                    {
                        continue;
                    }

                    var operation = initializerModel.GetOperation(
                        initializer.Value,
                        cancellationToken);
                    if (operation != null &&
                        !operationFacts.MayCompleteNormally(operation))
                    {
                        return false;
                    }
                }
            }
        }
        return true;
    }

    private static IEnumerable<EqualsValueClauseSyntax> GetMemberInitializers(
        MemberDeclarationSyntax member)
    {
        return member switch
        {
            BaseFieldDeclarationSyntax field => field.Declaration.Variables
                .Select(static variable => variable.Initializer)
                .OfType<EqualsValueClauseSyntax>(),
            PropertyDeclarationSyntax { Initializer: { } initializer } =>
                [initializer],
            _ => []
        };
    }

    private static bool HasMatchingInitializationKind(
        EqualsValueClauseSyntax initializer,
        bool isStatic,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var symbol = initializer.Parent switch
        {
            VariableDeclaratorSyntax variable => semanticModel.GetDeclaredSymbol(
                variable,
                cancellationToken),
            PropertyDeclarationSyntax property => semanticModel.GetDeclaredSymbol(
                property,
                cancellationToken),
            _ => null
        };
        return symbol is IFieldSymbol or IPropertySymbol or IEventSymbol &&
            symbol.IsStatic == isStatic;
    }

    private static bool ValidateContractClauses(
        IMethodSymbol method,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken)
    {
        var inventory = session.GetContractClauses(method);
        if (inventory.HasRejectedContractApiUsage &&
            session.TryMarkRejectedContractApiReported(method))
        {
            ReportRejectedContractApi(
                method,
                reportDiagnostic,
                cancellationToken);
        }
        var intrinsicViolations =
            session.GetContractIntrinsicViolations(inventory);
        ReportInvalidIntrinsics(intrinsicViolations, session, reportDiagnostic);
        ReportInvalidClauses(inventory.Clauses, reportDiagnostic);
        foreach (var owner in GetNestedOwners(inventory, session.Compilation))
        {
            ReportInvalidClauses(session.GetContractClauses(owner).Clauses, reportDiagnostic);
        }
        return inventory.HasRejectedContractApiUsage ||
            inventory.HasPlacementErrors ||
            !intrinsicViolations.IsDefaultOrEmpty;
    }

    private static void ReportRejectedContractApi(
        IMethodSymbol method,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken)
    {
        SharpProofControlAttributePolicy.ReportRejectedContractApi(
            method.Name,
            AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(
                method,
                cancellationToken),
            reportDiagnostic);
    }

    private static IEnumerable<IMethodSymbol> GetNestedOwners(
        ContractClauseInventory inventory,
        Compilation compilation)
    {
        return inventory.Clauses
            .Where(static clause => clause.Placement == ContractClausePlacement.NestedCallable)
            .Select(clause => SharpProof.Frontend.Host.CompilationModelProvider
                .GetSemanticModel(compilation, clause.Invocation.Syntax.SyntaxTree)
                .GetEnclosingSymbol(clause.Invocation.Syntax.SpanStart))
            .OfType<IMethodSymbol>()
            .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default);
    }

    private static void ReportInvalidIntrinsics(
        ImmutableArray<ContractIntrinsicViolation> violations,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic)
    {
        foreach (var violation in violations)
        {
            if (!session.TryMarkContractIntrinsicValidated(violation))
            {
                continue;
            }

            var isOld = violation.Failure is
                ContractBindingFailure.OldOutsideEnsures or ContractBindingFailure.NestedOld;
            var (argument, reason) = DescribeIntrinsicViolation(violation.Failure, isOld);
            reportDiagnostic(InvalidContractArgumentDiagnostics.Create(
                isOld ? "Contract.Old" : "Contract.Result",
                argument,
                reason,
                violation.Invocation.Syntax.GetLocation()));
        }
    }

    private static (string Argument, string Reason) DescribeIntrinsicViolation(
        ContractBindingFailure failure,
        bool isOld)
    {
        return AnalyzerDiagnosticCatalog.DescribeIntrinsicViolation(
            failure, isOld);
    }

    private static void ReportInvalidClauses(
        ImmutableArray<ContractClauseOccurrence> clauses,
        Action<Diagnostic> reportDiagnostic)
    {
        foreach (var clause in clauses)
        {
            if (clause.IsValid ||
                clause.Placement == ContractClausePlacement.NestedCallable)
            {
                continue;
            }

            reportDiagnostic(InvalidContractArgumentDiagnostics.Create(
                "Contract." + clause.Kind,
                "<placement>",
                DescribePlacement(clause.Placement),
                clause.Location));
        }
    }

    private static string DescribePlacement(ContractClausePlacement placement)
    {
        return AnalyzerDiagnosticCatalog.DescribePlacement(placement);
    }

    private static MethodSelection GetSelection(
        IMethodSymbol method,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken)
    {
        var features = session.Attributes.Select(
            method,
            session.Configuration.ContractsEnabled &&
            session.ResolveContractSource(method).HasSelectedContractIntent,
            SharpProofControlAttributePolicy.HasTrustedAttribute(
                method, session.Attributes)) &
            ((session.Configuration.ContractsEnabled
                  ? ContractSelectionFeatures.Contracts
                  : ContractSelectionFeatures.None) |
             (session.Configuration.EffectsEnabled
                  ? ContractSelectionFeatures.Effects
                  : ContractSelectionFeatures.None));
        var suppressed = SharpProofControlAttributePolicy.ValidateAndShouldSuppress(
            method, session, reportDiagnostic, cancellationToken);
        return new(features, suppressed);
    }

    private static MethodSelection GetFeatureSelection(
        IMethodSymbol method,
        AnalyzerSession session)
    {
        var features = session.Attributes.Select(
            method,
            session.Configuration.ContractsEnabled &&
            session.ResolveContractSource(method).HasSelectedContractIntent,
            SharpProofControlAttributePolicy.HasTrustedAttribute(
                method, session.Attributes)) &
            ((session.Configuration.ContractsEnabled
                  ? ContractSelectionFeatures.Contracts
                  : ContractSelectionFeatures.None) |
             (session.Configuration.EffectsEnabled
                  ? ContractSelectionFeatures.Effects
                  : ContractSelectionFeatures.None));
        return new(features, false);
    }

    private static SyntaxNode? FindDeclaration(
        IMethodSymbol method,
        ImmutableArray<IOperation> operationBlocks,
        CancellationToken cancellationToken)
    {
        var operationSyntax = operationBlocks
            .OrderByDescending(static operation => operation.Syntax.Span.Length)
            .FirstOrDefault()?.Syntax;
        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (operationSyntax == null ||
                reference.SyntaxTree == operationSyntax.SyntaxTree &&
                reference.Span.Contains(operationSyntax.Span))
            {
                return NormalizeDeclaration(reference.GetSyntax(cancellationToken));
            }
        }
        return null;
    }

    private static SyntaxNode NormalizeDeclaration(SyntaxNode declaration)
    {
        return declaration switch
        {
            ArrowExpressionClauseSyntax { Parent: { } parent } => parent,
            _ => declaration
        };
    }

    private readonly partial record struct MethodSelection
    {
        internal bool Contracts => (Features & ContractSelectionFeatures.Contracts) != 0;
        internal bool Effects => (Features & ContractSelectionFeatures.Effects) != 0;
        internal bool Any => Contracts || Effects;
    }
}
