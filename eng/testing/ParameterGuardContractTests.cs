using System.Collections.Immutable;
using System.Reflection;
using NUnit.Framework;

namespace SharpProof.TestSupport;

[TestFixture]
public sealed class ParameterGuardContractTests
{
    [Test]
    public void LinkedGuardsPreserveValuesAndNamedExceptionContracts()
    {
        var types = GuardTypes().ToImmutableArray();
        Assert.That(types, Is.Not.Empty, "The test's production dependency closure must include shared guards.");
        foreach (var type in types)
        {
            var value = new object();
            Assert.That(Invoke(type, "NotNull", [value, "input"]), Is.SameAs(value));
            AssertFailure(type, "NotNull", [null, "input"], typeof(ArgumentNullException), "input");
            Assert.That(Invoke(type, "ThrowIfMissing", [false, "input"]), Is.Null);
            AssertFailure(type, "ThrowIfMissing", [true, "input"], typeof(ArgumentNullException), "input");
            Assert.That(Invoke(type, "ThrowIfInvalid", [false, "input"]), Is.Null);
            AssertFailure(type, "ThrowIfInvalid", [true, "input"], typeof(ArgumentOutOfRangeException), "input");
            Assert.That(Invoke(type, "RequireInitialized", [false, "input", "detail"]), Is.Null);
            AssertFailure(type, "RequireInitialized", [true, "input", "detail"], typeof(ArgumentException), "input", "detail");
            AssertFailure(type, "RequireInitialized", [true, "input", null], typeof(ArgumentException), "input");
            Assert.That(Invoke(type, "ValidateInitialized", [value, false, "input", null]), Is.SameAs(value));
            Assert.That(Invoke(type, "ValidateInitialized", [null, false, "input", null]), Is.Null);
            AssertFailure(type, "ValidateInitialized", [value, true, "input", "detail"], typeof(ArgumentException), "input", "detail");
            foreach (var invalidName in ImmutableArray.Create<string?>(null, ""))
            {
                AssertFailure(type, "ValidateInitialized", [value, false, invalidName, null], typeof(ArgumentException), "parameterName");
                AssertFailure(type, "ThrowIfMissing", [false, invalidName], typeof(ArgumentException), "parameterName");
                AssertFailure(type, "NotNull", [value, invalidName], typeof(ArgumentException), "parameterName");
                AssertFailure(type, "ThrowIfInvalid", [false, invalidName], typeof(ArgumentException), "parameterName");
                AssertFailure(type, "RequireInitialized", [false, invalidName, null], typeof(ArgumentException), "parameterName");
            }
        }
    }

    private static IEnumerable<Type> GuardTypes()
    {
        var pending = ImmutableQueue.CreateRange(Assembly.GetExecutingAssembly().GetReferencedAssemblies());
        var visited = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        while (!pending.IsEmpty)
        {
            pending = pending.Dequeue(out var name);
            if (name.Name?.StartsWith("SharpProof.", StringComparison.Ordinal) != true || !visited.Add(name.FullName))
            { continue; }
            var assembly = Assembly.Load(name);
            var component = name.Name is "SharpProof.ConsoleLoggingAnalyzer" or "SharpProof.DefensiveCodingAnalyzer"
                ? name.Name["SharpProof.".Length..] : name.Name;
            if (assembly.GetType("SharpProof.Internal." + component.Replace(".", "", StringComparison.Ordinal) + ".ParameterGuard") is { } guard)
            { yield return guard; }
            foreach (var reference in assembly.GetReferencedAssemblies())
            { pending = pending.Enqueue(reference); }
        }
    }

    private static object? Invoke(Type type, string name, object?[] arguments)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(arguments);
        var method = type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;
        if (method.IsGenericMethodDefinition)
        { method = method.MakeGenericMethod(typeof(object)); }
        return method.Invoke(null, arguments);
    }

    private static void AssertFailure(Type type, string name, object?[] arguments,
        Type expectedType, string parameterName, string? detail = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(expectedType);
        ArgumentNullException.ThrowIfNull(parameterName);
        var wrapper = Assert.Throws<TargetInvocationException>((Action)(() => Invoke(type, name, arguments)));
        Assert.That(wrapper!.InnerException, Is.TypeOf(expectedType));
        var exception = (ArgumentException)wrapper.InnerException!;
        Assert.That(exception.ParamName, Is.EqualTo(parameterName));
        if (detail != null)
        { Assert.That(exception.Message, Does.StartWith(detail)); }
    }
}
