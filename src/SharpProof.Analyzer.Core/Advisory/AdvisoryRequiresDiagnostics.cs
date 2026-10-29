using SharpProof.Dataflow;

namespace SharpProof.Analyzer;

// SP0027: a call whose callee precondition is false in every state the
// advisory interpreter reaches it with violates that precondition. The
// analyzer never proves a call site; Z3 decides proofs.
internal static class AdvisoryRequiresDiagnostics
{
    // The callable's own outcome; its local functions are checked as well.
    // A call site reached both in a local function's own body and inlined
    // into its caller is reported once.
    internal static AnalyzerSemanticOutcome Analyze(IMethodSymbol method, SyntaxNode declaration, AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic, CancellationToken cancellationToken, (AdvisoryLoweredBody? Body, string? Gap)? lowered = null)
    {
        declaration = RepositoryParameterGuard.NotNull(declaration, nameof(declaration));
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        reportDiagnostic = RepositoryParameterGuard.NotNull(reportDiagnostic, nameof(reportDiagnostic));
        if (session.Compilation is not CSharpCompilation compilation)
        { return AnalyzerSemanticOutcome.NotApplicable; }
        var reported = ImmutableHashSet.CreateBuilder<(Location Location, string Message)>();
        var report = reportDiagnostic;
        reportDiagnostic = diagnostic =>
        {
            diagnostic = RepositoryParameterGuard.NotNull(diagnostic, nameof(diagnostic));
            var identity = (diagnostic.Location, diagnostic.GetMessage(CultureInfo.InvariantCulture));
            if (reported.Add(identity))
            {
                report(diagnostic);
            }
        };
        var model = Frontend.Host.CompilationModelProvider.GetSemanticModel(compilation, declaration.SyntaxTree);
        foreach (var local in declaration.DescendantNodes().OfType<LocalFunctionStatementSyntax>())
        {
            if (model.GetDeclaredSymbol(local, cancellationToken) is IMethodSymbol function)
            { _ = AnalyzeBody(compilation, function, local, session, reportDiagnostic, cancellationToken); }
        }
        return AnalyzeBody(compilation, method, declaration, session, reportDiagnostic, cancellationToken, lowered);
    }

    private static AnalyzerSemanticOutcome AnalyzeBody(CSharpCompilation compilation, IMethodSymbol method, SyntaxNode declaration,
        AnalyzerSession session, Action<Diagnostic> reportDiagnostic, CancellationToken cancellationToken, (AdvisoryLoweredBody? Body, string? Gap)? lowered = null)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        declaration = RepositoryParameterGuard.NotNull(declaration, nameof(declaration));
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        reportDiagnostic = RepositoryParameterGuard.NotNull(reportDiagnostic, nameof(reportDiagnostic));
        session = RepositoryParameterGuard.NotNull(session, nameof(session));
        var body = (lowered ?? AdvisoryLowering.Lower(compilation, method, declaration, cancellationToken)).Body;
        if (body == null)
        { return CallsPreconditions(compilation, declaration, session, cancellationToken) ? AnalyzerSemanticOutcome.Unknown : AnalyzerSemanticOutcome.NotApplicable; }
        var calls = body.Lowering.CallPreconditions;
        if (calls.IsEmpty)
        { return AnalyzerSemanticOutcome.NotApplicable; }
        var facts = new CoreIrAdvisoryInterpreter(body.Program, Variables(body.Program))
            .Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty, markers: [.. calls.Keys], token: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!facts.Accepted)
        { return AnalyzerSemanticOutcome.Unknown; }
        var outcome = AnalyzerSemanticOutcome.Unknown;
        foreach (var snapshot in facts.Markers.Where(static snapshot => snapshot is null ? throw new System.ArgumentNullException(nameof(snapshot)) : (snapshot.Value is { IsSingleton: true, SingletonValue: 0 }))
            .GroupBy(static snapshot => snapshot is null ? throw new System.ArgumentNullException(nameof(snapshot)) : (snapshot.Marker.Id)).Select(static group => group is null ? throw new System.ArgumentNullException(nameof(group)) : (group.First())))
        {
            var call = calls[snapshot.Marker];
            var location = body.Locate(snapshot.Marker.Operation) ?? Location.None;
            // Source calls can inline a suppressed local or ordinary method.
            // Keep its execution and preconditions in the IR, but apply the
            // reporting policy of the declaration that owns this call site.
            if (location.SourceTree is { } tree &&
                Frontend.Host.CompilationModelProvider.GetSemanticModel(compilation, tree)
                    .GetEnclosingSymbol(location.SourceSpan.Start, cancellationToken) is IMethodSymbol owner &&
                SharpProofControlAttributePolicy.ValidateAndShouldSuppress(
                    owner, session, reportDiagnostic, cancellationToken))
            { continue; }
            reportDiagnostic(Diagnostic.Create(GeneratedDiagnosticDescriptors.RequiresNotProvenRule, location,
                CalleeName(compilation, location, call.CalleeIdentity, cancellationToken),
                ClauseText(body.Locate(call.ClauseSite))));
            outcome = AnalyzerSemanticOutcome.Refuted;
        }
        return outcome;
    }

    // Whether the body calls anything with a precondition, for a body the IR
    // cannot lower.
    private static bool CallsPreconditions(CSharpCompilation compilation, SyntaxNode declaration, AnalyzerSession session,
        CancellationToken cancellationToken)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        declaration = RepositoryParameterGuard.NotNull(declaration, nameof(declaration));
        session = RepositoryParameterGuard.NotNull(session, nameof(session));
        var model = Frontend.Host.CompilationModelProvider.GetSemanticModel(compilation, declaration.SyntaxTree);
        return model.GetOperation(declaration, cancellationToken)?.DescendantsAndSelf().Any(operation => operation is null ? throw new System.ArgumentNullException(nameof(operation)) : (operation switch
        {
            IInvocationOperation invocation => session.HasPotentialCallPreconditions(invocation.TargetMethod),
            IObjectCreationOperation { Constructor: { } constructor } => session.HasPotentialCallPreconditions(constructor),
            _ => false
        })) == true;
    }

    private static ImmutableArray<IrVarId> Variables(IrProgram program)
    {
        program = RepositoryParameterGuard.NotNull(program, nameof(program));
        var variables = ImmutableHashSet.CreateBuilder<IrVarId>();
        var terms = ImmutableStack<IrTerm>.Empty;
        foreach (var instruction in program.Blocks.SelectMany(static block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions)))
        {
            variables.UnionWith(IrInstructionFacts.WrittenVariables(instruction));
            foreach (var term in IrInstructionFacts.ReadTerms(instruction))
            { terms = terms.Push(term); }
        }
        var visited = ImmutableHashSet.CreateBuilder<IrId>();
        while (!terms.IsEmpty)
        {
            var term = terms.Peek();
            terms = terms.Pop();
            if (!visited.Add(term.Id))
            { continue; }
            if (term is IrVariableTerm variable)
            { variables.Add(variable.Variable); }
            terms = IrTraversal.PushChildren(term, terms);
        }
        return [.. variables.ToImmutable().OrderBy(static variable => variable.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(variable)) : (variable.Value))];
    }

    // The marker names its callee by identity. The call-site syntax can bind
    // to another symbol (an implicit collection-initializer Add sits on its
    // argument; an accessor call on a property reference), so a candidate
    // names the callee only when its identity matches the marker's.
    internal static string CalleeName(CSharpCompilation compilation, Location location, string identity, CancellationToken cancellationToken)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        location = RepositoryParameterGuard.NotNull(location, nameof(location));
        var candidates = ImmutableArray.CreateBuilder<IMethodSymbol>();
        if (location.SourceTree is { } tree)
        {
            var node = tree.GetRoot(cancellationToken).FindNode(location.SourceSpan, getInnermostNodeForTie: true);
            var model = Frontend.Host.CompilationModelProvider.GetSemanticModel(compilation, tree);
            switch (model.GetOperation(node, cancellationToken))
            {
                case IInvocationOperation invocation:
                    candidates.Add(invocation.TargetMethod);
                    break;
                case IObjectCreationOperation { Constructor: { } constructor }:
                    candidates.Add(constructor);
                    break;
            }
            if (model.GetSymbolInfo(node, cancellationToken).Symbol is IMethodSymbol bound)
            { candidates.Add(bound); }
        }
        var separator = identity.IndexOf("::", StringComparison.Ordinal);
        if (separator >= 0)
        {
            candidates.AddRange(DocumentationCommentId.GetSymbolsForDeclarationId(identity.Substring(separator + 2), compilation)
                .OfType<IMethodSymbol>());
        }
        foreach (var method in candidates.ToImmutable())
        {
            if (string.Equals(CompilerIdentityBridge.CreateSymbolDisplay(method), identity, StringComparison.Ordinal) ||
                string.Equals(CompilerIdentityBridge.CreateSymbolDisplay(method.OriginalDefinition), identity, StringComparison.Ordinal))
            { return method.Name; }
        }
        return identity;
    }

    internal static string ClauseText(Location? clause)
    {
        return AdvisoryClauseDisplay.Text(clause);
    }
}
