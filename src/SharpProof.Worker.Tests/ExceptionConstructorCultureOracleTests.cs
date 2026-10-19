using System.Collections.Immutable;
using System.Globalization;
using NUnit.Framework;

namespace SharpProof.Worker.Test;

public sealed class ExceptionConstructorCultureOracleTests
{
    [TestCase("argument-null-default")]
    [TestCase("argument-null-name")]
    [TestCase("argument-range-name")]
    [TestCase("invalid-operation-default")]
    [TestCase("invalid-operation-null")]
    [TestCase("key-not-found-null")]
    [TestCase("aggregate-default")]
    [TestCase("aggregate-null")]
    [TestCase("unreachable-null")]
    [TestCase("divide-zero-default")]
    [TestCase("object-disposed-name")]
    public void ResourceMessageLookupCanMutateStateAndThrowThroughCulture(string scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        Func<Exception> create = scenario switch
        {
            "argument-null-default" => () => Activator.CreateInstance<ArgumentNullException>(),
            "argument-null-name" => () => new ArgumentNullException(nameof(scenario)),
            "argument-range-name" => () => new ArgumentOutOfRangeException(nameof(scenario)),
            "invalid-operation-default" => () => new InvalidOperationException(),
            "invalid-operation-null" => () => new InvalidOperationException((string?)null),
            "key-not-found-null" => () => new KeyNotFoundException((string?)null),
            "aggregate-default" => () => new AggregateException(),
            "aggregate-null" => () => new AggregateException((string?)null),
            "unreachable-null" => () => new System.Diagnostics.UnreachableException((string?)null),
            "divide-zero-default" => () => new DivideByZeroException(),
            "object-disposed-name" => () => new ObjectDisposedException("object"),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        Observe(create, true);
    }

    [TestCase("exception-null")]
    [TestCase("invalid-operation-message")]
    [TestCase("key-not-found-message")]
    [TestCase("argument-null-message")]
    [TestCase("argument-range-message")]
    [TestCase("argument-message")]
    public void ExplicitMessageConstructionDoesNotConsultAmbientCulture(string scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        Func<Exception> create = scenario switch
        {
            "exception-null" => () => (Exception)typeof(Exception).GetConstructor([typeof(string)])!.Invoke([null]),
            "invalid-operation-message" => () => new InvalidOperationException("message"),
            "key-not-found-message" => () => new KeyNotFoundException("message"),
            "argument-null-message" => () => new ArgumentNullException(nameof(scenario), "message"),
            "argument-range-message" => () => new ArgumentOutOfRangeException(nameof(scenario), "message"),
            "argument-message" => () => new ArgumentException((string?)null),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        Observe(create, false);
    }

    private static void Observe(Func<Exception> create, bool resourceMessage)
    {
        ArgumentNullException.ThrowIfNull(create);
        foreach (var throws in ImmutableArray.Create(false, true))
        {
            var previous = CultureInfo.CurrentUICulture;
            var culture = new CallbackCulture(throws);
            CultureInfo.CurrentUICulture = culture;
            culture.Armed = true;
            Exception? observed = null;
            try
            { _ = create(); }
            catch (FormatException exception)
            { observed = exception; }
            finally
            {
                culture.Armed = false;
                CultureInfo.CurrentUICulture = previous;
            }
            Assert.That(culture.Reads, resourceMessage ? Is.GreaterThan(0) : Is.EqualTo(0));
            Assert.That(observed, resourceMessage && throws ? Is.TypeOf<FormatException>() : Is.Null);
        }
    }

    private sealed class CallbackCulture(bool throws) : CultureInfo("en-US")
    {
        public bool Armed { get; set; }
        public int Reads { get; private set; }
        public override string Name
        {
            get
            {
                if (Armed)
                {
                    Reads++;
                    if (throws)
                    { throw new FormatException("culture callback"); }
                }
                return base.Name;
            }
        }
    }
}
