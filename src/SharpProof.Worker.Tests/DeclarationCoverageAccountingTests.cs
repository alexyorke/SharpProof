using System.Reflection.Metadata;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;

namespace SharpProof.Worker.Test;

[TestFixture]
[Platform("Linux")]
public sealed class DeclarationCoverageAccountingTests
{
    [Test]
    public void ConstantsHaveNoExecutablePdbPointsWhileReadonlyInitializersDo()
    {
        const string source = """
            public static class Subject
            {
                public const int Literal = 7;
                public const int Alias = Literal;
                public static readonly int RuntimeValue = 7;
                public static int Read() => RuntimeValue;
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(SourceText.From(source, Encoding.UTF8));
        var compilation = CSharpCompilation.Create("ConstantCoverageProbe",
            [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release));
        using var image = new MemoryStream();
        using var symbols = new MemoryStream();
        var emitted = compilation.Emit(image, symbols,
            options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        symbols.Position = 0;
        using var provider = MetadataReaderProvider.FromPortablePdbStream(symbols);
        var reader = provider.GetMetadataReader();
        var executableLines = new HashSet<int>();
        foreach (var handle in reader.MethodDebugInformation)
        {
            foreach (var point in reader.GetMethodDebugInformation(handle).GetSequencePoints())
            {
                if (!point.IsHidden)
                {
                    executableLines.Add(point.StartLine);
                }
            }
        }
        using (Assert.EnterMultipleScope())
        {
            Assert.That(executableLines, Does.Not.Contain(3), "A literal constant has no runtime initializer.");
            Assert.That(executableLines, Does.Not.Contain(4), "A constant alias is resolved at compile time.");
            Assert.That(executableLines, Does.Contain(5), "Readonly field initialization must retain executable coverage.");
            Assert.That(executableLines, Does.Contain(6), "Method execution must retain executable coverage.");
        }
    }

    [Test]
    public async Task ClassifiesOnlyDeclarationTokensAndRetainsExecutableOrUnknownRows()
    {
        var cases = new (string Name, string Declaration, bool Omit)[]
        {
            ("method header", "public int M(int value)\n { return value; }", true),
            ("abstract slot", "public abstract int M(int value);", true),
            ("uninitialized field", "private readonly int value;", true),
            ("initializer", "private int value = Compute();\n private static int Compute() => 1;", false),
            ("constant initializer", "private const int Value = 1;", true),
            ("constant alias", "private const int First = 1, Value = First + 1;", true),
            ("constant expression", "private const int Value = (1 << 10) + 2;", true),
            ("readonly literal initializer", "private static readonly int Value = 1;", false),
            ("constant sharing runtime row", "private const int Value = 1; private int runtime = 2;", false),
            ("local constant", "public int M() { const int value = 1; return value; }", false),
            ("expression body", "public int M() => 1;", false),
            ("inline body", "public int M() { return 1; }", false),
            ("auto accessor", "public int Value { get; set; }", false),
            ("accessor body", "public int Value { get { return 1; } }", false),
            ("property initializer", "public int Value { get; } = 1;", false),
            ("attribute", "[System.Obsolete] public int M()\n { return 1; }", false),
            ("constructor base initializer", "public Subject() : base()\n { }", false),
            ("constructor this initializer", "public Subject() : this(1)\n { }\n private Subject(int value) { }", false),
            ("default parameter", "public int M(int value = 1)\n { return value; }", true),
            ("mixed fields", "private int a, b = 1;", false),
            ("event field", "public event System.Action Changed;", true),
            ("invalid syntax", "public int M(\n { return 1; }", false),
            ("multiline string", "public string M() => \"\"\"\n text\n \"\"\";", false),
            ("primary constructor base", "public class Derived(int value) : Subject(Compute())\n { private static int Compute() => 1; }", false),
            ("multiline primary constructor base", "public class Derived(int value) :\n Subject(Compute())\n { private static int Compute() => 1; }", false)
        };
        var root = TestRepository.FindRoot();
        var temporary = Path.Combine(Path.GetTempPath(), "SharpProofDeclarationsTest." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var documents = cases.Select((item, index) =>
            {
                var path = Path.Combine(temporary, index + ".cs");
                File.WriteAllText(path, "public abstract class Subject\n{\n" + item.Declaration + "\n}\n");
                return new { path = item.Name, sourcePath = path, languageVersion = "preview", preprocessorSymbols = Array.Empty<string>() };
            }).ToArray();
            var request = Path.Combine(temporary, "request.json");
            await File.WriteAllTextAsync(request, JsonSerializer.Serialize(documents));
            const string script = """
                $ErrorActionPreference = 'Stop'
                $documents = @(Get-Content -LiteralPath $env:DECLARATION_REQUEST -Raw | ConvertFrom-Json)
                $result = @(& $env:DECLARATION_READER -RepositoryRoot $env:DECLARATION_ROOT -Documents $documents)
                ConvertTo-Json -InputObject $result -Depth 8 -Compress
                """;
            var invocation = ProcessRunner.CreateStartInfo(root, "pwsh", ["-NoLogo", "-NoProfile", "-EncodedCommand",
                Convert.ToBase64String(Encoding.Unicode.GetBytes(script))]);
            invocation.Environment["DECLARATION_REQUEST"] = request;
            invocation.Environment["DECLARATION_READER"] = Path.Combine(root, "eng/scripts", "Read-SharpProofDeclarationLines.ps1");
            invocation.Environment["DECLARATION_ROOT"] = root;
            var result = await ProcessRunner.RunCapturedAsync(invocation, TestContext.CurrentContext.CancellationToken);
            Assert.That(result.ExitCode, Is.Zero, result.CombinedOutput);
            using var json = JsonDocument.Parse(result.Output);
            using (Assert.EnterMultipleScope())
            {
                foreach (var item in json.RootElement.EnumerateArray())
                {
                    string name = item.GetProperty("path").GetString()!;
                    var omit = item.GetProperty("declarationLines").EnumerateArray().Any(line => line.GetInt32() == 3);
                    Assert.That(omit, Is.EqualTo(cases.Single(c => c.Name == name).Omit), name);
                    if (name == "multiline primary constructor base")
                    {
                        Assert.That(item.GetProperty("declarationLines").EnumerateArray()
                            .Any(line => line.GetInt32() == 4), Is.False, "Primary base initializer arguments retain their own physical row.");
                    }
                }
            }
        }
        finally { Directory.Delete(temporary, true); }
    }

    [Test]
    public async Task RequiresChecksumMatchingModuleCollectorAndNoOwnExecutablePdbCredit()
    {
        const string script = """
            $ErrorActionPreference = 'Stop'
            $ast = [Management.Automation.Language.Parser]::ParseFile($env:DECLARATION_GATE, [ref]$null, [ref]$null)
            foreach ($function in $ast.FindAll({ param($node)
                $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
                $node.Name -in @('Test-QualifiedDeclarationDocument', 'Get-QualifiedDeclarationQueries', 'Test-OmitDeclarationLine')
            }, $true)) { Invoke-Expression $function.Extent.Text }
            $source = [IO.Path]::GetTempFileName()
            try {
                [IO.File]::WriteAllText($source, 'public class Subject {}')
                $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($source))).ToLowerInvariant()
                $document = [pscustomobject]@{ path = 'Subject.cs'; sourceHashAlgorithm = '8829d00f-11b8-4213-878b-770e8597ac16'; sourceHash = $hash }
                $reported = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                [void]$reported.Add('Subject:Subject.cs')
                $qualified = Test-QualifiedDeclarationDocument -Document $document -RecordedDocument $document -AssemblyName Subject -ReportedModuleDocuments $reported -SourcePath $source
                $missingChecksum = Test-QualifiedDeclarationDocument -Document $document -RecordedDocument ([pscustomobject]@{ path = 'Subject.cs' }) -AssemblyName Subject -ReportedModuleDocuments $reported -SourcePath $source
                $missingRecord = Test-QualifiedDeclarationDocument -Document $document -AssemblyName Subject -ReportedModuleDocuments $reported -SourcePath $source
                $wrongModule = Test-QualifiedDeclarationDocument -Document $document -RecordedDocument $document -AssemblyName Other -ReportedModuleDocuments $reported -SourcePath $source
                $emptyCollector = Test-QualifiedDeclarationDocument -Document $document -RecordedDocument $document -AssemblyName Subject -ReportedModuleDocuments ([Collections.Generic.HashSet[string]]::new()) -SourcePath $source
                $module = [pscustomobject]@{project='Subject'; assemblyName='Subject'; moduleMvid='one'; pdbCodeViewGuid='one'; documents=@($document)}
                $other = [pscustomobject]@{project='Other'; assemblyName='Other'; moduleMvid='two'; pdbCodeViewGuid='two'; documents=@()}
                $projects = @{
                    Subject=[pscustomobject]@{compile=@([pscustomobject]@{path='Subject.cs'});parseOptions=[pscustomobject]@{languageVersion='latest';preprocessorSymbols=@()}}
                    Other=[pscustomobject]@{compile=@([pscustomobject]@{path='Subject.cs'});parseOptions=[pscustomobject]@{languageVersion='latest';preprocessorSymbols=@()}}
                }
                $recordedAuthority = [pscustomobject]@{modules=@($module,$other)}
                $singleModule = @(Get-QualifiedDeclarationQueries -Path Subject.cs -SourcePath $source -Modules @($module) -RecordedAuthority $recordedAuthority -Projects $projects -ReportedModuleDocuments $reported).Count -eq 1
                $wrongRecordedAuthority = [pscustomobject]@{modules=@([pscustomobject]@{project='Subject';assemblyName='Subject';moduleMvid='different';pdbCodeViewGuid='one';documents=@($document)})}
                $wrongModuleIdentity = @(Get-QualifiedDeclarationQueries -Path Subject.cs -SourcePath $source -Modules @($module) -RecordedAuthority $wrongRecordedAuthority -Projects $projects -ReportedModuleDocuments $reported).Count -gt 0
                $missingModules = @(Get-QualifiedDeclarationQueries -Path Subject.cs -SourcePath $source -Modules @() -RecordedAuthority $recordedAuthority -Projects $projects -ReportedModuleDocuments $reported).Count -gt 0
                $missingOtherPdb = @(Get-QualifiedDeclarationQueries -Path Subject.cs -SourcePath $source -Modules @($module,$other) -RecordedAuthority $recordedAuthority -Projects $projects -ReportedModuleDocuments $reported).Count -gt 0
                $other.documents=@($document)
                $missingOtherCollector = @(Get-QualifiedDeclarationQueries -Path Subject.cs -SourcePath $source -Modules @($module,$other) -RecordedAuthority $recordedAuthority -Projects $projects -ReportedModuleDocuments $reported).Count -gt 0
                [void]$reported.Add('Other:Subject.cs')
                $bothModules = @(Get-QualifiedDeclarationQueries -Path Subject.cs -SourcePath $source -Modules @($module,$other) -RecordedAuthority $recordedAuthority -Projects $projects -ReportedModuleDocuments $reported).Count -eq 2
                $wrongPdbAuthority = [pscustomobject]@{modules=@([pscustomobject]@{project='Subject';assemblyName='Subject';moduleMvid='one';pdbCodeViewGuid='different';documents=@($document)})}
                $wrongPdbIdentity = @(Get-QualifiedDeclarationQueries -Path Subject.cs -SourcePath $source -Modules @($module) -RecordedAuthority $wrongPdbAuthority -Projects $projects -ReportedModuleDocuments $reported).Count -gt 0
                $duplicateAuthority = [pscustomobject]@{modules=@($module,$module)}
                $duplicateModuleIdentity = @(Get-QualifiedDeclarationQueries -Path Subject.cs -SourcePath $source -Modules @($module) -RecordedAuthority $duplicateAuthority -Projects $projects -ReportedModuleDocuments $reported).Count -gt 0
                $duplicateDocumentModule = [pscustomobject]@{project='Subject';assemblyName='Subject';moduleMvid='one';pdbCodeViewGuid='one';documents=@($document,$document)}
                $duplicateDocuments = @(Get-QualifiedDeclarationQueries -Path Subject.cs -SourcePath $source -Modules @($duplicateDocumentModule) -RecordedAuthority $recordedAuthority -Projects $projects -ReportedModuleDocuments $reported).Count -gt 0
                $noCompileProjects = @{Subject=[pscustomobject]@{compile=@();parseOptions=$projects.Subject.parseOptions}}
                $missingCompile = @(Get-QualifiedDeclarationQueries -Path Subject.cs -SourcePath $source -Modules @($module) -RecordedAuthority $recordedAuthority -Projects $noCompileProjects -ReportedModuleDocuments $reported).Count -gt 0
                $unsupportedDocument = [pscustomobject]@{path='Subject.cs';sourceHashAlgorithm='unknown';sourceHash=$hash}
                $unsupportedChecksum = Test-QualifiedDeclarationDocument -Document $unsupportedDocument -RecordedDocument $unsupportedDocument -AssemblyName Subject -ReportedModuleDocuments $reported -SourcePath $source
                $mismatchedDocument = [pscustomobject]@{path='Subject.cs';sourceHashAlgorithm=$document.sourceHashAlgorithm;sourceHash=('0' * 64)}
                $mismatchedRecordedChecksum = Test-QualifiedDeclarationDocument -Document $document -RecordedDocument $mismatchedDocument -AssemblyName Subject -ReportedModuleDocuments $reported -SourcePath $source
                [IO.File]::WriteAllText($source, 'public class Different {}')
                $badChecksum = Test-QualifiedDeclarationDocument -Document $document -RecordedDocument $document -AssemblyName Subject -ReportedModuleDocuments $reported -SourcePath $source
                $declarations = [Collections.Generic.HashSet[int]]::new()
                [void]$declarations.Add(3)
                $noRange = Test-OmitDeclarationLine -Number 3 -DeclarationLines $declarations -Ranges @()
                $executableUnhit = Test-OmitDeclarationLine -Number 3 -DeclarationLines $declarations -Ranges @([pscustomobject]@{startLine=3;endLine=3;creditLine=3})
                $compilerGeneratedRange = Test-OmitDeclarationLine -Number 3 -DeclarationLines $declarations -Ranges @([pscustomobject]@{startLine=2;endLine=4;creditLine=0})
                $unknownSyntax = Test-OmitDeclarationLine -Number 4 -DeclarationLines $declarations -Ranges @()
                [pscustomobject]@{ wrongPdbIdentity=$wrongPdbIdentity; duplicateModuleIdentity=$duplicateModuleIdentity; duplicateDocuments=$duplicateDocuments; missingCompile=$missingCompile; unsupportedChecksum=$unsupportedChecksum; mismatchedRecordedChecksum=$mismatchedRecordedChecksum; qualified=$qualified; noRange=$noRange; singleModule=$singleModule; bothModules=$bothModules; wrongModuleIdentity=$wrongModuleIdentity; missingModules=$missingModules; missingOtherPdb=$missingOtherPdb; missingOtherCollector=$missingOtherCollector; missingChecksum=$missingChecksum; missingRecord=$missingRecord; wrongModule=$wrongModule; emptyCollector=$emptyCollector; badChecksum=$badChecksum; executableUnhit=$executableUnhit; compilerGeneratedRange=$compilerGeneratedRange; unknownSyntax=$unknownSyntax } | ConvertTo-Json -Compress
            }
            finally { [IO.File]::Delete($source) }
            """;
        var root = TestRepository.FindRoot();
        var invocation = ProcessRunner.CreateStartInfo(root, "pwsh", ["-NoLogo", "-NoProfile", "-EncodedCommand",
            Convert.ToBase64String(Encoding.Unicode.GetBytes(script))]);
        invocation.Environment["DECLARATION_GATE"] = Path.Combine(root, "eng/scripts", "Test-SharpProofCoverage.ps1");
        var result = await ProcessRunner.RunCapturedAsync(invocation, TestContext.CurrentContext.CancellationToken);
        Assert.That(result.ExitCode, Is.Zero, result.CombinedOutput);
        using var json = JsonDocument.Parse(result.Output);
        using (Assert.EnterMultipleScope())
        {
            foreach (var property in json.RootElement.EnumerateObject())
            {
                Assert.That(property.Value.GetBoolean(), Is.EqualTo(property.Name is "qualified" or "noRange" or "singleModule" or "bothModules" or "compilerGeneratedRange"), property.Name);
            }
        }
    }

    [TestCase("valid-only", true, true)]
    [TestCase("valid-no-declarations", true, false)]
    [TestCase("invalid-only", false, false)]
    [TestCase("invalid-then-valid", false, false)]
    [TestCase("valid-then-invalid", false, false)]
    [TestCase("no-qualified-contexts", false, false)]
    public async Task RequiresValidSyntaxInEveryQualifiedParseContext(
        string scenario, bool qualified, bool declaration)
    {
        System.ArgumentNullException.ThrowIfNull(scenario);
        // Explicit classification output after ownership/checksum qualification.
        // Invoke the production aggregation, including its final invalid set
        // removal, so result ordering cannot qualify an invalid parse context.
        const string script = """
            $ErrorActionPreference = 'Stop'
            Set-StrictMode -Version Latest
            $ast = [Management.Automation.Language.Parser]::ParseFile(
                $env:DECLARATION_GATE, [ref]$null, [ref]$null)
            $path = 'Subject.cs'
            $valid = [pscustomobject]@{path=$path;validSyntax=$true;declarationLines=@(3)}
            $invalid = [pscustomobject]@{path=$path;validSyntax=$false;declarationLines=@()}
            $classifications = switch ($env:DECLARATION_CONTEXT_SCENARIO) {
                'valid-only' { @($valid) }
                'valid-no-declarations' { @([pscustomobject]@{path=$path;validSyntax=$true;declarationLines=@()}) }
                'invalid-only' { @($invalid) }
                'invalid-then-valid' { @($invalid,$valid) }
                'valid-then-invalid' { @($valid,$invalid) }
                'no-qualified-contexts' { @() }
                default { throw 'Unexpected context scenario.' }
            }
            $declarationLinesByPath = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
            foreach ($name in @('$qualifiedUnmappedPaths', '$invalidSyntaxPaths')) {
                $assignments = @($ast.FindAll({ param($node)
                    $node -is [Management.Automation.Language.AssignmentStatementAst] -and
                    $node.Left.Extent.Text -ceq $name
                }, $true))
                if ($assignments.Count -ne 1) { throw "Expected one production initialization: $name" }
                . ([scriptblock]::Create($assignments[0].Extent.Text))
            }
            $loops = @($ast.FindAll({ param($node)
                $node -is [Management.Automation.Language.ForEachStatementAst] -and
                $node.Variable.VariablePath.UserPath -ceq 'classification'
            }, $true))
            if ($loops.Count -ne 1) { throw 'Expected one production classification loop.' }
            . ([scriptblock]::Create($loops[0].Extent.Text))
            $cleanup = @($ast.FindAll({ param($node)
                $node -is [Management.Automation.Language.PipelineAst] -and
                $node.Extent.Text.StartsWith('$qualifiedUnmappedPaths.ExceptWith(', [StringComparison]::Ordinal)
            }, $true))
            if ($cleanup.Count -ne 1) { throw 'Expected final production invalid-context removal.' }
            . ([scriptblock]::Create($cleanup[0].Extent.Text))
            $declaration = $declarationLinesByPath.ContainsKey($path) -and $declarationLinesByPath[$path].Contains(3)
            [pscustomobject]@{qualified=$qualifiedUnmappedPaths.Contains($path);declaration=$declaration} |
                ConvertTo-Json -Compress
            """;
        var root = TestRepository.FindRoot();
        var invocation = ProcessRunner.CreateStartInfo(root, "pwsh", ["-NoLogo", "-NoProfile", "-EncodedCommand",
            Convert.ToBase64String(Encoding.Unicode.GetBytes(script))]);
        invocation.Environment["DECLARATION_GATE"] = Path.Combine(root, "eng/scripts", "Test-SharpProofCoverage.ps1");
        invocation.Environment["DECLARATION_CONTEXT_SCENARIO"] = scenario;
        var result = await ProcessRunner.RunCapturedAsync(invocation, TestContext.CurrentContext.CancellationToken);
        Assert.That(result.ExitCode, Is.Zero, result.CombinedOutput);
        using var json = JsonDocument.Parse(result.Output);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(json.RootElement.GetProperty("qualified").GetBoolean(), Is.EqualTo(qualified));
            Assert.That(json.RootElement.GetProperty("declaration").GetBoolean(), Is.EqualTo(declaration));
        }
    }
}
