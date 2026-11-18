using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using NUnit.Framework;

namespace SharpProof.Worker.Test;

public sealed class ExceptionResourceLockOracleTests
{
    [Test]
    public void ResourceLockCertificateMatchesTheAuditedRuntimeBody()
    {
        var core = typeof(Exception).Assembly;
        Assert.That(core.ManifestModule.ModuleVersionId, Is.EqualTo(new Guid("067dffbc-1eee-41f1-8a2a-41abef72f2bb")));
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
    [TestCase("NotSupported")]
    [TestCase("NotImplemented")]
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

    private static readonly ImmutableArray<string> AuditedConstructorBody =
    [
        "ldarg.0", "call System.SR::get_Arg_{T}()", "call System.SystemException::.ctor(System.String)",
        "ldarg.0", "ldc.i4 <int32>", "call System.Exception::set_HResult(System.Int32)", "ret"
    ];

    private static readonly ImmutableArray<string> AuditedResourcePropertyBody =
        ["ldstr Arg_{T}", "call System.SR::GetResourceString(System.String)", "ret"];

    // The parameterless NotSupportedException and NotImplementedException
    // constructors were admitted by comparing their pinned-runtime IL with the
    // audited InvalidOperationException(): each passes its own SR resource
    // property, which only forwards its key to the hashed GetResourceString,
    // to SystemException(string) and then stores a constant HResult.
    [TestCase(typeof(NotSupportedException))]
    [TestCase(typeof(NotImplementedException))]
    public void ParameterlessConstructorBodyMatchesTheAuditedInvalidOperationBody(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        Assert.That(typeof(Exception).Assembly.ManifestModule.ModuleVersionId, Is.EqualTo(new Guid("067dffbc-1eee-41f1-8a2a-41abef72f2bb")));
        var audited = typeof(InvalidOperationException);
        var expected = Normalize(Body(audited.GetConstructor(Type.EmptyTypes)!), audited);
        var actual = Normalize(Body(type.GetConstructor(Type.EmptyTypes)!), type);
        Assert.That(actual, Is.EqualTo(expected));
        Assert.That(expected, Is.EqualTo(AuditedConstructorBody));
        var resources = typeof(Exception).Assembly.GetType("System.SR", throwOnError: true)!;
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        Assert.That(Normalize(Body(resources.GetProperty("Arg_" + type.Name, flags)!.GetMethod!), type),
            Is.EqualTo(Normalize(Body(resources.GetProperty("Arg_" + audited.Name, flags)!.GetMethod!), audited)));
        Assert.That(Normalize(Body(resources.GetProperty("Arg_" + type.Name, flags)!.GetMethod!), type),
            Is.EqualTo(AuditedResourcePropertyBody));
    }

    private static ImmutableArray<string> Normalize(ImmutableArray<string> body, Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return body.Select(instruction => instruction.Replace("Arg_" + type.Name, "Arg_{T}", StringComparison.Ordinal)).ToImmutableArray();
    }

    private static ImmutableArray<string> Body(MethodBase method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var il = method.GetMethodBody()!.GetILAsByteArray()!;
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(static field => (OpCode)field.GetValue(null)!).ToImmutableDictionary(static opcode => opcode.Value);
        var body = ImmutableArray.CreateBuilder<string>();
        for (var offset = 0; offset < il.Length;)
        {
            var value = il[offset] == 0xFE ? unchecked((short)(0xFE00 | il[offset + 1])) : il[offset];
            offset += il[offset] == 0xFE ? 2 : 1;
            var opcode = opcodes[value];
            switch (opcode.OperandType)
            {
                case OperandType.InlineNone:
                    body.Add(opcode.Name!);
                    break;
                case OperandType.InlineI:
                    body.Add(opcode.Name + " <int32>");
                    offset += 4;
                    break;
                case OperandType.InlineString:
                    body.Add(opcode.Name + " " + method.Module.ResolveString(BitConverter.ToInt32(il, offset)));
                    offset += 4;
                    break;
                case OperandType.InlineMethod:
                    var callee = method.Module.ResolveMethod(BitConverter.ToInt32(il, offset))!;
                    body.Add(opcode.Name + " " + callee.DeclaringType!.FullName + "::" + callee.Name + "(" +
                        string.Join(",", callee.GetParameters().Select(static parameter => parameter.ParameterType.FullName)) + ")");
                    offset += 4;
                    break;
                default:
                    // Any other operand kind is outside the audited shape.
                    body.Add("unaudited " + opcode.Name);
                    offset = il.Length;
                    break;
            }
        }
        return body.ToImmutable();
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
            "NotSupported" => new NotSupportedException(),
            "NotImplemented" => new NotImplementedException(),
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
