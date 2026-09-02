using System.Reflection;
using System.Runtime.Loader;

internal static class RuntimeAssemblyTestHost
{
    internal static void WithRuntimeAssembly(
        string contextName,
        byte[] image,
        Action<Assembly> action)
    {
        var context = new AssemblyLoadContext(
            contextName,
            isCollectible: true);
        context.Resolving += ResolveFromDefaultContext;
        try
        {
            using var stream = new MemoryStream(image, writable: false);
            action(context.LoadFromStream(stream));
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
