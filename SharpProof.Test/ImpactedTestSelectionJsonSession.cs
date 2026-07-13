using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NUnit.Framework;

namespace SharpProof.Test;

internal sealed class ImpactedTestSelectionJsonSession : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly Process _process;
    private readonly StringBuilder _stderr = new();
    private bool _disposed;

    private ImpactedTestSelectionJsonSession(Process process)
    {
        _process = process;
        _process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrEmpty(args.Data))
                lock (_stderr)
                {
                    _stderr.AppendLine(args.Data);
                }
        };
        _process.BeginErrorReadLine();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        _disposed = true;
        try
        {
            if (!_process.HasExited)
            {
                _process.StandardInput.Close();
                try
                {
                    await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (TimeoutException)
                {
                    TerminateProcess();
                }
            }
        }
        finally
        {
            _process.Dispose();
            _gate.Dispose();
        }
    }

    public static ImpactedTestSelectionJsonSession Start(string repositoryRoot)
    {
        var hostPath = Path.Combine(repositoryRoot, "SharpProof.Test", "ImpactedTestSelectionJsonHost.ps1");
        var selectorPath = Path.Combine(repositoryRoot, "scripts", "Invoke-SharpProofImpactedTests.ps1");
        var startInfo = new ProcessStartInfo
        {
            FileName = TestProcessSupport.FindPowerShellExecutable(),
            WorkingDirectory = repositoryRoot,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardInputEncoding = Utf8NoBom,
            StandardOutputEncoding = Utf8NoBom,
            StandardErrorEncoding = Utf8NoBom
        };

        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        if (OperatingSystem.IsWindows())
        {
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
        }

        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(hostPath);
        startInfo.ArgumentList.Add("-SelectorPath");
        startInfo.ArgumentList.Add(selectorPath);

        var process = Process.Start(startInfo) ??
                      throw new InvalidOperationException("Failed to start impacted selector JSON session.");
        return new ImpactedTestSelectionJsonSession(process);
    }

    public async Task<JsonDocument> InvokeJsonAsync(int workers, params string[] changedFiles)
    {
        await _gate.WaitAsync();
        try
        {
            ThrowIfDisposed();
            var request = JsonSerializer.Serialize(
                new Request(workers, changedFiles),
                new JsonSerializerOptions(JsonOptions)
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
            await _process.StandardInput.WriteLineAsync(request);
            await _process.StandardInput.FlushAsync();

            string? responseLine;
            try
            {
                responseLine = await _process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30));
            }
            catch (TimeoutException)
            {
                TerminateProcess();
                throw;
            }

            if (responseLine is null)
                throw new AssertionException(string.Join(
                    Environment.NewLine,
                    "Impacted test selector session ended unexpectedly.",
                    "stderr:",
                    ReadStderr()));

            var response = JsonSerializer.Deserialize<Response>(responseLine, JsonOptions);
            if (response is null) throw new AssertionException("Impacted test selector session returned invalid JSON.");

            if (!response.Success)
                throw new AssertionException(string.Join(
                    Environment.NewLine,
                    "Impacted test selector failed.",
                    "stderr:",
                    DecodeBase64(response.ErrorBase64)));

            return JsonDocument.Parse(DecodeBase64(response.OutputBase64));
        }
        finally
        {
            _gate.Release();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_process.HasExited)
            throw new AssertionException(string.Join(
                Environment.NewLine,
                "Impacted test selector session exited unexpectedly.",
                "Exit code: " + _process.ExitCode,
                "stderr:",
                ReadStderr()));
    }

    private void TerminateProcess()
    {
        if (!_process.HasExited) _process.Kill(true);
    }

    private string ReadStderr()
    {
        lock (_stderr)
        {
            return _stderr.ToString();
        }
    }

    private static string DecodeBase64(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        return Encoding.UTF8.GetString(Convert.FromBase64String(value));
    }

    private sealed record Request(int workers, string[] changedFiles);

    private sealed record Response(bool Success, string OutputBase64, string ErrorBase64);
}
