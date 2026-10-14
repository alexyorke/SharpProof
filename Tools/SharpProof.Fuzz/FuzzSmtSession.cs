using SharpProof.Host;
using SharpProof.Ir;
using SharpProof.Smt;

namespace SharpProof.Fuzz;

internal static class FuzzSmtSession
{
    internal static CallableSolverSession Create(IrFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ContainerNativeLibrary.InstallZ3ResolverRequired(
            typeof(Microsoft.Z3.Context).Assembly);
        return new CallableSolverSession(factory, new IrSmtBackendOptions());
    }
}
