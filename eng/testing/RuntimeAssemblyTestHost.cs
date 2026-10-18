using System.Reflection;
using System.Runtime.Loader;

internal static class RuntimeAssemblyTestHost
{
    internal static void WithRuntimeAssembly(
        string contextName,
        byte[] image,
        Action<Assembly> action)
    {
        System.ArgumentNullException.ThrowIfNull(image, nameof(image));
        System.ArgumentNullException.ThrowIfNull(action);
        System.ArgumentNullException.ThrowIfNull(contextName);
        using var stream = new MemoryStream(image, writable: false);
        WithRuntimeAssembly(contextName, stream, action);
    }

    internal static void WithRuntimeAssembly(
        string contextName,
        Stream image,
        Action<Assembly> action)
    {
        System.ArgumentNullException.ThrowIfNull(action);
        System.ArgumentNullException.ThrowIfNull(contextName);
        System.ArgumentNullException.ThrowIfNull(image);
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
        System.ArgumentNullException.ThrowIfNull(context);
        System.ArgumentNullException.ThrowIfNull(requestedName);
        return AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(candidate =>
                candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (AssemblyName.ReferenceMatchesDefinition(
                    candidate.GetName(),
                    requestedName)));
    }
}
