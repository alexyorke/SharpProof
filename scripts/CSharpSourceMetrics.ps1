Set-StrictMode -Version Latest

function Import-SharpProofRoslyn {
    if ($null -ne ('Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree' -as [type])) {
        return
    }

    $sdkVersion = (& dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sdkVersion)) {
        throw 'Could not resolve the active .NET SDK for source metrics.'
    }

    $sdkList = @(& dotnet --list-sdks)
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not enumerate installed .NET SDKs for source metrics.'
    }
    $escapedVersion = [regex]::Escape($sdkVersion)
    $sdkEntry = @($sdkList | Where-Object {
        $_ -match "^$escapedVersion\s+\[(?<root>.+)\]$"
    })
    if ($sdkEntry.Count -ne 1) {
        throw "Could not resolve the installation root for .NET SDK $sdkVersion."
    }
    [void]($sdkEntry[0] -match "^$escapedVersion\s+\[(?<root>.+)\]$")
    $roslynRoot = Join-Path $Matches.root "$sdkVersion\Roslyn\bincore"
    $codeAnalysisPath = Join-Path $roslynRoot 'Microsoft.CodeAnalysis.dll'
    $csharpPath = Join-Path $roslynRoot 'Microsoft.CodeAnalysis.CSharp.dll'
    if (-not (Test-Path -LiteralPath $codeAnalysisPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $csharpPath -PathType Leaf)) {
        throw "The active SDK does not contain the Roslyn compiler at $roslynRoot."
    }

    Add-Type -Path $codeAnalysisPath
    Add-Type -Path $csharpPath
}

Import-SharpProofRoslyn

$script:CSharpSyntaxTreeType =
    'Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree' -as [type]
$script:CSharpSyntaxKindType =
    'Microsoft.CodeAnalysis.CSharp.SyntaxKind' -as [type]
$script:CSharpExpressionSyntaxType =
    'Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax' -as [type]
if ($null -eq $script:CSharpSyntaxTreeType -or
    $null -eq $script:CSharpSyntaxKindType -or
    $null -eq $script:CSharpExpressionSyntaxType) {
    throw 'Roslyn source-metric types were not loaded.'
}

$script:DecisionKinds = [Collections.Generic.HashSet[string]]::new(
    [string[]]@(
        'IfStatement',
        'ForStatement',
        'ForEachStatement',
        'ForEachVariableStatement',
        'WhileStatement',
        'DoStatement',
        'CaseSwitchLabel',
        'CasePatternSwitchLabel',
        'CatchClause',
        'ConditionalExpression',
        'LogicalAndExpression',
        'LogicalOrExpression',
        'CoalesceExpression',
        'SwitchExpressionArm'
    ),
    [StringComparer]::Ordinal)

$script:MemberKinds = [Collections.Generic.HashSet[string]]::new(
    [string[]]@(
        'ClassDeclaration',
        'StructDeclaration',
        'InterfaceDeclaration',
        'RecordDeclaration',
        'RecordStructDeclaration',
        'EnumDeclaration',
        'DelegateDeclaration',
        'MethodDeclaration',
        'ConstructorDeclaration',
        'DestructorDeclaration',
        'PropertyDeclaration',
        'IndexerDeclaration',
        'EventDeclaration',
        'EventFieldDeclaration',
        'FieldDeclaration',
        'OperatorDeclaration',
        'ConversionOperatorDeclaration',
        'EnumMemberDeclaration',
        'LocalFunctionStatement',
        'GetAccessorDeclaration',
        'SetAccessorDeclaration',
        'InitAccessorDeclaration',
        'AddAccessorDeclaration',
        'RemoveAccessorDeclaration'
    ),
    [StringComparer]::Ordinal)

function Get-CSharpSyntaxKindName {
    param(
        [Parameter(Mandatory = $true)]
        $NodeOrToken
    )

    return [Enum]::GetName(
        $script:CSharpSyntaxKindType,
        [int]$NodeOrToken.RawKind)
}

function Measure-CSharpSourceText {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Source,

        [Parameter()]
        [string]$Path = '<memory>'
    )

    $tree = $script:CSharpSyntaxTreeType::ParseText($Source)
    $parseErrors = @($tree.GetDiagnostics() | Where-Object {
        $_.Severity.ToString() -eq 'Error'
    })
    if ($parseErrors.Count -ne 0) {
        throw (
            "$Path contains Roslyn parse errors: " +
            (($parseErrors | ForEach-Object { $_.ToString() }) -join '; '))
    }

    $root = $tree.GetRoot()
    $nodes = @($root.DescendantNodes())
    $tokens = @($root.DescendantTokens() | Where-Object {
        (Get-CSharpSyntaxKindName $_) -ne 'EndOfFileToken'
    })
    $decisionPoints = 0
    $members = 0
    $expressionNodes = 0
    foreach ($node in $nodes) {
        $kind = Get-CSharpSyntaxKindName $node
        if ($script:DecisionKinds.Contains($kind)) {
            $decisionPoints++
        }
        if ($script:MemberKinds.Contains($kind)) {
            $members++
        }
        if ($script:CSharpExpressionSyntaxType.IsAssignableFrom(
                $node.GetType())) {
            $expressionNodes++
        }
    }

    return [pscustomobject]@{
        path = $Path
        syntaxTokens = $tokens.Count
        syntaxNodes = $nodes.Count
        expressionNodes = $expressionNodes
        decisionPoints = $decisionPoints
        members = $members
    }
}
