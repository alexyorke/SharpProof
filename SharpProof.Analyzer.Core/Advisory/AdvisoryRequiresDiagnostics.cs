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
        Action<Diagnostic> reportDiagnostic, CancellationToken cancellationToken)
    {
        if (session.Compilation is not CSharpCompilation compilation)
        { return AnalyzerSemanticOutcome.NotApplicable; }
        var reported = new HashSet<(Location Location, string Message)>();
        var report = reportDiagnostic;
        reportDiagnostic = diagnostic =>
        {
            if (reported.Add((diagnostic.Location, diagnostic.GetMessage(CultureInfo.InvariantCulture))))
            { report(diagnostic); }
        };
        var model = Frontend.Host.CompilationModelProvider.GetSemanticModel(compilation, declaration.SyntaxTree);
        foreach (var local in declaration.DescendantNodes().OfType<LocalFunctionStatementSyntax>())
        {
            if (model.GetDeclaredSymbol(local, cancellationToken) is IMethodSymbol function)
            { _ = AnalyzeBody(compilation, function, local, session, reportDiagnostic, cancellationToken); }
        }
        return AnalyzeBody(compilation, method, declaration, session, reportDiagnostic, cancellationToken);
    }

    private static AnalyzerSemanticOutcome AnalyzeBody(CSharpCompilation compilation, IMethodSymbol method, SyntaxNode declaration,
        AnalyzerSession session, Action<Diagnostic> reportDiagnostic, CancellationToken cancellationToken)
    {
        var (body, _) = AdvisoryLowering.Lower(compilation, method, declaration, cancellationToken);
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
        foreach (var snapshot in facts.Markers.Where(static snapshot => snapshot.Value is { IsSingleton: true, SingletonValue: 0 })
            .GroupBy(static snapshot => snapshot.Marker.Id).Select(static group => group.First()))
        {
            var call = calls[snapshot.Marker];
            var location = body.Locate(snapshot.Marker.Operation) ?? Location.None;
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
        var model = Frontend.Host.CompilationModelProvider.GetSemanticModel(compilation, declaration.SyntaxTree);
        return model.GetOperation(declaration, cancellationToken)?.DescendantsAndSelf().Any(operation => operation switch
        {
            IInvocationOperation invocation => session.HasPotentialCallPreconditions(invocation.TargetMethod),
            IObjectCreationOperation { Constructor: { } constructor } => session.HasPotentialCallPreconditions(constructor),
            _ => false
        }) == true;
    }

    private static ImmutableArray<IrVarId> Variables(IrProgram program)
    {
        var variables = new HashSet<IrVarId>();
        var terms = new Stack<IrTerm>();
        foreach (var instruction in program.Blocks.SelectMany(static block => block.Instructions))
        {
            variables.UnionWith(IrInstructionFacts.WrittenVariables(instruction));
            foreach (var term in IrInstructionFacts.ReadTerms(instruction))
            { terms.Push(term); }
        }
        var visited = new HashSet<IrId>();
        while (terms.Count != 0)
        {
            var term = terms.Pop();
            if (!visited.Add(term.Id))
            { continue; }
            if (term is IrVariableTerm variable)
            { variables.Add(variable.Variable); }
            IrTraversal.PushChildren(term, terms);
        }
        return [.. variables.OrderBy(static variable => variable.Value)];
    }

    // The marker names its callee by identity. The call-site syntax can bind
    // to another symbol (an implicit collection-initializer Add sits on its
    // argument; an accessor call on a property reference), so a candidate
    // names the callee only when its identity matches the marker's.
    private static string CalleeName(CSharpCompilation compilation, Location location, string identity, CancellationToken cancellationToken)
    {
        var candidates = new List<IMethodSymbol>();
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
        foreach (var method in candidates)
        {
            if (string.Equals(CompilerIdentityBridge.CreateSymbolDisplay(method), identity, StringComparison.Ordinal) ||
                string.Equals(CompilerIdentityBridge.CreateSymbolDisplay(method.OriginalDefinition), identity, StringComparison.Ordinal))
            { return method.Name; }
        }
        return identity;
    }

    // A Requires clause is named by its condition; a closed attribute by the
    // attribute and the parameter it constrains.
    private static string ClauseText(Location? clause)
    {
        if (clause?.SourceTree is not { } tree)
        { return "<unknown>"; }
        var node = tree.GetRoot().FindNode(clause.SourceSpan, getInnermostNodeForTie: true);
        var text = node switch
        {
            InvocationExpressionSyntax { ArgumentList.Arguments: { Count: > 0 } arguments } => arguments[0].Expression.ToString(),
            ArgumentSyntax argument => argument.Expression.ToString(),
            AttributeSyntax { Parent.Parent: ParameterSyntax parameter } attribute => "[" + attribute + "] " + parameter.Identifier.ValueText,
            AttributeListSyntax { Parent: ParameterSyntax parameter } list => "[" + list.Attributes[0] + "] " + parameter.Identifier.ValueText,
            _ => node.ToString()
        };
        return string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
