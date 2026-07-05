[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SelectorPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

[Console]::InputEncoding = [System.Text.Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

function Convert-ToBase64
{
    param([string]$Text)

    if ($null -eq $Text)
    {
        $Text = ''
    }

    return [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($Text))
}

while (($line = [Console]::In.ReadLine()) -ne $null)
{
    if ([string]::IsNullOrWhiteSpace($line))
    {
        continue
    }

    $line = $line.TrimStart([char]0xFEFF)

    try
    {
        $request = $line | ConvertFrom-Json
        $changedFiles = @($request.changedFiles | ForEach-Object { [string]$_ })
        if ($null -ne $request.workers -and [int]$request.workers -gt 0)
        {
            $output = (& $SelectorPath -ListOnly -Json -NoExit -Workers ([int]$request.workers) -ChangedFile $changedFiles | Out-String).Trim()
        }
        else
        {
            $output = (& $SelectorPath -ListOnly -Json -NoExit -ChangedFile $changedFiles | Out-String).Trim()
        }
        $response = [ordered]@{
            success = $true
            outputBase64 = Convert-ToBase64 $output
            errorBase64 = ''
        }
    }
    catch
    {
        $response = [ordered]@{
            success = $false
            outputBase64 = ''
            errorBase64 = Convert-ToBase64 (($_ | Out-String).Trim())
        }
    }

    [Console]::Out.WriteLine(($response | ConvertTo-Json -Compress))
    [Console]::Out.Flush()
}
