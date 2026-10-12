using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace SharpProof.Worker.Test.LongFuzz;

[TestFixture]
public sealed class LongFuzzRuntimeCoverageRegressionTests
{
    private static readonly int[] SingleItem = [0];
    private static readonly bool[] ClaimValidity = [false, true];

    [TestCase(4, false)]
    [TestCase(4, true)]
    [TestCase(6, false)]
    [TestCase(6, true)]
    [TestCase(7, false)]
    [TestCase(7, true)]
    [TestCase(8, false)]
    [TestCase(8, true)]
    [TestCase(9, false)]
    [TestCase(9, true)]
    [TestCase(10, false)]
    [TestCase(10, true)]
    [TestCase(-7, false)]
    [TestCase(-7, true)]
    public async Task SourceLiteralSamplesPreserveValidClaimsAndObserveInvalidClaims(int literal, bool useY)
    {
        var parameter = useY ? "y" : "x";
        var entry = useY ? "y0" : "x0";
        var formattedLiteral = literal.ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (var valid in ClaimValidity)
        {
            var contract = valid ? $"Contract.Result<int>() == Contract.Old({parameter})"
                : $"Contract.Result<int>() != {formattedLiteral}";
            var post = valid ? $"r == {entry}" : $"r != {formattedLiteral}";
            var source = $$"""
                using SharpProof.Attributes;
                public static class C
                {
                    public static int State;
                    public static int Other;
                    public static int Target(int x, int y, int[]? a, string? s)
                    {
                        Contract.Requires(true);
                        Contract.Ensures({{contract}});
                        return {{parameter}};
                    }
                    public static bool Pre(int x, int y, int[]? a, string? s) => true;
                    public static bool Post(int r, int x0, int y0, int s0, int st) => {{post}};
                }
                """;
            var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12));
            var compilation = CSharpCompilation.Create("SourceLiteralWitness", [tree],
                TestMetadataReferences.WithSharpProof,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release,
                    nullableContextOptions: NullableContextOptions.Enable));
            TestCompilation.AssertNoErrors(compilation);
            using var image = new MemoryStream();
            var emission = compilation.Emit(image);
            Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
            image.Position = 0;
            var context = new AssemblyLoadContext("SourceLiteralWitness", isCollectible: true);
            try
            {
                var type = context.LoadFromStream(image).GetType("C")!;
                var x = useY ? 0 : literal;
                var y = useY ? literal : 0;
                object?[] arguments = [x, y, null, null];
                Assert.That(type.GetMethod("Pre")!.Invoke(null, arguments), Is.EqualTo(true));
                var result = type.GetMethod("Target")!.Invoke(null, arguments);
                Assert.That(result, Is.EqualTo(literal));
                object?[] postArguments = [result, x, y, 0, 0];
                Assert.That(type.GetMethod("Post")!.Invoke(null, postArguments), Is.EqualTo(valid));
            }
            finally
            {
                context.Unload();
            }
            var observed = await AnalyzerPipelineLongFuzzTests.RunCase(source, ContractClaimProgramGenerator.Ensures,
                static _ => { });
            Assert.That(observed.Outcome, Is.EqualTo(valid ? "Proven" : "Refuted"));
            Assert.That(observed.Reason, Is.EqualTo("None"));
            Assert.That(observed.Problems, Is.Empty);
            Assert.That(observed.Runtime, valid ? Does.StartWith("no violation on ") : Does.StartWith("postcondition false"));
        }
    }

    [Test]
    public async Task OracleFindsHostedPostconditionWitnessOutsideItsIntegerGrid()
    {
        const int seed = 449369090;
        var (source, mode, claim) = new ContractClaimProgramGenerator(new Random(seed)).Program();
        Assert.That(mode, Is.EqualTo(ContractClaimProgramGenerator.Ensures));
        Assert.That(claim, Is.EqualTo("Contract.Ensures(Contract.Result<int>() != 7);"));
        Assert.That(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source))),
            Is.EqualTo("b3bdf20a39700daed35d28ab2af0c6eccce2d778a2b4f5a4d0da5bce39db6483"),
            "The generated source must match the unchanged hosted finding.");
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12));
        var compilation = CSharpCompilation.Create("HostedLongFuzzWitness", [tree],
            TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Enable));
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
        image.Position = 0;
        var context = new AssemblyLoadContext("HostedLongFuzzWitness", isCollectible: true);
        try
        {
            var type = context.LoadFromStream(image).GetType("C")!;
            object?[] arguments = [7, 0, null, null];
            Assert.That(type.GetMethod("Pre")!.Invoke(null, arguments), Is.EqualTo(true));
            var result = type.GetMethod("Target")!.Invoke(null, arguments);
            Assert.That(result, Is.EqualTo(7));
            object?[] postArguments = [result, 7, 0, 0, 0];
            Assert.That(type.GetMethod("Post")!.Invoke(null, postArguments), Is.EqualTo(false));
            TestContext.WriteLine("Original hosted source: CLR Pre=true, Target=7, Post=false at x=7,y=0,a=null,s=null.");
        }
        finally
        {
            context.Unload();
        }
        var observed = await AnalyzerPipelineLongFuzzTests.RunCase(source, mode, static _ => { });
        TestContext.WriteLine($"Current default worker: {observed.Outcome}/{observed.Reason}; runtime: {observed.Runtime}.");
        foreach (var problem in observed.Problems)
        {
            TestContext.WriteLine($"{problem.Kind}: {problem.Details}");
        }
        Assert.That(observed.Outcome, Is.EqualTo("Refuted"));
        Assert.That(observed.Reason, Is.EqualTo("None"));
        Assert.That(observed.Problems, Is.Empty,
            "The runtime observer must find the independently emitted CLR postcondition witness.");
    }

    [TestCase(3000036, ContractClaimProgramGenerator.DoesNotThrow, typeof(InvalidOperationException))]
    [TestCase(3000183, ContractClaimProgramGenerator.EnforcePure, typeof(DivideByZeroException))]
    public void OracleFindsCompiledClrWitnesses(int seed, string expectedMode, Type expectedException)
    {
        var (source, mode, _) = new ContractClaimProgramGenerator(new Random(seed)).Program();
        Assert.That(mode, Is.EqualTo(expectedMode));
        Assert.That(source, Does.Contain(seed == 3000036 ? "Thrower((y + 5))" : "case 1: case 2: State++;"));
        var compilation = TestCompilation.Create("LongFuzzCoverage" + seed, ("Subject.cs", source));
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
        image.Position = 0;
        var context = new AssemblyLoadContext("LongFuzzCoverage" + seed, isCollectible: true);
        try
        {
            var type = context.LoadFromStream(image).GetType("C")!;
            var arguments = seed == 3000036
                ? new object?[] { 0, -2, null, null }
                : new object?[] { 0, 0, SingleItem.ToArray(), null };
            TargetInvocationException? thrown = null;
            try
            {
                type.GetMethod("Target")!.Invoke(null, arguments);
            }
            catch (TargetInvocationException exception)
            {
                thrown = exception;
            }
            Assert.That(thrown?.InnerException, Is.TypeOf(expectedException));
            if (seed == 3000183)
            {
                Assert.That((int)type.GetField("State")!.GetValue(null)!, Is.EqualTo(1));
            }
        }
        finally
        {
            context.Unload();
        }
        var oracle = typeof(AnalyzerPipelineLongFuzzTests).GetNestedType("RuntimeOracle", BindingFlags.NonPublic)!;
        var run = oracle.GetMethod("Run", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)!;
        var observation = run.Invoke(null, [source, mode])!;
        var violation = (string?)observation.GetType().GetProperty("Violation")!.GetValue(observation);
        Assert.That(violation, Is.Not.Null, $"seed {seed}: emitted CLR witness exists but runtime input grid missed it");
    }

    [TestCase(3001234, ContractClaimProgramGenerator.ZeroAllocations, "Contract.Requires((s != null && s.Length == 7));")]
    [TestCase(3001600, ContractClaimProgramGenerator.DoesNotThrow, "checked((int)(byte)(a == null ? 0 : a.Length) + 1)")]
    [TestCase(3001665, ContractClaimProgramGenerator.DoesNotThrow, "checked((int)(byte)(a == null ? 0 : a.Length) + 0)")]
    [TestCase(3001792, ContractClaimProgramGenerator.EnforcePure, "Contract.Requires((s != null && s.Length == 6));")]
    public void OracleFindsAdditionalCompiledClrWitnesses(int seed, string expectedMode, string sourceMarker)
    {
        var (source, mode, _) = new ContractClaimProgramGenerator(new Random(seed)).Program();
        Assert.That(mode, Is.EqualTo(expectedMode));
        Assert.That(source, Does.Contain(sourceMarker));
        var compilation = TestCompilation.Create("LongFuzzBoundary" + seed, ("Subject.cs", source));
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
        image.Position = 0;
        var context = new AssemblyLoadContext("LongFuzzBoundary" + seed, isCollectible: true);
        try
        {
            var type = context.LoadFromStream(image).GetType("C")!;
            var x = seed == 3001234 ? 100 : 0;
            int[]? array = seed is 3001600 or 3001665 ? new int[256] : null;
            string? value = seed switch
            {
                3001234 => "abcdefg",
                3001792 => "abcdef",
                _ => null
            };
            Assert.That((bool)type.GetMethod("Pre")!.Invoke(null, [x, 0, array, value])!, Is.True);
            var target = type.GetMethod("Target")!;
            if (seed == 3001792)
            {
                target.Invoke(null, [x, 0, array, value]);
                Assert.That((int)type.GetField("State")!.GetValue(null)!, Is.Not.Zero);
            }
            else
            {
                TargetInvocationException? thrown = null;
                try
                {
                    target.Invoke(null, [x, 0, array, value]);
                }
                catch (TargetInvocationException exception)
                {
                    thrown = exception;
                }
                var expected = seed == 3001234 ? typeof(InvalidOperationException) : typeof(OverflowException);
                Assert.That(thrown?.InnerException, Is.TypeOf(expected));
            }
        }
        finally
        {
            context.Unload();
        }
        var oracle = typeof(AnalyzerPipelineLongFuzzTests).GetNestedType("RuntimeOracle", BindingFlags.NonPublic)!;
        var run = oracle.GetMethod("Run", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)!;
        var observation = run.Invoke(null, [source, mode])!;
        var violation = (string?)observation.GetType().GetProperty("Violation")!.GetValue(observation);
        Assert.That(violation, Is.Not.Null, $"seed {seed}: compiled CLR witness exists but runtime input grid missed it");
    }
}
