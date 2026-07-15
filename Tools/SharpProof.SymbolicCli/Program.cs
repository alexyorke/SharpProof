try
{
    var expandedArguments = await SymbolicCliJsonRequest.ExpandArgumentsAsync(args, Console.In);
    var options = SymbolicCliOptions.Parse(expandedArguments);
    if (options.ShowHelp || !options.HasSource)
    {
        Console.Error.WriteLine(SymbolicCliOptions.Usage);
        return options.ShowHelp ? 0 : 64;
    }

    using var inputContext = await SymbolicCliInputContext.CreateAsync(options);
    options.ApplyProjectConfiguration(inputContext.ProjectContext);

    using var smtAnalysis = options.RequiresSmt
        ? new SmtAnalysisService(options.CreateSmtOptions())
        : null;

    if (options.Explain)
    {
        if (options.Json || options.Sarif || options.Markdown)
        {
            var report = await SymbolicCliExplainReport.CreateAsync(options, inputContext, smtAnalysis!);
            if (options.Sarif)
                Console.WriteLine(JsonSerializer.Serialize(
                    report.ToSarif(),
                    SymbolicCliOutputPolicy.CompactJsonOptions));
            else if (options.Markdown)
                Console.WriteLine(report.ToMarkdown());
            else
                Console.WriteLine(JsonSerializer.Serialize(
                    report,
                    SymbolicCliOutputPolicy.CompactJsonOptions));
        }
        else
        {
            await PrintExplainResultAsync(options, inputContext, smtAnalysis!);
        }

        return 0;
    }

    object result;
    if (options.RuntimeHazards)
        result = new SymbolicQueryService().QueryRuntimeHazards(
            new SymbolicQueryContext(
                inputContext.SourceInput,
                options.CreateRuntimeHazardTarget(),
                options.CreateQueryOptions(smtAnalysis, false)),
            options.CreateRuntimeHazardOptions());
    else if (options.Complexity)
        result = new SymbolicQueryService().QueryComplexity(
            new SymbolicQueryContext(
                inputContext.SourceInput,
                options.CreateComplexityTarget(),
                options.CreateQueryOptions(smtAnalysis, false)));
    else if (options.Capabilities)
        result = new SymbolicQueryService().QueryCapabilities(
            new SymbolicQueryContext(
                inputContext.SourceInput,
                options.CreateCapabilityTarget(),
                options.CreateQueryOptions(smtAnalysis, false)));
    else
        result = new SymbolicQueryService().Query(new SymbolicQueryContext(
            inputContext.SourceInput,
            options.CreateQueryTarget(),
            options.CreateQueryOptions(smtAnalysis, true)));

    if (options.HasRuntimeHazardFilter && result is SymbolicRuntimeHazardQueryResult runtimeHazardResult)
        result = options.FilterRuntimeHazards(runtimeHazardResult);

    if (options.InvariantJson)
    {
        if (result is not SymbolicQueryResult queryResult)
            throw new InvalidOperationException("Unexpected invariant query result type.");

        var invariantResult = queryResult.ToInvariantQueryResult(options.CreateCompactOptions());
        Console.WriteLine(JsonSerializer.Serialize(
            invariantResult,
            SymbolicCliOutputPolicy.CompactJsonOptions));
    }
    else if (options.CompactJson)
    {
        object compactResult;
        if (result is SymbolicQueryResult queryResult)
            compactResult = queryResult.ToCompactResult(options.CreateCompactOptions());
        else
            compactResult = result switch
        {
            SymbolicCapabilityResult capabilityResult => capabilityResult.ToCompactResult(),
            SymbolicComplexityResult complexityResult => complexityResult.ToCompactResult(),
            SymbolicRuntimeHazardQueryResult hazardResult => (object)hazardResult.ToCompactResult(
                options.CreateCompactHazardOptions()),
            _ => throw new InvalidOperationException("Unexpected query result type.")
        };
        Console.WriteLine(JsonSerializer.Serialize(
            compactResult,
            SymbolicCliOutputPolicy.CompactJsonOptions));
    }
    else if (options.Json)
    {
        var jsonResult = result is SymbolicQueryResult queryResult
            ? SymbolicCliQueryResultAdapter.ToFullJsonResult(queryResult)
            : result;
        Console.WriteLine(JsonSerializer.Serialize(
            jsonResult,
            jsonResult.GetType(),
            SymbolicCliOutputPolicy.FullJsonOptions));
    }
    else if (result is SymbolicRuntimeHazardQueryResult hazardResult)
    {
        PrintRuntimeHazardResult(hazardResult);
    }
    else if (result is SymbolicComplexityResult complexityResult)
    {
        PrintComplexityResult(complexityResult);
    }
    else if (result is SymbolicCapabilityResult capabilityResult)
    {
        PrintCapabilityResult(capabilityResult);
    }
    else if (result is SymbolicQueryResult queryResult)
    {
        switch (queryResult.Scope.Kind)
        {
            case SymbolicQueryScopeKind.File:
                PrintFileResult(queryResult, options);
                break;
            case SymbolicQueryScopeKind.Line:
                PrintLineResult(queryResult, options);
                break;
            case SymbolicQueryScopeKind.Span:
                PrintSpanResult(queryResult, options);
                break;
            default:
                PrintPointResult(queryResult.ProgramPoints.Single(), options, true);
                break;
        }
    }

    var gateFailures = SymbolicCliExitGateEvaluator.Evaluate(options, result);
    foreach (var failure in gateFailures)
        Console.Error.WriteLine($"CI gate failed [{failure.Code}]: {failure.Message}");
    return gateFailures.Count == 0 ? 0 : 1;
}
catch (Exception ex) when (!SymbolicErrorClassifier.IsFatal(ex))
{
    return SymbolicCliErrorWriter.Write(ex, args);
}
