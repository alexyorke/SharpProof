using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Immutable;
using System.Threading;

namespace SharpProof.Analyzer.Engine.Rules
{

    internal partial class ReturnStatementPurityRule : IPurityRule
    {
        private static bool IsConstructionWithEscapingParameters(
            IObjectCreationOperation objectCreationOperation,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (objectCreationOperation.Type is not INamedTypeSymbol namedType ||
                objectCreationOperation.Constructor == null)
            {
                return false;
            }

            foreach (var argument in objectCreationOperation.Arguments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var parameter = argument.Parameter;
                if (parameter == null)
                {
                    continue;
                }

                if (namedType.IsRecord && HasMatchingRecordProperty(namedType, parameter))
                {
                    return true;
                }

                if (RuleAnalysisHelper.ConstructorStoresParameterMatching(
                        objectCreationOperation.Constructor,
                        parameter,
                        semanticModel,
                        cancellationToken,
                        target =>
                            target is IFieldReferenceOperation fieldReference &&
                            IsInstanceMemberOfConstructedType(fieldReference.Field, objectCreationOperation.Constructor.ContainingType) &&
                            IsThisOrImplicitInstance(fieldReference.Instance) ||
                            target is IPropertyReferenceOperation propertyReference &&
                            IsInstanceMemberOfConstructedType(propertyReference.Property, objectCreationOperation.Constructor.ContainingType) &&
                            IsThisOrImplicitInstance(propertyReference.Instance)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsInstanceMemberOfConstructedType(ISymbol member, INamedTypeSymbol constructedType)
        {
            return member is IFieldSymbol { IsStatic: false } or IPropertySymbol { IsStatic: false } &&
                SymbolEqualityComparer.Default.Equals(member.ContainingType.OriginalDefinition, constructedType.OriginalDefinition);
        }

        private static bool IsThisOrImplicitInstance(IOperation? instance)
        {
            var unwrappedInstance = PurityAnalysisEngine.SkipImplicitConversions(instance);
            return unwrappedInstance == null ||
                unwrappedInstance is IInstanceReferenceOperation;
        }

        private static bool HasMatchingRecordProperty(INamedTypeSymbol recordType, IParameterSymbol parameter)
        {
            foreach (var member in recordType.GetMembers())
            {
                if (member is IPropertySymbol property &&
                    string.Equals(property.Name, parameter.Name, System.StringComparison.OrdinalIgnoreCase) &&
                    SymbolEqualityComparer.Default.Equals(property.Type, parameter.Type))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
