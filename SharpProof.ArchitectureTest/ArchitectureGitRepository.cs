internal static class ArchitectureGitRepository
{
    internal static async Task InitializeAsync(
        string repository,
        string email,
        string name,
        params (string Key, string Value)[] settings)
    {
        await RunCheckedAsync(
            repository,
            "git",
            "init",
            "--object-format=sha1");
        await RunCheckedAsync(repository, "git", "config", "user.email", email);
        await RunCheckedAsync(repository, "git", "config", "user.name", name);
        foreach (var (key, value) in settings)
        {
            await RunCheckedAsync(repository, "git", "config", key, value);
        }
    }

    private static async Task RunCheckedAsync(
        string repository,
        string fileName,
        params string[] arguments)
    {
        var result = await ProcessRunner.RunCapturedAsync(
            repository,
            fileName,
            arguments);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"'{fileName}' failed with exit code {result.ExitCode}: " +
                result.Error + result.Output);
        }
    }
}
