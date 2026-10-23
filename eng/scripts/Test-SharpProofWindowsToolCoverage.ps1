[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$InputDirectory,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# This is an installed-package consumer exception to the Linux repository-build
# rule. No source project is restored, built, or tested on the Windows runner.
if (-not $IsWindows -or [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne 'X64') {
    throw 'Native installed-tool coverage requires an actual Windows x64 process.'
}
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
[xml]$versions = Get-Content -LiteralPath (Join-Path $repositoryRoot 'eng/Versions.props') -Raw
$collectorVersion = [string]$versions.Project.PropertyGroup.DotNetCoverageToolVersion
if ($collectorVersion -cnotmatch '\A[0-9]+\.[0-9]+\.[0-9]+\z') { throw 'The coverage collector must have an exact version pin.' }
$InputDirectory = (Resolve-Path -LiteralPath $InputDirectory).Path
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Native coverage output must be a new directory.' }
foreach ($identity in @($env:GITHUB_RUN_ID, $env:GITHUB_RUN_ATTEMPT,
        $env:SHARPPROOF_WINDOWS_TOOL_INPUT_ARTIFACT_ID, $env:SHARPPROOF_WINDOWS_TOOL_INPUT_RUN_ATTEMPT)) {
    if ($identity -cnotmatch '\A[1-9][0-9]*\z') { throw 'Native coverage requires immutable workflow producer identities.' }
}
$producerPath = Join-Path $InputDirectory 'producer.json'
$producer = Get-Content -LiteralPath $producerPath -Raw | ConvertFrom-Json
if ($producer.schemaVersion -ne 1 -or $producer.kind -cne 'sharpproof-windows-tool-coverage-input' -or
    $producer.sourceCommit -cne $env:GITHUB_SHA -or $producer.sourceCommit -cnotmatch '\A[a-f0-9]{40}\z' -or
    $producer.runId -cne $env:GITHUB_RUN_ID -or
    $producer.producerAttempt -cne $env:SHARPPROOF_WINDOWS_TOOL_INPUT_RUN_ATTEMPT -or
    $producer.packageVersion -cne "0.0.0-ci.$($producer.runId).$($producer.producerAttempt)" -or
    $producer.phase -cne 'semantic-extras' -or $producer.module.project -cne 'SharpProof.Tool' -or
    $producer.module.assemblyName -cne 'SharpProof.Tool' -or
    $producer.collector.id -cne 'dotnet-coverage' -or $producer.collector.version -cne $collectorVersion) {
    throw 'The canonical Windows coverage producer receipt has unexpected identity.'
}
$inputPrefix = $InputDirectory.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
function Get-VerifiedInput {
    param([Parameter(Mandatory = $true)][object]$File)
    if ($File.path -cnotmatch '\A(?:feed/)?[A-Za-z0-9.-]+\z' -or $File.sha256 -cnotmatch '\A[a-f0-9]{64}\z') {
        throw 'Coverage input has an unsafe path or hash.'
    }
    $path = [IO.Path]::GetFullPath((Join-Path $InputDirectory $File.path))
    if (-not $path.StartsWith($inputPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $path -PathType Leaf) -or
        (Get-Item -LiteralPath $path).LinkType -or
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $File.sha256) {
        throw 'Coverage input is missing, linked, or does not match its sealed hash.'
    }
    return $path
}
foreach ($name in @('authority', 'package', 'assembly', 'pdb', 'collector')) {
    [void](Get-VerifiedInput $producer.files.$name)
}
$authority = Get-Content -LiteralPath (Get-VerifiedInput $producer.files.authority) -Raw | ConvertFrom-Json
$modules = @($authority.modules | Where-Object { $_.project -ceq 'SharpProof.Tool' })
if ($modules.Count -ne 1 -or $authority.commit -cne $producer.sourceCommit -or
    $modules[0].moduleMvid -cne $producer.module.moduleMvid -or
    $modules[0].pdbCodeViewGuid -cne $producer.module.pdbCodeViewGuid) {
    throw 'Producer files do not describe the same authoritative Tool module.'
}
if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) { throw 'The native consumer requires the runner temporary directory.' }
$scratch = Join-Path ([IO.Path]::GetFullPath($env:RUNNER_TEMP)) ('sharpproof-windows-tool-' + [Guid]::NewGuid().ToString('N'))
$checkoutPrefix = $repositoryRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
if ($scratch.StartsWith($checkoutPrefix, [StringComparison]::OrdinalIgnoreCase) -or
    $scratch.Equals($repositoryRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The installed-tool consumer must run outside the source checkout.'
}
[void][IO.Directory]::CreateDirectory($scratch)
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$previousEnvironment = @{}
foreach ($name in @('DOTNET_CLI_HOME', 'NUGET_PACKAGES', 'DOTNET_NOLOGO', 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_ROLL_FORWARD')) {
    $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
}
try {
    $env:DOTNET_CLI_HOME = Join-Path $scratch 'dotnet-home'
    $env:NUGET_PACKAGES = Join-Path $scratch 'packages'
    $env:DOTNET_NOLOGO = '1'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_ROLL_FORWARD = 'LatestPatch'
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'global.json') -Destination (Join-Path $scratch 'global.json')
    $feed = [Security.SecurityElement]::Escape((Join-Path $InputDirectory 'feed'))
    $config = Join-Path $scratch 'NuGet.config'
    [IO.File]::WriteAllText($config, ('<configuration><packageSources><clear /><add key="sealed" value="{0}" /></packageSources></configuration>' -f $feed), [Text.UTF8Encoding]::new($false))
    $tools = Join-Path $scratch 'tools'
    Push-Location $scratch
    try {
        & dotnet tool install SharpProof.Tool --version $producer.packageVersion --tool-path $tools --configfile $config
        if ($LASTEXITCODE -ne 0) { throw 'Installing the sealed SharpProof.Tool archive failed.' }
        & dotnet tool install dotnet-coverage --version $producer.collector.version --tool-path $tools --configfile $config
        if ($LASTEXITCODE -ne 0) { throw 'Installing the sealed coverage collector archive failed.' }
    }
    finally { Pop-Location }
    $installedAssemblies = @(Get-ChildItem -LiteralPath (Join-Path $tools '.store') -Recurse -Filter 'SharpProof.Tool.dll' -File)
    if ($installedAssemblies.Count -ne 1) { throw 'The installed Tool executable is ambiguous.' }
    $installedAssembly = $installedAssemblies[0].FullName
    $installedPdb = [IO.Path]::ChangeExtension($installedAssembly, '.pdb')
    if ((Get-FileHash -LiteralPath $installedAssembly -Algorithm SHA256).Hash.ToLowerInvariant() -cne $producer.files.assembly.sha256) {
        throw 'Installed Tool executable bytes differ from the canonical Linux authority.'
    }
    if (-not (Test-Path -LiteralPath $installedPdb)) {
        Copy-Item -LiteralPath (Get-VerifiedInput $producer.files.pdb) -Destination $installedPdb
    }
    if ((Get-FileHash -LiteralPath $installedPdb -Algorithm SHA256).Hash.ToLowerInvariant() -cne $producer.files.pdb.sha256) {
        throw 'The installed Tool portable PDB differs from the canonical Linux authority.'
    }
    $consumer = Join-Path $scratch 'consumer'
    [void][IO.Directory]::CreateDirectory($consumer)
    $project = Join-Path $consumer 'Consumer.csproj'
    [IO.File]::WriteAllText($project, '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>', [Text.UTF8Encoding]::new($false))
    $currentSid = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $security = [Security.AccessControl.FileSecurity]::new()
    $security.SetOwner($currentSid)
    $security.SetAccessRuleProtection($true, $false)
    $security.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($currentSid, [Security.AccessControl.FileSystemRights]::FullControl, [Security.AccessControl.AccessControlType]::Allow))
    Set-Acl -LiteralPath $project -AclObject $security
    $beforeAcl = Get-Acl -LiteralPath $project
    $beforeDacl = $beforeAcl.GetSecurityDescriptorSddlForm([Security.AccessControl.AccessControlSections]::Access)
    $beforeOwner = $beforeAcl.GetOwner([Security.Principal.SecurityIdentifier]).Value
    $beforeHash = (Get-FileHash -LiteralPath $project -Algorithm SHA256).Hash
    if (-not $beforeAcl.AreAccessRulesProtected -or $beforeOwner -cne $currentSid.Value) { throw 'The existing consumer project must have a protected current-user DACL and owner.' }
    $settings = Join-Path $scratch 'coverage.config'
    [IO.File]::WriteAllText($settings, @'
<Configuration>
  <CodeCoverage>
    <ModulePaths><Include><ModulePath>.*[\\/]SharpProof\.Tool\.dll$</ModulePath></Include></ModulePaths>
    <SkipAutoProperties>False</SkipAutoProperties>
    <UseVerifiableInstrumentation>True</UseVerifiableInstrumentation>
    <AllowLowIntegrityProcesses>False</AllowLowIntegrityProcesses>
    <CollectFromChildProcesses>True</CollectFromChildProcesses>
    <CollectAspDotNet>False</CollectAspDotNet>
    <EnableStaticManagedInstrumentation>False</EnableStaticManagedInstrumentation>
    <EnableDynamicManagedInstrumentation>True</EnableDynamicManagedInstrumentation>
    <EnableStaticNativeInstrumentation>False</EnableStaticNativeInstrumentation>
    <EnableDynamicNativeInstrumentation>False</EnableDynamicNativeInstrumentation>
  </CodeCoverage>
</Configuration>
'@, [Text.UTF8Encoding]::new($false))
    $binaryPath = Join-Path $OutputDirectory 'windows-tool.coverage'
    $reportPath = Join-Path $OutputDirectory 'windows-tool.cobertura.xml'
    $tool = Join-Path $tools 'dotnet-sharpproof.exe'
    $collector = Join-Path $tools 'dotnet-coverage.exe'
    $arguments = @('init', '--project', 'Consumer.csproj', '--verification', 'portable', '--apply', '--format', 'json')
    Push-Location $consumer
    try {
        $initializerOutput = @(& $collector collect --settings $settings --output $binaryPath --output-format coverage --disable-console-output --nologo $tool @arguments)
        if ($LASTEXITCODE -ne 0) { throw 'Native installed-tool initialization under dynamic coverage failed.' }
    }
    finally { Pop-Location }
    $initialization = ($initializerOutput -join "`n") | ConvertFrom-Json
    $projectChanges = @($initialization.generatedFiles | Where-Object { [IO.Path]::GetFullPath([string]$_.path).Equals($project, [StringComparison]::OrdinalIgnoreCase) })
    if ($initialization.command -cne 'init' -or $initialization.exitCode -ne 0 -or
        $projectChanges.Count -ne 1 -or -not $projectChanges[0].changed -or -not $projectChanges[0].written -or
        (Get-FileHash -LiteralPath $project -Algorithm SHA256).Hash -ceq $beforeHash) {
        throw 'Native initialization did not replace the existing protected project.'
    }
    $afterAcl = Get-Acl -LiteralPath $project
    $afterDacl = $afterAcl.GetSecurityDescriptorSddlForm([Security.AccessControl.AccessControlSections]::Access)
    $afterOwner = $afterAcl.GetOwner([Security.Principal.SecurityIdentifier]).Value
    if (-not $afterAcl.AreAccessRulesProtected -or
        $afterDacl -cne $beforeDacl -or $afterOwner -cne $beforeOwner) {
        # Keep the preservation requirements exact. These bounded descriptors
        # identify owner, control-flag, or ACE drift without printing account
        # names, consumer paths, package contents, or inherited environment.
        $diagnostic = [ordered]@{
            case        = 'existing-protected-project'
            expected    = [ordered]@{
                protected      = [bool]$beforeAcl.AreAccessRulesProtected
                ownerSid       = $beforeOwner
                daclSddl       = $beforeDacl.Substring(0, [Math]::Min(2048, $beforeDacl.Length))
                daclSddlLength = $beforeDacl.Length
                daclSha256     = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($beforeDacl))).ToLowerInvariant()
            }
            actual      = [ordered]@{
                protected      = [bool]$afterAcl.AreAccessRulesProtected
                ownerSid       = $afterOwner
                daclSddl       = $afterDacl.Substring(0, [Math]::Min(2048, $afterDacl.Length))
                daclSddlLength = $afterDacl.Length
                daclSha256     = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($afterDacl))).ToLowerInvariant()
            }
            comparison  = [ordered]@{
                protectionPreserved = $afterAcl.AreAccessRulesProtected -eq $beforeAcl.AreAccessRulesProtected
                daclPreserved       = $afterDacl -ceq $beforeDacl
                ownerPreserved      = $afterOwner -ceq $beforeOwner
            }
            initializer = [ordered]@{
                exitCode           = $initialization.exitCode
                projectChanged     = [bool]$projectChanges[0].changed
                projectWritten     = [bool]$projectChanges[0].written
                generatedFileCount = @($initialization.generatedFiles).Count
            }
        }
        Write-Host ('Native Windows security mismatch: ' + ($diagnostic | ConvertTo-Json -Depth 5 -Compress))
        throw 'Native initialization failed to preserve the protected project DACL and owner.'
    }
    [xml]$projectXml = Get-Content -LiteralPath $project -Raw
    $references = @($projectXml.SelectNodes('/Project/ItemGroup/PackageReference[@Include="SharpProof"]'))
    if ($references.Count -ne 1 -or $references[0].GetAttribute('Version') -cne $producer.packageVersion -or
        $projectXml.SelectSingleNode('/Project/PropertyGroup/SharpProofVerify').InnerText -cne 'false') {
        throw 'The installed Tool did not apply its own exact portable package policy.'
    }
    if (@(Get-ChildItem -LiteralPath $consumer -Recurse -Force -File -Filter '.sharpproof-init-*').Count -ne 0) { throw 'Native initialization leaked staging files.' }
    if ((Get-FileHash -LiteralPath $installedAssembly -Algorithm SHA256).Hash.ToLowerInvariant() -cne $producer.files.assembly.sha256 -or
        (Get-FileHash -LiteralPath $installedPdb -Algorithm SHA256).Hash.ToLowerInvariant() -cne $producer.files.pdb.sha256) {
        throw 'Dynamic coverage changed the installed authoritative executable or symbols.'
    }
    & $collector merge $binaryPath --output $reportPath --output-format cobertura --disable-console-output --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Converting native binary coverage to Cobertura failed.' }
    [xml]$report = Get-Content -LiteralPath $reportPath -Raw
    $classes = @($report.SelectNodes('/coverage/packages/package/classes/class'))
    if ($classes.Count -eq 0) { throw 'Native coverage did not capture the Tool module.' }
    $sourceRoots = @($report.SelectNodes('/coverage/sources/source') | ForEach-Object { $_.InnerText.Replace('\', '/').TrimEnd('/') })
    # Preserve counters, only resolve the canonical Linux PDB's source prefix.
    # Every accepted filename must identify exactly one Tool authority document.
    foreach ($class in $classes) {
        $filename = $class.GetAttribute('filename').Replace('\', '/')
        $candidates = @($filename) + @($sourceRoots | ForEach-Object { $_ + '/' + $filename })
        $documentMatches = @($modules[0].documents | Where-Object {
                $documentPath = [string]$_.path
                @($candidates | Where-Object {
                        $_ -ceq $documentPath -or $_.EndsWith('/' + $documentPath, [StringComparison]::Ordinal)
                    }).Count -gt 0
            })
        if ($documentMatches.Count -ne 1) { throw 'Native coverage source does not map uniquely to a canonical Tool PDB document.' }
        $class.SetAttribute('filename', [string]$documentMatches[0].path)
    }
    $xmlSettings = [Xml.XmlWriterSettings]::new()
    $xmlSettings.Encoding = [Text.UTF8Encoding]::new($false)
    $xmlSettings.Indent = $true
    $xmlSettings.NewLineChars = "`n"
    $writer = [Xml.XmlWriter]::Create($reportPath, $xmlSettings)
    try { $report.Save($writer) } finally { $writer.Dispose() }
    $receipt = [ordered]@{
        schemaVersion         = 1
        kind                  = 'sharpproof-windows-tool-coverage'
        sourceCommit          = $producer.sourceCommit
        runId                 = $env:GITHUB_RUN_ID
        runAttempt            = $env:GITHUB_RUN_ATTEMPT
        producerAttempt       = $producer.producerAttempt
        producerArtifactId    = $env:SHARPPROOF_WINDOWS_TOOL_INPUT_ARTIFACT_ID
        packageVersion        = $producer.packageVersion
        module                = $producer.module
        producerReceiptSha256 = (Get-FileHash -LiteralPath $producerPath -Algorithm SHA256).Hash.ToLowerInvariant()
        files                 = [ordered]@{
            report = [ordered]@{ path = 'windows-tool.cobertura.xml'; sha256 = (Get-FileHash -LiteralPath $reportPath -Algorithm SHA256).Hash.ToLowerInvariant() }
            binary = [ordered]@{ path = 'windows-tool.coverage'; sha256 = (Get-FileHash -LiteralPath $binaryPath -Algorithm SHA256).Hash.ToLowerInvariant() }
        }
        checks                = [ordered]@{
            nativeWindows = $true; architecture = 'X64'; outsideCheckout = $true
            installedAssemblySha256 = $producer.files.assembly.sha256; installedPdbSha256 = $producer.files.pdb.sha256
            assemblyUnmodified = $true; existingProject = $true; protectedDacl = $true; daclPreserved = $true
            ownerPreserved = $true; projectChanged = $true; initializerWritten = $true; noStagingFiles = $true
        }
        command               = [ordered]@{ tool = 'dotnet-sharpproof'; arguments = $arguments }
    }
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'consumer.json'), ($receipt | ConvertTo-Json -Depth 8) + "`n", [Text.UTF8Encoding]::new($false))
    Write-Host 'Native Windows installed-tool initialization preserved the existing protected DACL and owner; coverage is retained for independent Linux validation.'
}
finally {
    foreach ($name in $previousEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name]) }
    # Runner scratch is retained until job teardown; never recursively remove a
    # computed Windows path from a coverage evidence collection script.
}
