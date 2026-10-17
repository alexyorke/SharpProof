#if SHARPPROOF_PARAMETER_GUARD_CONSOLELOGGINGANALYZER
namespace SharpProof.Internal.ConsoleLoggingAnalyzer;
#elif SHARPPROOF_PARAMETER_GUARD_DEFENSIVECODINGANALYZER
namespace SharpProof.Internal.DefensiveCodingAnalyzer;
#elif SHARPPROOF_PARAMETER_GUARD_SHARPPROOF_ANALYZER
namespace SharpProof.Internal.SharpProofAnalyzer;
#elif SHARPPROOF_PARAMETER_GUARD_SHARPPROOF_ANALYZER_CORE
namespace SharpProof.Internal.SharpProofAnalyzerCore;
#elif SHARPPROOF_PARAMETER_GUARD_SHARPPROOF_COMPILERARTIFACT
namespace SharpProof.Internal.SharpProofCompilerArtifact;
#elif SHARPPROOF_PARAMETER_GUARD_SHARPPROOF_COMPILERCOLLECTOR
namespace SharpProof.Internal.SharpProofCompilerCollector;
#elif SHARPPROOF_PARAMETER_GUARD_SHARPPROOF_COMPILERPROBE_TESTASSET
namespace SharpProof.Internal.SharpProofCompilerProbeTestAsset;
#elif SHARPPROOF_PARAMETER_GUARD_SHARPPROOF_CONTRACTFORGENERATOR
namespace SharpProof.Internal.SharpProofContractForGenerator;
#elif SHARPPROOF_PARAMETER_GUARD_SHARPPROOF_CONTRACTS
namespace SharpProof.Internal.SharpProofContracts;
#elif SHARPPROOF_PARAMETER_GUARD_SHARPPROOF_DATAFLOW
namespace SharpProof.Internal.SharpProofDataflow;
#elif SHARPPROOF_PARAMETER_GUARD_SHARPPROOF_EFFECTS
namespace SharpProof.Internal.SharpProofEffects;
#elif SHARPPROOF_PARAMETER_GUARD_SHARPPROOF_FRONTEND
namespace SharpProof.Internal.SharpProofFrontend;
#elif SHARPPROOF_PARAMETER_GUARD_SHARPPROOF_FUZZ
namespace SharpProof.Internal.SharpProofFuzz;
#elif SHARPPROOF_PARAMETER_GUARD_SHARPPROOF_GATES
namespace SharpProof.Internal.SharpProofGates;
#elif SHARPPROOF_PARAMETER_GUARD_SHARPPROOF_HOST
namespace SharpProof.Internal.SharpProofHost;
#elif SHARPPROOF_PARAMETER_GUARD_SHARPPROOF_IR
namespace SharpProof.Internal.SharpProofIr;
#elif SHARPPROOF_PARAMETER_GUARD_SHARPPROOF_SMT
namespace SharpProof.Internal.SharpProofSmt;
#elif SHARPPROOF_PARAMETER_GUARD_SHARPPROOF_SPECS
namespace SharpProof.Internal.SharpProofSpecs;
#elif SHARPPROOF_PARAMETER_GUARD_SHARPPROOF_TESTING
namespace SharpProof.Internal.SharpProofTesting;
#elif SHARPPROOF_PARAMETER_GUARD_SHARPPROOF_VERIFY
namespace SharpProof.Internal.SharpProofVerify;
#elif SHARPPROOF_PARAMETER_GUARD_SHARPPROOF_WORKER
namespace SharpProof.Internal.SharpProofWorker;
#elif SHARPPROOF_PARAMETER_GUARD_SHARPPROOF_WORKER_PROTOCOL
namespace SharpProof.Internal.SharpProofWorkerProtocol;
#endif

// Shared argument validation keeps exception contracts consistent across
// repository assemblies. Callers retain their parameter names and conditions.
internal static class ParameterGuard
{
    internal static T NotNull<T>(T? value, string parameterName)
    {
        ValidateName(parameterName);
        if (value is null)
        {
            throw new System.ArgumentNullException(parameterName);
        }
        return value;
    }

    internal static void ThrowIfMissing(bool missing, string parameterName)
    {
        ValidateName(parameterName);
        if (missing)
        {
            throw new System.ArgumentNullException(parameterName);
        }
    }

    internal static void ThrowIfInvalid(bool invalid, string parameterName)
    {
        ValidateName(parameterName);
        if (invalid)
        {
            throw new System.ArgumentOutOfRangeException(parameterName);
        }
    }

    internal static void RequireInitialized(bool invalid, string parameterName, string? message)
    {
        ValidateName(parameterName);
        if (invalid)
        {
            throw new System.ArgumentException(message, parameterName);
        }
    }

    internal static T ValidateInitialized<T>(
        T /* SPINT001: all values valid - the caller supplies the invalid-state predicate and the validated input is preserved */ value,
        bool invalid, string parameterName, string? message)
    {
        RequireInitialized(invalid, parameterName, message);
        return value;
    }

    private static void ValidateName(string parameterName)
    {
        if (string.IsNullOrEmpty(parameterName))
        {
            throw new System.ArgumentException("Parameter name cannot be empty.", nameof(parameterName));
        }
    }
}
