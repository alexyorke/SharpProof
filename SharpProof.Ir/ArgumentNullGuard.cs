#if SHARPPROOF_DATAFLOW_ARGUMENT_GUARD || SHARPPROOF_SMT_ARGUMENT_GUARD
namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Parameter)]
    internal sealed class NotNullAttribute : Attribute
    {
    }
}
#endif

#if SHARPPROOF_DATAFLOW_ARGUMENT_GUARD
namespace SharpProof.Dataflow
#elif SHARPPROOF_SMT_ARGUMENT_GUARD
namespace SharpProof.Smt
#else
namespace SharpProof
#endif
{
    internal static class ArgumentNullGuard
    {
        internal static int RequireNonnegative(int value, string parameterName)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }

            return value;
        }

        internal static int RequireIndex(
            int value,
            int length,
            string parameterName)
        {
            if ((uint)value >= (uint)length)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }

            return value;
        }

        internal static long RequireNonnegative(long value, string parameterName)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }

            return value;
        }

        internal static int RequirePositive(int value, string parameterName)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }

            return value;
        }

        internal static long RequirePositive(long value, string parameterName)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }

            return value;
        }

        internal static uint RequirePositive(uint value, string parameterName)
        {
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
            if (!Enum.IsDefined(typeof(TEnum), value))
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }

            return value;
        }

        internal static T NotNull<T>(
#if SHARPPROOF_DATAFLOW_ARGUMENT_GUARD || SHARPPROOF_SMT_ARGUMENT_GUARD
            [System.Diagnostics.CodeAnalysis.NotNull] T? value,
#else
            T? value,
#endif
            string parameterName,
            string? message = null)
        {
            if (value != null)
            {
                return value;
            }

            throw message == null
                ? new ArgumentNullException(parameterName)
                : new ArgumentNullException(parameterName, message);
        }
    }
}
