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
            if (TryParseWarning(
                    line,
                    out var code,
                    out var file,
                    out var lineNumber,
                    out var columnNumber,
                    out var message))
            {
                Log.LogWarning(
                    string.Empty,
                    code,
                    string.Empty,
                    file,
                    lineNumber,
                    columnNumber,
                    0,
                    0,
                    message);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(line))
            {
                Log.LogMessage(MessageImportance.High, "{0}", line);
            }
        }
    }

    private static bool TryParseWarning(
        string line,
        out string code,
        out string file,
        out int lineNumber,
        out int columnNumber,
        out string message)
    {
        const string sp0047Marker = ": warning SP0047: ";
        const string sp0048Marker = ": warning SP0048: ";
        var marker = line.IndexOf(sp0047Marker, StringComparison.Ordinal);
        var markerText = sp0047Marker;
        code = "SP0047";
        if (marker <= 0)
        {
            marker = line.IndexOf(sp0048Marker, StringComparison.Ordinal);
            markerText = sp0048Marker;
            code = "SP0048";
        }
        if (marker <= 0)
        {
            file = string.Empty;
            lineNumber = 0;
            columnNumber = 0;
            message = string.Empty;
            return false;
        }

        var location = line.Substring(0, marker);
        message = line.Substring(marker + markerText.Length);
        file = string.Equals(location, "SharpProof", StringComparison.Ordinal)
            ? string.Empty
            : location;
        lineNumber = 0;
        columnNumber = 0;
        if (!location.EndsWith(')'))
        {
            return true;
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
            return true;
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
