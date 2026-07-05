using Microsoft.CodeAnalysis;
using SearchLib.Smt;

namespace SharpProof.Symbolic.Ir
{
    internal static partial class SymbolicIrLowerer
    {
        private static bool TryGetSymbolType(ISymbol symbol, out ITypeSymbol type)
        {
            switch (symbol)
            {
                case ILocalSymbol local:
                    type = local.Type;
                    return true;
                case IParameterSymbol parameter:
                    type = parameter.Type;
                    return true;
                case IPropertySymbol property:
                    type = property.Type;
                    return true;
                case IFieldSymbol field:
                    type = field.Type;
                    return true;
                default:
                    type = null!;
                    return false;
            }
        }

        private static bool TryGetValueKind(ITypeSymbol type, out SmtValueKind kind)
        {
            if (type.SpecialType == SpecialType.System_Boolean)
            {
                kind = SmtValueKind.Bool;
                return true;
            }

            if (IsIntegerSmtType(type))
            {
                kind = SmtValueKind.Int;
                return true;
            }

            if (type.TypeKind == TypeKind.Dynamic ||
                type.IsReferenceType ||
                SymbolicTypeFacts.IsBuiltInSpanOrMemoryType(type) ||
                IsSupportedTupleCarrierType(type))
            {
                kind = SmtValueKind.Reference;
                return true;
            }

            kind = default;
            return false;
        }

        private static bool IsIntegerSmtType(ITypeSymbol type)
        {
            return type.SpecialType is
                SpecialType.System_Char or
                SpecialType.System_SByte or
                SpecialType.System_Byte or
                SpecialType.System_Int16 or
                SpecialType.System_UInt16 or
                SpecialType.System_Int32 or
                SpecialType.System_UInt32 or
                SpecialType.System_Int64 or
                SpecialType.System_UInt64 ||
                type.TypeKind == TypeKind.Enum ||
                IsBigIntegerType(type);
        }

        private static bool IsSupportedTupleCarrierType(ITypeSymbol type)
        {
            return type is INamedTypeSymbol { IsTupleType: true, TupleElements.Length: > 0 };
        }
    }
}
