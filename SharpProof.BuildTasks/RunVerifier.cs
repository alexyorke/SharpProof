using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using SharpProof.Host;

namespace SharpProof.BuildTasks;

public sealed class RunVerifier : Microsoft.Build.Utilities.Task, ICancelableTask
{
    private readonly object _synchronization = new();
    private Process? _process;
    private bool _canceled;

    [Required]
    public string Executable { get; set; } = string.Empty;

    [Required]
    [SuppressMessage(
        "Performance",
        "CA1819:Properties should not return arrays",
        Justification = "MSBuild task item parameters use ITaskItem arrays.")]
    public ITaskItem[] Arguments { get; set; } = [];

    [Required]
    public string WorkingDirectory { get; set; } = string.Empty;

    [Output]
    public int ExitCode { get; set; }

    [Output]
    public bool HasStructuredError { get; set; }

    internal bool HasActiveProcess
    {
        get
        {
            lock (_synchronization)
            {
                return _process != null && !_process.HasExited;
            }
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The MSBuild boundary reports every launch failure as a classified task result.")]
    public override bool Execute()
    {
        Process? process = null;
        HasStructuredError = false;
        try
        {
            ContainerContract.ValidateRequired();
            var resolvedExecutable = ResolveDotNetHost(Executable);
            process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = resolvedExecutable,
                    WorkingDirectory = Path.GetFullPath(WorkingDirectory),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            foreach (var argument in Arguments)
            {
                process.StartInfo.ArgumentList.Add(argument.ItemSpec);
            }
            lock (_synchronization)
            {
                if (_canceled)
                {
                    ExitCode = -1;
                    return true;
                }
                if (!process.Start())
                {
                    throw new InvalidOperationException(
                        "The SharpProof verifier process could not be started.");
                }
                _process = process;
            }
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            var output = standardOutput.GetAwaiter().GetResult();
            var error = standardError.GetAwaiter().GetResult();
            if (!string.IsNullOrWhiteSpace(output))
            {
                Log.LogMessage(MessageImportance.High, "{0}", output);
            }
            if (!string.IsNullOrWhiteSpace(error))
            {
                LogStandardError(error);
            }
            ExitCode = process.ExitCode;
        }
        catch (Exception exception)
        {
            ExitCode = -1;
            Log.LogMessage(
                MessageImportance.High,
                "SharpProof verifier launch failed: {0}",
                exception.Message);
        }
        finally
        {
            lock (_synchronization)
            {
                if (ReferenceEquals(_process, process))
                {
                    _process = null;
                }
            }
            process?.Dispose();
        }
        return true;
    }

    internal void LogStandardError(string standardError)
    {
        using var reader = new StringReader(standardError);
        while (reader.ReadLine() is { } line)
        {
            VerifierDiagnostic diagnostic;
            if (VerifierDiagnosticTransport.TryDeserialize(
                    line,
                    out var structured))
            {
                diagnostic = structured;
            }
            else if (!TryParseLegacyDiagnostic(
                         line,
                         out diagnostic))
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    Log.LogMessage(MessageImportance.High, "{0}", line);
                }
                continue;
            }

            if (diagnostic.Severity == "error")
            {
                HasStructuredError = true;
                Log.LogError(
                    string.Empty,
                    diagnostic.Code,
                    string.Empty,
                    diagnostic.File,
                    diagnostic.Line,
                    diagnostic.Column,
                    0,
                    0,
                    diagnostic.Message);
            }
            else
            {
                Log.LogWarning(
                    string.Empty,
                    diagnostic.Code,
                    string.Empty,
                    diagnostic.File,
                    diagnostic.Line,
                    diagnostic.Column,
                    0,
                    0,
                    diagnostic.Message);
            }
        }
    }

    private static bool TryParseLegacyDiagnostic(
        string line,
        out VerifierDiagnostic diagnostic)
    {
        (string Severity, string Code, string Marker)[] markers =
        {
            ("warning", "SP0047", ": warning SP0047: "),
            ("warning", "SP0048", ": warning SP0048: "),
            ("error", "SP0047", ": error SP0047: "),
            ("error", "SP0048", ": error SP0048: ")
        };
        diagnostic = null!;
        var selectedIndex = -1;
        foreach (var candidate in markers)
        {
            var marker = line.LastIndexOf(
                candidate.Marker,
                StringComparison.Ordinal);
            while (marker > 0)
            {
                var location = line.Substring(0, marker);
                if (marker > selectedIndex &&
                    TryParseLocation(
                        location,
                        out var file,
                        out var lineNumber,
                        out var columnNumber))
                {
                    selectedIndex = marker;
                    diagnostic = new VerifierDiagnostic(
                        candidate.Severity,
                        candidate.Code,
                        file,
                        lineNumber,
                        columnNumber,
                        line.Substring(marker + candidate.Marker.Length));
                    break;
                }
                marker = line.LastIndexOf(
                    candidate.Marker,
                    marker - 1,
                    StringComparison.Ordinal);
            }
        }
        return selectedIndex >= 0;
    }

    private static bool TryParseLocation(
        string location,
        out string file,
        out int lineNumber,
        out int columnNumber)
    {
        file = string.Empty;
        lineNumber = 0;
        columnNumber = 0;
        if (string.Equals(location, "SharpProof", StringComparison.Ordinal))
        {
            return true;
        }

        if (!location.EndsWith(')'))
        {
            return false;
        }
        var openParenthesis = location.LastIndexOf('(');
        var comma = location.LastIndexOf(',');
        if (openParenthesis <= 0 || comma <= openParenthesis ||
            !int.TryParse(
                location.AsSpan(
                    openParenthesis + 1,
                    comma - openParenthesis - 1),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out lineNumber) ||
            !int.TryParse(
                location.AsSpan(
                    comma + 1,
                    location.Length - comma - 2),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out columnNumber))
        {
            lineNumber = 0;
            columnNumber = 0;
            return false;
        }

        file = location.Substring(0, openParenthesis);
        return true;
    }

    internal static string ResolveDotNetHost(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            throw new InvalidOperationException(
                "SharpProof verifier host must name the direct dotnet muxer.");
        }

        var disclosedHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        var trusted = !string.IsNullOrWhiteSpace(disclosedHost)
            ? ValidateDotNetInstallation(disclosedHost)
            : ValidateDotNetInstallation(ResolveDotNetFromPath());
        if (string.Equals(
                executable,
                "dotnet",
                StringComparison.Ordinal))
        {
            return trusted;
        }
        if (!Path.IsPathRooted(executable) ||
            !string.Equals(
                Path.GetFileName(executable),
                "dotnet",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "SharpProof verifier host must name the direct dotnet muxer.");
        }
        var configured = ValidateDotNetInstallation(executable);
        if (!LinuxPathIdentity.AreSameExistingFile(configured, trusted))
        {
            throw new InvalidOperationException(
                "SharpProof verifier host must match the trusted current dotnet muxer.");
        }
        return configured;
    }

    public void Cancel()
    {
        Process? process;
        lock (_synchronization)
        {
            _canceled = true;
            process = _process;
        }
        if (process == null)
        {
            return;
        }
        try
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
    }

    private static string ResolveDotNetFromPath()
    {
        foreach (var value in (Environment.GetEnvironmentVariable("PATH") ??
                     string.Empty).Split(
                     [Path.PathSeparator],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var directory = value.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(directory) ||
                directory == "." ||
                !Path.IsPathRooted(directory))
            {
                continue;
            }
            var candidate = Path.Combine(directory, "dotnet");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        throw new InvalidOperationException(
            "SharpProof could not resolve a trusted dotnet muxer from PATH.");
    }

    private static string ValidateDotNetInstallation(string candidate)
    {
        if (!Path.IsPathRooted(candidate) ||
            !string.Equals(
                Path.GetFileName(candidate),
                "dotnet",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "SharpProof verifier host must name the direct dotnet muxer.");
        }
        var resolved = LinuxPathIdentity.Canonicalize(candidate);
        var directoryPath = Path.GetDirectoryName(resolved);
        if (!File.Exists(resolved) ||
            string.IsNullOrEmpty(directoryPath) ||
            !Directory.Exists(Path.Combine(directoryPath, "host", "fxr")))
        {
            throw new InvalidOperationException(
                "SharpProof verifier host must be a complete dotnet installation.");
        }
        LinuxPathIdentity.Canonicalize(
            Path.Combine(directoryPath, "host", "fxr"));
        return resolved;
    }

}
