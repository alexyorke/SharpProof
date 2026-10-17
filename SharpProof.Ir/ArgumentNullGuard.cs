#if SHARPPROOF_WORKER_PROTOCOL
namespace SharpProof.Worker.Protocol
#elif SHARPPROOF_DATAFLOW_ARGUMENT_GUARD
namespace SharpProof.Dataflow
#else
namespace SharpProof
#endif
{
    internal static class ArgumentNullGuard
    {
        internal static int RequireNonnegative(int value, string parameterName)
        {
            return (int)RequireNonnegative((long)value, parameterName);
        }

        internal static int RequireIndex(
            int value,
            int length,
            string parameterName)
        {
            RequireName(parameterName);
            if ((uint)value >= (uint)length)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }

            return value;
        }

        internal static long RequireNonnegative(long value, string parameterName)
        {
            RequireName(parameterName);
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }

            return value;
        }

        internal static int RequirePositive(int value, string parameterName)
        {
            return (int)RequirePositive((long)value, parameterName);
        }

        internal static long RequirePositive(long value, string parameterName)
        {
            RequireName(parameterName);
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }

            return value;
        }

        internal static uint RequirePositive(uint value, string parameterName)
        {
            RequireName(parameterName);
            if (value == 0)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }

            return value;
        }

        internal static TEnum RequireDefined<TEnum>(
            TEnum value,
            string parameterName)
            where TEnum : struct, Enum
        {
            RequireName(parameterName);
            if (!Enum.IsDefined(typeof(TEnum), value))
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }

            return value;
        }

        internal static T NotNull<T>(
            [System.Diagnostics.CodeAnalysis.NotNull] T? value,
            string parameterName,
            [SharpProof.Internal.AllValuesValid("Optional exception text accepts every string and null.")] string? message = null)
        {
            RequireName(parameterName);
            if (value != null)
            {
                return value;
            }

            throw message == null
                ? new ArgumentNullException(parameterName)
                : new ArgumentNullException(parameterName, message);
        }

        private static string RequireName(string parameterName)
        {
            if (string.IsNullOrEmpty(parameterName))
            {
                throw new ArgumentException("Parameter name cannot be empty.", nameof(parameterName));
            }

            return parameterName;
        }
    }
}
