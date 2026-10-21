using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using NUnit.Framework;

namespace SharpProof.Worker.Test;

public sealed class ExceptionResourceLockOracleTests
{
    [Test]
    public void ResourceLockCertificateMatchesTheAuditedRuntimeBody()
    {
        var core = typeof(Exception).Assembly;
        Assert.That(core.ManifestModule.ModuleVersionId, Is.EqualTo(new Guid("9f81bf37-7ee9-46d6-8148-5938d2a8d041")));
        var resources = core.GetType("System.SR", throwOnError: true)!;
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        foreach (var (name, digest) in ImmutableArray.Create(("InternalGetResourceString", "937984798B88E166526578372CAD3CC7A8D14887FDF2EAD89F5377238A91CE16"), ("GetResourceString", "7CE40FA63ED608DE5CE42E1644F5EAF44C19C43B85BF783383FE45C882FF40D5"), ("UsingResourceKeys", "F18D2D1686BF45A38EDAA7249E3683A2CFD67448110A1D01C46D8E1B0C3A5557")
))
        {
            var method = resources.GetMethod(name, flags)!;
            var body = method.GetMethodBody()!.GetILAsByteArray()!.ToImmutableArray();
            Assert.That(Convert.ToHexString(SHA256.HashData(body.AsSpan())), Is.EqualTo(digest), name);
        }
    }

    [TestCase("Argument")]
    [TestCase("ArgumentNull")]
    [TestCase("ArgumentNullName")]
    [TestCase("ArgumentRange")]
    [TestCase("ArgumentRangeName")]
    [TestCase("InvalidOperation")]
    [TestCase("InvalidOperationNull")]
    [TestCase("DivideByZero")]
    [TestCase("IOException")]
    [TestCase("ObjectDisposedName")]
    [TestCase("Aggregate")]
    [TestCase("AggregateNull")]
    [TestCase("KeyNotFoundNull")]
    public void ActualConstructorHoldsTheCertifiedLockBeforeCallingCulture(string kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        var previousUi = CultureInfo.CurrentUICulture;
        var previous = CultureInfo.CurrentCulture;
        ObservingCulture? culture = null;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            var observation = PassiveCallableVcPlan.ProbeExceptionResourceLock(kind);
            Assert.That(observation?.Synchronization, Is.Not.Null);
            culture = new ObservingCulture(observation!.Synchronization!);
            CultureInfo.CurrentUICulture = culture;
            culture.Armed = true;
            _ = Construct(kind);
        }
        finally
        {
            if (culture != null)
            { culture.Armed = false; }
            CultureInfo.CurrentUICulture = previousUi;
            CultureInfo.CurrentCulture = previous;
        }
        Assert.That(culture!.Reads, Is.GreaterThan(0));
        Assert.That(culture.AlwaysHeld, Is.True);
    }

    private static Exception Construct(string kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        // Invoke the actual overload, independently of the production replay
        // probe. These tests intentionally include parameter-only constructors.
#pragma warning disable CA2208 // Exercise exact runtime APIs, not this helper's argument policy.
        return kind switch
        {
            "Argument" => new ArgumentException(),
            "ArgumentNull" => new ArgumentNullException(),
            "ArgumentNullName" => new ArgumentNullException(nameof(kind)),
            "ArgumentRange" => new ArgumentOutOfRangeException(),
            "ArgumentRangeName" => new ArgumentOutOfRangeException(nameof(kind)),
            "InvalidOperation" => new InvalidOperationException(),
            "InvalidOperationNull" => new InvalidOperationException((string?)null),
            "DivideByZero" => new DivideByZeroException(),
            "IOException" => new IOException(),
            "ObjectDisposedName" => new ObjectDisposedException(nameof(kind)),
            "Aggregate" => new AggregateException(),
            "AggregateNull" => new AggregateException((string?)null),
            "KeyNotFoundNull" => new KeyNotFoundException((string?)null),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
#pragma warning restore CA2208
    }

    private sealed class ObservingCulture(object synchronization) : CultureInfo("en-US")
    {
        private readonly object _synchronization = synchronization ?? throw new ArgumentNullException(nameof(synchronization));
        public bool Armed { get; set; }
        public int Reads { get; private set; }
        public bool AlwaysHeld { get; private set; } = true;
        public override string Name
        {
            get
            {
                if (Armed)
                {
                    Reads++;
                    AlwaysHeld &= Monitor.IsEntered(_synchronization);
                }
                return base.Name;
            }
        }
    }
}
