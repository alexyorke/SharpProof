using Microsoft.Build.Locator;

namespace SharpProof.HmInference;

internal static class ProjectLoading
{
    internal static bool IsProjectInput(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Path.GetExtension(path).ToUpperInvariant() is ".CSPROJ" or ".SLN" or ".SLNX" or ".SLNF";
    }

    internal static async Task<int> Run(string[] args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        var offset = args is ["--project" or "--solution", ..] ? 1 : 0;
        if (args.Length <= offset || !IsProjectInput(args[offset]))
        { Program.Log.Error("Expected a C# project or solution path."); return 2; }
        var path = Path.GetFullPath(args[offset]);
        if (!File.Exists(path))
        { Program.Log.Error("File not found: " + path); return 2; }
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var restore = true;
        var buildTools = true;
        for (var index = offset + 1; index < args.Length; index++)
        {
            if (index < 0)
            { throw new InvalidOperationException("Invalid option index."); }
            if (args[index] == "--no-build")
            {
                if (!buildTools)
                { Program.Log.Error("Duplicate project option."); return 2; }
                buildTools = false;
                continue;
            }
            if (args[index] == "--no-restore")
            {
                if (!restore)
                { Program.Log.Error("Duplicate project option."); return 2; }
                restore = false;
                continue;
            }
            if (index + 1 >= args.Length)
            { Program.Log.Error("Missing value for project option."); return 2; }
            var option = args[index];
            var value = args[++index];
            var name = option switch { "--framework" => "TargetFramework", "--configuration" => "Configuration", _ => null };
            if (option == "--property")
            {
                var equals = value.IndexOf('=', StringComparison.Ordinal);
                if (equals <= 0)
                { Program.Log.Error("MSBuild properties must use Name=Value."); return 2; }
                name = value[..equals];
                value = value[(equals + 1)..];
            }
            if (name is null || string.IsNullOrWhiteSpace(name) || !properties.TryAdd(name, value))
            { Program.Log.Error("Unknown or duplicate project option."); return 2; }
        }
        foreach (var name in properties.Keys)
        {
            try
            { System.Xml.XmlConvert.VerifyNCName(name); }
            catch (System.Xml.XmlException)
            { Program.Log.Error("Invalid MSBuild property name."); return 2; }
        }
        try
        {
            if (!MSBuildLocator.IsRegistered)
            {
                var query = VisualStudioInstanceQueryOptions.Default;
                query.WorkingDirectory = Path.GetDirectoryName(path)!;
                var instance = MSBuildLocator.QueryVisualStudioInstances(query).FirstOrDefault();
                if (instance is null)
                { Program.Log.Error("No compatible MSBuild SDK found for this project."); return 1; }
                MSBuildLocator.RegisterInstance(instance);
            }
            if (restore && !await ProjectRestoration.RunAsync(path, properties, cancellationToken).ConfigureAwait(false))
            { return 1; }
            return await Load(path, properties, buildTools, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException or NotSupportedException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or AggregateException
            || exception.GetType().FullName is "Microsoft.Build.Exceptions.InvalidProjectFileException" or "Microsoft.Build.Exceptions.CircularDependencyException")
        { Program.Log.Error("Project loading failed: " + exception.Message); return 1; }
    }

    // Keep SDK assembly resolution behind MSBuildLocator registration.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static async Task<int> Load(string path, Dictionary<string, string> properties, bool buildTools, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(properties);
        var contexts = new ProjectBuildContexts(path, properties);
        var session = new ProjectLoadSession(buildTools);
        foreach (var root in contexts.Roots)
        {
            if (!await session.LoadAsync(root, cancellationToken).ConfigureAwait(false))
            { return 1; }
        }
        return await session.AnalyzeAsync(cancellationToken).ConfigureAwait(false);
    }
}
