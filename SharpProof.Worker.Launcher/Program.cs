using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Specs;
using SharpProof.Host;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Launcher;

internal static class Program
{
    private const int TerminationCleanupReserveMilliseconds = 100;

    internal static async Task<int> Main(string[] args)
    {
        return await RunMain(
            args,
            static path => WorkerBinaryIdentity.ComputeSha256(path))
            .ConfigureAwait(false);
    }

    internal static async Task<int> RunMain(
        string[] args,
        Func<string, string> computeWorkerSha256,
        Func<LauncherArguments, WorkerVerifyRequest, string, string, int>? runWorker = null)
    {
        if (!LauncherArguments.TryParse(args, out var arguments))
        {
            Console.Error.WriteLine(
                "Usage: SharpProof.Worker.Launcher verify --worker <path> --request <path> --result <path> " +
                "--compiler-manifest <path> --verify-policy <policy> --assumption-policy <policy> " +
                "[--publish-request <path> --publish-result <path> --publish-compiler-manifest <path> " +
                "[--publish-sarif <path>]] [budget options]");
            return 2;
        }

        WorkerVerifyRequest request;
        CompilerManifestArtifact artifact;
        byte[] artifactBytes;
        string expectedInputHash;
        WorkerVersionSummary expectedVersions;
        CompilerResponseEvidenceAuthority responseAuthority;
        WorkerRuntimeClosureSnapshot? runtimeSnapshot = null;
        try
        {
            arguments.ValidatePreflight();
            arguments.ValidateDistinctPaths(runtimeSnapshot);
            runtimeSnapshot = WorkerBinaryIdentity.CreateSnapshot(
                arguments.WorkerPath);
            request = arguments.CreateRequest(
                runtimeSnapshot, out artifact, out artifactBytes);
            expectedInputHash = ComputeExpectedInputHash(
                request,
                artifactBytes,
                runtimeSnapshot);
            expectedVersions = ComputeExpectedVersions(runtimeSnapshot);
            responseAuthority = new CompilerResponseEvidenceAuthority(
                CompilerManifestArtifactJson.DecodeCallables(artifact));
            var validation = WorkerProtocolJson.Validate(request);
            if (!validation.IsValid)
            {
                runtimeSnapshot.Dispose();
                runtimeSnapshot = null;
                WriteErrors(validation.Errors, string.Empty);
                return 2;
            }
            await AtomicFile.WriteUtf8Async(arguments.RequestPath,
                WorkerProtocolJson.SerializeRequest(request)).ConfigureAwait(false);
            DeleteIfExists(arguments.ResultPath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                ArgumentException or FormatException or OverflowException or
                InvalidDataException or JsonException or KeyNotFoundException or
                InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            runtimeSnapshot?.Dispose();
            runtimeSnapshot = null;
            Console.Error.WriteLine(
                "SharpProof launcher input is invalid: " +
                exception.GetType().Name + ": " + exception.Message);
            return 2;
        }

        int exitCode;
        try
        {
            using (runtimeSnapshot)
            {
                if (computeWorkerSha256(
                        runtimeSnapshot.ExecutionWorkerPath) !=
                    runtimeSnapshot.Sha256)
                {
                    throw new InvalidOperationException(
                        "The staged worker runtime closure changed before launch.");
                }

                exitCode = (runWorker ?? RunWorker)(
                    arguments,
                    request,
                    artifact.Compilation.ProjectDirectory,
                    runtimeSnapshot.ExecutionWorkerPath);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        // Matches the worker's own discipline (Worker/Program.cs): ordinary
        // failures are caught so the launcher leaves a fail-closed result, while
        // cancellation and process-fatal exceptions remain observable.
        catch (Exception exception) when (
            exception is not OutOfMemoryException and not StackOverflowException)
        {
            var failure = ClassifyLauncherFailure(exception);
            exitCode = failure.ExitCode;
            Console.Error.WriteLine(failure.ConsoleMessage);
            await WriteLauncherFailureAsync(arguments.ResultPath, request, artifact, expectedInputHash,
                expectedVersions, failure.Status, failure.Reason,
                failure.Code, failure.Message).ConfigureAwait(false);
        }
        if (exitCode == 124)
        {
            DeleteIfExists(arguments.ResultPath);
        }
        if (!File.Exists(arguments.ResultPath))
        {
            LauncherFailure launcherFailure =
                LauncherPresentation.NoResultFailure(exitCode);
            await WriteLauncherFailureAsync(arguments.ResultPath, request, artifact, expectedInputHash,
                expectedVersions, launcherFailure.Status, launcherFailure.Reason,
                launcherFailure.Code, launcherFailure.Message).ConfigureAwait(false);
        }
        var resultExitCode = ValidateAndReport(arguments.ResultPath, request, expectedInputHash,
            artifact.Manifest, expectedVersions,
            out var validResponse, out var validatedResponse,
            arguments.TerminationGraceMilliseconds,
            responseAuthority);
        if (!validResponse)
        {
            await WriteLauncherFailureAsync(arguments.ResultPath, request, artifact, expectedInputHash,
                expectedVersions, WorkerRunStatus.Failed,
                WorkerRunFailureReason.MalformedResult, "worker.malformed_result",
                "The worker result was unavailable or malformed.").ConfigureAwait(false);
            resultExitCode = ValidateAndReport(arguments.ResultPath, request, expectedInputHash,
                artifact.Manifest, expectedVersions,
                out validResponse, out validatedResponse,
                arguments.TerminationGraceMilliseconds,
                responseAuthority);
        }
        if (validResponse)
        {
            try
            {
                PublishOutputs(arguments, request, artifact, artifactBytes, expectedInputHash,
                    expectedVersions, validatedResponse!, responseAuthority);
            }
            catch (Exception exception) when (
                exception is IOException or InvalidDataException or
                    UnauthorizedAccessException or ArgumentException or
                    System.ComponentModel.Win32Exception)
            {
                Console.Error.WriteLine(
                    "SharpProof worker result could not be published.");
                return 3;
            }
        }
        if (exitCode == 0)
        {
            return resultExitCode;
        }

        if (validResponse & resultExitCode != 0)
        {
            return resultExitCode;
        }

        Console.Error.WriteLine("SharpProof worker failed closed with exit code " +
            exitCode.ToString(CultureInfo.InvariantCulture) + ".");
        return exitCode;
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

    private static int RunWorker(
        LauncherArguments arguments, WorkerVerifyRequest request,
        string projectDirectory, string workerPath)
    {
        var terminationStart = TimeSpan.FromMilliseconds(ComputeHardLimit(
            request.Budgets.ProjectWallTimeMilliseconds,
            arguments.TerminationGraceMilliseconds));
        var finalLimit = TimeSpan.FromMilliseconds(ComputeFinalLimit(
            request.Budgets.ProjectWallTimeMilliseconds,
            arguments.TerminationGraceMilliseconds));
        using var process = LinuxWorkerProcess.Start(
            ResolveDotNetHostPath(projectDirectory),
            [workerPath, "verify", "--request", arguments.RequestPath,
                "--result", arguments.ResultPath, "--start-stdin"],
            projectDirectory);
        var completion = process.WaitForExit(
            terminationStart,
            finalLimit);
        if (completion.Kind == LinuxWorkerCompletionKind.Exited)
        {
            return completion.ExitCode;
        }
        return 124;
    }

    internal static string ResolveDotNetHostPath(string projectDirectory)
    {
        return ValidateDotNetHostPath(Environment.ProcessPath ??
            throw new InvalidOperationException(
                "The dotnet host path is unavailable."), projectDirectory);
    }

    internal static string ValidateDotNetHostPath(
        string candidate, string projectDirectory)
    {
        var hostPath = NormalizeAbsolutePath(candidate);
        var hostRoot = Path.GetDirectoryName(hostPath) ?? string.Empty;
        var projectRoot = NormalizeAbsolutePath(projectDirectory);
        if (!Path.EndsInDirectorySeparator(projectRoot))
        {
            projectRoot += Path.DirectorySeparatorChar;
        }
        if (!Path.IsPathFullyQualified(candidate) |
            !string.Equals(Path.GetFileName(hostPath), "dotnet",
                StringComparison.Ordinal) |
            !File.Exists(hostPath) |
            !Directory.Exists(Path.Combine(hostRoot, "host", "fxr")) |
            hostPath.StartsWith(projectRoot, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The current process is not hosted by a trusted absolute .NET installation.");
        }

        return hostPath;
    }

    internal static string NormalizeAbsolutePath(string path)
    {
        return LinuxPathIdentity.Canonicalize(path);
    }

    internal static int ComputeHardLimit(
        int projectMilliseconds, int terminationGraceMilliseconds)
    {
        return checked(projectMilliseconds + Math.Max(1,
            terminationGraceMilliseconds - TerminationCleanupReserveMilliseconds));
    }

    internal static int ComputeFinalLimit(
        int projectMilliseconds, int terminationGraceMilliseconds)
    {
        return checked(projectMilliseconds + terminationGraceMilliseconds);
    }

    internal static string ComputeExpectedInputHash(
        string workerPath, WorkerVerifyRequest request, byte[] artifactBytes)
    {
        using var snapshot = WorkerBinaryIdentity.CreateSnapshot(workerPath);
        return ComputeExpectedInputHash(
            request,
            artifactBytes,
            snapshot);
    }

    internal static WorkerVersionSummary ComputeExpectedVersions(
        WorkerRuntimeClosureSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var version = FileVersionInfo.GetVersionInfo(snapshot.ExecutionWorkerPath);
        return new WorkerVersionSummary
        {
            WorkerVersion = RequiredVersion(
                version.ProductVersion,
                "product version"),
            ApiSpecVersion = ApiSpecTable.DefaultTableVersion,
            WorkerBinarySha256 = snapshot.Sha256,
            ApiSpecContentSha256 = ApiSpecTable.Default.ContentSha256
        };
    }

    internal static string ComputeExpectedInputHash(
        WorkerVerifyRequest request,
        byte[] artifactBytes,
        WorkerRuntimeClosureSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var version = FileVersionInfo.GetVersionInfo(snapshot.ExecutionWorkerPath);
        return CompilerArtifactInputHash.Compute(
            request, artifactBytes, RequiredVersion(version.ProductName, "product name"),
            RequiredVersion(version.ProductVersion, "product version"),
            snapshot.Sha256,
            ApiSpecTable.DefaultTableIdentity, ApiSpecTable.DefaultTableVersion,
            ApiSpecTable.Default.ContentSha256);
    }

    private static string RequiredVersion(string? value, string name)
    {
        return !string.IsNullOrWhiteSpace(value) ? value : throw new InvalidDataException("The worker " + name + " is unavailable.");
    }

    internal static int ValidateAndReport(
        string resultPath, WorkerVerifyRequest request,
        string? expectedInputHash, WorkerClaimManifest? expectedManifest,
        WorkerVersionSummary? expectedVersions,
        out bool validResponse, out WorkerVerifyResponse? validatedResponse,
        int terminationGraceMilliseconds = WorkerLauncherDefaults.TerminationGraceMilliseconds,
        IWorkerResponseEvidenceAuthority? responseAuthority = null)
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
        else if (responseAuthority is { } authority)
        {
            validation = WorkerProtocolJson.ValidateForRequest(
                response, WorkerProtocolJson.ComputeRequestHash(request),
                expectedInputHash, expectedManifest, request,
                expectedVersions ?? throw new InvalidOperationException(
                    "Expected runtime provenance is unavailable."),
                authority,
                terminationGraceMilliseconds);
        }
        else
        {
            validation = WorkerProtocolJson.ValidateForRequest(
                response, WorkerProtocolJson.ComputeRequestHash(request),
                expectedInputHash, expectedManifest, request,
                expectedVersions ?? throw new InvalidOperationException(
                    "Expected runtime provenance is unavailable."),
                terminationGraceMilliseconds);
        }
        if (!validation.IsValid)
        {
            WriteErrors(validation.Errors, "SharpProof ");
            WriteErrors(response?.Errors ?? [], "SharpProof worker ");
            return 3;
        }
        validResponse = true;
        ArgumentNullException.ThrowIfNull(response);
        WorkerProtocolJson.Canonicalize(response);
        validatedResponse = response;
        WriteErrors(response.Errors, "SharpProof ");

        var manifestClaims = response.Manifest.Claims.ToDictionary(static claim => claim.ClaimId, StringComparer.Ordinal);
        var refuted = response.ClaimResults.Any(static result => result.Outcome == WorkerClaimOutcome.Refuted);
        foreach (var result in response.ClaimResults)
        {
            var claim = manifestClaims[result.ClaimId];
            var reason = result.Reason == WorkerClaimReason.None ? string.Empty : " (" + result.Reason + ")";
            Console.WriteLine("SharpProof " + result.Outcome + " " + claim.CallableId + " " +
                LauncherPresentation.ClaimKind(claim) + " claim " + result.ClaimId + reason);
        }
        var incomplete = response.CallableResults
            .Where(static result => result.Coverage == WorkerCallableCoverage.Incomplete).ToArray();
        var unknownClaims = response.ClaimResults.Count(static result => result.Outcome == WorkerClaimOutcome.Unknown);
        if (incomplete.Length != 0)
        {
            ReportDiagnostic(
                response.Manifest.Callables.First(callable => callable.CallableId == incomplete[0].CallableId).Location,
                LauncherPresentation.Level(request.VerifyPolicy, "info"), "SP0047",
                FormattableString.Invariant(
                    $"Selected analysis is incomplete: callables={incomplete.Length}, unknown-claims={unknownClaims}."));
        }

        var incompleteError = incomplete.Length != 0 &
            request.VerifyPolicy == WorkerVerifyPolicy.RequireProven;
        var assumptionError = ReportAssumptions(request.AssumptionPolicy, response);
        Console.WriteLine("SharpProof summary " + JsonSerializer.Serialize(
            new
            {
                response.RunStatus,
                response.FailureReason,
                response.Summary
            },
            WorkerProtocolJson.Options));
        if (response.RunStatus != WorkerRunStatus.Complete)
        {
            Console.Error.WriteLine("SharpProof worker run " + response.RunStatus +
                " (" + response.FailureReason + ").");
            return LauncherPresentation.ExitCode(response.RunStatus);
        }
        if (response.Errors.Length != 0)
        {
            return 3;
        }

        return refuted ? 5 : incompleteError | assumptionError ? 6 : 0;
    }
    private static bool ReportAssumptions(
        WorkerAssumptionPolicy policy, WorkerVerifyResponse response)
    {
        var assumptions = response.Summary.Assumptions;
        if (assumptions.User + assumptions.Trusted == 0)
        {
            return false;
        }

        var total = assumptions.User + assumptions.Trusted;
        ReportDiagnostic(response.Manifest.Callables[0].Location,
            LauncherPresentation.Level(policy, "info"), "SP0048",
            FormattableString.Invariant(
                $"User assumption/trusted evidence declared: total={total}, user={assumptions.User}, trusted={assumptions.Trusted}."));
        return policy == WorkerAssumptionPolicy.Error;
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

    private static void PublishOutputs(
        LauncherArguments arguments, WorkerVerifyRequest request,
        CompilerManifestArtifact artifact, byte[] artifactBytes, string expectedInputHash,
        WorkerVersionSummary expectedVersions,
        WorkerVerifyResponse response,
        IWorkerResponseEvidenceAuthority responseAuthority)
    {
        if (arguments.PublishRequestPath == null)
        {
            return;
        }

        using var publication = LinuxPathIdentity.AcquirePublicationSet(
            new[]
            {
                arguments.PublishRequestPath,
                arguments.PublishResultPath,
                arguments.PublishCompilerManifestPath,
                arguments.PublishSarifPath
            }.OfType<string>(),
            TimeSpan.FromSeconds(30));

        request.CompilerManifest.Path = arguments.PublishCompilerManifestPath!;
        response.RequestHash = WorkerProtocolJson.ComputeRequestHash(request);
        if (!WorkerProtocolJson.ValidateForRequest(
                response, response.RequestHash, expectedInputHash,
                artifact.Manifest, request,
                expectedVersions,
                responseAuthority,
                arguments.TerminationGraceMilliseconds).IsValid)
        {
            throw new IOException("The worker response binding is invalid.");
        }

        var members = new List<PublicationMember>
        {
            new(
                arguments.PublishCompilerManifestPath!,
                artifactBytes),
            new(
                arguments.PublishRequestPath,
                Encoding.UTF8.GetBytes(
                    WorkerProtocolJson.SerializeRequest(request)))
        };
        if (arguments.PublishSarifPath != null)
        {
            members.Add(new PublicationMember(
                arguments.PublishSarifPath,
                Encoding.UTF8.GetBytes(
                    SarifProjection.Serialize(request, response))));
        }
        members.Add(new PublicationMember(
            arguments.PublishResultPath!,
            Encoding.UTF8.GetBytes(
                WorkerProtocolJson.SerializeResponse(response))));

        var previous = CapturePreviousPublication(members);
        var commitStarted = false;
        try
        {
            StagePublication(members);
            foreach (var member in members.Take(members.Count - 1))
            {
                commitStarted = true;
                PublishMember(member);
            }
            commitStarted = true;
            PublishMember(members[^1]);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException)
        {
            if (commitStarted)
            {
                TryRollbackPublication(members, previous);
            }
            throw;
        }
        finally
        {
            foreach (var member in members)
            {
                if (member.Temporary != null)
                {
                    AtomicFile.TryDeleteStaged(member.Temporary);
                }
            }
        }
    }

    private static PreviousPublication CapturePreviousPublication(
        IReadOnlyList<PublicationMember> members)
    {
        var content = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var complete = true;
        foreach (var member in members)
        {
            if (File.Exists(member.Path))
            {
                content.Add(member.Path, File.ReadAllBytes(member.Path));
                continue;
            }

            if (Directory.Exists(member.Path))
            {
                throw new IOException(
                    "SharpProof publication members must be regular files.");
            }

            complete = false;
        }

        return new PreviousPublication(complete, content);
    }

    private static void StagePublication(
        IReadOnlyList<PublicationMember> members)
    {
        foreach (var member in members)
        {
            member.Temporary = AtomicFile.PrepareStaged(member.Path);
            AtomicFile.WriteStagedBytes(member.Temporary, member.Content);
            LinuxPathIdentity.SyncDirectory(
                Path.GetDirectoryName(member.Path)!);
        }
    }

    private static void PublishMember(PublicationMember member)
    {
        var temporary = member.Temporary ??
            throw new IOException("SharpProof publication staging is incomplete.");
        AtomicFile.PublishStaged(temporary, member.Path);
        member.Temporary = null;
        LinuxPathIdentity.SyncDirectory(
            Path.GetDirectoryName(member.Path)!);
    }

    private static void TryRollbackPublication(
        IReadOnlyList<PublicationMember> members,
        PreviousPublication previous)
    {
        try
        {
            if (previous.IsComplete)
            {
                RestorePreviousPublication(members, previous);
            }
            else
            {
                InvalidatePublication(members);
            }
        }
        catch (Exception exception) when (
            exception is not OutOfMemoryException and
            not StackOverflowException and
            not OperationCanceledException)
        {
            TryInvalidatePublication(members);
        }
    }

    private static void RestorePreviousPublication(
        IReadOnlyList<PublicationMember> members,
        PreviousPublication previous)
    {
        var restoreMembers = members
            .Select(member => new PublicationMember(
                member.Path,
                previous.Content[member.Path]))
            .ToArray();
        try
        {
            StagePublication(restoreMembers);
            foreach (var member in restoreMembers)
            {
                PublishMember(member);
            }
        }
        finally
        {
            foreach (var member in restoreMembers)
            {
                if (member.Temporary != null)
                {
                    AtomicFile.TryDeleteStaged(member.Temporary);
                }
            }
        }
    }

    private static void TryInvalidatePublication(
        IReadOnlyList<PublicationMember> members)
    {
        try
        {
            InvalidatePublication(members);
        }
        catch (Exception exception) when (
            exception is not OutOfMemoryException and
            not StackOverflowException and
            not OperationCanceledException)
        {
        }
    }

    private static void InvalidatePublication(
        IReadOnlyList<PublicationMember> members)
    {
        Exception? failure = null;
        foreach (var member in members)
        {
            try
            {
                if (Directory.Exists(member.Path))
                {
                    throw new IOException(
                        "SharpProof publication members must be regular files.");
                }
                if (File.Exists(member.Path))
                {
                    File.Delete(member.Path);
                    LinuxPathIdentity.SyncDirectory(
                        Path.GetDirectoryName(member.Path)!);
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or
                    ArgumentException or System.ComponentModel.Win32Exception)
            {
                failure ??= exception;
            }
        }

        if (failure != null)
        {
            throw failure;
        }
    }

    private sealed class PublicationMember
    {
        internal PublicationMember(string path, byte[] content)
        {
            Path = path;
            Content = content;
        }

        internal string Path { get; }
        internal byte[] Content { get; }
        internal string? Temporary { get; set; }
    }

    private sealed class PreviousPublication
    {
        internal PreviousPublication(
            bool isComplete,
            Dictionary<string, byte[]> content)
        {
            IsComplete = isComplete;
            Content = content;
        }

        internal bool IsComplete { get; }
        internal Dictionary<string, byte[]> Content { get; }
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
        return AtomicFile.WriteUtf8Async(path, WorkerProtocolJson.SerializeResponse(response));
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

internal static partial class LauncherPresentation
{
    internal static string Level(WorkerVerifyPolicy policy, string advisory)
    {
        return Level((object)policy, advisory);
    }

    internal static string Level(WorkerAssumptionPolicy policy, string advisory)
    {
        return Level((object)policy, advisory);
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
        if (args.Length < 3 || !string.Equals(args[0], "verify", StringComparison.Ordinal) || args.Length % 2 == 0)
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
            if (!s_allowed.Contains(key) | !values.TryAdd(key, args[index + 1]))
            {
                return false;
            }
        }
        if (s_required.Any(key => !values.TryGetValue(key, out var value) | string.IsNullOrWhiteSpace(value)))
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
        return CreateRequest(null, out artifact, out artifactBytes);
    }

    internal WorkerVerifyRequest CreateRequest(
        WorkerRuntimeClosureSnapshot? runtimeSnapshot,
        out CompilerManifestArtifact artifact, out byte[] artifactBytes)
    {
        var cacheEnabled = Boolean("cache-enabled", true);
        ValidateDistinctPaths(
            runtimeSnapshot,
            cacheEnabled ? Optional("cache-directory") : null);
        var compilerManifest = CreateCompilerManifestReference(
            out artifact,
            out artifactBytes);
        var request = ProjectRequest(compilerManifest);
        ValidateDistinctPaths(
            runtimeSnapshot,
            cacheEnabled
                ? WorkerCachePath.Resolve(
                    Optional("cache-directory"),
                    artifact.Compilation.ProjectDirectory)
                : null);
        return request;
    }

    internal void ValidateDistinctPaths(
        WorkerRuntimeClosureSnapshot? runtimeSnapshot,
        string? cacheDirectory = null)
    {
        var workerPath = WorkerPath;
        var runtimeRoots = new[] {
            workerPath,
            Path.ChangeExtension(workerPath, ".deps.json"),
            Path.ChangeExtension(workerPath, ".runtimeconfig.json")
        };
        var publicationPaths = new[] {
            PublishRequestPath, PublishResultPath, PublishCompilerManifestPath,
            PublishSarifPath
        }.OfType<string>().ToArray();
        foreach (var publicationPath in publicationPaths)
        {
            LinuxPathIdentity.RequireLocalPath(publicationPath);
        }
        string?[] candidates = [..runtimeRoots,
            ..LauncherArguments.LauncherRuntimePaths,
            cacheDirectory, RequestPath, ResultPath, CompilerManifestPath,
            ..publicationPaths,
            ..publicationPaths.Select(
                LinuxPathIdentity.PublicationMarkerPath)];
        var paths = candidates.OfType<string>()
            .Concat(runtimeSnapshot?.ComponentPaths.Where(path =>
                !runtimeRoots.Contains(path, StringComparer.Ordinal) &&
                !LauncherArguments.LauncherRuntimePaths.Contains(
                    path, StringComparer.Ordinal)) ?? [])
            .Select(LinuxPathIdentity.Canonicalize)
            .ToArray();
        for (var index = 0; index < paths.Length; index++)
        {
            for (var otherIndex = 0; otherIndex < index; otherIndex++)
            {
                if (LinuxPathIdentity.PathsConflict(
                        paths[otherIndex],
                        paths[index]))
                {
                    throw new ArgumentException(
                        "SharpProof I/O paths must be distinct and non-nested.");
                }
            }
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
        out CompilerManifestArtifact artifact, out byte[] bytes)
    {
        var path = FullPath("compiler-manifest");
        bytes = ReadCompilerManifest(path);
        artifact = CompilerManifestArtifactJson.Deserialize(new UTF8Encoding(false, true).GetString(bytes));
        return new WorkerFileReference { Path = path, Sha256 = WorkerProtocolJson.ComputeSha256(bytes) };
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
