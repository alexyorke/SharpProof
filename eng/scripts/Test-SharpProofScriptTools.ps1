[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
Assert-SharpProofContainer 'Script-tool regression tests require the canonical Linux container.'
$checker = Join-Path $PSScriptRoot 'Format-Scripts.ps1'
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('sharpproof-script-check-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($fixture)
$encoding = [Text.UTF8Encoding]::new($false)

function Invoke-FixtureCheck {
    param([bool]$ExpectedSuccess, [string]$ExpectedMessage = '', [switch]$Apply)
    $start = [Diagnostics.ProcessStartInfo]::new('pwsh')
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in @('-NoLogo', '-NoProfile', '-File', $checker, '-RepositoryRoot', $fixture)) {
        [void]$start.ArgumentList.Add($argument)
    }
    if (-not $Apply) { [void]$start.ArgumentList.Add('-VerifyNoChanges') }
    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(60000)) {
            $process.Kill($true)
            $process.WaitForExit()
            throw 'Script-tool regression timed out.'
        }
        $output = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
        if (($process.ExitCode -eq 0) -ne $ExpectedSuccess -or
            ($ExpectedMessage -and -not $output.Contains($ExpectedMessage, [StringComparison]::Ordinal))) {
            throw "Unexpected script-tool result: $($output.Substring(0, [Math]::Min(2000, $output.Length)))"
        }
    }
    finally { $process.Dispose() }
}

try {
    $files = ([ordered]@{
            'probe.ps1'           = "Write-Output 'fixture'`n"
            'probe.psm1'          = "function Get-FixtureValue { 1 }`nExport-ModuleMember -Function 'Get-FixtureValue'`n"
            'probe.psd1'          = "@{ Value = 1 }`n"
            'probe.bash'          = "#!/usr/bin/env bash`nprintf '%s\n' fixture`n"
            'utility with spaces' = "#!/usr/bin/env bash`nprintf '%s\n' fixture`n"
        }).AsReadOnly()
    foreach ($entry in $files.GetEnumerator()) {
        [IO.File]::WriteAllText((Join-Path $fixture $entry.Key), $entry.Value, $encoding)
    }
    & git -C $fixture init --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Cannot initialize script fixture.' }
    & git -C $fixture add -- .
    if ($LASTEXITCODE -ne 0) { throw 'Cannot track script fixtures.' }
    Invoke-FixtureCheck -ExpectedSuccess $true -Apply
    Invoke-FixtureCheck -ExpectedSuccess $true -ExpectedMessage 'Checked 3 PowerShell files and 2 shell scripts'
    $cases = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@(
            ([ordered]@{ File = 'probe.ps1'; Text = ''; Message = 'Formatting differs: probe.ps1' }).AsReadOnly(),
            ([ordered]@{ File = 'probe.ps1'; Text = "if(`$true){Write-Output 'fixture'}`n"; Message = 'Formatting differs: probe.ps1' }).AsReadOnly(),
            ([ordered]@{ File = 'probe.ps1'; Text = "`$Error = 'invalid'`n"; Message = 'PSAvoidAssignmentToAutomaticVariable' }).AsReadOnly(),
            ([ordered]@{ File = 'probe.ps1'; Text = "if (`n"; Message = 'PowerShell parse error' }).AsReadOnly(),
            ([ordered]@{ File = 'probe.psm1'; Text = "function Get-FixtureValue{1}`n"; Message = 'Formatting differs: probe.psm1' }).AsReadOnly(),
            ([ordered]@{ File = 'probe.psd1'; Text = "@{Value=1}`n"; Message = 'Formatting differs: probe.psd1' }).AsReadOnly(),
            ([ordered]@{ File = 'probe.bash'; Text = "#!/usr/bin/env bash`nif true;then`nprintf fixture`nfi`n"; Message = 'Shell formatting failed' }).AsReadOnly(),
            ([ordered]@{ File = 'utility with spaces'; Text = "#!/usr/bin/env bash`nprintf '%s\n' `$value`n"; Message = 'SC2086' }).AsReadOnly()
        ))
    foreach ($case in $cases) {
        $path = Join-Path $fixture $case.File
        $original = [IO.File]::ReadAllText($path)
        try {
            [IO.File]::WriteAllText($path, $case.Text, $encoding)
            Invoke-FixtureCheck -ExpectedSuccess $false -ExpectedMessage $case.Message
            if ([IO.File]::ReadAllText($path) -cne $case.Text) { throw 'Verify mode changed fixture source.' }
        }
        finally { [IO.File]::WriteAllText($path, $original, $encoding) }
    }
    # Pinned Arcade bytes are checked, rather than restyled as repository code.
    $commonRoot = Join-Path $fixture 'eng/common'
    [void][IO.Directory]::CreateDirectory($commonRoot)
    $pinnedPath = Join-Path $commonRoot 'build.sh'
    $repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'eng/common/build.sh') -Destination $pinnedPath
    & git -C $fixture add -- eng/common/build.sh
    if ($LASTEXITCODE -ne 0) { throw 'Cannot track pinned bootstrap fixture.' }
    Invoke-FixtureCheck -ExpectedSuccess $true -ExpectedMessage 'Checked 3 PowerShell files and 2 shell scripts'
    [IO.File]::AppendAllText($pinnedPath, "# modified upstream bytes`n", $encoding)
    Invoke-FixtureCheck -ExpectedSuccess $false -ExpectedMessage 'Modified Arcade bootstrap file: eng/common/build.sh'
    & git -C $fixture rm --quiet --force -- eng/common/build.sh
    if ($LASTEXITCODE -ne 0) { throw 'Cannot remove pinned bootstrap fixture.' }
    [void][IO.Directory]::CreateDirectory($commonRoot)
    $unpinnedPath = Join-Path $commonRoot 'unpinned.ps1'
    [IO.File]::WriteAllText($unpinnedPath, "Write-Output 'untrusted'`n", $encoding)
    & git -C $fixture add -- eng/common/unpinned.ps1
    if ($LASTEXITCODE -ne 0) { throw 'Cannot track unpinned bootstrap fixture.' }
    Invoke-FixtureCheck -ExpectedSuccess $false -ExpectedMessage 'Unpinned Arcade bootstrap file: eng/common/unpinned.ps1'
    & git -C $fixture rm --quiet --force -- eng/common/unpinned.ps1
    if ($LASTEXITCODE -ne 0) { throw 'Cannot remove unpinned bootstrap fixture.' }

    $literalPath = Join-Path $fixture 'probe.ps1'
    $literalText = "`$message = @'`nkept" + '  ' + "`n'@`nWrite-Output `$message`n"
    [IO.File]::WriteAllText($literalPath, $literalText, $encoding)
    Invoke-FixtureCheck -ExpectedSuccess $true -Apply
    if (-not [IO.File]::ReadAllText($literalPath).Contains("kept" + '  ' + "`n", [StringComparison]::Ordinal)) {
        throw 'Formatting changed trailing whitespace inside literal data.'
    }
    Invoke-FixtureCheck -ExpectedSuccess $true
    Write-Host 'Script-tool regressions passed: PowerShell parsing/lint/format, shell lint/format, extensionless scripts, spaces in paths, and read-only verification.'
}
finally {
    $resolvedFixture = [IO.Path]::GetFullPath($fixture)
    if (-not $resolvedFixture.StartsWith(
            (Join-Path ([IO.Path]::GetTempPath()) 'sharpproof-script-check-'), [StringComparison]::Ordinal)) {
        throw 'Invalid script-test temporary directory.'
    }
    Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
}
