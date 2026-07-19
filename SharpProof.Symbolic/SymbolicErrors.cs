namespace SharpProof.Symbolic;

internal static class SymbolicErrorCodes
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

internal static class SymbolicErrorExitCodes
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

internal sealed class SymbolicErrorEnvelope(SharpProofError error)
{
    public string Kind => "error";

    public int SchemaVersion => 1;

    public SharpProofError Error { get; } = error ?? throw new ArgumentNullException(nameof(error));
}

internal sealed class SymbolicQueryException : Exception
{
    public SymbolicQueryException(SharpProofError error)
        : base(error?.Message)
    {
        Error = error ?? throw new ArgumentNullException(nameof(error));
    }

    public SymbolicQueryException(SharpProofError error, Exception innerException)
        : base(error?.Message, innerException)
    {
        Error = error ?? throw new ArgumentNullException(nameof(error));
    }

    public SharpProofError Error { get; }
}

internal sealed class SymbolicOperationResult<T>(T? value, SharpProofError? error)
    where T : class
{
    public bool IsSuccess => Error == null;

    public T? Value { get; } = value;

    public SharpProofError? Error { get; } = error;

    public static SymbolicOperationResult<T> Success(T value)
    {
        return new SymbolicOperationResult<T>(
            value ?? throw new ArgumentNullException(nameof(value)),
            null);
    }

    public static SymbolicOperationResult<T> Failure(SharpProofError error)
    {
        return new SymbolicOperationResult<T>(
            null,
            error ?? throw new ArgumentNullException(nameof(error)));
    }
}

internal static class SymbolicErrorClassifier
{
    public static SharpProofError FromException(Exception exception)
    {
        if (exception == null) throw new ArgumentNullException(nameof(exception));

        var relevant = Unwrap(exception);
        if (relevant is SymbolicQueryException queryException) return queryException.Error;

        var exceptionDetails = CreateExceptionDetails(relevant);
        if (relevant is OperationCanceledException)
            return new SharpProofError(
                SymbolicErrorCodes.Canceled,
                SharpProofErrorCategory.Cancellation,
                "The symbolic query was canceled.",
                SymbolicErrorExitCodes.Canceled,
                false,
                exceptionDetails);

        if (relevant is TimeoutException || IsExceptionType(relevant, "System.Text.RegularExpressions.RegexMatchTimeoutException"))
            return new SharpProofError(
                SymbolicErrorCodes.TimedOut,
                SharpProofErrorCategory.Timeout,
                string.IsNullOrWhiteSpace(relevant.Message) ? "The symbolic query timed out." : relevant.Message,
                SymbolicErrorExitCodes.TemporaryFailure,
                true,
                exceptionDetails);

        if (IsNativeSolverLoadFailure(relevant))
            return new SharpProofError(
                SymbolicErrorCodes.NativeSolverUnavailable,
                SharpProofErrorCategory.Solver,
                "The native SMT solver could not be loaded: " + relevant.Message,
                SymbolicErrorExitCodes.Unavailable,
                false,
                exceptionDetails);

        if (IsZ3Exception(relevant))
            return new SharpProofError(
                SymbolicErrorCodes.SolverFailed,
                SharpProofErrorCategory.Solver,
                "The SMT solver failed: " + relevant.Message,
                SymbolicErrorExitCodes.TemporaryFailure,
                true,
                exceptionDetails);

        if (relevant is NotSupportedException)
            return new SharpProofError(
                SymbolicErrorCodes.UnsupportedTarget,
                SharpProofErrorCategory.Unsupported,
                relevant.Message,
                SymbolicErrorExitCodes.InvalidData,
                false,
                exceptionDetails);

        if (relevant is FileNotFoundException fileNotFound)
            return new SharpProofError(
                SymbolicErrorCodes.SourceNotFound,
                SharpProofErrorCategory.Input,
                relevant.Message,
                SymbolicErrorExitCodes.MissingInput,
                false,
                AddPath(exceptionDetails, fileNotFound.FileName));

        if (relevant is DirectoryNotFoundException)
            return new SharpProofError(
                SymbolicErrorCodes.SourceNotFound,
                SharpProofErrorCategory.Input,
                relevant.Message,
                SymbolicErrorExitCodes.MissingInput,
                false,
                exceptionDetails);

        if (relevant is ArgumentOutOfRangeException)
            return new SharpProofError(
                SymbolicErrorCodes.InvalidTarget,
                SharpProofErrorCategory.Input,
                relevant.Message,
                SymbolicErrorExitCodes.InvalidData,
                false,
                exceptionDetails);

        if (relevant is FormatException or InvalidDataException)
            return new SharpProofError(
                SymbolicErrorCodes.ParseFailed,
                SharpProofErrorCategory.Parse,
                relevant.Message,
                SymbolicErrorExitCodes.InvalidData,
                false,
                exceptionDetails);

        if (relevant is BadImageFormatException)
            return new SharpProofError(
                SymbolicErrorCodes.ParseFailed,
                SharpProofErrorCategory.Parse,
                relevant.Message,
                SymbolicErrorExitCodes.InvalidData,
                false,
                exceptionDetails);

        if (relevant is ArgumentException)
            return new SharpProofError(
                SymbolicErrorCodes.InvalidRequest,
                SharpProofErrorCategory.Usage,
                relevant.Message,
                SymbolicErrorExitCodes.Usage,
                false,
                exceptionDetails);

        return new SharpProofError(
            SymbolicErrorCodes.InternalFailure,
            SharpProofErrorCategory.Internal,
            string.IsNullOrWhiteSpace(relevant.Message)
                ? "The symbolic query failed unexpectedly."
                : relevant.Message,
            SymbolicErrorExitCodes.InternalFailure,
            false,
            exceptionDetails);
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

    private static ImmutableDictionary<string, string> CreateExceptionDetails(Exception exception)
    {
        return ImmutableDictionary<string, string>.Empty
            .Add("exceptionType", exception.GetType().FullName ?? exception.GetType().Name);
    }

    private static ImmutableDictionary<string, string> AddPath(
        ImmutableDictionary<string, string> details,
        string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return details;

        return details.SetItem("path", path!);
    }

    private static bool IsNativeSolverLoadFailure(Exception exception)
    {
        return exception is DllNotFoundException ||
               ((exception is BadImageFormatException or FileLoadException) &&
               (exception.Message.IndexOf("z3", StringComparison.OrdinalIgnoreCase) >= 0 ||
                exception.Message.IndexOf("solver", StringComparison.OrdinalIgnoreCase) >= 0));
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
