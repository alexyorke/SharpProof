function Assert-UniqueJsonProperties {
    param(
        [Parameter(Mandatory = $true)]
        [System.Text.Json.JsonElement]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    if ($Value.ValueKind -eq [System.Text.Json.JsonValueKind]::Array) {
        $index = 0
        foreach ($item in $Value.EnumerateArray()) {
            Assert-UniqueJsonProperties $item "$Context[$index]"
            $index++
        }
        return
    }
    if ($Value.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) {
        return
    }

    $names = [Collections.Immutable.ImmutableHashSet[string]]::Empty.WithComparer(
        [StringComparer]::Ordinal).ToBuilder()
    foreach ($property in $Value.EnumerateObject()) {
        if (-not $names.Add($property.Name)) {
            throw "$Context contains duplicate property '$($property.Name)'."
        }
        Assert-UniqueJsonProperties $property.Value `
            "$Context.$($property.Name)"
    }
}

function Assert-SharpProofExactJsonProperties {
    param(
        [Parameter(Mandatory = $true)][string[]]$Actual,
        [Parameter(Mandatory = $true)][string[]]$Expected,
        [Parameter(Mandatory = $true)][string]$Description,
        [switch]$RejectDuplicates
    )

    $actualSnapshot = [Collections.Immutable.ImmutableArray]::CreateRange[string]($Actual)
    $expectedSnapshot = [Collections.Immutable.ImmutableArray]::CreateRange[string]($Expected)
    if (($RejectDuplicates -and
            @($actualSnapshot | Select-Object -Unique).Count -ne $actualSnapshot.Length) -or
        $actualSnapshot.Length -ne $expectedSnapshot.Length -or
        @($actualSnapshot | Where-Object { $expectedSnapshot -cnotcontains $_ }).Count -ne 0) {
        throw "$Description has an unexpected property set."
    }
}
