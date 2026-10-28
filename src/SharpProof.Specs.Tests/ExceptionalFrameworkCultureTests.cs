using System.Globalization;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using SharpProof.Specs;

namespace SharpProof.Specs.Test;

[TestFixture]
public sealed class ExceptionalFrameworkCultureTests
{
    [TestCase("bcl.math.abs.int32")]
    [TestCase("bcl.nullable.value")]
    public void ResourceCallbacksPreventClosedEffectAndExceptionBounds(string identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        var template = ApiSpecTable.Default.Templates.Single(row => row is null
            ? throw new ArgumentNullException(nameof(row)) : row.Target.WitnessIdentifier == identifier);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(template.Facets.Effects.Effects, Is.EqualTo(SpecEffect.Unknown));
            Assert.That(template.Facets.Throws.Behavior, Is.EqualTo(SpecThrowBehavior.Unknown));
            Assert.That(template.Facets.Throws.ExceptionMetadataNames, Is.Empty);
            Assert.That(template.Facets.Allocation.Behavior, Is.EqualTo(SpecAllocationBehavior.MayAllocate));
            Assert.That(template.Facets.Termination!.Behavior, Is.EqualTo(SpecTerminationBehavior.Unknown));
        }
    }

    [TestCase(false, false, false)]
    [TestCase(false, false, true)]
    [TestCase(false, true, false)]
    [TestCase(false, true, true)]
    [TestCase(true, false, false)]
    [TestCase(true, false, true)]
    [TestCase(true, true, false)]
    [TestCase(true, true, true)]
    public void OnlyExceptionalPathsConsultAmbientCulture(bool nullable, bool fault, bool callbackThrows)
    {
        var previous = CultureInfo.CurrentUICulture;
        var culture = new CallbackCulture(callbackThrows);
        Exception? observed = null;
        int? result = null;
        try
        {
            CultureInfo.CurrentUICulture = culture;
            culture.Armed = true;
            try
            {
                result = nullable ? NullableValue(!fault) : Math.Abs(fault ? int.MinValue : -7);
            }
            catch (FormatException exception)
            {
                observed = exception;
            }
            catch (OverflowException exception)
            {
                observed = exception;
            }
            catch (InvalidOperationException exception)
            {
                observed = exception;
            }
        }
        finally
        {
            culture.Armed = false;
            CultureInfo.CurrentUICulture = previous;
        }
        using (Assert.EnterMultipleScope())
        {
            Assert.That(culture.Reads, fault ? Is.GreaterThan(0) : Is.Zero);
            Assert.That(result, fault ? Is.Null : Is.EqualTo(7));
            if (!fault)
            {
                Assert.That(observed, Is.Null);
            }
            else
            {
                var expected = callbackThrows ? typeof(FormatException)
                    : nullable ? typeof(InvalidOperationException) : typeof(OverflowException);
                Assert.That(observed, Is.TypeOf(expected));
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int NullableValue(bool present)
    {
        int? value = present ? 7 : null;
        return value!.Value;
    }

    private sealed class CallbackCulture(bool throws) : CultureInfo("en-US")
    {
        internal bool Armed { get; set; }
        internal int Reads { get; private set; }
        public override string Name
        {
            get
            {
                if (Armed)
                {
                    Reads++;
                    if (throws)
                    { throw new FormatException("resource culture callback"); }
                }
                return base.Name;
            }
        }
    }
}
