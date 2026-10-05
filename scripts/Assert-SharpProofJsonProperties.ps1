function Assert-UniqueJsonProperties
{
    param(
        [Parameter(Mandatory = $true)]
        [System.Text.Json.JsonElement]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    if ($Value.ValueKind -eq [System.Text.Json.JsonValueKind]::Array)
    {
        $index = 0
        foreach ($item in $Value.EnumerateArray())
        {
            Assert-UniqueJsonProperties $item "$Context[$index]"
            $index++
        }
        return
    }
    if ($Value.ValueKind -ne [System.Text.Json.JsonValueKind]::Object)
    {
        return
    }

    $names = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($property in $Value.EnumerateObject())
    {
        if (-not $names.Add($property.Name))
        {
            throw "$Context contains duplicate property '$($property.Name)'."
        }
        Assert-UniqueJsonProperties $property.Value `
            "$Context.$($property.Name)"
    }
}

function Assert-SharpProofExactJsonProperties
{
    param(
        [Parameter(Mandatory = $true)][string[]]$Actual,
        [Parameter(Mandatory = $true)][string[]]$Expected,
        [Parameter(Mandatory = $true)][string]$Description,
        [switch]$RejectDuplicates
    )

    $actual = @($Actual)
    if (($RejectDuplicates -and
            @($actual | Select-Object -Unique).Count -ne $actual.Count) -or
        $actual.Count -ne $Expected.Count -or
        @($actual | Where-Object { $Expected -cnotcontains $_ }).Count -ne 0) {
        throw "$Description has an unexpected property set."
    }
}
