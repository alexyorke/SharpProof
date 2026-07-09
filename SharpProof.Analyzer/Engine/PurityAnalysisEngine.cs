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
        private readonly CompilationPurityService? _purityService;
        private readonly SmtAnalysisService _smtAnalysis;
        private readonly SharpProofAttributeIdentityPolicy _attributePolicy;

        public PurityAnalysisEngine(CompilationPurityService purityService)
        {
            _purityService = purityService ?? throw new ArgumentNullException(nameof(purityService));
            _smtAnalysis = purityService.SmtAnalysis;
            _attributePolicy = purityService.AttributePolicy;
        }

        internal PurityAnalysisEngine(SmtAnalysisService smtAnalysis)
            : this(smtAnalysis, RequiresContractHelpers.OfficialAttributePolicy)
        {
        }

        internal PurityAnalysisEngine(SmtAnalysisService smtAnalysis, SharpProofAttributeIdentityPolicy attributePolicy)
        {
            _smtAnalysis = smtAnalysis ?? throw new ArgumentNullException(nameof(smtAnalysis));
            _attributePolicy = attributePolicy ?? throw new ArgumentNullException(nameof(attributePolicy));
        }


        private static readonly SymbolDisplayFormat _signatureFormat = new SymbolDisplayFormat(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            memberOptions:
                SymbolDisplayMemberOptions.IncludeContainingType |

                SymbolDisplayMemberOptions.IncludeParameters |
                SymbolDisplayMemberOptions.IncludeModifiers,
            parameterOptions:
                SymbolDisplayParameterOptions.IncludeType |
                SymbolDisplayParameterOptions.IncludeParamsRefOut |
                SymbolDisplayParameterOptions.IncludeDefaultValue,



            miscellaneousOptions:
                SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
                SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
                SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
        );


        private static readonly ImmutableList<IPurityRule> _purityRules = Rules.RuleRegistry.GetDefaultRules();

        /// <summary>First registry rule per <see cref="OperationKind"/>; matches former <c>FirstOrDefault</c> over <see cref="_purityRules"/>.</summary>
        private static readonly ImmutableDictionary<OperationKind, IPurityRule> _firstRuleByOperationKind = BuildFirstRuleByOperationKind(_purityRules);

        private static ImmutableDictionary<OperationKind, IPurityRule> BuildFirstRuleByOperationKind(ImmutableList<IPurityRule> rules)
        {
            var builder = ImmutableDictionary.CreateBuilder<OperationKind, IPurityRule>();
            foreach (var rule in rules)
            {
                foreach (var kind in rule.ApplicableOperationKinds)
                {
                    if (!builder.ContainsKey(kind))
                        builder.Add(kind, rule);
                }
            }
            return builder.ToImmutable();
        }


















    }
}
