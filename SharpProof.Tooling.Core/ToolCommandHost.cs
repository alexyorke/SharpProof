namespace SharpProof.Tools.Shared;

public static class ToolCommandHost
{
    public static int Run(
        Func<int> command,
        int argumentErrorExitCode,
        TextWriter error,
        Action<TextWriter>? writeUsage = null)
    {
        try
        {
            return command();
        }
        catch (ArgumentException exception)
        {
            error.WriteLine(exception.Message);
            writeUsage?.Invoke(error);
            return argumentErrorExitCode;
        }
    }

    public static async Task<int> RunAsync(
        Func<Task<int>> command,
        int argumentErrorExitCode,
        TextWriter error,
        Action<TextWriter>? writeUsage = null)
    {
        try
        {
            return await command().ConfigureAwait(false);
        }
        catch (ArgumentException exception)
        {
            error.WriteLine(exception.Message);
            writeUsage?.Invoke(error);
            return argumentErrorExitCode;
        }
    }

    public static async Task<int> RunAsync(
        Func<Task<int>> command,
        Func<Exception, bool> shouldHandle,
        Func<Exception, int> writeError)
    {
        try
        {
            return await command().ConfigureAwait(false);
        }
        catch (Exception exception) when (shouldHandle(exception))
        {
            return writeError(exception);
        }
    }
}
