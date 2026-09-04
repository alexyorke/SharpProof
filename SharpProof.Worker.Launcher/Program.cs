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
        Func<LauncherArguments, WorkerVerifyRequest, string, string, int>? runWorker = null,
        Action<LauncherArguments>? validatePreflight = null)
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
            if (validatePreflight == null)
            {
                arguments.ValidatePreflight();
            }
            else
            {
                validatePreflight(arguments);
            }
            arguments.ValidateDistinctPaths(runtimeSnapshot);
            runtimeSnapshot = WorkerBinaryIdentity.CreateSnapshot(
                arguments.WorkerPath);
            request = arguments.CreateRequest(
                runtimeSnapshot,
                out artifact,
                out artifactBytes,
                pathsAlreadyValidated: true);
            var workerVersion = ReadWorkerVersion(runtimeSnapshot);
            expectedInputHash = ComputeExpectedInputHash(
                request,
                artifactBytes,
                runtimeSnapshot,
                workerVersion);
            expectedVersions = ComputeExpectedVersions(
                runtimeSnapshot,
                workerVersion.ProductVersion);
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
        catch (PlatformNotSupportedException exception)
        {
            runtimeSnapshot?.Dispose();
            runtimeSnapshot = null;
            var failure = ClassifyLauncherFailure(exception);
            Console.Error.WriteLine(failure.ConsoleMessage);
            return failure.ExitCode;
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
        else if (exitCode == 0)
        {
            await PromotePreManifestProjectTimeoutAsync(
                    arguments.ResultPath,
                    request,
                    artifact,
                    expectedInputHash,
                    expectedVersions,
                    arguments.TerminationGraceMilliseconds)
                .ConfigureAwait(false);
        }
        var resultExitCode = ValidateAndReport(arguments.ResultPath, request, expectedInputHash,
            artifact.Manifest, expectedVersions,
            out var validResponse, out var validatedResponse,
            arguments.TerminationGraceMilliseconds,
            responseAuthority,
            exitCode);
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
                responseAuthority,
                exitCode);
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

        if (validResponse && resultExitCode != 0)
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
        var terminationStart = TimeSpan.FromMilliseconds(
            WorkerExecutionEnvelope.MaximumElapsedMilliseconds(
            request,
            arguments.TerminationGraceMilliseconds));
        var finalLimit = TimeSpan.FromMilliseconds(checked(
            request.Budgets.ProjectWallTimeMilliseconds +
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
        if (!Path.IsPathFullyQualified(candidate) ||
            !string.Equals(Path.GetFileName(hostPath), "dotnet",
                StringComparison.Ordinal) ||
            !File.Exists(hostPath) ||
            !Directory.Exists(Path.Combine(hostRoot, "host", "fxr")) ||
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
        var workerVersion = ReadWorkerVersion(snapshot);
        return ComputeExpectedVersions(snapshot, workerVersion.ProductVersion);
    }

    private static WorkerVersionSummary ComputeExpectedVersions(
        WorkerRuntimeClosureSnapshot snapshot,
        string workerVersion)
    {
        return new WorkerVersionSummary
        {
            WorkerVersion = workerVersion,
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
        var workerVersion = ReadWorkerVersion(snapshot);
        return ComputeExpectedInputHash(
            request,
            artifactBytes,
            snapshot,
            workerVersion);
    }

    private static string ComputeExpectedInputHash(
        WorkerVerifyRequest request,
        byte[] artifactBytes,
        WorkerRuntimeClosureSnapshot snapshot,
        (string ProductName, string ProductVersion) workerVersion)
    {
        return CompilerArtifactInputHash.Compute(
            request, artifactBytes, workerVersion.ProductName,
            workerVersion.ProductVersion,
            snapshot.Sha256,
            ApiSpecTable.DefaultTableIdentity, ApiSpecTable.DefaultTableVersion,
            ApiSpecTable.Default.ContentSha256);
    }

    private static (string ProductName, string ProductVersion) ReadWorkerVersion(
        WorkerRuntimeClosureSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var version = FileVersionInfo.GetVersionInfo(snapshot.ExecutionWorkerPath);
        return (
            RequiredVersion(version.ProductName, "product name"),
            RequiredVersion(version.ProductVersion, "product version"));
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
        IWorkerResponseEvidenceAuthority? responseAuthority = null,
        int? workerExitCode = null)
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
            return 3;
        }
        if (workerExitCode is not (null or 0) &&
            response?.RunStatus == WorkerRunStatus.Complete)
        {
            Console.Error.WriteLine(
                "SharpProof worker result is inconsistent with its process exit code.");
            return 3;
        }
        validResponse = true;
        ArgumentNullException.ThrowIfNull(response);
        WorkerProtocolJson.Canonicalize(response);
        validatedResponse = response;
        WriteErrors(response.Errors, "SharpProof ");

        var refuted = false;
        var unknownClaims = 0;
        for (var index = 0; index < response.ClaimResults.Length; index++)
        {
            var result = response.ClaimResults[index];
            var claim = response.Manifest.Claims[index];
            var reason = result.Reason == WorkerClaimReason.None ? string.Empty : " (" + result.Reason + ")";
            Console.WriteLine("SharpProof " + result.Outcome + " " + claim.CallableId + " " +
                LauncherPresentation.ClaimKind(claim) + " claim " + result.ClaimId + reason);
            refuted |= result.Outcome == WorkerClaimOutcome.Refuted;
            if (result.Outcome == WorkerClaimOutcome.Unknown)
            {
                unknownClaims++;
            }
        }
        var incompleteCount = 0;
        var firstIncompleteIndex = -1;
        for (var index = 0; index < response.CallableResults.Length; index++)
        {
            var result = response.CallableResults[index];
            if (result.Coverage != WorkerCallableCoverage.Incomplete)
            {
                continue;
            }

            if (incompleteCount == 0)
            {
                firstIncompleteIndex = index;
            }
            incompleteCount++;
        }
        if (incompleteCount != 0)
        {
            ReportDiagnostic(
                response.Manifest.Callables[firstIncompleteIndex].Location,
                LauncherPresentation.Level(request.VerifyPolicy, "info"),
                VerifierDiagnosticCodes.IncompleteSelectedCallable,
                FormattableString.Invariant(
                    $"Selected analysis is incomplete: callables={incompleteCount}, unknown-claims={unknownClaims}."));
        }

        var incompleteError = incompleteCount != 0 &&
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
        if (response.RunStatus != WorkerRunStatus.Complete)
        {
            Console.Error.WriteLine("SharpProof worker run " + response.RunStatus +
                " (" + response.FailureReason + ").");
            return LauncherPresentation.ExitCode(
                response.RunStatus,
                response.FailureReason);
        }
        if (response.Errors.Length != 0)
        {
            return 3;
        }

        return refuted ? 5 : incompleteError || assumptionError ? 6 : 0;
    }
    private static bool ReportAssumptions(
        WorkerAssumptionPolicy policy, WorkerVerifyResponse response)
    {
        var assumptions = response.Summary.Assumptions;
        if (assumptions.User + assumptions.Trusted == 0)
        {
            return false;
        }

        ReportDiagnostic(response.Manifest.Callables[0].Location,
            LauncherPresentation.Level(policy, "info"),
            VerifierDiagnosticCodes.AssumptionsDeclared,
            LauncherPresentation.AssumptionsDeclaredMessage(assumptions));
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
                    SarifProjection.Serialize(
                        request,
                        response,
                        artifact.Compilation.ProjectDirectory))));
        }
        members.Add(new PublicationMember(
            arguments.PublishResultPath!,
            Encoding.UTF8.GetBytes(
                WorkerProtocolJson.SerializeResponse(response))));

        using var previous = CapturePreviousPublication(members);
        var commitStarted = false;
        try
        {
            StagePublication(members);
            commitStarted = true;
            foreach (var member in members)
            {
                PublishMember(member);
            }
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
            CleanupPublicationStaging(members);
        }
    }

    private static PreviousPublication CapturePreviousPublication(
        IReadOnlyList<PublicationMember> members)
    {
        var backups = new Dictionary<string, string>(StringComparer.Ordinal);
        var complete = true;
        foreach (var member in members)
        {
            if (File.Exists(member.Path))
            {
                // Keep rollback snapshots on disk. Reading every destination into
                // managed memory made publication allocation proportional to the
                // size of all existing outputs.
                var backup = AtomicFile.PrepareStaged(member.Path);
                try
                {
                    File.Copy(member.Path, backup);
                    backups.Add(member.Path, backup);
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                    AtomicFile.TryDeleteStaged(backup);
                    throw;
                }
                continue;
            }

            if (Directory.Exists(member.Path))
            {
                throw new IOException(
                    "SharpProof publication members must be regular files.");
            }

            complete = false;
        }

        return new PreviousPublication(complete, backups);
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
        try
        {
            foreach (var member in members)
            {
                member.Temporary = AtomicFile.PrepareStaged(member.Path);
                File.Copy(previous.BackupPaths[member.Path], member.Temporary);
                LinuxPathIdentity.SyncDirectory(Path.GetDirectoryName(member.Path)!);
            }
            foreach (var member in members)
            {
                PublishMember(member);
            }
        }
        finally
        {
            CleanupPublicationStaging(members);
        }
    }

    private static void CleanupPublicationStaging(
        IReadOnlyList<PublicationMember> members)
    {
        foreach (var member in members)
        {
            if (member.Temporary != null)
            {
                AtomicFile.TryDeleteStaged(member.Temporary);
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

    private sealed record PublicationMember(string Path, byte[] Content)
    {
        internal string? Temporary { get; set; }
    }

    private sealed record PreviousPublication(
        bool IsComplete,
        Dictionary<string, string> BackupPaths) : IDisposable
    {
        public void Dispose()
        {
            foreach (var path in BackupPaths.Values)
            {
                AtomicFile.TryDeleteStaged(path);
            }
        }
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

    private static async Task PromotePreManifestProjectTimeoutAsync(
        string path,
        WorkerVerifyRequest request,
        CompilerManifestArtifact artifact,
        string expectedInputHash,
        WorkerVersionSummary expectedVersions,
        int terminationGraceMilliseconds)
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

        var emptyManifest = WorkerResultAssembler.EmptyManifest();
        var requestHash = WorkerProtocolJson.ComputeRequestHash(request);
        if (response is not
            {
                RunStatus: WorkerRunStatus.TimedOut,
                FailureReason: WorkerRunFailureReason.None,
                CallableResults.Length: 0,
                ClaimResults.Length: 0,
                Errors.Length: 1
            } ||
            response.Errors[0].Code != "worker.timeout" ||
            !WorkerProtocolJson.ValidateForRequest(
                    response,
                    requestHash,
                    WorkerResultAssembler.EmptyInputHash,
                    emptyManifest,
                    request,
                    expectedVersions,
                    terminationGraceMilliseconds)
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
            errors: null,
            expectedVersions,
            response.Summary.ElapsedMilliseconds);
        await AtomicFile.WriteUtf8Async(
                path,
                WorkerProtocolJson.SerializeResponse(promoted))
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
        return CreateRequest(null, out artifact, out artifactBytes);
    }

    internal WorkerVerifyRequest CreateRequest(
        WorkerRuntimeClosureSnapshot? runtimeSnapshot,
        out CompilerManifestArtifact artifact,
        out byte[] artifactBytes,
        bool pathsAlreadyValidated = false)
    {
        var cacheEnabled = Boolean("cache-enabled", true);
        if (!pathsAlreadyValidated)
        {
            var configuredCacheDirectory = cacheEnabled
                ? OptionalFullPath("cache-directory")
                : null;
            ValidateDistinctPaths(runtimeSnapshot, configuredCacheDirectory);
        }
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
        if (cacheDirectory is null && Boolean("cache-enabled", true))
        {
            cacheDirectory = OptionalFullPath("cache-directory");
        }
        var workerPath = WorkerPath;
        var launcherRuntimePaths = LauncherArguments.LauncherRuntimePaths;
        if (Directory.Exists(ResultPath))
        {
            throw new ArgumentException(
                "The SharpProof result path must name a file.");
        }

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
        var runtimeDirectories = runtimeRoots
            .Concat(launcherRuntimePaths)
            .Select(static path => Path.GetDirectoryName(
                LinuxPathIdentity.Canonicalize(path))!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        string?[] writableCandidates = [
            cacheDirectory, RequestPath, ResultPath, CompilerManifestPath,
            ..publicationPaths
        ];
        var writablePaths = writableCandidates
            .OfType<string>()
            .Select(LinuxPathIdentity.Canonicalize);
        if (writablePaths.Any(path => runtimeDirectories.Any(directory =>
                LinuxPathIdentity.IsSameOrDescendant(path, directory))))
        {
            throw new ArgumentException(
                "SharpProof writable paths must be outside the worker runtime directory.");
        }
        string?[] candidates = [..runtimeRoots,
            ..launcherRuntimePaths,
            cacheDirectory, RequestPath, ResultPath, CompilerManifestPath,
            ..publicationPaths,
            ..publicationPaths.Select(
                LinuxPathIdentity.PublicationMarkerPath)];
        var paths = candidates.OfType<string>()
            .Concat(runtimeSnapshot?.ComponentPaths.Where(path =>
                !runtimeRoots.Contains(path, StringComparer.Ordinal) &&
                !launcherRuntimePaths.Contains(
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
