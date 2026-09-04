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
