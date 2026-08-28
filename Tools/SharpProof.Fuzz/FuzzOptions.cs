using System.Globalization;

namespace SharpProof.Fuzz;

public sealed record FuzzOptions(
    int Cases,
    int Seed,
    int MaximumParallelism,
    int? ReplayCaseIndex = null)
{
    public const int DefaultCases = 1000;
    public const int MaximumCases = 1_000_000;
    public const int DefaultSeed = 0x5A17;
    public const int DefaultMaximumParallelism = 4;

    public static FuzzOptions Parse(IReadOnlyList<string> arguments)
    {
        if (arguments == null)
        {
            throw new ArgumentNullException(nameof(arguments));
        }

        var cases = DefaultCases;
        var seed = DefaultSeed;
        var maximumParallelism = DefaultMaximumParallelism;
        var replayCaseIndex = (int?)null;
        var casesSpecified = false;

        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (argument is "--help" or "-h")
            {
                throw new FuzzUsageException("");
            }

            if (index + 1 >= arguments.Count)
            {
                throw new FuzzUsageException("Missing value for " + argument + ".");
            }

            var value = arguments[++index];
            switch (argument)
            {
                case "--cases":
                    casesSpecified = true;
                    cases = ParsePositive(value, argument);
                    if (cases > MaximumCases)
                    {
                        throw new FuzzUsageException(
                            "--cases cannot exceed the limit of " +
                            MaximumCases.ToString(CultureInfo.InvariantCulture) +
                            ".");
                    }
                    break;
                case "--seed":
                    if (!int.TryParse(
                            value,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out seed))
                    {
                        throw new FuzzUsageException("--seed requires a 32-bit integer.");
                    }

                    break;
                case "--replay-index":
                    if (replayCaseIndex.HasValue ||
                        !int.TryParse(
                            value,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out var parsedIndex) ||
                        parsedIndex < 0 ||
                        parsedIndex >= MaximumCases)
                    {
                        throw new FuzzUsageException(
                            "--replay-index requires a case index from 0 through " +
                            (MaximumCases - 1).ToString(CultureInfo.InvariantCulture) + ".");
                    }

                    replayCaseIndex = parsedIndex;
                    break;
                case "--max-parallelism":
                    maximumParallelism = ParsePositive(value, argument);
                    if (maximumParallelism > 4)
                    {
                        throw new FuzzUsageException(
                            "--max-parallelism cannot exceed the limit of 4.");
                    }

                    break;
                default:
                    throw new FuzzUsageException("Unknown option: " + argument + ".");
            }
        }

        if (replayCaseIndex.HasValue)
        {
            if (casesSpecified && cases != 1)
            {
                throw new FuzzUsageException(
                    "--replay-index requires --cases 1 when --cases is specified.");
            }

            cases = 1;
        }

        return new FuzzOptions(
            cases,
            seed,
            maximumParallelism,
            replayCaseIndex);
    }

    private static int ParsePositive(string value, string option)
    {
        if (!int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed) ||
            parsed <= 0)
        {
            throw new FuzzUsageException(option + " requires a positive integer.");
        }

        return parsed;
    }
}

public sealed class FuzzUsageException : Exception
{
    public FuzzUsageException()
    {
    }

    public FuzzUsageException(string message)
        : base(message)
    {
    }

    public FuzzUsageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
