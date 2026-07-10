using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.FlowAnalysis;
using System.Collections.Immutable;
using System;
using System.IO;
using System.Globalization;
using SharpProof.Analyzer.Engine.Analysis;
using SharpProof.Analyzer.Engine.Rules;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;
using SearchLib.Smt;
using System.Threading;

namespace SharpProof.Analyzer.Engine
{

    internal partial class PurityAnalysisEngine
    {

        private static SyntaxNode? GetBodySyntaxNode(IMethodSymbol methodSymbol, CancellationToken cancellationToken)
        {

            var declaringSyntaxes = methodSymbol.DeclaringSyntaxReferences;
            foreach (var syntaxRef in declaringSyntaxes)
            {
                var syntaxNode = syntaxRef.GetSyntax(cancellationToken);


                if (syntaxNode is ArrowExpressionClauseSyntax arrowExpressionClauseSyntax &&
                    (arrowExpressionClauseSyntax.Parent is PropertyDeclarationSyntax ||
                     arrowExpressionClauseSyntax.Parent is IndexerDeclarationSyntax))
                {
                    return syntaxNode;
                }

                if (syntaxNode is MethodDeclarationSyntax ||
                    syntaxNode is LocalFunctionStatementSyntax ||
                    syntaxNode is AnonymousFunctionExpressionSyntax ||
                    syntaxNode is AccessorDeclarationSyntax ||
                    syntaxNode is ConstructorDeclarationSyntax ||
                    syntaxNode is OperatorDeclarationSyntax ||
                    syntaxNode is ConversionOperatorDeclarationSyntax)
                {
                    return syntaxNode;
                }
            }
            return null;
        }

    }
}
