using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class NativeSpecifiedCallTests
{
    [Test]
    public async Task IntegralToInt64OverflowResourceCallbacksInvalidateCallerMemory()
    {
        await CheckInterfaceArrayContract("int[] values", "Contract.Requires(values != null && values.Length > 0); Contract.Ensures(Contract.Result<int>() == 17); values[0] = 17; try { System.Convert.ToInt64(ulong.MaxValue); } catch (System.OverflowException) { return values[0]; } return 0;", WorkerClaimOutcome.Unknown);
    }

    [TestCase(false)]
    [TestCase(true)]
    [NonParallelizable]
    public void IntegralToInt64OverflowCanInvokeUserCultureCode(bool throwFromCulture)
    {
        var saved = System.Globalization.CultureInfo.CurrentUICulture;
        var culture = new ConvertOverflowCulture { ThrowFromName = throwFromCulture };
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = culture;
            culture.Armed = true;
            Exception? fault = null;
            try
            { _ = System.Convert.ToInt64(ulong.MaxValue); }
            catch (Exception exception) when (exception is OverflowException or InvalidOperationException)
            { fault = exception; }
            culture.Armed = false;
            Assert.That(fault, Is.TypeOf(throwFromCulture ? typeof(InvalidOperationException) : typeof(OverflowException)));
            Assert.That(culture.Values[0], Is.EqualTo(37));
        }
        finally
        {
            culture.Armed = false;
            System.Globalization.CultureInfo.CurrentUICulture = saved;
        }
    }

    private sealed class ConvertOverflowCulture : System.Globalization.CultureInfo
    {
        public ConvertOverflowCulture() : base("en-US") { }
        public bool Armed;
        public bool ThrowFromName;
        public int[] Values { get; } = [17];
        public override string Name
        {
            get
            {
                if (Armed)
                {
                    Values[0] = 37;
                    if (ThrowFromName)
                    { throw new InvalidOperationException("culture callback"); }
                }
                return base.Name;
            }
        }
    }
}
