using System.Collections.Immutable;

namespace SharpProof.Symbolic;

public static class SymbolicErrorCodes
{
    public const string InvalidRequest = "SPQ1000";
    public const string InvalidTarget = "SPQ1001";
    public const string UnsupportedTarget = "SPQ1002";
    public const string SourceNotFound = "SPQ1100";
    public const string ReferenceNotFound = "SPQ1101";
    public const string ParseFailed = "SPQ1200";
    public const string ProjectLoadFailed = "SPQ1300";
    public const string NativeSolverUnavailable = "SPQ2000";
    public const string SolverFailed = "SPQ2001";
    public const string TimedOut = "SPQ2100";
    public const string Canceled = "SPQ3000";
    public const string InternalFailure = "SPQ9000";
}

public static class SymbolicErrorExitCodes
{
    public const int GateFailure = 1;
    public const int Usage = 64;
    public const int InvalidData = 65;
    public const int MissingInput = 66;
    public const int Unavailable = 69;
    public const int InternalFailure = 70;
    public const int TemporaryFailure = 75;
    public const int Canceled = 130;
}

public enum SymbolicErrorCategory
{
    Usage,
    Input,
    Unsupported,
    Parse,
    Project,
    Solver,
    Timeout,
    Cancellation,
    Internal
}

public sealed class SymbolicError
{
    public SymbolicError(
        string code,
        SymbolicErrorCategory category,
        string message,
        int recommendedExitCode,
        bool isRetryable = false,
        IEnumerable<KeyValuePair<string, string>>? details = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Error code is required.", nameof(code));

        if (!Enum.IsDefined(typeof(SymbolicErrorCategory), category))
            throw new ArgumentOutOfRangeException(nameof(category), category, "Error category is not defined.");

        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Error message is required.", nameof(message));

        if (recommendedExitCode is < 1 or > 255)
            throw new ArgumentOutOfRangeException(
                nameof(recommendedExitCode),
                recommendedExitCode,
                "Recommended exit code must be between 1 and 255.");

        Code = code.Trim();
        Category = category;
        Message = message.Trim();
        RecommendedExitCode = recommendedExitCode;
        IsRetryable = isRetryable;
        Details = NormalizeDetails(details);
    }

    public string Code { get; }

    public SymbolicErrorCategory Category { get; }

    public string Message { get; }

    public int RecommendedExitCode { get; }

    public bool IsRetryable { get; }

    public IReadOnlyDictionary<string, string> Details { get; }

    private static IReadOnlyDictionary<string, string> NormalizeDetails(
        IEnumerable<KeyValuePair<string, string>>? details)
    {
        if (details == null) return ImmutableDictionary<string, string>.Empty;

        var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (var detail in details)
        {
            if (string.IsNullOrWhiteSpace(detail.Key))
                throw new ArgumentException("Error detail keys cannot be empty.", nameof(details));

            builder[detail.Key.Trim()] = detail.Value ?? string.Empty;
        }

        return builder.ToImmutable();
    }
}

public sealed class SymbolicErrorEnvelope
{
    public SymbolicErrorEnvelope(SymbolicError error)
    {
        Error = error ?? throw new ArgumentNullException(nameof(error));
    }

    public string Kind => "error";

    public int SchemaVersion => 1;

    public SymbolicError Error { get; }
}

public sealed class SymbolicQueryException : Exception
{
    public SymbolicQueryException(SymbolicError error)
        : base(error?.Message)
    {
        Error = error ?? throw new ArgumentNullException(nameof(error));
    }

    public SymbolicQueryException(SymbolicError error, Exception innerException)
        : base(error?.Message, innerException)
    {
        Error = error ?? throw new ArgumentNullException(nameof(error));
    }

    public SymbolicError Error { get; }
}

public sealed class SymbolicOperationResult<T>
    where T : class
{
    private SymbolicOperationResult(T? value, SymbolicError? error)
    {
        Value = value;
        Error = error;
    }

    public bool IsSuccess => Error == null;

    public T? Value { get; }

    public SymbolicError? Error { get; }

    public static SymbolicOperationResult<T> Success(T value)
    {
        return new SymbolicOperationResult<T>(
            value ?? throw new ArgumentNullException(nameof(value)),
            null);
    }

    public static SymbolicOperationResult<T> Failure(SymbolicError error)
    {
        return new SymbolicOperationResult<T>(
            null,
            error ?? throw new ArgumentNullException(nameof(error)));
    }
}

public static class SymbolicErrorClassifier
{
    public static SymbolicError FromException(Exception exception)
    {
        if (exception == null) throw new ArgumentNullException(nameof(exception));

        var relevant = Unwrap(exception);
        if (relevant is SymbolicQueryException queryException) return queryException.Error;

        var exceptionDetails = CreateExceptionDetails(relevant);
        if (relevant is OperationCanceledException)
            return new SymbolicError(
                SymbolicErrorCodes.Canceled,
                SymbolicErrorCategory.Cancellation,
                "The symbolic query was canceled.",
                SymbolicErrorExitCodes.Canceled,
                details: exceptionDetails);

        if (relevant is TimeoutException || IsExceptionType(relevant, "System.Text.RegularExpressions.RegexMatchTimeoutException"))
            return new SymbolicError(
                SymbolicErrorCodes.TimedOut,
                SymbolicErrorCategory.Timeout,
                string.IsNullOrWhiteSpace(relevant.Message) ? "The symbolic query timed out." : relevant.Message,
                SymbolicErrorExitCodes.TemporaryFailure,
                true,
                exceptionDetails);

        if (IsNativeSolverLoadFailure(relevant))
            return new SymbolicError(
                SymbolicErrorCodes.NativeSolverUnavailable,
                SymbolicErrorCategory.Solver,
                "The native SMT solver could not be loaded: " + relevant.Message,
                SymbolicErrorExitCodes.Unavailable,
                false,
                exceptionDetails);

        if (IsZ3Exception(relevant))
            return new SymbolicError(
                SymbolicErrorCodes.SolverFailed,
                SymbolicErrorCategory.Solver,
                "The SMT solver failed: " + relevant.Message,
                SymbolicErrorExitCodes.TemporaryFailure,
                true,
                exceptionDetails);

        if (relevant is NotSupportedException)
            return new SymbolicError(
                SymbolicErrorCodes.UnsupportedTarget,
                SymbolicErrorCategory.Unsupported,
                relevant.Message,
                SymbolicErrorExitCodes.InvalidData,
                details: exceptionDetails);

        if (relevant is FileNotFoundException fileNotFound)
            return new SymbolicError(
                SymbolicErrorCodes.SourceNotFound,
                SymbolicErrorCategory.Input,
                relevant.Message,
                SymbolicErrorExitCodes.MissingInput,
                details: AddPath(exceptionDetails, fileNotFound.FileName));

        if (relevant is DirectoryNotFoundException)
            return new SymbolicError(
                SymbolicErrorCodes.SourceNotFound,
                SymbolicErrorCategory.Input,
                relevant.Message,
                SymbolicErrorExitCodes.MissingInput,
                details: exceptionDetails);

        if (relevant is ArgumentOutOfRangeException)
            return new SymbolicError(
                SymbolicErrorCodes.InvalidTarget,
                SymbolicErrorCategory.Input,
                relevant.Message,
                SymbolicErrorExitCodes.InvalidData,
                details: exceptionDetails);

        if (relevant is FormatException or InvalidDataException)
            return new SymbolicError(
                SymbolicErrorCodes.ParseFailed,
                SymbolicErrorCategory.Parse,
                relevant.Message,
                SymbolicErrorExitCodes.InvalidData,
                details: exceptionDetails);

        if (relevant is ArgumentException)
            return new SymbolicError(
                SymbolicErrorCodes.InvalidRequest,
                SymbolicErrorCategory.Usage,
                relevant.Message,
                SymbolicErrorExitCodes.Usage,
                details: exceptionDetails);

        return new SymbolicError(
            SymbolicErrorCodes.InternalFailure,
            SymbolicErrorCategory.Internal,
            string.IsNullOrWhiteSpace(relevant.Message)
                ? "The symbolic query failed unexpectedly."
                : relevant.Message,
            SymbolicErrorExitCodes.InternalFailure,
            details: exceptionDetails);
    }

    public static bool IsFatal(Exception exception)
    {
        if (exception == null) throw new ArgumentNullException(nameof(exception));

        return exception is OutOfMemoryException or StackOverflowException or AccessViolationException;
    }

    private static Exception Unwrap(Exception exception)
    {
        while (true)
        {
            switch (exception)
            {
                case AggregateException aggregateException when aggregateException.InnerExceptions.Count == 1:
                    exception = aggregateException.InnerExceptions[0];
                    continue;
                case TypeInitializationException typeInitializationException
                    when typeInitializationException.InnerException != null:
                    exception = typeInitializationException.InnerException;
                    continue;
                default:
                    return exception;
            }
        }
    }

    private static IReadOnlyDictionary<string, string> CreateExceptionDetails(Exception exception)
    {
        return ImmutableDictionary<string, string>.Empty
            .Add("exceptionType", exception.GetType().FullName ?? exception.GetType().Name);
    }

    private static IReadOnlyDictionary<string, string> AddPath(
        IReadOnlyDictionary<string, string> details,
        string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return details;

        return details.ToImmutableDictionary(StringComparer.Ordinal).SetItem("path", path!);
    }

    private static bool IsNativeSolverLoadFailure(Exception exception)
    {
        return exception is DllNotFoundException ||
               ((exception is BadImageFormatException or FileLoadException) &&
                (exception.Message.Contains("z3", StringComparison.OrdinalIgnoreCase) ||
                 exception.Message.Contains("solver", StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsZ3Exception(Exception exception)
    {
        return IsExceptionType(exception, "Microsoft.Z3.Z3Exception") ||
               string.Equals(exception.GetType().Name, "Z3Exception", StringComparison.Ordinal);
    }

    private static bool IsExceptionType(Exception exception, string fullName)
    {
        return string.Equals(exception.GetType().FullName, fullName, StringComparison.Ordinal);
    }
}
