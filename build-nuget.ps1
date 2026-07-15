param(
	[string]$Configuration = 'Release',
	[string]$OutputDir = 'artifacts/nuget',
	[string]$InstallProject = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSCommandPath
$dotnetWrapper = Join-Path $repoRoot 'scripts\Invoke-SharpProofDotnet.ps1'

Push-Location $repoRoot
try {
	# Ensure output directory exists
	$outFull = if ([System.IO.Path]::IsPathRooted($OutputDir)) {
		$OutputDir
	}
	else {
		Join-Path $repoRoot $OutputDir
	}
	New-Item -ItemType Directory -Force -Path $outFull | Out-Null
	$stagingDir = Join-Path $outFull ('.staging-' + [Guid]::NewGuid().ToString('N'))
	New-Item -ItemType Directory -Path $stagingDir | Out-Null

	try {
		Write-Host "Building solution ($Configuration)..." -ForegroundColor Cyan
		& $dotnetWrapper -DotnetArgs @('build', '-c', $Configuration)
		if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }

		Write-Host "Packing NuGet packages to staging directory $stagingDir" -ForegroundColor Cyan
		$packageProjects = @((Get-Content -LiteralPath (Join-Path $repoRoot 'scripts\package-projects.json') -Raw | ConvertFrom-Json).projects)
		foreach ($packageProject in $packageProjects) {
			& $dotnetWrapper -DotnetArgs @(
				'pack', $packageProject, '-c', $Configuration, '-o', $stagingDir, '--no-build')
			if ($LASTEXITCODE -ne 0) { throw "dotnet pack failed with exit code $LASTEXITCODE" }
		}

		$stagedPackages = @(Get-ChildItem -Path $stagingDir -Filter *.nupkg -File | Sort-Object Name)
		if ($stagedPackages.Count -ne $packageProjects.Count) {
			throw "Expected $($packageProjects.Count) NuGet packages in $stagingDir, but found $($stagedPackages.Count)."
		}

		$publishedNames = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
		foreach ($package in $stagedPackages) {
			[void]$publishedNames.Add($package.Name)
			Copy-Item -LiteralPath $package.FullName -Destination (Join-Path $outFull $package.Name) -Force
		}

		Get-ChildItem -Path $outFull -Filter *.nupkg -File -ErrorAction SilentlyContinue |
			Where-Object { -not $publishedNames.Contains($_.Name) } |
			Remove-Item -Force
	}
	finally {
		$resolvedOutput = [System.IO.Path]::GetFullPath($outFull).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
		$resolvedStaging = [System.IO.Path]::GetFullPath($stagingDir)
		if (-not $resolvedStaging.StartsWith($resolvedOutput, [StringComparison]::OrdinalIgnoreCase)) {
			throw "Refusing to remove staging directory outside output root: $resolvedStaging"
		}

		if (Test-Path -LiteralPath $resolvedStaging) {
			Remove-Item -LiteralPath $resolvedStaging -Recurse -Force
		}
	}

	$packages = @(Get-ChildItem -Path $outFull -Filter *.nupkg -File | Sort-Object Name)

	Write-Host "Built packages:" -ForegroundColor Green
	$packages | ForEach-Object { Write-Host " - $($_.FullName)" -ForegroundColor Green }

	if ($InstallProject) {
		$projPath = Resolve-Path $InstallProject
		Write-Host "Installing SharpProof package from local source into project: $projPath" -ForegroundColor Cyan
		# Install the main analyzer package (includes Attributes for NuGet consumption)
		& $dotnetWrapper -DotnetArgs @('add', "$projPath", 'package', 'SharpProof', '--source', "$outFull")
		if ($LASTEXITCODE -ne 0) { throw "dotnet add failed with exit code $LASTEXITCODE" }
	}
}
finally {
	Pop-Location
}
