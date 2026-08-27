function Assert-SharpProofUniqueJsonProperties {
    param(
        [Parameter(Mandatory = $true)]
        [System.Text.Json.JsonElement]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    if ($Value.ValueKind -eq [System.Text.Json.JsonValueKind]::Array) {
        $index = 0
        foreach ($item in $Value.EnumerateArray()) {
            Assert-SharpProofUniqueJsonProperties `
                -Value $item `
                -Context "$Context[$index]"
            $index++
        }
        return
    }
    if ($Value.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) {
        return
    }

    $names = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($property in $Value.EnumerateObject()) {
        if (-not $names.Add($property.Name)) {
            throw "$Context contains duplicate property '$($property.Name)'."
        }
        Assert-SharpProofUniqueJsonProperties `
            -Value $property.Value `
            -Context "$Context.$($property.Name)"
    }
}
