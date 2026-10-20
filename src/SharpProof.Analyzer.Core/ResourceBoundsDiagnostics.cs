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
        // Compiler hosts consume diagnostic severities into the syntax-tree
        // provider; they need not retain those reserved keys in generic options.
        if (context.Compilation.Options.SyntaxTreeOptionsProvider is { } treeOptions &&
            (treeOptions.TryGetDiagnosticValue(context.Node.SyntaxTree, "SP0060", context.CancellationToken, out var treeSeverity) ||
                treeOptions.TryGetGlobalDiagnosticValue("SP0060", context.CancellationToken, out treeSeverity)))
        { enabled = treeSeverity is not (ReportDiagnostic.Suppress or ReportDiagnostic.Default); }
        else if (options.GetOptions(context.Node.SyntaxTree).TryGetValue(key, out var configured) ||
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
            properties = properties.Add("ResourceAsymptoticModel", ResourceAsymptoticGrowth.ModelId);
            properties = AddAsymptotic(properties, "LoopIterations", bounds.LoopIterations.Expression, context.CancellationToken);
            properties = AddAsymptotic(properties, "ArrayAllocations", bounds.Arrays.Expression, context.CancellationToken);
            properties = AddAsymptotic(properties, "AllocatedElements", bounds.Elements.Expression, context.CancellationToken);
            properties = properties.Add("ResourceExpressionModel", ResourceExpression.ModelVersion.ToString(CultureInfo.InvariantCulture));
            properties = properties.Add("ResourceProofBasis", result.Assumptions.IsEmpty ? "Structural" : "ConditionalOnMetadataSpecifications");
            if (!result.Assumptions.IsEmpty)
            {
                properties = properties.Add("ResourceAssumptions", string.Join("\n", result.Assumptions))
                    .Add("ResourceMetadataCatalogSha256", ResourceMetadataCostCatalog.ContentSha256);
            }
            var qualification = result.Assumptions.IsEmpty ? "" : " under recorded metadata assumptions";
            message = $"Proven upper bounds{qualification}: loop iterations <= {bounds.LoopIterations.Text}; cumulative arrays <= {bounds.Arrays.Text}; cumulative elements <= {bounds.Elements.Text}";
            message += "; asymptotic upper bounds of the unbounded formula extension: loops " +
                OrderText(properties, "LoopIterations") + "; arrays " + OrderText(properties, "ArrayAllocations") +
                "; elements " + OrderText(properties, "AllocatedElements");
        }
        else
        {
            properties = properties.Add("UnknownReason", result.UnknownReason);
            message = "Unknown resource bounds: " + result.UnknownReason;
        }
        context.ReportDiagnostic(Diagnostic.Create(Rule, declaration.Identifier.GetLocation(), properties, method.Name, message));
    }

    private static string OrderText(ImmutableDictionary<string, string?> properties, string metric)
    {
        properties = ArgumentNullGuard.NotNull(properties, nameof(properties));
        metric = ArgumentNullGuard.NotNull(metric, nameof(metric));
        if (!properties.TryGetValue(metric + "AsymptoticUpperBound", out var order))
        { return "Unknown"; }
        return properties.TryGetValue(metric + "AsymptoticInputName", out var name)
            ? order + " [n=" + properties[metric + "AsymptoticInputKind"] + "(" + name + ")]" : order!;
    }

    private static ImmutableDictionary<string, string?> AddAsymptotic(ImmutableDictionary<string, string?> properties,
        string metric, ResourceExpression upper, CancellationToken cancellation)
    {
        properties = ArgumentNullGuard.NotNull(properties, nameof(properties));
        metric = ArgumentNullGuard.NotNull(metric, nameof(metric));
        upper = ArgumentNullGuard.NotNull(upper, nameof(upper));
        var growth = ResourceAsymptoticGrowth.Analyze(upper, cancellation);
        if (growth.Order is not { } order)
        { return properties.Add(metric + "AsymptoticUnknownReason", growth.UnknownReason); }
        properties = properties.Add(metric + "AsymptoticUpperBound", "O(" + order + ")");
        if (growth.Input is { } input)
        {
            properties = properties.Add(metric + "AsymptoticInputOrdinal", input.Ordinal.ToString(CultureInfo.InvariantCulture))
                .Add(metric + "AsymptoticInputKind", input.Kind.ToString())
                .Add(metric + "AsymptoticInputName", input.DisplayName);
        }
        return properties;
    }
}
