namespace SharpProof.Analyzer;

internal static partial class AnalyzerFeaturePipeline
{
    internal static void AnalyzeSynthesizedRecordMembers(
        SyntaxNodeAnalysisContext context,
        AnalyzerSession session)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (context.Node is not TypeDeclarationSyntax declaration ||
            declaration.Kind() is not (
                SyntaxKind.RecordDeclaration or
                SyntaxKind.RecordStructDeclaration) ||
            AnalyzerGeneratedCodePolicy.IsGenerated(
                context.Node.SyntaxTree,
                context.Compilation,
                context.CancellationToken) ||
            context.SemanticModel.GetDeclaredSymbol(
                declaration,
                context.CancellationToken) is not INamedTypeSymbol type)
        {
            return;
        }

        var analyzedTargets = new HashSet<IMethodSymbol>(
            SymbolEqualityComparer.Default);
        foreach (var method in type.GetMembers()
                     .OfType<IMethodSymbol>()
                     .Where(static method => method.IsImplicitlyDeclared))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (!session.TryBeginRequiresCallSiteAnalysis(method))
            {
                continue;
            }

            var outcome = AnalyzerSemanticOutcome.NotApplicable;
            if (SharpProofControlAttributePolicy.ValidateAndShouldSuppress(
                    method,
                    session,
                    context.ReportDiagnostic,
                    context.CancellationToken))
            {
                session.RecordSemanticOutcome(
                    method,
                    AnalyzerSemanticOutcome.Suppressed);
                continue;
            }

            foreach (var target in GetSynthesizedTargets(
                         type,
                         method,
                         declaration))
            {
                if (!analyzedTargets.Add(target) ||
                    !session.HasPotentialCallPreconditions(target) ||
                    !TryGetSyntheticOrigin(
                        target,
                        context.Compilation,
                        context.CancellationToken,
                        out var origin))
                {
                    continue;
                }

                outcome = AnalyzerSemanticOutcomes.Combine(
                    outcome,
                    RequiresCallSiteAnalyzer.AnalyzeSynthesizedCall(
                        method,
                        declaration,
                        context.SemanticModel,
                        session,
                        context.ReportDiagnostic,
                        target,
                        origin,
                        context.CancellationToken));
            }

            session.RecordSemanticOutcome(method, outcome);
        }
    }

    private static IEnumerable<IMethodSymbol> GetSynthesizedTargets(
        INamedTypeSymbol type,
        IMethodSymbol method,
        TypeDeclarationSyntax declaration)
    {
        if (method.MethodKind == MethodKind.Constructor &&
            RequiresCallSiteDiscovery.IsRecordCopyConstructor(method))
        {
            var baseType = type.BaseType;
            var copy = baseType?.InstanceConstructors.FirstOrDefault(
                candidate => RequiresCallSiteDiscovery.IsRecordCopyConstructor(
                    candidate));
            if (copy != null)
            {
                yield return copy;
            }
        }

        if (method.Name == "PrintMembers" &&
            method.Parameters.Length == 1)
        {
            if (method.OverriddenMethod != null)
            {
                yield return method.OverriddenMethod;
            }

            if (method.IsImplicitlyDeclared)
            {
                foreach (var property in type.GetMembers()
                             .OfType<IPropertySymbol>()
                             .Where(static property =>
                                 !property.IsStatic &&
                                 !property.IsIndexer &&
                                 property.Name != "EqualityContract" &&
                                 property.GetMethod != null))
                {
                    yield return property.GetMethod!;
                }
            }
        }

        if (method.Name == "GetHashCode" &&
            method.Parameters.IsEmpty &&
            method.OverriddenMethod != null)
        {
            yield return method.OverriddenMethod;
        }

        if (method.Name == "ToString" && method.Parameters.IsEmpty)
        {
            var printMembers = type.GetMembers("PrintMembers")
                .OfType<IMethodSymbol>()
                .FirstOrDefault(static candidate =>
                    candidate.Parameters.Length == 1);
            if (printMembers is { IsImplicitlyDeclared: false })
            {
                yield return printMembers;
            }
        }

        if (method.Name == "<Clone>$" && method.Parameters.IsEmpty)
        {
            var copy = type.InstanceConstructors.FirstOrDefault(
                candidate => RequiresCallSiteDiscovery.IsRecordCopyConstructor(
                    candidate));
            if (copy is { IsImplicitlyDeclared: false })
            {
                yield return copy;
            }
        }

        if (method.Name == "Deconstruct" &&
            declaration.ParameterList != null)
        {
            foreach (var parameter in method.Parameters)
            {
                var property = type.GetMembers(parameter.Name)
                    .OfType<IPropertySymbol>()
                    .FirstOrDefault(static candidate =>
                        !candidate.IsStatic && candidate.GetMethod != null);
                if (property?.GetMethod != null)
                {
                    yield return property.GetMethod;
                }
            }
        }

        if (method.MethodKind == MethodKind.UserDefinedOperator &&
            method.Name is "op_Equality" or "op_Inequality")
        {
            var equals = type.GetMembers("Equals")
                .OfType<IMethodSymbol>()
                .FirstOrDefault(candidate =>
                    candidate.Parameters.Length == 1 &&
                    SymbolEqualityComparer.Default.Equals(
                        candidate.Parameters[0].Type,
                        type));
            if (equals != null)
            {
                yield return equals;
            }
        }
    }

    private static bool TryGetSyntheticOrigin(
        IMethodSymbol target,
        Compilation compilation,
        CancellationToken cancellationToken,
        out IOperation origin)
    {
        foreach (var reference in target.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var syntax = reference.GetSyntax(cancellationToken);
            var model = SharpProof.Frontend.Host.CompilationModelProvider
                .GetSemanticModel(compilation, syntax.SyntaxTree);
            var candidate = syntax switch
            {
                BaseMethodDeclarationSyntax method when method.Body != null =>
                    model.GetOperation(method.Body, cancellationToken),
                BaseMethodDeclarationSyntax method when method.ExpressionBody != null =>
                    model.GetOperation(
                        method.ExpressionBody.Expression,
                        cancellationToken),
                AccessorDeclarationSyntax accessor when accessor.Body != null =>
                    model.GetOperation(accessor.Body, cancellationToken),
                AccessorDeclarationSyntax accessor when accessor.ExpressionBody != null =>
                    model.GetOperation(
                        accessor.ExpressionBody.Expression,
                        cancellationToken),
                _ => model.GetOperation(syntax, cancellationToken)
            };
            if (candidate != null)
            {
                origin = candidate;
                return true;
            }
        }

        origin = null!;
        return false;
    }
}
