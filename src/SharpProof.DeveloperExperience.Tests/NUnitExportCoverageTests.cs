using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.Tool;
using SharpProof.Worker.Protocol;

namespace SharpProof.DeveloperExperience.Tests;

[TestFixture]
public sealed class NUnitExportCoverageTests
{
    private const string DefaultSource = "using SharpProof.Attributes; public static class Api { public static int Run(int value) { Contract.Ensures(Contract.Result<int>() > value); return value; } }";

    [TestCase("netstandard2.0", "net8.0", false)]
    [TestCase("netstandard2.1", "net8.0", true)]
    [TestCase("net8.0", "net8.0", false)]
    [TestCase("net9.0", "net9.0", true)]
    [TestCase("net10.0", "net10.0", false)]
    public async Task ExportProducesAnIndependentCompilableRegressionProject(string consumerFramework, string testFramework, bool checkOverflow)
    {
        ArgumentException.ThrowIfNullOrEmpty(consumerFramework);
        ArgumentException.ThrowIfNullOrEmpty(testFramework);
        using var fixture = new ExportFixture(targetFramework: consumerFramework);
        fixture.Snapshot = fixture.Snapshot with { Options = fixture.Snapshot.Options with { CheckOverflow = checkOverflow } };
        await fixture.ExportAsync(TestContext.CurrentContext.CancellationToken);
        var source = await File.ReadAllTextAsync(Path.Combine(fixture.OutputPath, "Regression.cs"), TestContext.CurrentContext.CancellationToken);
        var project = XDocument.Load(Path.Combine(fixture.OutputPath, "SharpProof.Regression.csproj"));
        var reference = project.Descendants("ProjectReference").Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(project.Root!.Attribute("Sdk")!.Value, Is.EqualTo("Microsoft.NET.Sdk"));
            Assert.That(project.Descendants("TargetFramework").Single().Value, Is.EqualTo(testFramework));
            Assert.That(project.Descendants("IsTestProject").Single().Value, Is.EqualTo("true"));
            Assert.That(project.Descendants("ManagePackageVersionsCentrally").Single().Value, Is.EqualTo("false"));
            Assert.That(reference.Attribute("Include")!.Value, Is.EqualTo("../Consumer/Consumer.csproj"));
            Assert.That(Path.GetFullPath(Path.Combine(fixture.OutputPath, reference.Attribute("Include")!.Value)), Is.EqualTo(fixture.ProjectPath));
            Assert.That(reference.Attribute("AdditionalProperties")!.Value, Does.StartWith("SharpProofProfile=advisory;SharpProofVerify=false;DefineConstants="));
            Assert.That(reference.Attribute("AdditionalProperties")!.Value, Does.Contain(";CheckForOverflowUnderflow=" + (checkOverflow ? "true" : "false")));
            Assert.That(project.Descendants("PackageReference").Select(static item =>
                (item ?? throw new ArgumentNullException(nameof(item))).Attribute("Include")!.Value),
                Is.EquivalentTo(ImmutableArray.Create("Microsoft.NET.Test.Sdk", "NUnit", "NUnit3TestAdapter")));
            Assert.That(source, Does.Contain("global::@Api.@Run(_argument0)"));
            Assert.That(source, Does.Contain(checkOverflow ? "Assert.That(checked(" : "Assert.That(unchecked("));
            Assert.That(source, Does.Not.Contain("Contract.Result").And.Not.Contain("Contract.Ensures"));
            Assert.That(Directory.GetDirectories(fixture.Root, ".sharpproof-export-*"), Is.Empty);
        }

        var compilation = fixture.Compilation.AddReferences(MetadataReference.CreateFromFile(typeof(Assert).Assembly.Location))
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(source));
        AssertNoCompilerErrors(compilation);
    }

    [Test]
    public async Task ExportEscapesNamespaceTypeMethodAndBooleanInputs()
    {
        const string source = "using SharpProof.Attributes; namespace @event { public class @class { public class @struct { public static bool @switch(bool value) { Contract.Ensures(!Contract.Result<bool>() == (value ? true : false)); return value; } } } }";
        using var fixture = new ExportFixture(source, methodName: "switch");
        fixture.Result = fixture.Result with { Model = [new() { Variable = "parameter:0", Kind = "Boolean", Value = "true" }] };
        await fixture.ExportAsync(TestContext.CurrentContext.CancellationToken);
        var generated = await File.ReadAllTextAsync(Path.Combine(fixture.OutputPath, "Regression.cs"), TestContext.CurrentContext.CancellationToken);
        Assert.That(generated, Does.Contain("var _argument0 = true;").And.Contain("global::@event.@class.@struct.@switch(_argument0)"));
        AssertNoCompilerErrors(fixture.Compilation.AddReferences(MetadataReference.CreateFromFile(typeof(Assert).Assembly.Location))
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(generated)));
    }

    [Test]
    public async Task ParameterlessRegressionStillCallsAndChecksTheOriginalMethod()
    {
        using var fixture = new ExportFixture("using SharpProof.Attributes; public static class Api { public static int Run() { Contract.Ensures(Contract.Result<int>() > 0); return 0; } }");
        await fixture.ExportAsync(TestContext.CurrentContext.CancellationToken);
        var generated = await File.ReadAllTextAsync(Path.Combine(fixture.OutputPath, "Regression.cs"), TestContext.CurrentContext.CancellationToken);
        Assert.That(generated, Does.Contain("global::@Api.@Run()").And.Not.Contain("_argument"));
        AssertNoCompilerErrors(fixture.Compilation.AddReferences(MetadataReference.CreateFromFile(typeof(Assert).Assembly.Location))
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(generated)));
    }

    [Test]
    public async Task ProjectReferencesTreatMsBuildMetacharactersAsLiteralPathCharacters()
    {
        using var fixture = new ExportFixture(projectDirectoryName: "Consumer;With%3B$(Missing)@(Items)' spaces");
        await fixture.ExportAsync(TestContext.CurrentContext.CancellationToken);
        var projectPath = Path.Combine(fixture.OutputPath, "SharpProof.Regression.csproj");
        var project = XDocument.Load(projectPath);
        var include = project.Descendants("ProjectReference").Single().Attribute("Include")!.Value;
        Assert.That(include, Is.EqualTo("../Consumer%3BWith%253B%24%28Missing%29%40%28Items%29%27 spaces/Consumer.csproj"));
        var evaluation = await ToolProcess.RunAsync("dotnet", fixture.Root, TestContext.CurrentContext.CancellationToken,
            "msbuild", projectPath, "-nologo", "-getItem:ProjectReference");
        Assert.That(evaluation.ExitCode, Is.Zero, evaluation.Output);
        using var document = JsonDocument.Parse(evaluation.Output);
        var references = document.RootElement.GetProperty("Items").GetProperty("ProjectReference");
        Assert.That(references.GetArrayLength(), Is.EqualTo(1));
        Assert.That(references[0].GetProperty("FullPath").GetString(), Is.EqualTo(fixture.ProjectPath));
        Assert.That(references[0].GetProperty("Identity").GetString(),
            Is.EqualTo(Path.GetRelativePath(fixture.OutputPath, fixture.ProjectPath).Replace(Path.DirectorySeparatorChar, '/')));
    }

    [TestCase("directory")]
    [TestCase("file")]
    public void ExistingOutputIsPreserved(string kind)
    {
        ArgumentException.ThrowIfNullOrEmpty(kind);
        using var fixture = new ExportFixture();
        if (kind == "directory")
        {
            Directory.CreateDirectory(fixture.OutputPath);
            File.WriteAllText(Path.Combine(fixture.OutputPath, "keep.txt"), "keep");
        }
        else
        {
            File.WriteAllText(fixture.OutputPath, "keep");
        }

        Assert.ThrowsAsync<IOException>(new Func<Task>(async () => await fixture.ExportAsync(TestContext.CurrentContext.CancellationToken)));
        Assert.That(File.ReadAllText(kind == "directory" ? Path.Combine(fixture.OutputPath, "keep.txt") : fixture.OutputPath), Is.EqualTo("keep"));
    }

    [Test]
    public void MissingProjectAndClaimAreRejectedBeforeEvaluation()
    {
        using var fixture = new ExportFixture();
        Assert.ThrowsAsync<ArgumentException>(new Func<Task>(async () => await NUnitExporter.ExportAsync(Path.Combine(fixture.Root, "missing.csproj"),
            fixture.Evidence, fixture.Claim.ClaimId, fixture.OutputPath, TestContext.CurrentContext.CancellationToken)));
        fixture.Response.Manifest.Claims = [];
        Assert.ThrowsAsync<ArgumentException>(new Func<Task>(async () => await fixture.ExportAsync(TestContext.CurrentContext.CancellationToken)));
        Assert.That(Directory.Exists(fixture.OutputPath), Is.False);
    }

    [TestCase("outcome")]
    [TestCase("kind")]
    [TestCase("companion")]
    [TestCase("attribute")]
    [TestCase("vacuity")]
    public void OnlyDirectNonvacuousRefutationsCanBeExported(string unsupported)
    {
        ArgumentException.ThrowIfNullOrEmpty(unsupported);
        using var fixture = new ExportFixture();
        switch (unsupported)
        {
            case "outcome":
                fixture.Result = fixture.Result with { Outcome = WorkerClaimOutcome.Unknown };
                break;
            case "kind":
                fixture.Claim = fixture.Claim with { Kind = WorkerClaimKind.Effect };
                break;
            case "companion":
                fixture.Claim = fixture.Claim with { Evidence = WorkerClaimEvidence.CompanionClause };
                break;
            case "attribute":
                fixture.Claim = fixture.Claim with { Evidence = WorkerClaimEvidence.ReturnAttribute };
                break;
            case "vacuity":
                fixture.Result = fixture.Result with { Vacuity = WorkerVacuityKind.ContradictoryPreconditions };
                break;
        }

        AssertExportRejected<InvalidOperationException>(fixture, "Only replayed direct scalar");
    }

    [Test]
    public void InvalidProjectEvaluationIsReportedWithoutAnOutputDirectory()
    {
        using var fixture = new ExportFixture();
        File.WriteAllText(fixture.ProjectPath, "<Project><broken>");
        AssertExportRejected<InvalidOperationException>(fixture, "could not be evaluated");
    }

    [TestCase("assembly")]
    [TestCase("framework")]
    [TestCase("multitarget")]
    public void ProjectEvaluationMustMatchTheCapturedSingleTarget(string mismatch)
    {
        ArgumentException.ThrowIfNullOrEmpty(mismatch);
        using var fixture = new ExportFixture();
        var project = XDocument.Load(fixture.ProjectPath);
        project.Descendants(mismatch switch { "assembly" => "AssemblyName", "framework" => "TargetFramework", _ => "TargetFrameworks" }).Single().Value =
            mismatch switch { "assembly" => "AnotherAssembly", "framework" => "net9.0", _ => "net8.0;net9.0" };
        project.Save(fixture.ProjectPath);
        AssertExportRejected<InvalidOperationException>(fixture, "matching single-target");
    }

    [TestCase("options")]
    [TestCase("trees")]
    [TestCase("references")]
    [TestCase("empty")]
    public void CapturedCompilationMustContainItsRequiredInputs(string missing)
    {
        ArgumentException.ThrowIfNullOrEmpty(missing);
        using var fixture = new ExportFixture();
        fixture.Snapshot = missing switch
        {
            "options" => fixture.Snapshot with { Options = null! },
            "trees" => fixture.Snapshot with { SyntaxTrees = default },
            "references" => fixture.Snapshot with { References = default },
            _ => fixture.Snapshot with { SyntaxTrees = [] }
        };
        AssertExportRejected<InvalidDataException>(fixture, "captured source, compiler options");
    }

    [TestCase("null")]
    [TestCase("negative")]
    [TestCase("oversized")]
    public void InvalidSourceMetadataIsRejectedBeforeReading(string shape)
    {
        ArgumentException.ThrowIfNullOrEmpty(shape);
        using var fixture = new ExportFixture();
        fixture.Snapshot = fixture.Snapshot with
        {
            SyntaxTrees = [shape == "null" ? null! : fixture.Snapshot.SyntaxTrees[0] with
            {
                TextLength = shape == "negative" ? -1 : CompilerManifestArtifactFile.MaximumBytes + 1
            }]
        };
        AssertExportRejected<InvalidDataException>(fixture, "source metadata exceeds");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ChangedSourceNeverProducesAReplay(bool larger)
    {
        using var fixture = new ExportFixture();
        File.WriteAllText(fixture.SourcePath, larger ? new string(' ', DefaultSource.Length * 5) : DefaultSource.Replace("return value", "return false", StringComparison.Ordinal));
        AssertExportRejected<InvalidDataException>(fixture, larger ? "Source size changed" : "Source changed");
    }

    [TestCase("language")]
    [TestCase("script")]
    public void UnsupportedRecordedParseModesAreRejected(string mode)
    {
        ArgumentException.ThrowIfNullOrEmpty(mode);
        using var fixture = new ExportFixture();
        fixture.Snapshot = fixture.Snapshot with
        {
            SyntaxTrees = [fixture.Snapshot.SyntaxTrees[0] with { LanguageVersion = mode == "language" ? "FutureLanguage" : "CSharp13", Kind = mode == "script" ? "Script" : "Regular" }]
        };
        AssertExportRejected<InvalidOperationException>(fixture, "recorded source parse mode");
    }

    [TestCase("count")]
    [TestCase("null")]
    [TestCase("modules")]
    [TestCase("aliases")]
    [TestCase("multimodule")]
    [TestCase("extern")]
    [TestCase("interop")]
    [TestCase("nullmodule")]
    [TestCase("zero")]
    [TestCase("oversized")]
    [TestCase("size")]
    [TestCase("digest")]
    public void ChangedOrUnsupportedReferenceClosuresNeverProduceAReplay(string shape)
    {
        ArgumentException.ThrowIfNullOrEmpty(shape);
        using var fixture = new ExportFixture();
        var reference = fixture.Snapshot.References[0];
        var module = reference.Modules[0];
        var changed = shape switch
        {
            "null" => null!,
            "modules" => reference with { Modules = default },
            "aliases" => reference with { Aliases = default },
            "multimodule" => reference with { Modules = [module, module] },
            "extern" => reference with { Aliases = ["custom"] },
            "interop" => reference with { EmbedInteropTypes = true },
            "nullmodule" => reference with { Modules = [null!] },
            "zero" => reference with { Modules = [module with { SizeBytes = 0 }] },
            "oversized" => reference with { Modules = [module with { SizeBytes = (long)CompilerReferenceLimits.MaximumModuleBytes + 1 }] },
            "size" => reference with { Modules = [module with { SizeBytes = module.SizeBytes + 1 }] },
            "digest" => reference with { Modules = [module with { Sha256 = new string('0', 64) }] },
            _ => reference
        };
        fixture.Snapshot = fixture.Snapshot with
        {
            References = shape == "count" ? Enumerable.Repeat(reference, CompilerReferenceLimits.MaximumModuleCount + 1).ToImmutableArray() : [changed]
        };
        var message = shape switch
        {
            "count" => "Too many captured references",
            "null" or "modules" or "aliases" => "Invalid captured reference metadata",
            "multimodule" or "extern" or "interop" => "ordinary single-module",
            "nullmodule" or "zero" or "oversized" => "export size limits",
            "size" => "changed size",
            _ => "changed since verification"
        };
        if (shape is "multimodule" or "extern" or "interop")
        {
            AssertExportRejected<InvalidOperationException>(fixture, message);
        }
        else
        {
            AssertExportRejected<InvalidDataException>(fixture, message);
        }
    }

    [Test]
    public void CompilerBindingErrorsAreNotExported()
    {
        using var fixture = new ExportFixture(DefaultSource.Replace("return value", "return missing", StringComparison.Ordinal));
        AssertExportRejected<InvalidOperationException>(fixture, "without compiler errors");
    }

    [Test]
    public void CallableIdentityAndClauseSpanMustStillMatch()
    {
        using var fixture = new ExportFixture();
        var original = fixture.Claim;
        fixture.Claim = original with { CallableId = "M:Missing.Run(System.Int32)" };
        AssertExportRejected<InvalidOperationException>(fixture, "uniquely matched");
        fixture.Claim = original with { Location = original.Location with { Start = original.Location.Start + 1 } };
        AssertExportRejected<InvalidOperationException>(fixture, "source span");
    }

    [TestCase("public class Api { public int Run(int value) { Contract.Ensures(value > 0); return value; } }")]
    [TestCase("public static class Api { public static int Run<T>(int value) { Contract.Ensures(value > 0); return value; } }")]
    [TestCase("public static class Api { public static async System.Threading.Tasks.Task<int> Run(int value) { Contract.Ensures(value > 0); await System.Threading.Tasks.Task.Yield(); return value; } }")]
    [TestCase("public static class Api { private static int Run(int value) { Contract.Ensures(value > 0); return value; } }")]
    [TestCase("public static class Api { public static void Run(int value) { Contract.Ensures(value > 0); } }")]
    [TestCase("public static class Api { public static int Run(ref int value) { Contract.Ensures(value > 0); return value; } }")]
    [TestCase("public static class Api { public static int Run(string value) { Contract.Ensures(value != null); return 0; } }")]
    [TestCase("internal static class Api { public static int Run(int value) { Contract.Ensures(value > 0); return value; } }")]
    [TestCase("public static class Api<T> { public static int Run(int value) { Contract.Ensures(value > 0); return value; } }")]
    [TestCase("public static class Outer { internal static class Api { public static int Run(int value) { Contract.Ensures(value > 0); return value; } } }")]
    public void UnobservableCallableShapesAreRejected(string declaration)
    {
        ArgumentException.ThrowIfNullOrEmpty(declaration);
        using var fixture = new ExportFixture("using SharpProof.Attributes; " + declaration);
        AssertExportRejected<InvalidOperationException>(fixture, "public nongeneric synchronous static");
    }

    [Test]
    public void BodylessAndAmbiguousPartialDeclarationsCannotBeExported()
    {
        using var expressionBody = new ExportFixture("public static class Api { public static int Run(int value) => value; }");
        AssertExportRejected<InvalidOperationException>(expressionBody, "source span");
        using var partial = new ExportFixture("using SharpProof.Attributes; public static partial class Api { public static partial int Run(int value); public static partial int Run(int value) { Contract.Ensures(value > 0); return value; } }");
        AssertExportRejected<InvalidOperationException>(partial, "uniquely matched");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void EveryInputNeedsExactlyOneReplayValue(bool duplicate)
    {
        using var fixture = new ExportFixture();
        fixture.Result = fixture.Result with { Model = duplicate ? [fixture.Result.Model[0], fixture.Result.Model[0]] : [] };
        AssertExportRejected<InvalidDataException>(fixture, "exactly one value for every input");
    }

    [Test]
    public void UnsupportedRuntimesAndMissingOutputParentsLeaveNoFiles()
    {
        using var unsupported = new ExportFixture(targetFramework: "net7.0");
        AssertExportRejected<InvalidOperationException>(unsupported, "Other test runtimes are not qualified");
        using var missingParent = new ExportFixture();
        missingParent.OutputPath = Path.Combine(missingParent.Root, "missing", "Regression");
        AssertExportRejected<IOException>(missingParent, "parent directory must already exist");
    }

    [Test]
    public void CanceledExportDoesNotEvaluateTheConsumerOrWriteOutput()
    {
        using var fixture = new ExportFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(new Func<Task>(async () => await fixture.ExportAsync(cancellation.Token)));
        Assert.That(Directory.Exists(fixture.OutputPath), Is.False);
        Assert.That(Directory.GetDirectories(fixture.Root, ".sharpproof-export-*"), Is.Empty);
    }

    [Test]
    public void PublicationFailureRemovesTheCompletedTemporaryProject()
    {
        using var fixture = new ExportFixture();
        // Linux permits this path syntactically but cannot publish a directory component over 255 bytes.
        fixture.OutputPath = Path.Combine(fixture.Root, new string('x', 300));
        Assert.ThrowsAsync<PathTooLongException>(new Func<Task>(async () => await fixture.ExportAsync(TestContext.CurrentContext.CancellationToken)));
        Assert.That(Directory.GetDirectories(fixture.Root, ".sharpproof-export-*"), Is.Empty);
        Assert.That(Directory.GetFiles(fixture.Root, "Regression.cs", SearchOption.AllDirectories), Is.Empty);
        Assert.That(File.Exists(fixture.SourcePath), Is.True);
    }

    [TestCase(SpecialType.System_Boolean, "Boolean", "TRUE", "true")]
    [TestCase(SpecialType.System_SByte, "Integer", "-128", "(sbyte)(-128L)")]
    [TestCase(SpecialType.System_Byte, "Integer", "255", "(byte)(255L)")]
    [TestCase(SpecialType.System_Int16, "Integer", "-32768", "(short)(-32768L)")]
    [TestCase(SpecialType.System_UInt16, "Integer", "65535", "(ushort)(65535L)")]
    [TestCase(SpecialType.System_Char, "Integer", "65535", "(char)(65535L)")]
    [TestCase(SpecialType.System_Int32, "Integer", "-2147483648", "(int)(-2147483648L)")]
    [TestCase(SpecialType.System_UInt32, "Integer", "4294967295", "(uint)(4294967295L)")]
    [TestCase(SpecialType.System_Int64, "Integer", "-9223372036854775808", "(long)(-9223372036854775808L)")]
    [TestCase(SpecialType.System_UInt64, "Integer", "18446744073709551615", "(ulong)(18446744073709551615UL)")]
    public void ScalarModelLiteralsRetainTheirExactTypeAndValue(SpecialType type, string kind, string value, string expected)
    {
        ArgumentException.ThrowIfNullOrEmpty(kind);
        ArgumentException.ThrowIfNullOrEmpty(value);
        ArgumentException.ThrowIfNullOrEmpty(expected);
        var compilation = CompanionScaffoldingTests.Compilation("public static class Values { }");
        var symbol = compilation.GetSpecialType(type);
        Assert.That(NUnitExporter.IsScalar(symbol), Is.True);
        var literal = NUnitExporter.ModelLiteral(new() { Kind = kind, Value = value }, symbol);
        Assert.That(literal, Is.EqualTo(expected));
        AssertNoCompilerErrors(compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("public static class Literal { public static object Get() => " + literal + "; }")));
    }

    [TestCase(SpecialType.System_Boolean, "Integer", "1")]
    [TestCase(SpecialType.System_Boolean, "Boolean", "not-bool")]
    [TestCase(SpecialType.System_Int32, "Integer", "1.5")]
    [TestCase(SpecialType.System_Int32, "Boolean", "1")]
    [TestCase(SpecialType.System_Byte, "Integer", "-1")]
    [TestCase(SpecialType.System_Byte, "Integer", "256")]
    [TestCase(SpecialType.System_UInt64, "Integer", "18446744073709551616")]
    public void InvalidReplayLiteralsCannotBeInterpolatedAsSource(SpecialType type, string kind, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(kind);
        ArgumentException.ThrowIfNullOrEmpty(value);
        var compilation = CompanionScaffoldingTests.Compilation("public static class Values { }");
        Assert.Throws<InvalidDataException>(new Action(() => { _ = NUnitExporter.ModelLiteral(new() { Kind = kind, Value = value }, compilation.GetSpecialType(type)); }));
    }

    [Test]
    public void NonScalarTypesCannotUseIntegralReplayLiterals()
    {
        var compilation = CompanionScaffoldingTests.Compilation("public static class Values { }");
        var type = compilation.GetSpecialType(SpecialType.System_String);
        Assert.That(NUnitExporter.IsScalar(type), Is.False);
        Assert.Throws<InvalidOperationException>(new Action(() => { _ = NUnitExporter.ModelLiteral(new() { Kind = "Integer", Value = "1" }, type); }));
        Assert.Throws<InvalidOperationException>(new Action(() => { _ = NUnitExporter.ScalarTypeName(type); }));
        Assert.That(NUnitExporter.ScalarTypeName(compilation.GetSpecialType(SpecialType.System_Boolean)), Is.EqualTo("bool"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExportedRegressionRetainsRecordedSymbolsAcrossBuildConfigurations(bool customSymbols)
    {
        var directive = customSymbols ? "TRACE && AUDIT_REPLAY" : "!DEBUG";
        var source = "using SharpProof.Attributes; public static class Api { public static int Run(int value) { Contract.Ensures(Contract.Result<int>() > 0);\n#if " + directive + "\nreturn value;\n#else\nreturn 1;\n#endif\n} }";
        using var fixture = new ExportFixture(source, targetFramework: "net10.0");
        if (customSymbols)
        {
            fixture.Snapshot = fixture.Snapshot with { SyntaxTrees = [fixture.Snapshot.SyntaxTrees[0] with { PreprocessorSymbols = ["TRACE", "AUDIT_REPLAY"] }] };
        }
        await fixture.ExportAsync(TestContext.CurrentContext.CancellationToken);
        var consumer = new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"), new XElement("AssemblyName", "ExportFixture")),
            new XElement("ItemGroup", new XElement("Reference", new XAttribute("Include", "SharpProof.Attributes"), new XElement("HintPath", typeof(Contract).Assembly.Location)))));
        consumer.Save(fixture.ProjectPath);
        var testProject = Path.Combine(fixture.OutputPath, "SharpProof.Regression.csproj");
        foreach (var configuration in new[] { "Debug", "Release" })
        {
            var tested = await ToolProcess.RunAsync("dotnet", fixture.Root, TestContext.CurrentContext.CancellationToken,
                "test", testProject, "-c", configuration, "--nologo");
            Assert.That(tested.ExitCode, Is.EqualTo(1), tested.Output);
            Assert.That(tested.Output, Does.Contain("Failed:     1"), tested.Output);
        }
        await File.WriteAllTextAsync(fixture.SourcePath, source.Replace("return value;", "return 1;", StringComparison.Ordinal), TestContext.CurrentContext.CancellationToken);
        var fixedTest = await ToolProcess.RunAsync("dotnet", fixture.Root, TestContext.CurrentContext.CancellationToken,
            "test", testProject, "--nologo");
        Assert.That(fixedTest.ExitCode, Is.Zero, fixedTest.Output);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExportRehydratesTopLevelApplications(bool windowed)
    {
        using var fixture = new ExportFixture("using SharpProof.Attributes; int marker = 1; public static class Api { public static int Run(int value) { Contract.Ensures(Contract.Result<int>() > 0); return value; } }");
        fixture.Snapshot = fixture.Snapshot with { Options = fixture.Snapshot.Options with { OutputKind = windowed ? CompilerOutputKind.WindowsApplication : CompilerOutputKind.ConsoleApplication } };
        await fixture.ExportAsync(TestContext.CurrentContext.CancellationToken);
        Assert.That(File.Exists(Path.Combine(fixture.OutputPath, "Regression.cs")), Is.True);
        new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup", new XElement("TargetFramework", "net8.0"), new XElement("AssemblyName", "ExportFixture"),
                new XElement("OutputType", windowed ? "WinExe" : "Exe")),
            new XElement("ItemGroup", new XElement("Reference", new XAttribute("Include", "SharpProof.Attributes"),
                new XElement("HintPath", typeof(Contract).Assembly.Location))))).Save(fixture.ProjectPath);
        var tested = await ToolProcess.RunAsync("dotnet", fixture.Root, TestContext.CurrentContext.CancellationToken,
            "test", Path.Combine(fixture.OutputPath, "SharpProof.Regression.csproj"), "--nologo");
        Assert.That(tested.ExitCode, Is.EqualTo(1), tested.Output);
        Assert.That(tested.Output, Does.Contain("Failed:     1"), tested.Output);
    }

    private static void AssertExportRejected<TException>(ExportFixture fixture, string message) where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentException.ThrowIfNullOrEmpty(message);
        var exception = Assert.ThrowsAsync<TException>(new Func<Task>(async () => await fixture.ExportAsync(TestContext.CurrentContext.CancellationToken)));
        Assert.That(exception!.Message, Does.Contain(message));
        Assert.That(Directory.Exists(fixture.OutputPath), Is.False);
        Assert.That(Directory.GetDirectories(fixture.Root, ".sharpproof-export-*"), Is.Empty);
    }

    private static void AssertNoCompilerErrors(CSharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic =>
            (diagnostic ?? throw new ArgumentNullException(nameof(diagnostic))).Severity == DiagnosticSeverity.Error), Is.Empty);
    }

    internal sealed class ExportFixture : IDisposable
    {
        private static readonly Lazy<ImmutableArray<CompilerReferenceSnapshot>> ReferenceSnapshots = new(CaptureReferences);
        internal string Root { get; }
        internal string ProjectPath { get; }
        internal string SourcePath { get; }
        internal string OutputPath { get; set; }
        internal CSharpCompilation Compilation { get; }
        internal WorkerVerifyResponse Response { get; }
        internal CompilerManifestArtifact Artifact { get; private set; }
        internal EvidenceSet Evidence => new(Response, new WorkerVerifyRequest(), Artifact);
        internal CompilerCompilationSnapshot Snapshot
        {
            get => Artifact.Compilation;
            set => Artifact = Artifact with { Compilation = value ?? throw new ArgumentNullException(nameof(value)) };
        }
        internal WorkerClaimManifestEntry Claim
        {
            get => Response.Manifest.Claims[0];
            set => Response.Manifest.Claims = [value ?? throw new ArgumentNullException(nameof(value))];
        }
        internal WorkerClaimResult Result
        {
            get => Response.ClaimResults[0];
            set => Response.ClaimResults = [value ?? throw new ArgumentNullException(nameof(value))];
        }

        internal ExportFixture(string source = DefaultSource, string targetFramework = "net8.0", string methodName = "Run",
            string? root = null, string projectDirectoryName = "Consumer")
        {
            ArgumentException.ThrowIfNullOrEmpty(source);
            ArgumentException.ThrowIfNullOrEmpty(targetFramework);
            ArgumentException.ThrowIfNullOrEmpty(methodName);
            ArgumentException.ThrowIfNullOrEmpty(projectDirectoryName);
            Root = root ?? Path.Combine(Path.GetTempPath(), "sharpproof-nunit-export-" + Guid.NewGuid().ToString("N"));
            var directory = Path.Combine(Root, projectDirectoryName);
            Directory.CreateDirectory(directory);
            ProjectPath = Path.Combine(directory, "Consumer.csproj");
            SourcePath = Path.Combine(directory, "Api.cs");
            OutputPath = Path.Combine(Root, "Regression");
            // A property-only MSBuild project evaluates without imports, targets, or consumer execution.
            new XDocument(new XElement("Project", new XElement("PropertyGroup", new XElement("AssemblyName", "ExportFixture"),
                new XElement("TargetFramework", targetFramework), new XElement("TargetFrameworks", "")))).Save(ProjectPath);
            File.WriteAllText(SourcePath, source, new UTF8Encoding(false));
            var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp13), SourcePath);
            Compilation = CSharpCompilation.Create("ExportFixture", [tree], ReferenceSnapshots.Value.Select(static reference =>
                MetadataReference.CreateFromFile((reference ?? throw new ArgumentNullException(nameof(reference))).Modules[0].Path)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First(declaration =>
                (declaration ?? throw new ArgumentNullException(nameof(declaration))).Identifier.ValueText == methodName);
            var symbol = (IMethodSymbol)Compilation.GetSemanticModel(tree).GetDeclaredSymbol(method)!;
            var clause = method.Body?.Statements.OfType<ExpressionStatementSyntax>().Select(static statement =>
                (statement ?? throw new ArgumentNullException(nameof(statement))).Expression).OfType<InvocationExpressionSyntax>().FirstOrDefault();
            var claim = new WorkerClaimManifestEntry
            {
                ClaimId = "claim-export",
                CallableId = DocumentationCommentId.CreateDeclarationId(symbol)!,
                Kind = WorkerClaimKind.Postcondition,
                Evidence = WorkerClaimEvidence.DirectClause,
                Location = new() { Path = SourcePath, Start = clause?.SpanStart ?? 0, Length = clause?.Span.Length ?? 0 }
            };
            Response = new()
            {
                Manifest = new() { Claims = [claim] },
                ClaimResults = [new()
                {
                    ClaimId = claim.ClaimId,
                    Outcome = WorkerClaimOutcome.Refuted,
                    Vacuity = WorkerVacuityKind.None,
                    Model = symbol.Parameters.Select(static parameter =>
                    {
                        ArgumentNullException.ThrowIfNull(parameter);
                        return new WorkerModelValue { Variable = "parameter:" + parameter.Ordinal.ToString(CultureInfo.InvariantCulture),
                            Kind = parameter.Type.SpecialType == SpecialType.System_Boolean ? "Boolean" : "Integer",
                            Value = parameter.Type.SpecialType == SpecialType.System_Boolean ? "false" : "0" };
                    }).ToImmutableArray()
                }]
            };
            Artifact = new()
            {
                Manifest = Response.Manifest,
                Compilation = new()
                {
                    ProjectDirectory = directory,
                    AssemblyName = "ExportFixture",
                    TargetFramework = targetFramework,
                    Options = new() { OutputKind = CompilerOutputKind.DynamicallyLinkedLibrary },
                    SyntaxTrees = [new() { Path = SourcePath, TextLength = source.Length, Sha256 = Digest(Encoding.UTF8.GetBytes(source)),
                        LanguageVersion = "CSharp13", Kind = "Regular" }],
                    References = ReferenceSnapshots.Value
                }
            };
        }

        internal Task ExportAsync(CancellationToken cancellationToken)
        {
            return NUnitExporter.ExportAsync(ProjectPath, Evidence, "claim-export", OutputPath, cancellationToken);
        }

        public void Dispose()
        {
            Directory.Delete(Root, recursive: true);
        }

        private static ImmutableArray<CompilerReferenceSnapshot> CaptureReferences()
        {
            var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                .Where(static path => Path.GetFileName(path ?? throw new ArgumentNullException(nameof(path))) is "System.Private.CoreLib.dll" or "System.Runtime.dll" or "netstandard.dll")
                .Append(typeof(Contract).Assembly.Location).Distinct(StringComparer.Ordinal);
            return paths.Select(static path =>
            {
                ArgumentException.ThrowIfNullOrEmpty(path);
                var bytes = File.ReadAllBytes(path);
                return new CompilerReferenceSnapshot { Aliases = ["global"], Modules = [new() { Path = path, Name = Path.GetFileName(path), SizeBytes = bytes.Length, Sha256 = Digest(bytes) }] };
            }).ToImmutableArray();
        }

        private static string Digest(byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            return Convert.ToHexString(SHA256.HashData(bytes));
        }
    }
}
