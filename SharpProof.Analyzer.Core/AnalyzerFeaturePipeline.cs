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
        if (context.Symbol is not IMethodSymbol method ||
            method.DeclaringSyntaxReferences.IsDefaultOrEmpty)
        {
            return;
        }
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
        if (method.PartialDefinitionPart != null &&
            !session.TryBeginExecutableAnalysis(method))
        {
            return;
        }
        method = AnalyzerSession.NormalizeMethod(method);

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
            context.Compilation is CSharpCompilation compilation &&
            AdvisoryLowering.Lower(compilation, method, declaration, context.CancellationToken).Gap is { } gap)
        {
            ReportSelectedAnalysisIncomplete(
                context.ReportDiagnostic,
                AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(
                    declaration),
                method.Name,
                "Advisory:" + gap);
            outcome = AnalyzerSemanticOutcome.Abstained;
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
                    context.CancellationToken));
        }

        session.RecordSemanticOutcome(method, outcome);
    }

    internal static void AnalyzeLambdaEffects(
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

        var operation = context.SemanticModel.GetOperation(
            context.Node,
            context.CancellationToken);
        if (operation is not (IAnonymousFunctionOperation or
            ILocalFunctionOperation))
        {
            return;
        }

        var method = operation switch
        {
            IAnonymousFunctionOperation anonymousFunction =>
                anonymousFunction.Symbol,
            ILocalFunctionOperation localFunction => localFunction.Symbol,
            _ => throw new InvalidOperationException(
                "Unexpected nested callable operation.")
        };
        var body = operation switch
        {
            IAnonymousFunctionOperation anonymousFunction =>
                anonymousFunction.Body,
            ILocalFunctionOperation localFunction => localFunction.Body,
            _ => null
        };
        if (body == null)
        {
            return;
        }
        if (AnalyzerGeneratedCodePolicy.IsGenerated(
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
            ReportAnonymousRequiresPlacement(
                method,
                session.GetContractClauses(method).Clauses,
                session,
                context.ReportDiagnostic);
        }

        EffectContractDiagnostics.ValidateArguments(
            method,
            session,
            context.ReportDiagnostic);
        var rejectedContractApi =
            session.Attributes.GetRejectedSelectionFeatures(method) !=
            ContractSelectionFeatures.None;
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

        var selection = GetSelection(
            method,
            session,
            context.ReportDiagnostic,
            context.CancellationToken);
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

        return SharpProofControlAttributePolicy
            .ValidateAndShouldSuppress(
                constructor,
                session,
                reportDiagnostic,
                cancellationToken)
            ? AnalyzerSemanticOutcome.Suppressed
            : AnalyzerSemanticOutcome.NotApplicable;
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
        ReportInvalidClauses(inventory.Clauses, reportDiagnostic);
        if (method.MethodKind == MethodKind.AnonymousFunction)
        {
            ReportAnonymousRequiresPlacement(
                method,
                inventory.Clauses,
                session,
                reportDiagnostic);
        }
        foreach (var owner in GetNestedOwners(inventory, session.Compilation))
        {
            ReportInvalidClauses(session.GetContractClauses(owner).Clauses, reportDiagnostic);
        }
        return inventory.HasRejectedContractApiUsage ||
            inventory.HasPlacementErrors ||
            !intrinsicViolations.IsDefaultOrEmpty;
    }

    private static void ReportAnonymousRequiresPlacement(
        IMethodSymbol method,
        ImmutableArray<ContractClauseOccurrence> clauses,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic)
    {
        if (method.MethodKind != MethodKind.AnonymousFunction ||
            !session.TryBeginAnonymousRequiresPlacementAnalysis(method))
        {
            return;
        }

        foreach (var clause in clauses)
        {
            if (clause.Kind != BoundContractKind.Requires ||
                clause.Placement != ContractClausePlacement.ValidPrologue)
            {
                continue;
            }

            reportDiagnostic(InvalidContractArgumentDiagnostics.Create(
                "Contract.Requires",
                "<placement>",
                "preconditions inside lambda and anonymous methods cannot be enforced at delegate invocation sites",
                clause.Location));
        }
    }

    private static bool TryRecordRejectedContractAbstention(
        IMethodSymbol method,
        bool rejectedContractApi,
        AnalyzerSession session)
    {
        if (!rejectedContractApi)
        {
            return false;
        }

        session.RecordSemanticOutcome(
            method,
            AnalyzerSemanticOutcome.Abstained);
        return true;
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

            reportDiagnostic(
                InvalidContractArgumentDiagnostics.Create(violation));
        }
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
                AnalyzerDiagnosticCatalog.DescribePlacement(clause.Placement),
                clause.Location));
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
