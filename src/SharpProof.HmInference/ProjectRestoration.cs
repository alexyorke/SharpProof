using System.Diagnostics;
using Microsoft.Build.Evaluation;

namespace SharpProof.HmInference;

internal static class ProjectRestoration
{
    internal static async Task<bool> RunAsync(string path, Dictionary<string, string> properties, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(properties);
        cancellationToken.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(path)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("restore");
        start.ArgumentList.Add(path);
        start.ArgumentList.Add("--nologo");
        start.ArgumentList.Add("--verbosity:quiet");
        foreach (var property in properties)
        { start.ArgumentList.Add("-p:" + property.Key + "=" + ProjectCollection.Escape(property.Value).Replace(",", "%2C", StringComparison.Ordinal).Replace("\"", "%22", StringComparison.Ordinal)); }
        using var process = new Process { StartInfo = start };
        if (!process.Start())
        { throw new InvalidOperationException("Cannot start project restore."); }
        var output = ReadTailAsync(process.StandardOutput, cancellationToken);
        var errors = ReadTailAsync(process.StandardError, cancellationToken);
        try
        { await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            try
            { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException)
            { /* The child may have exited between cancellation and termination. */ }
            try
            { await Task.WhenAll(output, errors).ConfigureAwait(false); }
            catch (OperationCanceledException)
            { /* Observe canceled readers before disposing the process streams. */ }
            throw;
        }
        var tails = await Task.WhenAll(output, errors).ConfigureAwait(false);
        if (process.ExitCode == 0)
        { return true; }
        foreach (var line in tails.SelectMany(lines =>
        {
            ArgumentNullException.ThrowIfNull(lines);
            return lines;
        }).TakeLast(20))
        { Program.Log.Error(line); }
        Program.Log.Error("Project restore failed; inference skipped.");
        return false;
    }

    private static async Task<string[]> ReadTailAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var lines = new Queue<string>();
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (lines.Count == 20)
            { lines.Dequeue(); }
            lines.Enqueue(line);
        }
        return lines.ToArray();
    }
}
