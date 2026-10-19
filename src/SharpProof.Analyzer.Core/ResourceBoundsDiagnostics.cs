namespace SharpProof.Analyzer;

internal static class ResourceBoundsDiagnostics
{
    internal static DiagnosticDescriptor Rule => GeneratedDiagnosticDescriptors.ResourceBoundsRule;

    internal static void Register(CompilationStartAnalysisContext context)
    {
        context = RepositoryParameterGuard.ValidateInitialized(context, context.Compilation is null, nameof(context), "Value must be initialized.");
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.MethodDeclaration);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        context = RepositoryParameterGuard.ValidateInitialized(context, context.Node is null, nameof(context), "Value must be initialized.");
        var options = context.Options.AnalyzerConfigOptionsProvider;
        var key = "dotnet_diagnostic.SP0060.severity";
        var enabled = context.Compilation.Options.SpecificDiagnosticOptions.TryGetValue("SP0060", out var severity) &&
            severity is not (ReportDiagnostic.Suppress or ReportDiagnostic.Default);
        if (options.GetOptions(context.Node.SyntaxTree).TryGetValue(key, out var configured) ||
            options.GlobalOptions.TryGetValue(key, out configured))
        {
            enabled = configured.Equals("suggestion", StringComparison.OrdinalIgnoreCase) ||
                configured.Equals("warning", StringComparison.OrdinalIgnoreCase) ||
                configured.Equals("error", StringComparison.OrdinalIgnoreCase) ||
                configured.Equals("silent", StringComparison.OrdinalIgnoreCase);
        }
        if (!enabled)
        { return; }
        var declaration = (MethodDeclarationSyntax)context.Node;
        if (context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is not IMethodSymbol method)
        { return; }
        if (AnalyzerGeneratedCodePolicy.IsGenerated(method, declaration.SyntaxTree, context.Compilation, context.CancellationToken))
        { return; }
        var body = declaration.Body is { } block
            ? context.SemanticModel.GetOperation(block, context.CancellationToken)
            : declaration.ExpressionBody is { } arrow
                ? context.SemanticModel.GetOperation(arrow.Expression, context.CancellationToken) : null;
        var result = ResourceBoundsAnalysis.Analyze(method, body, context.CancellationToken);
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add("ResourceStatus", result.Proven ? "Proven" : "Unknown")
            .Add("ResourceCostModel", "loop-entries-and-primitive-array-elements-v3");
        string message;
        if (result.Bounds is { } bounds)
        {
            properties = properties.Add("LoopIterationsUpperBound", bounds.LoopIterations.Text)
                .Add("ArrayAllocationsUpperBound", bounds.Arrays.Text)
                .Add("AllocatedElementsUpperBound", bounds.Elements.Text);
            properties = properties.Add("ResourceProofBasis", result.Assumptions.IsEmpty ? "Structural" : "ConditionalOnMetadataSpecifications");
            if (!result.Assumptions.IsEmpty)
            {
                properties = properties.Add("ResourceAssumptions", string.Join("\n", result.Assumptions))
                    .Add("ResourceMetadataCatalogSha256", ResourceMetadataCostCatalog.ContentSha256);
            }
            var qualification = result.Assumptions.IsEmpty ? "" : " under recorded metadata assumptions";
            message = $"Proven upper bounds{qualification}: loop iterations <= {bounds.LoopIterations.Text}; cumulative arrays <= {bounds.Arrays.Text}; cumulative elements <= {bounds.Elements.Text}";
        }
        else
        {
            properties = properties.Add("UnknownReason", result.UnknownReason);
            message = "Unknown resource bounds: " + result.UnknownReason;
        }
        context.ReportDiagnostic(Diagnostic.Create(Rule, declaration.Identifier.GetLocation(), properties, method.Name, message));
    }
}
