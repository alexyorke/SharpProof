using System.Text;
using System.Text.Json;
using NUnit.Framework;

namespace SharpProof.Worker.Test;

[TestFixture]
[Platform("Linux")]
public sealed class LinkedProductionCoverageTests
{
    // COVERAGE-LINKED-MODULE-001: these are explicit post-authentication unit
    // inputs. They execute the production accumulator, report merge and totals;
    // they do not stand in for the end-to-end inventory authentication gate.
    [Test]
    public async Task CountsLinkedStartsSeparatelyForEveryProductionAssembly()
    {
        using var result = await RunAsync("ordinary");
        Assert.That(result.RootElement.GetProperty("error").ValueKind, Is.EqualTo(JsonValueKind.Null));
        var rows = result.RootElement.GetProperty("projects").EnumerateArray().ToArray();
        Assert.That(rows, Has.Length.EqualTo(2));
        foreach (var row in rows)
        {
            Assert.That(row.GetProperty("coverableLines").GetInt32(), Is.EqualTo(3), row.ToString());
            Assert.That(row.GetProperty("coveredLines").GetInt32(), Is.EqualTo(2), row.ToString());
            Assert.That(row.GetProperty("passed").GetBoolean(), Is.False, row.ToString());
        }
        Assert.That(result.RootElement.GetProperty("aggregateCovered").GetInt32(), Is.EqualTo(4));
        Assert.That(result.RootElement.GetProperty("aggregateCoverable").GetInt32(), Is.EqualTo(6));
        Assert.That(result.RootElement.GetProperty("sharedA10").GetInt32(), Is.EqualTo(7));
        Assert.That(result.RootElement.GetProperty("sharedB10").GetInt32(), Is.Zero);
    }

    [TestCase("foreign-source", "ProductionB:SharpProof.Frontend/CompilationModelProvider.cs")]
    [TestCase("foreign-range", "ProductionB:eng/RoslynSymbolFacts.cs:20")]
    public async Task RejectsEvidenceBelongingOnlyToAnotherProductionModule(string scenario, string identity)
    {
        using var result = await RunAsync(scenario);
        Assert.That(result.RootElement.GetProperty("error").GetString(),
            Does.Contain("authenticated"), scenario);
        Assert.That(result.RootElement.GetProperty("error").GetString(), Does.Contain(identity));
    }

    private static async Task<JsonDocument> RunAsync(string scenario)
    {
        const string script = """
            $ErrorActionPreference = 'Stop'
            Set-StrictMode -Version Latest
            $repositoryRoot = $env:SHARPPROOF_LINKED_ROOT
            . (Join-Path $repositoryRoot 'scripts/Resolve-SharpProofSourceDocument.ps1')
            Import-Module (Join-Path $repositoryRoot 'scripts/SharpProof.ContainerExecution.psm1') -Force
            $tokens = $null; $parseErrors = $null
            $ast = [Management.Automation.Language.Parser]::ParseFile(
                (Join-Path $repositoryRoot 'scripts/Test-SharpProofCoverage.ps1'),
                [ref]$tokens, [ref]$parseErrors)
            if ($parseErrors.Count -ne 0) { throw 'Coverage script did not parse.' }
            foreach ($function in $ast.FindAll({ param($node)
                $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
                $node.Name -in @('Test-CoverageFloor', 'Get-OrdinalSortedUniqueStrings')
            }, $true)) { Invoke-Expression $function.Extent.Text }
            $statements = @($ast.EndBlock.Statements)
            function Invoke-ProductionBlock([string]$First, [string]$Until) {
                $active = $false
                foreach ($statement in $statements) {
                    if ($statement.Extent.Text.StartsWith($Until, [StringComparison]::Ordinal)) { break }
                    if ($statement.Extent.Text.StartsWith($First, [StringComparison]::Ordinal)) { $active = $true }
                    if ($active) { . ([scriptblock]::Create($statement.Extent.Text)) }
                }
                if (-not $active) { throw "Production block missing: $First" }
            }
            $shared = 'eng/RoslynSymbolFacts.cs'
            $ownA = 'SharpProof.Frontend/CompilationModelProvider.cs'
            $ownB = 'SharpProof.Worker/ArtifactValidator.cs'
            function New-Document([string]$Path, [int[]]$Starts) {
                [pscustomobject]@{ path = $Path; sequencePoints = @($Starts)
                    sequencePointRanges = @($Starts | ForEach-Object {
                        [pscustomobject]@{ startLine = $_; endLine = $_ }
                    }) }
            }
            $projectsInput = @(
                [pscustomobject]@{ name = 'A'; assemblyName = 'ProductionA'
                    projectPath = 'SharpProof.Frontend/SharpProof.Frontend.csproj'
                    compile = @([pscustomobject]@{path=$ownA;generated=$false}, [pscustomobject]@{path=$shared;generated=$false}) },
                [pscustomobject]@{ name = 'B'; assemblyName = 'ProductionB'
                    projectPath = 'SharpProof.Worker/SharpProof.Worker.csproj'
                    compile = @([pscustomobject]@{path=$ownB;generated=$false}, [pscustomobject]@{path=$shared;generated=$false}) }
            )
            $recomputedAuthority = [pscustomobject]@{ commit = ('a' * 40); projects = $projectsInput
                modules = @(
                    [pscustomobject]@{ project='A'; assemblyName='ProductionA'; moduleMvid='a'; pdbCodeViewGuid='a'
                        documents=@((New-Document $ownA @(10)), (New-Document $shared @(10,20))) },
                    [pscustomobject]@{ project='B'; assemblyName='ProductionB'; moduleMvid='b'; pdbCodeViewGuid='b'
                        documents=@((New-Document $ownB @(10)), (New-Document $shared @(10,30))) }
                ) }
            $baseline = [pscustomobject]@{ projects = [pscustomobject]@{A=100;B=100} }
            $resolvedCoverageRoot = Join-Path ([IO.Path]::GetTempPath()) ([Guid]::NewGuid().ToString('N'))
            [void][IO.Directory]::CreateDirectory($resolvedCoverageRoot)
            try {
                # Dot-source so the actual production variables retain their normal scope.
                . Invoke-ProductionBlock '$authorityProjectsByName =' '$reportFiles ='
                $extra = switch ($env:SHARPPROOF_LINKED_SCENARIO) {
                    'foreign-source' { "<class filename='$ownA'><lines><line number='10' hits='9'/></lines></class>" }
                    'foreign-range' { "<class filename='$shared'><lines><line number='20' hits='9'/></lines></class>" }
                    default { '' }
                }
                $xml = @"
            <coverage><sources/><packages>
            <package name='ProductionA'><classes>
            <class filename='$ownA'><lines><line number='10' hits='1'/></lines></class>
            <class filename='$shared'><lines><line number='10' hits='7'/><line number='10' hits='1'/><line number='20' hits='0'/></lines></class>
            </classes></package>
            <package name='ProductionB'><classes>
            <class filename='$ownB'><lines><line number='10' hits='1'/></lines></class>
            <class filename='$shared'><lines><line number='10' hits='0'/><line number='30' hits='1'/></lines></class>$extra
            </classes></package>
            <package name='TestOnly'><classes><class filename='$shared'><lines><line number='20' hits='99'/></lines></class></classes></package>
            </packages><sharpProofAuthority schemaVersion='1' commit='$($recomputedAuthority.commit)' modules='$expectedModuleIdentityText'/></coverage>
            "@
                $reportPath = Join-Path $resolvedCoverageRoot 'unit.cobertura.xml'
                [IO.File]::WriteAllText($reportPath, $xml)
                # Repeat the report too: duplicate observations must not add obligations.
                $reports = @((Get-Item $reportPath), (Get-Item $reportPath))
                . Invoke-ProductionBlock '$reportFiles =' '$aggregatePercent ='
                $keyA = if ($lineHits.ContainsKey('ProductionA:' + $shared)) { 'ProductionA:' + $shared } else { $shared }
                $keyB = if ($lineHits.ContainsKey('ProductionB:' + $shared)) { 'ProductionB:' + $shared } else { $shared }
                [pscustomobject]@{error=$null;projects=@($projects);aggregateCovered=$aggregateCovered
                    aggregateCoverable=$aggregateCoverable;sharedA10=$lineHits[$keyA][10]
                    sharedB10=$lineHits[$keyB][10]} | ConvertTo-Json -Depth 6 -Compress
            }
            catch { [pscustomobject]@{error=$_.Exception.Message} | ConvertTo-Json -Compress }
            finally { [IO.Directory]::Delete($resolvedCoverageRoot, $true) }
            """;
        var root = TestRepository.FindRoot();
        var invocation = ProcessRunner.CreateStartInfo(root, "pwsh", [
            "-NoLogo", "-NoProfile", "-EncodedCommand",
            Convert.ToBase64String(Encoding.Unicode.GetBytes(script))]);
        invocation.Environment["SHARPPROOF_LINKED_ROOT"] = root;
        invocation.Environment["SHARPPROOF_LINKED_SCENARIO"] = scenario;
        var result = await ProcessRunner.RunCapturedAsync(invocation, TestContext.CurrentContext.CancellationToken);
        Assert.That(result.ExitCode, Is.Zero, result.CombinedOutput);
        return JsonDocument.Parse(result.Output);
    }
}
