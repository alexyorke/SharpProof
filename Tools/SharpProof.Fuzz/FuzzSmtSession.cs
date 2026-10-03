using SharpProof.Host;
using SharpProof.Ir;
using SharpProof.Smt;
using SharpProof.Verify;

namespace SharpProof.Fuzz;

internal sealed class FuzzSmtSession : IDisposable
{
    private readonly CallableSolverSession _backend;

    private FuzzSmtSession(CallableSolverSession backend)
    {
        _backend = backend;
        Kernel = new ProofKernel(backend);
    }

    internal ProofKernel Kernel
    {
        get;
    }

    internal static FuzzSmtSession Create(IrFactory factory)
    {
        ContainerNativeLibrary.InstallZ3ResolverRequired(
            typeof(Microsoft.Z3.Context).Assembly);
        var backend = new CallableSolverSession(factory, new IrSmtBackendOptions());
        try
        {
            return new FuzzSmtSession(backend);
        }
        catch
        {
            backend.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _backend.Dispose();
    }
}
