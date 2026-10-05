using System.Reflection;
using System.Runtime.Loader;

internal static class RuntimeAssemblyTestHost
{
    internal static void WithRuntimeAssembly(
        string contextName,
        byte[] image,
        Action<Assembly> action)
    {
        using var stream = new MemoryStream(image, writable: false);
        WithRuntimeAssembly(contextName, stream, action);
    }

    internal static void WithRuntimeAssembly(
        string contextName,
        Stream image,
        Action<Assembly> action)
    {
        var context = new AssemblyLoadContext(
            contextName,
            isCollectible: true);
        context.Resolving += ResolveFromDefaultContext;
        try
        {
            image.Position = 0;
            action(context.LoadFromStream(image));
        }
        finally
        {
            context.Resolving -= ResolveFromDefaultContext;
            context.Unload();
        }
    }

    private static Assembly? ResolveFromDefaultContext(
        AssemblyLoadContext context,
        AssemblyName requestedName)
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(candidate =>
                AssemblyName.ReferenceMatchesDefinition(
                    candidate.GetName(),
                    requestedName));
    }
}
