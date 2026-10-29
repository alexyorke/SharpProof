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

        var outcome = AdvisoryRequiresDiagnostics.Analyze(
            method,
            declaration,
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
        var method = (IMethodSymbol)context.Symbol;
        if (method.PartialImplementationPart != null)
        {
            return;
        }
        var firstDeclaration = method.DeclaringSyntaxReferences[0]
            .GetSyntax(context.CancellationToken);
        var isGenerated = AnalyzerGeneratedCodePolicy.IsGenerated(
            method,
            firstDeclaration.SyntaxTree,
            context.Compilation,
            context.CancellationToken);
        // The implementation owns validation, but either partial declaration
        // can be handwritten.
        if (isGenerated && method.PartialDefinitionPart is { } definition)
        {
            isGenerated = definition.DeclaringSyntaxReferences.All(reference =>
                reference is null ? throw new System.ArgumentNullException(nameof(reference)) : (AnalyzerGeneratedCodePolicy.IsGenerated(
                    definition,
                    reference.SyntaxTree,
                    context.Compilation,
                    context.CancellationToken)));
        }
        if (isGenerated)
        {
            return;
        }

        EffectContractDiagnostics.ValidateArguments(method, session, context.ReportDiagnostic);
        ClosedContractDiagnostics.Validate(method, session, context.ReportDiagnostic);
        var rejectedContractApi =
            session.Attributes.GetRejectedSelectionFeatures(
                method,
                out var rejectedCallableFeatures) !=
            ContractSelectionFeatures.None;
        var rejectedCallableApi = rejectedCallableFeatures !=
            ContractSelectionFeatures.None;
        if (rejectedCallableApi &&
            session.TryMarkRejectedContractApiReported(method))
        {
            SharpProofControlAttributePolicy.ReportRejectedContractApi(
                method.Name,
                AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(
                    method,
                    context.CancellationToken),
                context.ReportDiagnostic);
        }
        var selection = GetSelection(
            method, session, context.ReportDiagnostic, context.CancellationToken);
        if (IsConcreteSemicolonAccessor(method, context.CancellationToken) &&
            selection.Any)
        {
            if (TryRecordSuppressed(method, selection, session))
            {
                return;
            }

            if (AutoPropertyFacts.IsAccessor(
                    method,
                    context.CancellationToken))
            {
                var declaration = method.DeclaringSyntaxReferences
                    .FirstOrDefault()?
                    .GetSyntax(context.CancellationToken);
                if (declaration != null)
                {
                    var outcome = EffectContractDiagnostics.Analyze(
                        method,
                        declaration,
                        session,
                        context.ReportDiagnostic,
                        context.CancellationToken);
                    session.RecordSemanticOutcome(method, outcome);
                }
                return;
            }

            session.RegisterSelectedSemicolonAccessor(method);
        }
        if ((!method.IsAbstract && !method.IsExtern) || !selection.Any)
        {
            return;
        }

        if (TryRecordSuppressed(method, selection, session))
        {
            return;
        }
        if (TryRecordRejectedContractAbstention(
                method,
                rejectedContractApi,
                session))
        {
            return;
        }
        var contractSource = session.ResolveContractSource(method);
        if ((method.IsAbstract || method.IsExtern) &&
            contractSource.UsesCompanion &&
            contractSource.Failure == ContractBindingFailure.None &&
            !contractSource.HasValidDirectClause)
        {
            session.RecordSemanticOutcome(
                method,
                AnalyzerSemanticOutcome.NotApplicable);
            return;
        }
        if (!selection.Contracts &&
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
        if (!selection.Contracts && selection.Effects)
        {
            ReportSelectedAnalysisIncomplete(
                context.ReportDiagnostic,
                AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(
                    method,
                    context.CancellationToken),
                method.Name,
                "BodylessEffectContractNotEnforced");
            session.RecordSemanticOutcome(
                method,
                AnalyzerSemanticOutcome.NotApplicable);
            return;
        }
        ReportSelectedAnalysisIncomplete(
            context.ReportDiagnostic,
            AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(
                method,
                context.CancellationToken),
            method.Name,
            "MissingOperationRoot");
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
        method = AnalyzerSession.NormalizeMethod(method);

        var declaration = method.DeclaringSyntaxReferences.IsDefaultOrEmpty
            ? null
            : FindDeclaration(method, context.OperationBlocks, context.CancellationToken);
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
        OverridePreconditionDiagnostics.Validate(
            method,
            session,
            context.ReportDiagnostic,
            context.CancellationToken);
        if (TryRecordRejectedContractAbstention(
                method,
                rejectedContractApi,
                session))
        {
            return;
        }
        if (TryRecordSuppressed(method, selection, session))
        {
            return;
        }
        if ((method.IsAbstract || method.IsExtern) &&
            !selection.Contracts &&
            selection.Effects)
        {
            session.RecordSemanticOutcome(
                method,
                AnalyzerSemanticOutcome.NotApplicable);
            return;
        }

        var outcome = AnalyzerSemanticOutcome.NotApplicable;
        (AdvisoryLoweredBody? Body, string? Gap)? lowered = null;
        if (selection.Effects)
        {
            outcome = EffectContractDiagnostics.Analyze(
                method,
                declaration,
                session,
                context.ReportDiagnostic,
                context.CancellationToken);
        }
        // A body the IR cannot lower leaves its postconditions unknown.
        else if (selection.Contracts && !hasInvalidContractClauses &&
            !method.IsAbstract && !method.IsExtern &&
            context.Compilation is CSharpCompilation compilation)
        {
            var result = AdvisoryLowering.Lower(compilation, method, declaration, context.CancellationToken);
            // The requires analysis lowers the same body of the session's
            // compilation; reuse this lowering rather than repeat it.
            if (ReferenceEquals(compilation, session.Compilation))
            {
                lowered = result;
            }
            if (result.Gap is { } gap)
            {
                ReportSelectedAnalysisIncomplete(
                    context.ReportDiagnostic,
                    AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(
                        declaration),
                    method.Name,
                    "Advisory:" + gap);
                outcome = AnalyzerSemanticOutcome.Abstained;
            }
        }

        if (session.Configuration.ContractsEnabled &&
            !IsNestedCallable(method))
        {
            outcome = AnalyzerSemanticOutcomes.Combine(
                outcome,
                AdvisoryRequiresDiagnostics.Analyze(
                    method,
                    declaration,
                    session,
                    context.ReportDiagnostic,
                    context.CancellationToken,
                    lowered));
        }

        session.RecordSemanticOutcome(method, outcome);
    }

    internal static void AnalyzeLambdaEffects(
        SyntaxNodeAnalysisContext context,
        AnalyzerSession session)
    {
        context.CancellationToken.ThrowIfCancellationRequested();

        var operation = context.SemanticModel.GetOperation(
            context.Node,
            context.CancellationToken);
        IMethodSymbol? method = operation switch
        {
            IAnonymousFunctionOperation { Body: not null } anonymousFunction => anonymousFunction.Symbol,
            ILocalFunctionOperation { Body: not null } localFunction => localFunction.Symbol,
            _ => null
        };
        if (method == null)
        {
            return;
        }
        var rejectedContractApi =
            session.Attributes.GetRejectedSelectionFeatures(method) !=
            ContractSelectionFeatures.None;
        var selection = GetSelection(
            method,
            session,
            context.ReportDiagnostic,
            context.CancellationToken);
        if (!selection.Any &&
            !rejectedContractApi &&
            AnalyzerGeneratedCodePolicy.IsGenerated(
                method,
                context.Node.SyntaxTree,
                context.Compilation,
                context.CancellationToken))
        {
            return;
        }

        if (method.MethodKind == MethodKind.AnonymousFunction &&
            session.Configuration.ContractsEnabled)
        {
            session.ClausePlacement.ReportAnonymousRequires(
                session.GetContractClauses(method),
                context.ReportDiagnostic);
        }

        EffectContractDiagnostics.ValidateArguments(
            method,
            session,
            context.ReportDiagnostic);
        if (rejectedContractApi &&
            session.TryMarkRejectedContractApiReported(method))
        {
            SharpProofControlAttributePolicy.ReportRejectedContractApi(
                method.Name,
                AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(
                    method,
                    context.CancellationToken),
                context.ReportDiagnostic);
        }

        if (!selection.Effects ||
            !session.TryBeginExecutableAnalysis(method))
        {
            return;
        }
        if (TryRecordRejectedContractAbstention(
                method,
                rejectedContractApi,
                session))
        {
            return;
        }
        if (TryRecordSuppressed(method, selection, session))
        {
            return;
        }

        session.RecordSemanticOutcome(
            method,
            EffectContractDiagnostics.Analyze(
                method,
                context.Node,
                session,
                context.ReportDiagnostic,
                context.CancellationToken));
    }

    internal static void ReconcileSelectedSemicolonAccessors(
        CompilationAnalysisContext context,
        AnalyzerSession session)
    {
        session = RepositoryParameterGuard.NotNull(session, nameof(session));
        foreach (var method in session.GetUnrecordedSelectedSemicolonAccessors())
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            ReportSelectedAnalysisIncomplete(
                context.ReportDiagnostic,
                AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(
                    method,
                    context.CancellationToken),
                method.Name,
                "MissingOperationRoot");
            session.RecordSemanticOutcome(
                method,
                AnalyzerSemanticOutcome.Abstained);
        }
    }

    private static bool IsNestedCallable(IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        return method.MethodKind is
            MethodKind.LocalFunction or MethodKind.AnonymousFunction;
    }

    private static bool IsConcreteSemicolonAccessor(
        IMethodSymbol method,
        CancellationToken cancellationToken)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        return !method.IsAbstract &&
            !method.IsExtern &&
            method.MethodKind is
                MethodKind.PropertyGet or
                MethodKind.PropertySet or
                MethodKind.EventAdd or
                MethodKind.EventRemove &&
            method.DeclaringSyntaxReferences.Any(reference =>
                reference is null ? throw new System.ArgumentNullException(nameof(reference)) : (reference.GetSyntax(cancellationToken) is AccessorDeclarationSyntax
                {
                    Body: null,
                    ExpressionBody: null,
                    SemicolonToken.RawKind: not 0
                }));
    }

    internal static void AnalyzeMemberInitializer(
        SyntaxNodeAnalysisContext context,
        AnalyzerSession session)
    {
        session = RepositoryParameterGuard.NotNull(session, nameof(session));
        context.CancellationToken.ThrowIfCancellationRequested();
        var initializer = context.Node as EqualsValueClauseSyntax;
        var declaredOwner = initializer?.Parent switch
        {
            VariableDeclaratorSyntax
            {
                Parent: VariableDeclarationSyntax
                {
                    Parent: FieldDeclarationSyntax or EventFieldDeclarationSyntax
                }
            } variable => context.SemanticModel.GetDeclaredSymbol(
                    variable, context.CancellationToken),
            PropertyDeclarationSyntax property =>
                context.SemanticModel.GetDeclaredSymbol(
                    property, context.CancellationToken),
            _ => null
        };
        if (initializer == null || declaredOwner is not (IFieldSymbol or IPropertySymbol or IEventSymbol))
        {
            return;
        }

        var rawOwner = context.SemanticModel.GetEnclosingSymbol(
            initializer.Value.SpanStart, context.CancellationToken);
        var owner = rawOwner;
        if (rawOwner is IFieldSymbol backingField &&
            backingField.AssociatedSymbol is IPropertySymbol or IEventSymbol)
        {
            owner = backingField.AssociatedSymbol;
        }
        if (!SymbolEqualityComparer.Default.Equals(owner, declaredOwner))
        {
            return;
        }

        var operation = context.SemanticModel.GetOperation(
            initializer, context.CancellationToken);
        if (operation == null || owner == null ||
            AnalyzerGeneratedCodePolicy.IsGenerated(
                owner, initializer.SyntaxTree, context.Compilation, context.CancellationToken))
        {
            return;
        }

        ReportInvalidIntrinsics(
            session.GetMemberInitializerContractIntrinsicViolations(operation),
            session,
            context.ReportDiagnostic);
    }

    internal static void AnalyzePrimaryConstructor(
        SyntaxNodeAnalysisContext context,
        AnalyzerSession session)
    {
        session = RepositoryParameterGuard.NotNull(session, nameof(session));
        context.CancellationToken.ThrowIfCancellationRequested();
        if (context.Node is not TypeDeclarationSyntax declaration ||
            AnalyzerGeneratedCodePolicy.IsGenerated(
                context.Node.SyntaxTree,
                context.Compilation,
                context.CancellationToken))
        {
            return;
        }

        if (!PrimaryConstructorCallableInventory.TryGet(
                declaration,
                context.SemanticModel,
                context.CancellationToken,
                out var constructor) &&
            !PrimaryConstructorCallableInventory.TryGetSynthesizedDefault(
                declaration,
                context.SemanticModel,
                context.CancellationToken,
                out constructor))
        {
            return;
        }

        var outcome = AnalyzePrimaryConstructor(constructor, declaration,
            context.SemanticModel, session, context.ReportDiagnostic, context.CancellationToken);
        if (outcome.HasValue)
        {
            session.RecordSemanticOutcome(constructor, outcome.Value);
        }
    }

    internal static AnalyzerSemanticOutcome? AnalyzePrimaryConstructor(
        IMethodSymbol constructor,
        TypeDeclarationSyntax declaration,
        SemanticModel semanticModel,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken)
    {
        if (AnalyzerGeneratedCodePolicy.IsGenerated(constructor, declaration.SyntaxTree,
                semanticModel.Compilation, cancellationToken) ||
            !session.TryBeginExecutableAnalysis(constructor))
        {
            return null;
        }

        var suppressed = SharpProofControlAttributePolicy
            .ValidateAndShouldSuppress(
                constructor,
                session,
                reportDiagnostic,
                cancellationToken);
        var hasInvalidIntrinsics = false;
        if (declaration.BaseList != null)
        {
            foreach (var baseType in declaration.BaseList.Types
                         .OfType<PrimaryConstructorBaseTypeSyntax>())
            {
                foreach (var argument in baseType.ArgumentList.Arguments)
                {
                    var operation = semanticModel.GetOperation(argument, cancellationToken);
                    if (operation == null)
                    {
                        var expression = argument.Expression;
                        while (expression is ParenthesizedExpressionSyntax or CheckedExpressionSyntax)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            expression = expression is ParenthesizedExpressionSyntax parenthesized
                                ? parenthesized.Expression
                                : ((CheckedExpressionSyntax)expression).Expression;
                        }
                        operation = semanticModel.GetOperation(expression, cancellationToken);
                    }
                    var violations = session.GetContractIntrinsicViolations(constructor, operation);
                    hasInvalidIntrinsics |= !violations.IsDefaultOrEmpty;
                    ReportInvalidIntrinsics(violations, session, reportDiagnostic);
                }
            }
        }
        if (!session.Configuration.ContractsEnabled && !hasInvalidIntrinsics)
        {
            return null;
        }
        return suppressed
            ? AnalyzerSemanticOutcome.Suppressed
            : hasInvalidIntrinsics
                ? AnalyzerSemanticOutcome.Abstained
                : AnalyzerSemanticOutcome.NotApplicable;
    }

    private static bool ValidateContractClauses(
        IMethodSymbol method,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        reportDiagnostic = RepositoryParameterGuard.NotNull(reportDiagnostic, nameof(reportDiagnostic));
        session = RepositoryParameterGuard.NotNull(session, nameof(session));
        var inventory = session.GetContractClauses(method);
        if (inventory.HasRejectedContractApiUsage &&
            session.TryMarkRejectedContractApiReported(method))
        {
            SharpProofControlAttributePolicy.ReportRejectedContractApi(
                method.Name,
                AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(
                    method,
                    cancellationToken),
                reportDiagnostic);
        }
        var intrinsicViolations =
            session.GetContractIntrinsicViolations(inventory);
        ReportInvalidIntrinsics(intrinsicViolations, session, reportDiagnostic);
        session.ClausePlacement.Report(inventory, reportDiagnostic);
        return inventory.HasRejectedContractApiUsage ||
            inventory.HasPlacementErrors ||
            !intrinsicViolations.IsDefaultOrEmpty;
    }

    private static bool TryRecordRejectedContractAbstention(
        IMethodSymbol method,
        bool rejectedContractApi,
        AnalyzerSession session)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        session = RepositoryParameterGuard.NotNull(session, nameof(session));
        if (!rejectedContractApi)
        {
            return false;
        }

        session.RecordSemanticOutcome(
            method,
            AnalyzerSemanticOutcome.Abstained);
        return true;
    }

    private static void ReportInvalidIntrinsics(
        ImmutableArray<ContractIntrinsicViolation> violations,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic)
    {

        RepositoryParameterGuard.ThrowIfInvalid(violations.IsDefault, nameof(violations));
        reportDiagnostic = RepositoryParameterGuard.NotNull(reportDiagnostic, nameof(reportDiagnostic));
        session = RepositoryParameterGuard.NotNull(session, nameof(session));
        foreach (var violation in violations)
        {
            if (!session.TryMarkContractIntrinsicValidated(violation))
            {
                continue;
            }

            reportDiagnostic(
                InvalidContractArgumentDiagnostics.Create(violation));
        }
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
            session.ResolveContractSource(method).HasSelectedContractIntent) &
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

    private static bool TryRecordSuppressed(
        IMethodSymbol method,
        MethodSelection selection,
        AnalyzerSession session)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        session = RepositoryParameterGuard.NotNull(session, nameof(session));
        if (!selection.IsSuppressed)
        {
            return false;
        }

        session.RecordSemanticOutcome(
            method,
            AnalyzerSemanticOutcome.Suppressed);
        return true;
    }

    private static void ReportSelectedAnalysisIncomplete(
        Action<Diagnostic> report,
        Location location,
        string methodName,
        object reason)
    {
        location = RepositoryParameterGuard.NotNull(location, nameof(location));
        methodName = RepositoryParameterGuard.NotNull(methodName, nameof(methodName));
        reason = RepositoryParameterGuard.NotNull(reason, nameof(reason));
        report = RepositoryParameterGuard.NotNull(report, nameof(report));
        report(Diagnostic.Create(
            GeneratedDiagnosticDescriptors.SelectedAnalysisIncompleteRule,
            location,
            methodName,
            reason));
    }

    private static SyntaxNode? FindDeclaration(
        IMethodSymbol method,
        ImmutableArray<IOperation> operationBlocks,
        CancellationToken cancellationToken)
    {

        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        RepositoryParameterGuard.RequireInitialized(operationBlocks.IsDefault, nameof(operationBlocks), "Array must be initialized.");
        var operationSyntax = operationBlocks
            .OrderByDescending(static operation => operation is null ? throw new System.ArgumentNullException(nameof(operation)) : (operation.Syntax.Span.Length))
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
        declaration = RepositoryParameterGuard.NotNull(declaration, nameof(declaration));
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
