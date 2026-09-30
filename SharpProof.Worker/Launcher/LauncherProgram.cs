using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Runtime.InteropServices;
using System.Text.Json;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Specs;
using SharpProof.Host;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Launcher;

internal static class Program
{
    // Refuted contracts are semantic verifier failures. Keep their transport
    // identity separate from incomplete analysis and assumption diagnostics so
    // the build task can preserve the source location and claim-specific code.
    private const string RefutedContractDiagnosticCode =
        VerifierDiagnosticCodes.RefutedContract;

    internal static async Task<int> Main(string[] args)
    {
        if (TrustedChildEnvironment.FindUnsafeRuntimeVariable() is { } variable)
        {
            Console.Error.WriteLine(
                "SharpProof launcher refused unsafe runtime environment variable " +
                variable + ".");
            return 125;
        }
        return await RunMain(args).ConfigureAwait(false);
    }

    internal static async Task<int> RunMain(
        string[] args,
        Func<WorkerVerifyRequest, CancellationToken, Task<WorkerVerifyResponse>>? verify = null,
        Action<LauncherArguments>? validatePreflight = null)
    {
        if (!LauncherArguments.TryParse(args, out var arguments))
        {
            Console.Error.WriteLine(
                "Usage: SharpProof.Worker " + WorkerInvocationArguments.Command + " " +
                WorkerInvocationArguments.RequestOption + " <path> " +
                WorkerInvocationArguments.ResultOption + " <path> " +
                "--compiler-manifest <path> --verify-policy <policy> --assumption-policy <policy> " +
                "[--publish-request <path> --publish-result <path> --publish-compiler-manifest <path> " +
                "[--publish-sarif <path>]] [budget options]");
            return 2;
        }

        WorkerVerifyRequest request;
        var operationStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        CompilerManifestArtifact artifact;
        byte[] artifactBytes;
        WorkerInputSnapshot preparedInput;
        string expectedInputHash;
        WorkerVersionSummary expectedVersions;
        try
        {
            (validatePreflight ?? (static value => value.ValidatePreflight()))(arguments);
            expectedVersions = ExpectedVersions();
            request = arguments.CreateRequest(out artifact, out artifactBytes, out preparedInput);
            expectedInputHash = preparedInput.InputHash;
            var validation = WorkerProtocolJson.Validate(request);
            if (!validation.IsValid)
            {
                WriteErrors(validation.Errors, string.Empty);
                return 2;
            }
            await AtomicFile.WriteUtf8Async(arguments.RequestPath,
                WorkerProtocolJson.SerializeRequest(request)).ConfigureAwait(false);
            DeleteIfExists(arguments.ResultPath);
        }
        catch (PlatformNotSupportedException exception)
        {
            var failure = ClassifyLauncherFailure(exception);
            Console.Error.WriteLine(failure.ConsoleMessage);
            return failure.ExitCode;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                ArgumentException or FormatException or OverflowException or
                InvalidDataException or JsonException or KeyNotFoundException or
                InvalidOperationException)
        {
            Console.Error.WriteLine(
                "SharpProof launcher input is invalid: " +
                exception.GetType().Name + ": " + exception.Message);
            return 2;
        }

        using var cancellation = new CancellationTokenSource();
        using var terminate = PosixSignalRegistration.Create(
            PosixSignal.SIGTERM,
            context =>
            {
                context.Cancel = true;
                cancellation.Cancel();
            });
        try
        {
            var response = await (verify != null
                    ? verify(request, cancellation.Token)
                    : WorkerHost.VerifyAsync(request, cancellation.Token, preparedInput, operationStarted))
                .ConfigureAwait(false);
            await AtomicFile.WriteUtf8Async(
                    arguments.ResultPath,
                    WorkerProtocolJson.SerializeCanonicalResponse(response))
                .ConfigureAwait(false);
        }
        catch (AggregateException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is not OutOfMemoryException and not StackOverflowException and
                not OperationCanceledException)
        {
            var failure = ClassifyLauncherFailure(exception);
            Console.Error.WriteLine(failure.ConsoleMessage);
            await WriteLauncherFailureAsync(arguments.ResultPath, request, artifact, expectedInputHash,
                expectedVersions, failure.Status, failure.Reason,
                failure.Code, failure.Message).ConfigureAwait(false);
        }
        await PromotePreManifestProjectTimeoutAsync(
                arguments.ResultPath,
                request,
                artifact,
                expectedInputHash,
                expectedVersions)
            .ConfigureAwait(false);
        var resultExitCode = ValidateAndReport(arguments.ResultPath, request, expectedInputHash,
            artifact.Manifest, expectedVersions,
            out var validResponse, out var validatedResponse);
        if (!validResponse)
        {
            await WriteLauncherFailureAsync(arguments.ResultPath, request, artifact, expectedInputHash,
                expectedVersions, WorkerRunStatus.Failed,
                WorkerRunFailureReason.MalformedResult, "worker.malformed_result",
                "The worker result was unavailable or malformed.").ConfigureAwait(false);
            resultExitCode = ValidateAndReport(arguments.ResultPath, request, expectedInputHash,
                artifact.Manifest, expectedVersions,
                out validResponse, out validatedResponse);
        }
        if (validResponse)
        {
            try
            {
                await PublishOutputsAsync(arguments, request, artifact, artifactBytes, expectedInputHash,
                    expectedVersions, validatedResponse!).ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is IOException or InvalidDataException or
                    UnauthorizedAccessException or ArgumentException)
            {
                Console.Error.WriteLine(
                    "SharpProof worker result could not be published.");
                return 3;
            }
        }
        return resultExitCode;
    }

    private static LauncherFailure ClassifyLauncherFailure(Exception exception)
    {
        return exception switch
        {
            OverflowException => new(3, WorkerRunStatus.Failed, WorkerRunFailureReason.InvalidRequest,
                "launcher.timeout_overflow", "The combined project timeout and termination grace exceed the supported range.",
                "SharpProof launcher timeout is invalid."),
            PlatformNotSupportedException => new(125, WorkerRunStatus.Failed, WorkerRunFailureReason.ContainmentFailure,
                "containment.unsupported", exception.Message, exception.Message),
            InvalidOperationException or IOException or
                System.ComponentModel.Win32Exception => new(
                125, WorkerRunStatus.Failed, WorkerRunFailureReason.ContainmentFailure,
                "containment.unavailable", "Required worker containment could not be established.",
                "SharpProof worker containment could not be established."),
            // Anything unclassified (an IOException out of RunWorker, say) still
            // has to produce a result file rather than escape Main.
            _ => new(3, WorkerRunStatus.Failed, WorkerRunFailureReason.InfrastructureFailure,
                "launcher.infrastructure",
                "The SharpProof launcher failed before the worker produced a result.",
                "SharpProof launcher failed before the worker produced a result.")
        };
    }

    internal sealed record LauncherFailure(
        int ExitCode, WorkerRunStatus Status, WorkerRunFailureReason Reason,
        string Code, string Message, string ConsoleMessage);

    internal static string NormalizeAbsolutePath(string path)
    {
        return Path.GetFullPath(path);
    }

    internal static string ComputeExpectedInputHash(
        WorkerVerifyRequest request,
        byte[] artifactBytes)
    {
        var identity = WorkerCacheIdentity.Current;
        return CompilerArtifactInputHash.Compute(
            request, ArtifactDigest.Compute(artifactBytes), identity.ToolIdentity, identity.ToolVersion,
            identity.WorkerBinarySha256, identity.ApiSpecIdentity,
            identity.ApiSpecVersion, identity.ApiSpecContentSha256);
    }

    internal static WorkerVersionSummary ExpectedVersions()
    {
        var identity = WorkerCacheIdentity.Current;
        return new WorkerVersionSummary
        {
            WorkerVersion = identity.ToolVersion,
            ApiSpecVersion = identity.ApiSpecVersion,
            WorkerBinarySha256 = identity.WorkerBinarySha256,
            ApiSpecContentSha256 = identity.ApiSpecContentSha256
        };
    }

    internal static int ValidateAndReport(
        string resultPath, WorkerVerifyRequest request,
        string? expectedInputHash, WorkerClaimManifest? expectedManifest,
        WorkerVersionSummary? expectedVersions,
        out bool validResponse, out WorkerVerifyResponse? validatedResponse)
    {
        validResponse = false;
        validatedResponse = null;
        WorkerVerifyResponse? response;
        try
        {
            response = WorkerProtocolJson.DeserializeResponse(
                WorkerProtocolJson.ReadUtf8File(resultPath));
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or InvalidDataException or
                UnauthorizedAccessException or JsonException)
        {
            Console.Error.WriteLine(
                "SharpProof worker result is unavailable or malformed.");
            return 3;
        }
        WorkerProtocolValidationResult validation;
        if (expectedManifest == null || expectedInputHash == null)
        {
            validation = WorkerProtocolJson.Validate(response);
        }
        else
        {
            validation = WorkerProtocolJson.ValidateForRequest(
                response, WorkerProtocolJson.ComputeRequestHash(request),
                expectedInputHash, expectedManifest, request,
                expectedVersions ?? throw new InvalidOperationException(
                    "Expected runtime provenance is unavailable."));
        }
        if (!validation.IsValid)
        {
            WriteErrors(validation.Errors, "SharpProof ");
            return 3;
        }
        validResponse = true;
        ArgumentNullException.ThrowIfNull(response);
        WorkerProtocolJson.Canonicalize(response);
        validatedResponse = response;
        WriteErrors(response.Errors, "SharpProof ");
        var hasProjectTimeoutCoverage = response.CallableResults.Any(
            static result => result.Coverage == WorkerCallableCoverage.Incomplete &&
                result.Reason == WorkerCallableCoverageReason.ProjectTimeout);
        var hasMethodTimeoutCoverage = response.CallableResults.Any(
            static result => result.Coverage == WorkerCallableCoverage.Incomplete &&
                result.Reason == WorkerCallableCoverageReason.MethodTimeout);
        var projectTimedOut = hasProjectTimeoutCoverage ||
            response.CallableResults.Length == 0 &&
                response.RunStatus == WorkerRunStatus.TimedOut &&
                response.FailureReason == WorkerRunFailureReason.None;
        var timeoutAttributedToCoverage = projectTimedOut || hasMethodTimeoutCoverage;

        var refuted = false;
        for (var index = 0; index < response.ClaimResults.Length; index++)
        {
            var result = response.ClaimResults[index];
            var claim = response.Manifest.Claims[index];
            var reason = result.Reason == WorkerClaimReason.None ? string.Empty : " (" + result.Reason + ")";
            var vacuity = result.Vacuity == WorkerVacuityKind.None
                ? string.Empty
                : " [vacuous: " + result.Vacuity + "]";
            Console.WriteLine("SharpProof " + result.Outcome + " " + claim.CallableId + " " +
                LauncherPresentation.ClaimKind(claim) + " claim " + result.ClaimId + reason + vacuity);
            if (result.Outcome == WorkerClaimOutcome.Refuted)
            {
                ReportRefutedClaim(claim, result);
            }
            refuted |= result.Outcome == WorkerClaimOutcome.Refuted;
        }
        var incompleteCount = response.CallableResults.Count(
            static result => result.Coverage == WorkerCallableCoverage.Incomplete);
        var callablesById = response.Manifest.Callables.ToDictionary(
            static callable => callable.CallableId, StringComparer.Ordinal);
        foreach (var result in response.CallableResults)
        {
            if (result.Coverage != WorkerCallableCoverage.Incomplete)
            {
                continue;
            }

            var callable = callablesById[result.CallableId];
            ReportDiagnostic(
                callable.Location,
                LauncherPresentation.Level(request.VerifyPolicy, "info"),
                VerifierDiagnosticCodes.IncompleteSelectedCallable,
                result.Reason == WorkerCallableCoverageReason.ProjectTimeout
                    ? FormattableString.Invariant(
                        $"Project analysis timed out for {result.CallableId} ({result.Reason}).")
                    : FormattableString.Invariant(
                        $"Selected analysis is incomplete for {result.CallableId} ({result.Reason})."));
        }
        if (incompleteCount == 0 && projectTimedOut)
        {
            ReportDiagnostic(
                new WorkerSourceLocation(),
                LauncherPresentation.Level(request.VerifyPolicy, "info"),
                VerifierDiagnosticCodes.IncompleteSelectedCallable,
                "Project analysis timed out before selected callable coverage was published.");
        }

        var incompleteError = (incompleteCount != 0 || projectTimedOut) &&
            request.VerifyPolicy == WorkerVerifyPolicy.RequireProven;
        var assumptionError = ReportAssumptions(request.AssumptionPolicy, response);
        Console.WriteLine("SharpProof summary " + JsonSerializer.Serialize(
            new
            {
                response.RunStatus,
                response.FailureReason,
                response.Summary
            },
            WorkerProtocolJson.SharedOptions));
        if (response.RunStatus != WorkerRunStatus.Complete &&
            !timeoutAttributedToCoverage)
        {
            Console.Error.WriteLine("SharpProof worker run " + response.RunStatus +
                " (" + response.FailureReason + ").");
            return LauncherPresentation.ExitCode(
                response.RunStatus,
                response.FailureReason);
        }
        if (response.Errors.Length != 0 && !timeoutAttributedToCoverage)
        {
            return 3;
        }

        return refuted ? 5 : incompleteError || assumptionError ? 6 : 0;
    }

    private static void ReportRefutedClaim(
        WorkerClaimManifestEntry claim, WorkerClaimResult result)
    {
        var message = "Refuted " + LauncherPresentation.ClaimKind(claim) +
            " claim " + result.ClaimId + " for " + claim.CallableId + "." +
            (result.Model is { Length: > 0 }
                ? " Counterexample: " + string.Join(
                    ", ", result.Model.Select(static value =>
                        value.Variable + " = " + value.Value)) + "."
                : string.Empty);

        // Keep this diagnostic on stderr so RunVerifier consumes it through
        // the structured diagnostic channel. The shared transport validator
        // owns the accepted code set; this line remains schema-compatible when
        // the launcher is paired with an older host during package bootstrap.
        Console.Error.WriteLine(
            VerifierDiagnosticTransport.Prefix + JsonSerializer.Serialize(new
            {
                schema = 1,
                severity = "error",
                code = RefutedContractDiagnosticCode,
                file = claim.Location.Path ?? string.Empty,
                line = claim.Location.Line,
                column = claim.Location.Column,
                message
            }));
        var prefix = string.IsNullOrWhiteSpace(claim.Location.Path)
            ? "SharpProof"
            : claim.Location.Path + FormattableString.Invariant(
                $"({claim.Location.Line},{claim.Location.Column})");
        Console.Out.WriteLine(prefix + ": error " +
            RefutedContractDiagnosticCode + ": " + message);
    }

    private static bool ReportAssumptions(
        WorkerAssumptionPolicy policy, WorkerVerifyResponse response)
    {
        var callablesById = response.Manifest.Callables.ToDictionary(
            static callable => callable.CallableId, StringComparer.Ordinal);
        var reported = false;
        foreach (var result in response.CallableResults)
        {
            var assumptions = result.Assumptions
                .Where(static assumption =>
                    assumption.Kind is WorkerAssumptionKind.UserAssume or
                        WorkerAssumptionKind.TrustedBoundary)
                .ToArray();
            if (assumptions.Length == 0)
            {
                continue;
            }

            ReportDiagnostic(
                callablesById[result.CallableId].Location,
                LauncherPresentation.Level(policy, "info"),
                VerifierDiagnosticCodes.AssumptionsDeclared,
                LauncherPresentation.AssumptionsDeclaredMessage(
                    result.CallableId, assumptions));
            reported = true;
        }
        return reported && policy == WorkerAssumptionPolicy.Error;
    }

    private static void ReportDiagnostic(
        WorkerSourceLocation location, string severity, string id, string message)
    {
        var prefix = string.IsNullOrWhiteSpace(location.Path)
            ? "SharpProof"
            : location.Path + FormattableString.Invariant(
                $"({location.Line},{location.Column})");
        var diagnostic = prefix + ": " + severity + " " + id + ": " + message;
        if (severity == "info")
        {
            Console.Out.WriteLine(diagnostic);
            return;
        }

        Console.Error.WriteLine(VerifierDiagnosticTransport.Serialize(
            new VerifierDiagnostic(
                severity,
                id,
                location.Path ?? string.Empty,
                location.Line,
                location.Column,
                message)));
        Console.Out.WriteLine(diagnostic);
    }

    // Each member is written atomically; the result is written last so a
    // reader that sees the new result also sees the matching inputs.
    private static async Task PublishOutputsAsync(
        LauncherArguments arguments, WorkerVerifyRequest request,
        CompilerManifestArtifact artifact, byte[] artifactBytes, string expectedInputHash,
        WorkerVersionSummary expectedVersions,
        WorkerVerifyResponse response)
    {
        if (arguments.PublishRequestPath == null)
        {
            return;
        }

        request.CompilerManifest.Path = arguments.PublishCompilerManifestPath!;
        response.RequestHash = WorkerProtocolJson.ComputeRequestHash(request);
        if (!WorkerProtocolJson.ValidateForRequest(
                response, response.RequestHash, expectedInputHash,
                artifact.Manifest, request,
                expectedVersions).IsValid)
        {
            throw new IOException("The worker response binding is invalid.");
        }

        await AtomicFile.WriteBytesAsync(
            arguments.PublishCompilerManifestPath!, artifactBytes).ConfigureAwait(false);
        await AtomicFile.WriteUtf8Async(
            arguments.PublishRequestPath,
            WorkerProtocolJson.SerializeRequest(request)).ConfigureAwait(false);
        if (arguments.PublishSarifPath != null)
        {
            await AtomicFile.WriteUtf8Async(
                arguments.PublishSarifPath,
                SarifProjection.Serialize(
                    request,
                    response,
                    artifact.Compilation.ProjectDirectory)).ConfigureAwait(false);
        }
        await AtomicFile.WriteUtf8Async(
            arguments.PublishResultPath!,
            WorkerProtocolJson.SerializeCanonicalResponse(response)).ConfigureAwait(false);
    }

    private static Task WriteLauncherFailureAsync(
        string path, WorkerVerifyRequest request, CompilerManifestArtifact artifact,
        string expectedInputHash, WorkerVersionSummary expectedVersions,
        WorkerRunStatus status,
        WorkerRunFailureReason reason, string code, string message)
    {
        var timeout = status == WorkerRunStatus.TimedOut;
        var response = WorkerResultAssembler.CreateIncomplete(
            expectedInputHash,
            WorkerProtocolJson.ComputeRequestHash(request),
            artifact.Manifest, request.Budgets, status, reason,
            timeout ? WorkerCallableCoverageReason.ProjectTimeout : WorkerCallableCoverageReason.InfrastructureFailure,
            timeout ? WorkerClaimReason.ProjectTimeout : WorkerClaimReason.InfrastructureFailure,
            [new WorkerProtocolError { Code = code, Message = message }],
            expectedVersions);
        return AtomicFile.WriteUtf8Async(path, WorkerProtocolJson.SerializeCanonicalResponse(response));
    }

    private static async Task PromotePreManifestProjectTimeoutAsync(
        string path,
        WorkerVerifyRequest request,
        CompilerManifestArtifact artifact,
        string expectedInputHash,
        WorkerVersionSummary expectedVersions)
    {
        WorkerVerifyResponse? response;
        try
        {
            response = WorkerProtocolJson.DeserializeResponse(
                WorkerProtocolJson.ReadUtf8File(path));
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or
                InvalidDataException or UnauthorizedAccessException or
                JsonException)
        {
            return;
        }

        if (response is not
            {
                RunStatus: WorkerRunStatus.TimedOut,
                FailureReason: WorkerRunFailureReason.None,
                CallableResults.Length: 0,
                ClaimResults.Length: 0,
                Errors.Length: 1
            } ||
            response.Errors[0].Code != WorkerProtocolErrorCodes.WorkerTimeout)
        {
            return;
        }

        var emptyManifest = WorkerResultAssembler.EmptyManifest();
        var requestHash = WorkerProtocolJson.ComputeRequestHash(request);
        if (!WorkerProtocolJson.ValidateForRequest(
                response,
                requestHash,
                WorkerResultAssembler.EmptyInputHash,
                emptyManifest,
                request,
                expectedVersions)
            .IsValid)
        {
            return;
        }

        var promoted = WorkerResultAssembler.CreateIncomplete(
            expectedInputHash,
            requestHash,
            artifact.Manifest,
            request.Budgets,
            WorkerRunStatus.TimedOut,
            WorkerRunFailureReason.None,
            WorkerCallableCoverageReason.ProjectTimeout,
            WorkerClaimReason.ProjectTimeout,
            response.Errors,
            expectedVersions,
            response.Summary.ElapsedMilliseconds);
        await AtomicFile.WriteUtf8Async(
                path,
                WorkerProtocolJson.SerializeCanonicalResponse(promoted))
            .ConfigureAwait(false);
    }

    private static void DeleteIfExists(string? path)
    {
        if (path != null && File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void WriteErrors(
        IEnumerable<WorkerProtocolError> errors, string prefix)
    {
        foreach (var error in errors)
        {
            Console.Error.WriteLine(prefix + error.Code + ": " + error.Message);
        }
    }
}

internal sealed partial class LauncherArguments
{
    internal const int MaximumCompilerManifestBytes =
        CompilerManifestArtifactFile.MaximumBytes;

    private readonly IReadOnlyDictionary<string, string> _values;

    private LauncherArguments(IReadOnlyDictionary<string, string> values)
    {
        _values = values;
    }

    internal static bool TryParse(string[] args, out LauncherArguments arguments)
    {
        arguments = null!;
        if (args.Length < 3 ||
            !string.Equals(
                args[0],
                WorkerInvocationArguments.Command,
                StringComparison.Ordinal) ||
            args.Length % 2 == 0)
        {
            return false;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Length; index += 2)
        {
            var key = args[index];
            if (!key.StartsWith("--", StringComparison.Ordinal))
            {
                return false;
            }

            key = key.Substring(2);
            if (!s_allowed.Contains(key) || !values.TryAdd(key, args[index + 1]))
            {
                return false;
            }
        }
        if (s_required.Any(key => !values.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value)))
        {
            return false;
        }

        var publicationCount = s_publication.Count(key => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value));
        if (publicationCount is not (0 or 3) ||
            values.TryGetValue("publish-sarif", out var sarif) &&
            (string.IsNullOrWhiteSpace(sarif) || publicationCount != 3))
        {
            return false;
        }

        arguments = new LauncherArguments(values);
        return true;
    }

    internal WorkerVerifyRequest CreateRequest(
        out CompilerManifestArtifact artifact, out byte[] artifactBytes)
    {
        return CreateRequest(out artifact, out artifactBytes, out _);
    }

    internal WorkerVerifyRequest CreateRequest(
        out CompilerManifestArtifact artifact, out byte[] artifactBytes,
        out WorkerInputSnapshot preparedInput)
    {
        ValidateDistinctPaths(
            Boolean("cache-enabled", true) ? OptionalFullPath("cache-directory") : null);
        var compilerManifest = CreateCompilerManifestReference(
            out var validated,
            out artifactBytes);
        artifact = validated.Manifest;
        var request = ProjectRequest(compilerManifest);
        preparedInput = ArtifactValidator.Bind(request, validated, WorkerCacheIdentity.Current);
        ValidateDistinctPaths(
            Boolean("cache-enabled", true)
                ? Program.NormalizeAbsolutePath(WorkerCachePath.Resolve(
                    Optional("cache-directory"),
                    artifact.Compilation.ProjectDirectory))
                : null);
        return request;
    }

    internal void ValidateDistinctPaths(string? cacheDirectory = null)
    {
        if (Directory.Exists(ResultPath))
        {
            throw new ArgumentException(
                "The SharpProof result path must name a file.");
        }

        string?[] candidates = [
            cacheDirectory, RequestPath, ResultPath, CompilerManifestPath,
            PublishRequestPath, PublishResultPath, PublishCompilerManifestPath,
            PublishSarifPath
        ];
        var paths = candidates.OfType<string>().ToArray();
        if (paths.Distinct(StringComparer.Ordinal).Count() != paths.Length)
        {
            throw new ArgumentException(
                "SharpProof I/O paths must be distinct.");
        }
    }

    internal void ValidatePreflight()
    {
        ContainerContract.ValidateRequired();
        var graceMilliseconds = TerminationGraceMilliseconds;
        ArgumentOutOfRangeException.ThrowIfLessThan(
            graceMilliseconds, 1, "termination-grace-ms");
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            graceMilliseconds,
            WorkerLauncherDefaults.MaximumTerminationGraceMilliseconds,
            "termination-grace-ms");
    }

    private WorkerFileReference CreateCompilerManifestReference(
        out ValidatedArtifact artifact, out byte[] bytes)
    {
        var path = FullPath("compiler-manifest");
        bytes = ReadCompilerManifest(path);
        artifact = ArtifactValidator.Decode(bytes);
        return new WorkerFileReference { Path = path, Sha256 = artifact.Digest };
    }

    internal static byte[] ReadCompilerManifest(string path)
    {
        return CompilerManifestArtifactFile.ReadAllBytes(path);
    }

    private string FullPath(string key)
    {
        return Program.NormalizeAbsolutePath(Required(key));
    }

    private string? OptionalFullPath(string key)
    {
        return Optional(key) is { } value
            ? Program.NormalizeAbsolutePath(value)
            : null;
    }

    private string Required(string key)
    {
        return _values.TryGetValue(key, out var value) ? value :
        throw new ArgumentException("A required launcher argument is missing.", key);
    }

    private string? Optional(string key)
    {
        return _values.TryGetValue(key, out var value) &&
        !string.IsNullOrWhiteSpace(value) ? value : null;
    }

    private T Number<T>(string key, T fallback) where T : struct, INumberBase<T>
    {
        return _values.TryGetValue(key, out var value) ? T.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture) : fallback;
    }

    private bool Boolean(string key, bool? fallback = null)
    {
        return _values.TryGetValue(key, out var value)
        ? bool.Parse(value) : fallback ?? bool.Parse(Required(key));
    }
}
