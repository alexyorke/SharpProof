using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using SharpProof.Worker.Protocol;

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

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The MSBuild boundary reports every launch failure as a classified task result.")]
    public override bool Execute()
    {
        Process? process = null;
        try
        {
            var resolvedExecutable = ResolveDotNetHost(Executable);
            var commandLine = string.Join(
                " ",
                Arguments.Select(static argument =>
                    QuoteArgument(argument.ItemSpec)));
            process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = resolvedExecutable,
                    Arguments = commandLine,
                    WorkingDirectory = Path.GetFullPath(WorkingDirectory),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
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
                Log.LogMessage(MessageImportance.High, "{0}", error);
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

    internal static string ResolveDotNetHost(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            throw new InvalidOperationException(
                "SharpProofDotNetHost must name the direct dotnet.exe muxer.");
        }

        var disclosedHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        var trusted = !string.IsNullOrWhiteSpace(disclosedHost)
            ? ValidateDotNetInstallation(disclosedHost)
            : ValidateDotNetInstallation(ResolveDotNetFromPath());
        if (string.Equals(
                executable,
                "dotnet",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                executable,
                "dotnet.exe",
                StringComparison.OrdinalIgnoreCase))
        {
            return trusted;
        }
        if (!Path.IsPathRooted(executable) ||
            !string.Equals(
                Path.GetFileName(executable),
                "dotnet.exe",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "SharpProofDotNetHost must name the direct dotnet.exe muxer.");
        }
        var configured = ValidateDotNetInstallation(executable);
        if (!WindowsPathIdentity.AreSameExistingFile(configured, trusted))
        {
            throw new InvalidOperationException(
                "SharpProofDotNetHost must match the trusted current dotnet.exe muxer.");
        }
        return configured;
    }

    internal static string QuoteArgument(string argument)
    {
        var quoted = new StringBuilder(argument.Length + 2);
        quoted.Append('"');
        var backslashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }
            if (character == '"')
            {
                quoted.Append('\\', (backslashes * 2) + 1);
                quoted.Append(character);
                backslashes = 0;
                continue;
            }
            quoted.Append('\\', backslashes);
            quoted.Append(character);
            backslashes = 0;
        }
        quoted.Append('\\', backslashes * 2);
        quoted.Append('"');
        return quoted.ToString();
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
                     [';'],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var directory = value.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(directory) ||
                directory == "." ||
                !Path.IsPathRooted(directory))
            {
                continue;
            }
            var candidate = Path.Combine(directory, "dotnet.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        throw new InvalidOperationException(
            "SharpProofDotNetHost could not resolve a trusted dotnet.exe from PATH.");
    }

    private static string ValidateDotNetInstallation(string candidate)
    {
        if (!Path.IsPathRooted(candidate) ||
            !string.Equals(
                Path.GetFileName(candidate),
                "dotnet.exe",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "SharpProofDotNetHost must name the direct dotnet.exe muxer.");
        }
        var resolved = WindowsPathIdentity.Canonicalize(candidate);
        var directoryPath = Path.GetDirectoryName(resolved);
        if (!File.Exists(resolved) ||
            string.IsNullOrEmpty(directoryPath) ||
            !Directory.Exists(Path.Combine(directoryPath, "host", "fxr")))
        {
            throw new InvalidOperationException(
                "SharpProofDotNetHost must be a complete dotnet.exe installation.");
        }
        WindowsPathIdentity.Canonicalize(
            Path.Combine(directoryPath, "host", "fxr"));
        return resolved;
    }

}
