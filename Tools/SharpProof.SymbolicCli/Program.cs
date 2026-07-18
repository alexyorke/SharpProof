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
    using var analysisSession = SharpProofAnalysisSession.Create(inputContext.SourceInput, queryOptions);
    var query = options.RuntimeHazards
        ? SharpProofQuery.RuntimeHazards(
            options.CreateRuntimeHazardTarget(),
            options.CreateRuntimeHazardOptions())
        : options.Complexity
            ? SharpProofQuery.Complexity(options.CreateComplexityTarget())
            : options.Capabilities
                ? SharpProofQuery.Capabilities(options.CreateCapabilityTarget())
                : SharpProofQuery.SourceLocation(options.CreateQueryTarget());
    var analysisResult = analysisSession.Analyze(query);
    if (!analysisResult.IsSuccess)
        throw new SymbolicQueryException(analysisResult.Error ?? new SymbolicError(
            SymbolicErrorCodes.InternalFailure,
            SymbolicErrorCategory.Internal,
            "The analysis session failed without error details.",
            SymbolicErrorExitCodes.InternalFailure));

    object result = analysisResult.Payload switch
    {
        SourceQueryPayload source => source.Value,
        RuntimeHazardQueryPayload hazards => hazards.Value,
        CapabilityQueryPayload capability => capability.Value,
        ComplexityQueryPayload complexity => complexity.Value,
        ConditionQueryPayload condition => condition.Value,
        _ => throw new InvalidOperationException("The analysis session returned no typed payload.")
    };

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
catch (Exception ex) when (!SymbolicErrorClassifier.IsFatal(ex))
{
    return SymbolicCliErrorWriter.Write(ex, args);
}
