using SharpProof.Tools.Shared;

if (ConfigurationReferenceCommand.TryRun(args, out var configurationReferenceExitCode))
    return configurationReferenceExitCode;

return await ToolCommandHost.RunAsync(
    () => RunAsync(args),
    static exception => !SymbolicErrorClassifier.IsFatal(exception),
    exception => SymbolicCliErrorWriter.Write(exception, args));

static async Task<int> RunAsync(string[] args)
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
                    SymbolicCliOutputPolicy.JsonOptions));
            else if (options.Markdown)
                Console.WriteLine(report.ToMarkdown());
            else
                Console.WriteLine(JsonSerializer.Serialize(
                    report,
                    SymbolicCliOutputPolicy.JsonOptions));
        }
        else
        {
            await PrintExplainResultAsync(options, inputContext, smtAnalysis!);
        }

        return 0;
    }

    var queryOptions = options.CreateQueryOptions(smtAnalysis, !options.RuntimeHazards &&
                                                               !options.Complexity &&
                                                               !options.Capabilities);
    var executor = new SymbolicQueryExecutor();
    object result = options.RuntimeHazards
        ? executor.QueryRuntimeHazards(
            new SymbolicQueryContext(inputContext.SourceInput, options.CreateRuntimeHazardTarget(), queryOptions),
            options.CreateRuntimeHazardOptions())
        : options.Complexity
            ? executor.QueryComplexity(
                new SymbolicQueryContext(inputContext.SourceInput, options.CreateComplexityTarget(), queryOptions))
            : options.Capabilities
                ? executor.QueryCapabilities(
                    new SymbolicQueryContext(inputContext.SourceInput, options.CreateCapabilityTarget(), queryOptions))
                : executor.Query(
                    new SymbolicQueryContext(inputContext.SourceInput, options.CreateQueryTarget(), queryOptions));

    if (options.HasRuntimeHazardFilter && result is SymbolicRuntimeHazardQueryResult runtimeHazardResult)
        result = options.FilterRuntimeHazards(runtimeHazardResult);

    if (options.Json)
    {
        Console.WriteLine(JsonSerializer.Serialize(
            result,
            result.GetType(),
            SymbolicCliOutputPolicy.JsonOptions));
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
