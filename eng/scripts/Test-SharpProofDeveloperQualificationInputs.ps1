[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
Assert-SharpProofContainer 'Developer qualification input regressions require the canonical container.'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.DeveloperPackageReproduction.psm1') -Force

$checks = 0
$commit = 'a' * 40
$version = '1.2.3-preview.1'
$image = 'ghcr.io/alexyorke/sharpproof@sha256:' + ('b' * 64)
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('sharpproof-developer-inputs-' + [Guid]::NewGuid().ToString('N'))

function Assert-Rejected([scriptblock]$Action, [string]$Message) {
    try { & $Action | Out-Null }
    catch {
        if (-not $_.Exception.Message.Contains($Message, [StringComparison]::Ordinal)) { throw }
        return
    }
    throw "Developer qualification accepted an invalid input: $Message"
}

function Write-EditorArchive([string]$Path, [string]$Id = 'SharpProof.CodeFixes',
    [string]$Version = '1.2.3-preview.1', [string]$Commit = ('a' * 40)) {
    if ([IO.File]::Exists($Path)) { [IO.File]::Delete($Path) }
    $archive = [IO.Compression.ZipFile]::Open($Path, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $writer = [IO.StreamWriter]::new($archive.CreateEntry('SharpProof.CodeFixes.nuspec').Open())
        try {
            $writer.Write('<package><metadata><id>' + $Id + '</id><version>' + $Version +
                '</version><repository type="git" url="https://github.com/alexyorke/SharpProof" commit="' +
                $Commit + '"/></metadata></package>')
        }
        finally { $writer.Dispose() }
    }
    finally { $archive.Dispose() }
}

try {
    [IO.Directory]::CreateDirectory($fixture) | Out-Null
    $ordinary = Get-SharpProofDeveloperQualificationBuildProjects -RepositoryRoot $repositoryRoot
    $combined = Get-SharpProofDeveloperQualificationBuildProjects -RepositoryRoot $repositoryRoot -IncludeConsumerHarness
    $harnessProject = 'src/SharpProof.Package.IntegrationTests/SharpProof.Package.IntegrationTests.csproj'
    if ($ordinary.Length -ne 5 -or $ordinary -ccontains $harnessProject -or $combined.Length -ne 6 -or
        $combined -cnotcontains $harnessProject -or @($ordinary | Where-Object { $combined -cnotcontains $_ }).Count -ne 0 -or
        ($combined -join '|') -cne (($combined | Sort-Object -CaseSensitive -Unique) -join '|')) {
        throw 'The optional combined graph must preserve every ordinary developer root and add only the package harness.'
    }
    $checks++

    $source = Join-Path $fixture 'editor'
    [IO.Directory]::CreateDirectory($source) | Out-Null
    $package = Join-Path $source "SharpProof.CodeFixes.$version.nupkg"
    Write-EditorArchive -Path $package
    $editor = Get-SharpProofPreparedCodeFixesPackage -PackageSource $source -PackageVersion $version -RepositoryCommit $commit
    if ($editor.Path -cne $package -or $editor.Sha256 -cne (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()) {
        throw 'Prepared editor qualification must return the exact supplied archive and hash.'
    }
    $checks++
    foreach ($change in @('identity', 'version', 'commit')) {
        switch ($change) {
            'identity' { Write-EditorArchive -Path $package -Id 'SharpProof.Tool' }
            'version' { Write-EditorArchive -Path $package -Version '9.9.9' }
            'commit' { Write-EditorArchive -Path $package -Commit ('c' * 40) }
        }
        Assert-Rejected { Get-SharpProofPreparedCodeFixesPackage -PackageSource $source -PackageVersion $version -RepositoryCommit $commit } `
            'The prepared editor archive differs from the expected version or source commit.'
        $checks++
    }
    Write-EditorArchive -Path $package
    $extra = Join-Path $source 'unrequested.nupkg'
    [IO.File]::WriteAllText($extra, 'unexpected')
    Assert-Rejected { Get-SharpProofPreparedCodeFixesPackage -PackageSource $source -PackageVersion $version -RepositoryCommit $commit } `
        'The prepared editor source must contain only the expected SharpProof.CodeFixes archive.'
    [IO.File]::Delete($extra)
    $checks++
    Assert-Rejected { Get-SharpProofPreparedCodeFixesPackage -PackageSource $package -PackageVersion $version -RepositoryCommit $commit } `
        'The prepared editor package source must be an ordinary directory.'
    $checks++
    Assert-Rejected { Get-SharpProofPreparedCodeFixesPackage -PackageSource $source -PackageVersion $version -RepositoryCommit 'HEAD' } `
        'Prepared editor qualification requires the expected version and exact source commit.'
    $checks++

    $tokens = $null
    $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile(
        (Join-Path $PSScriptRoot 'Test-SharpProofDeveloperExperience.ps1'), [ref]$tokens, [ref]$errors)
    if ($errors.Count -ne 0) { throw 'Developer qualification fixture must parse.' }
    $guard = @($ast.FindAll({ param($node)
                $node -is [Management.Automation.Language.IfStatementAst] -and
                $node.Clauses[0].Item1.Extent.Text.StartsWith('$preparedDeveloperBuild -and', [StringComparison]::Ordinal)
            }, $true))
    $preparation = @($ast.FindAll({ param($node)
                $node -is [Management.Automation.Language.IfStatementAst] -and
                $node.Clauses[0].Item1.Extent.Text -ceq '$preparedDeveloperBuild' -and
                $node.Clauses[0].Item2.Extent.Text.Contains('Get-SharpProofPreparedCodeFixesPackage', [StringComparison]::Ordinal)
            }, $true))
    $packing = @($ast.FindAll({ param($node)
                $node -is [Management.Automation.Language.ForEachStatementAst] -and
                $node.Condition.Extent.Text -ceq '$developerPackages'
            }, $true))
    if ($guard.Count -ne 1 -or $preparation.Count -ne 1 -or $packing.Count -ne 1) {
        throw 'Expected unique actual prepared-input admission, preparation, and packing blocks.'
    }
    # Recreated statement blocks have no source-file root. Supply that same
    # directory explicitly while retaining their actual admission and commands.
    $guardBody = [scriptblock]::Create($guard[0].Extent.Text)
    $preparationBody = [scriptblock]::Create($preparation[0].Extent.Text.Replace('$PSScriptRoot', '$qualificationScriptRoot'))
    $packingBody = [scriptblock]::Create($packing[0].Extent.Text)

    function Test-PreparationRoute([string]$Route) {
        # The extracted production blocks resolve these named inputs in this
        # fixture scope. Keep the complete invocation context explicit.
        $fixtureValues = [ordered]@{
            qualificationScriptRoot = $PSScriptRoot
            preparedDeveloperBuild  = $Route -cne 'Default'
            PreparedCodeFixesSource = $source
            DeveloperHarness        = 'artifacts/developer-harness'
            PackageSource           = 'nupkgs'
            ToolPackageSource       = 'tool-nupkgs'
            QualificationMode       = 'AnalyzerEnabled'
            PackOnly                = $false
            evidenceRoot            = Join-Path $fixture ('evidence-' + $Route)
            sourceCommit            = $commit
            ConsumerImage           = $image
            toolPackageMode         = 'qualified-artifact'
        }
        switch ($Route) {
            'NoEditor' { $fixtureValues.PreparedCodeFixesSource = '' }
            'NoHarness' { $fixtureValues.DeveloperHarness = '' }
            'NoProduct' { $fixtureValues.PackageSource = '' }
            'NoTool' { $fixtureValues.ToolPackageSource = '' }
            'Fast' { $fixtureValues.QualificationMode = 'FeedbackOnly' }
            'PackOnly' { $fixtureValues.PackOnly = $true }
        }
        foreach ($fixtureValue in $fixtureValues.GetEnumerator()) {
            Set-Variable -Name $fixtureValue.Key -Value $fixtureValue.Value
        }
        if ($Route -cin @('NoEditor', 'NoHarness', 'NoProduct', 'NoTool', 'Fast', 'PackOnly')) {
            Assert-Rejected { . $guardBody } 'Prepared developer qualification requires all exact producer inputs, normal analyzers, and the complete qualification.'
            return
        }
        . $guardBody
        $feed = Join-Path $fixture ('feed-' + $Route)
        [IO.Directory]::CreateDirectory($feed) | Out-Null
        $observed = [Collections.Generic.List[object]]::new()
        function Run([string]$Name, [string]$Executable, [string[]]$Arguments) {
            $observed.Add([ordered]@{ name = $Name; executable = $Executable; arguments = $Arguments })
            if ($Route -ceq 'HarnessFailure' -and $Name -ceq 'developer-unit-tests') { throw 'harness rejected' }
        }
        if ($Route -ceq 'HarnessFailure') {
            Assert-Rejected { . $preparationBody } 'harness rejected'
            if ($observed.Count -ne 1) { throw 'Harness rejection must prevent even the Templates restore.' }
            return
        }
        . $preparationBody
        . $packingBody
        if ($Route -ceq 'Default') {
            if (($observed.name -join '|') -cne 'pack-CodeFixes|pack-Templates') {
                throw 'Ordinary qualified-tool developer packing changed.'
            }
            return
        }
        if (($observed.name -join '|') -cne 'developer-unit-tests|restore-Templates|pack-Templates' -or
            $observed[0].executable -cne 'pwsh' -or
            $observed[0].arguments -cnotcontains (Join-Path $PSScriptRoot 'Invoke-SharpProofDeveloperHarnessTests.ps1') -or
            $observed[0].arguments -cnotcontains $DeveloperHarness -or $observed[0].arguments -cnotcontains $feed -or
            $observed[1].arguments[0] -cne 'restore' -or
            $observed[1].arguments[1] -cne 'src/SharpProof.Templates/SharpProof.Templates.csproj' -or
            $observed[1].arguments -cnotcontains '--locked-mode' -or
            $observed[1].arguments -cnotcontains '-p:Configuration=Release' -or
            $observed[2].arguments -cnotcontains '--no-build' -or $observed[2].arguments -cnotcontains '--no-restore' -or
            $observed[2].arguments -cnotcontains "-p:SharpProofConsumerImage=$image" -or
            $observed[2].arguments -cnotcontains "-p:RepositoryCommit=$commit" -or
            $observed[2].arguments -cnotcontains "-p:SourceRevisionId=$commit" -or
            (Get-FileHash -LiteralPath (Join-Path $feed ([IO.Path]::GetFileName($package))) -Algorithm SHA256).Hash.ToLowerInvariant() -cne $editor.Sha256) {
            throw 'Prepared qualification must execute the authenticated tests before only restoring and packing image-bound Templates.'
        }
    }
    foreach ($route in @('Default', 'Prepared', 'HarnessFailure', 'NoEditor', 'NoHarness', 'NoProduct', 'NoTool', 'Fast', 'PackOnly')) {
        Test-PreparationRoute $route
        $checks++
    }
}
finally {
    if ([IO.Directory]::Exists($fixture)) { [IO.Directory]::Delete($fixture, $true) }
}
Write-Host "Developer qualification input regressions passed ($checks controls)."
