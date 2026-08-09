using Serilog;
using SharpProof.Attributes;

namespace SharpProof.Pilots.SerilogEffects;

public static class LoggingAdapter
{
    [DoesNotThrow]
    public static void Emit(ILogger logger, int value)
    {
        logger.Information("Pilot value {Value}", value);
    }

    [ZeroAllocations]
    public static ILogger CreateLogger() =>
        new LoggerConfiguration().CreateLogger();
}
