using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Symbolic;

namespace SharpProof.Test;

[TestFixture]
internal sealed class SymbolicErrorModelTests
{
    [TestCaseSource(nameof(ExceptionClassifications))]
    public void SymbolicErrorClassifier_MapsStableFailureFamilies(
        Exception exception,
        string expectedCode,
        SharpProofErrorCategory expectedCategory,
        int expectedExitCode)
    {
        var error = SymbolicErrorClassifier.FromException(exception);

        Assert.That(error.Code, Is.EqualTo(expectedCode));
        Assert.That(error.Category, Is.EqualTo(expectedCategory));
        Assert.That(error.RecommendedExitCode, Is.EqualTo(expectedExitCode));
        Assert.That(error.Message, Is.Not.Empty);
        Assert.That(error.Details["exceptionType"], Is.Not.Empty);
    }

    [Test]
    public void SymbolicQueryException_PreservesExplicitProjectAndReferenceErrors()
    {
        var referenceError = new SharpProofError(
            SymbolicErrorCodes.ReferenceNotFound,
            SharpProofErrorCategory.Input,
            "Reference was not found.",
            SymbolicErrorExitCodes.MissingInput,
            false,
            ImmutableDictionary<string, string>.Empty.Add("path", "Missing.dll"));
        var projectError = new SharpProofError(
            SymbolicErrorCodes.ProjectLoadFailed,
            SharpProofErrorCategory.Project,
            "Project load failed.",
            SymbolicErrorExitCodes.InvalidData,
            false,
            ImmutableDictionary<string, string>.Empty);

        Assert.That(
            SymbolicErrorClassifier.FromException(new SymbolicQueryException(referenceError)),
            Is.SameAs(referenceError));
        Assert.That(
            SymbolicErrorClassifier.FromException(new SymbolicQueryException(projectError)),
            Is.SameAs(projectError));
    }

    [Test]
    public void SymbolicErrorEnvelope_ExposesStableSchemaAndRetryMetadata()
    {
        var error = SymbolicErrorClassifier.FromException(new TimeoutException("query timed out"));
        var envelope = new SymbolicErrorEnvelope(error);

        Assert.That(envelope.Kind, Is.EqualTo("error"));
        Assert.That(envelope.SchemaVersion, Is.EqualTo(1));
        Assert.That(envelope.Error, Is.SameAs(error));
        Assert.That(envelope.Error.IsRetryable, Is.True);
    }

    [Test]
    public void SymbolicQueryService_TryQuery_ReturnsTypedSuccessAndInvalidTargetFailure()
    {
        const string source = "class C { int M(int value) => value; }";
        var service = new SymbolicQueryExecutor();

        var success = Capture(() => service.Query(new SymbolicQueryContext(
            SymbolicSourceInput.FromText(source, "TryQuery.cs"),
            SharpProofTarget.Point(1, 1))));
        var failure = Capture(() => service.Query(new SymbolicQueryContext(
            SymbolicSourceInput.FromText(source, "TryQuery.cs"),
            SharpProofTarget.Point(99, 1))));

        Assert.That(success.IsSuccess, Is.True);
        Assert.That(success.Value, Is.Not.Null);
        Assert.That(success.Error, Is.Null);
        Assert.That(failure.IsSuccess, Is.False);
        Assert.That(failure.Value, Is.Null);
        Assert.That(failure.Error!.Code, Is.EqualTo(SymbolicErrorCodes.InvalidTarget));
        Assert.That(failure.Error.RecommendedExitCode, Is.EqualTo(SymbolicErrorExitCodes.InvalidData));
    }

    [Test]
    public void SymbolicQueryService_TryProve_ReturnsTypedInvalidRequestFailure()
    {
        var context = new SymbolicQueryContext(
            SymbolicSourceInput.FromText("class C { int M(int value) => value; }", "TryProve.cs"),
            SharpProofTarget.Point(1, 1),
            SymbolicQueryOptions.Default);

        var result = Capture(() => new SymbolicQueryExecutor().Prove(context, "value >= 0"));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(SymbolicErrorCodes.InvalidRequest));
        Assert.That(result.Error.Category, Is.EqualTo(SharpProofErrorCategory.Usage));
    }

    [Test]
    public void SymbolicQueryService_PublicQueriesShareNullContextValidation()
    {
        var service = new SymbolicQueryExecutor();

        Assert.Multiple(() =>
        {
            Assert.That(Assert.Throws<ArgumentNullException>(() => service.Query(null!))!.ParamName,
                Is.EqualTo("context"));
            Assert.That(Assert.Throws<ArgumentNullException>(() => service.Prove(null!, string.Empty))!.ParamName,
                Is.EqualTo("context"));
            Assert.That(Assert.Throws<ArgumentNullException>(() => service.QueryRuntimeHazards(null!))!.ParamName,
                Is.EqualTo("context"));
            Assert.That(Assert.Throws<ArgumentNullException>(() => service.QueryComplexity(null!))!.ParamName,
                Is.EqualTo("context"));
            Assert.That(Assert.Throws<ArgumentNullException>(() => service.QueryCapabilities(null!))!.ParamName,
                Is.EqualTo("context"));
        });
    }

    [Test]
    public void SymbolicQueryService_TryQuery_ReturnsTypedCancellationFailure()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = Capture(() => new SymbolicQueryExecutor().Query(
            new SymbolicQueryContext(
                SymbolicSourceInput.FromText("class C { }", "Canceled.cs"),
                SharpProofTarget.Point(1, 1)),
            cancellation.Token));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(SymbolicErrorCodes.Canceled));
        Assert.That(result.Error.RecommendedExitCode, Is.EqualTo(SymbolicErrorExitCodes.Canceled));
    }

    private static IEnumerable<TestCaseData> ExceptionClassifications()
    {
        yield return Case(
            new ArgumentException("invalid options"),
            SymbolicErrorCodes.InvalidRequest,
            SharpProofErrorCategory.Usage,
            SymbolicErrorExitCodes.Usage);
        yield return Case(
            new ArgumentOutOfRangeException("line", "invalid target"),
            SymbolicErrorCodes.InvalidTarget,
            SharpProofErrorCategory.Input,
            SymbolicErrorExitCodes.InvalidData);
        yield return Case(
            new NotSupportedException("unsupported target"),
            SymbolicErrorCodes.UnsupportedTarget,
            SharpProofErrorCategory.Unsupported,
            SymbolicErrorExitCodes.InvalidData);
        yield return Case(
            new FileNotFoundException("source missing", "Missing.cs"),
            SymbolicErrorCodes.SourceNotFound,
            SharpProofErrorCategory.Input,
            SymbolicErrorExitCodes.MissingInput);
        yield return Case(
            new FormatException("parse failed"),
            SymbolicErrorCodes.ParseFailed,
            SharpProofErrorCategory.Parse,
            SymbolicErrorExitCodes.InvalidData);
        yield return Case(
            new BadImageFormatException("metadata parse failed"),
            SymbolicErrorCodes.ParseFailed,
            SharpProofErrorCategory.Parse,
            SymbolicErrorExitCodes.InvalidData);
        yield return Case(
            new DllNotFoundException("libz3 was not found"),
            SymbolicErrorCodes.NativeSolverUnavailable,
            SharpProofErrorCategory.Solver,
            SymbolicErrorExitCodes.Unavailable);
        yield return Case(
            new TimeoutException("query timed out"),
            SymbolicErrorCodes.TimedOut,
            SharpProofErrorCategory.Timeout,
            SymbolicErrorExitCodes.TemporaryFailure);
        yield return Case(
            new OperationCanceledException(),
            SymbolicErrorCodes.Canceled,
            SharpProofErrorCategory.Cancellation,
            SymbolicErrorExitCodes.Canceled);
    }

    private static TestOutcome<T> Capture<T>(Func<T> operation) where T : class
    {
        try
        {
            return new TestOutcome<T>(operation(), null);
        }
        catch (Exception exception) when (!SymbolicErrorClassifier.IsFatal(exception))
        {
            return new TestOutcome<T>(null, SymbolicErrorClassifier.FromException(exception));
        }
    }

    private sealed record TestOutcome<T>(T? Value, SharpProofError? Error) where T : class
    {
        internal bool IsSuccess => Error == null;
    }

    private static TestCaseData Case(
        Exception exception,
        string expectedCode,
        SharpProofErrorCategory expectedCategory,
        int expectedExitCode)
    {
        return new TestCaseData(exception, expectedCode, expectedCategory, expectedExitCode)
            .SetName("SymbolicErrorClassifier_" + expectedCode);
    }
}
