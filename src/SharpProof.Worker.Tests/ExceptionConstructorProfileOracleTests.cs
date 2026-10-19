using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Frontend;

namespace SharpProof.Worker.Test;

public sealed class ExceptionConstructorProfileOracleTests
{
    [Test]
    public void EveryGuardedCoreConstructorRetainsItsRuntimeCallbackBoundary()
    {
        var core = typeof(Exception).Assembly;
        var compilation = CSharpCompilation.Create("ExceptionProfileAudit", references: [MetadataReference.CreateFromFile(core.Location)]);
        var signatures = 0;
        var guarded = 0;
        var cases = 0;
        var mismatches = ImmutableArray.CreateBuilder<string>();
        foreach (var type in core.GetTypes().Where(type => type is null ? throw new ArgumentNullException(nameof(type)) : type.IsVisible && !type.IsAbstract && !type.ContainsGenericParameters && typeof(Exception).IsAssignableFrom(type)))
        {
            foreach (var constructor in type.GetConstructors().Where(constructor => constructor is null ? throw new ArgumentNullException(nameof(constructor)) : constructor.GetParameters().All(parameter => parameter is null ? throw new ArgumentNullException(nameof(parameter)) : parameter.ParameterType == typeof(string) || parameter.ParameterType == typeof(Exception))))
            {
                var parameters = constructor.GetParameters();
                var id = "M:" + type.FullName!.Replace('+', '.') + ".#ctor" + (parameters.Length == 0 ? "" : "(" + string.Join(",", parameters.Select(parameter => parameter is null ? throw new ArgumentNullException(nameof(parameter)) : parameter.ParameterType.FullName)) + ")");
                var symbol = (IMethodSymbol)DocumentationCommentId.GetFirstSymbolForDeclarationId(id, compilation)!;
                Assert.That(symbol, Is.Not.Null, id);
                var guard = CSharpOperationSemantics.ExceptionConstructorMessageGuard(symbol);
                signatures++;
                if (guard is not null)
                { guarded++; }
                var combinations = (int)Math.Pow(3, parameters.Count(parameter => parameter is null ? throw new ArgumentNullException(nameof(parameter)) : parameter.ParameterType == typeof(string)));
                for (var combination = 0; combination < combinations; combination++)
                {
                    foreach (var throws in ImmutableArray.Create(false, true))
                    {
                        var inner = new CallbackException(throws);
                        var arguments = new object?[parameters.Length];
                        var remainder = combination;
                        for (var index = 0; index < parameters.Length; index++)
                        {
                            if (parameters[index].ParameterType == typeof(Exception))
                            { arguments[index] = inner; continue; }
                            var category = remainder % 3;
                            remainder /= 3;
                            arguments[index] = category == 0 ? "message" : category == 1 ? "" : null;
                        }
                        var culture = new CallbackCulture(throws);
                        var previousUi = CultureInfo.CurrentUICulture;
                        var previous = CultureInfo.CurrentCulture;
                        bool failed = false;
                        try
                        {
                            CultureInfo.CurrentUICulture = culture;
                            CultureInfo.CurrentCulture = culture;
                            culture.Armed = inner.Armed = true;
                            _ = constructor.Invoke(arguments);
                        }
                        // Reflection's own wrapper can perform a resource lookup
                        // and let the marker escape before wrapping the failure.
                        catch (Exception exception) when (exception is TargetInvocationException or FormatException)
                        { failed = true; }
                        finally
                        {
                            culture.Armed = inner.Armed = false;
                            CultureInfo.CurrentUICulture = previousUi;
                            CultureInfo.CurrentCulture = previous;
                        }
                        var safe = guard is { } ordinal && (ordinal == -1 || arguments[ordinal] is not null);
                        if (safe && (failed || culture.Reads != 0 || inner.Reads != 0))
                        { mismatches.Add(id + ": category=" + combination + ", throws=" + throws); }
                        cases++;
                    }
                }
            }
        }
        using (Assert.EnterMultipleScope())
        {
            // These are finite runtime/input-category checks, not exhaustive
            // behavioral contracts. New signatures/profiles require review.
            Assert.That(signatures, Is.EqualTo(312));
            Assert.That(guarded, Is.EqualTo(214));
            Assert.That(cases, Is.EqualTo(1756));
            Assert.That(mismatches.ToImmutable(), Is.Empty);
        }
    }

    private sealed class CallbackCulture(bool throws) : CultureInfo("en-US")
    {
        public bool Armed { get; set; }
        public int Reads { get; private set; }
        private void Observe()
        {
            if (!Armed)
            { return; }
            Reads++;
            if (throws)
            { throw new FormatException("culture callback"); }
        }
        public override string Name { get { Observe(); return base.Name; } }
        public override object? GetFormat(Type? formatType) { Observe(); return base.GetFormat(formatType); }
    }

    private sealed class CallbackException : Exception
    {
        private readonly bool _throws;
        public CallbackException() { }
        public CallbackException(string? message) : base(message) { }
        public CallbackException(string? message, Exception? innerException) : base(message, innerException) { }
        public CallbackException(bool throws) : base("inner") { _throws = throws; }
        public bool Armed { get; set; }
        public int Reads { get; private set; }
        private void Observe()
        {
            if (!Armed)
            { return; }
            Reads++;
            if (_throws)
            { throw new FormatException("inner callback"); }
        }
        public override string Message { get { Observe(); return base.Message; } }
        public override string ToString() { Observe(); return base.ToString(); }
    }
}
