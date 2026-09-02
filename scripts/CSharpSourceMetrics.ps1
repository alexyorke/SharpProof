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
if ($null -eq $script:CSharpSyntaxTreeType -or
    $null -eq $script:CSharpSyntaxKindType) {
    throw 'Roslyn source-metric types were not loaded.'
}

function Get-CSharpSyntaxKindName {
    param(
        [Parameter(Mandatory = $true)]
        $NodeOrToken
    )

    return [Enum]::GetName(
        $script:CSharpSyntaxKindType,
        [int]$NodeOrToken.RawKind)
}

if ($null -eq ('SharpProof.ScriptSupport.CSharpSourceMetricsEngine' -as [type])) {
    $metricsEngine = @'
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.ScriptSupport
{
    public static class CSharpSourceMetricsEngine
    {
        public static int[] Measure(SyntaxNode root)
        {
            int syntaxTokens = 0;
            foreach (SyntaxToken token in root.DescendantTokens())
            {
                if ((SyntaxKind)token.RawKind != SyntaxKind.EndOfFileToken)
                {
                    syntaxTokens++;
                }
            }

            int syntaxNodes = 0;
            int expressionNodes = 0;
            int decisionPoints = 0;
            int members = 0;
            foreach (SyntaxNode node in root.DescendantNodes())
            {
                syntaxNodes++;
                if (node is ExpressionSyntax)
                {
                    expressionNodes++;
                }

                SyntaxKind kind = (SyntaxKind)node.RawKind;
                switch (kind)
                {
                    case SyntaxKind.IfStatement:
                    case SyntaxKind.ForStatement:
                    case SyntaxKind.ForEachStatement:
                    case SyntaxKind.ForEachVariableStatement:
                    case SyntaxKind.WhileStatement:
                    case SyntaxKind.DoStatement:
                    case SyntaxKind.CaseSwitchLabel:
                    case SyntaxKind.CasePatternSwitchLabel:
                    case SyntaxKind.CatchClause:
                    case SyntaxKind.ConditionalExpression:
                    case SyntaxKind.LogicalAndExpression:
                    case SyntaxKind.LogicalOrExpression:
                    case SyntaxKind.CoalesceExpression:
                    case SyntaxKind.SwitchExpressionArm:
                        decisionPoints++;
                        break;
                }

                switch (kind)
                {
                    case SyntaxKind.ClassDeclaration:
                    case SyntaxKind.StructDeclaration:
                    case SyntaxKind.InterfaceDeclaration:
                    case SyntaxKind.RecordDeclaration:
                    case SyntaxKind.RecordStructDeclaration:
                    case SyntaxKind.EnumDeclaration:
                    case SyntaxKind.DelegateDeclaration:
                    case SyntaxKind.MethodDeclaration:
                    case SyntaxKind.ConstructorDeclaration:
                    case SyntaxKind.DestructorDeclaration:
                    case SyntaxKind.PropertyDeclaration:
                    case SyntaxKind.IndexerDeclaration:
                    case SyntaxKind.EventDeclaration:
                    case SyntaxKind.EventFieldDeclaration:
                    case SyntaxKind.FieldDeclaration:
                    case SyntaxKind.OperatorDeclaration:
                    case SyntaxKind.ConversionOperatorDeclaration:
                    case SyntaxKind.EnumMemberDeclaration:
                    case SyntaxKind.LocalFunctionStatement:
                    case SyntaxKind.GetAccessorDeclaration:
                    case SyntaxKind.SetAccessorDeclaration:
                    case SyntaxKind.InitAccessorDeclaration:
                    case SyntaxKind.AddAccessorDeclaration:
                    case SyntaxKind.RemoveAccessorDeclaration:
                        members++;
                        break;
                }
            }

            return new[]
            {
                syntaxTokens,
                syntaxNodes,
                expressionNodes,
                decisionPoints,
                members
            };
        }
    }
}
'@
    Add-Type `
        -TypeDefinition $metricsEngine `
        -IgnoreWarnings `
        -WarningAction SilentlyContinue `
        -ReferencedAssemblies @(
            [Microsoft.CodeAnalysis.SyntaxNode].Assembly.Location,
            [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree].Assembly.Location)
}

function Measure-CSharpSourceText {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Source,

        [Parameter()]
        [string]$Path = '<memory>',

        [Parameter()]
        $ParseOptions
    )

    $tree = if ($null -eq $ParseOptions) {
        $script:CSharpSyntaxTreeType::ParseText($Source)
    }
    else {
        $script:CSharpSyntaxTreeType::ParseText(
            $Source,
            $ParseOptions,
            $Path)
    }
    $parseErrors = @($tree.GetDiagnostics() | Where-Object {
        $_.Severity.ToString() -eq 'Error'
    })
    if ($parseErrors.Count -ne 0) {
        throw (
            "$Path contains Roslyn parse errors: " +
            (($parseErrors | ForEach-Object { $_.ToString() }) -join '; '))
    }

    $metrics =
        [SharpProof.ScriptSupport.CSharpSourceMetricsEngine]::Measure(
            $tree.GetRoot())

    return [pscustomobject]@{
        path = $Path
        syntaxTokens = $metrics[0]
        syntaxNodes = $metrics[1]
        expressionNodes = $metrics[2]
        decisionPoints = $metrics[3]
        members = $metrics[4]
    }
}

function New-SharpProofCSharpParseOptions {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LanguageVersion,

        [Parameter()]
        [AllowEmptyCollection()]
        [string[]]$PreprocessorSymbols = @()
    )

    $normalizedLanguageVersion = $LanguageVersion.Trim()
    if ([string]::IsNullOrWhiteSpace($normalizedLanguageVersion)) {
        throw 'The evaluated C# language version is blank.'
    }
    $version = [Microsoft.CodeAnalysis.CSharp.LanguageVersion]::Default
    if (-not [Microsoft.CodeAnalysis.CSharp.LanguageVersionFacts]::TryParse(
            $normalizedLanguageVersion,
            [ref]$version)) {
        throw "Unsupported C# language version '$LanguageVersion'."
    }
    $symbols = @(
        $PreprocessorSymbols |
            ForEach-Object { [string]$_ } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Sort-Object -Unique)
    return [Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::Default.
        WithLanguageVersion($version).
        WithPreprocessorSymbols($symbols)
}
