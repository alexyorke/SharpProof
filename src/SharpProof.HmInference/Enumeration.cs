using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.HmInference;

public static partial class Inference
{
    private sealed partial class Session
    {
        private INamedTypeSymbol? iteratorReturn;
        private HmType? iteratorElement;
        private ITypeSymbol? iteratorDeclaredElement;

        private void SetIteratorReturn(SyntaxNode declaration, IMethodSymbol method)
        {
            if (declaration is null)
            { throw new ArgumentNullException(nameof(declaration)); }
            if (method is null)
            { throw new ArgumentNullException(nameof(method)); }
            var hasYield = false;
            foreach (var yielded in declaration.DescendantNodes().OfType<YieldStatementSyntax>())
            {
                foreach (var owner in yielded.Ancestors())
                {
                    if (owner is MethodDeclarationSyntax or LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax)
                    { hasYield |= ReferenceEquals(owner, declaration); break; }
                }
            }
            if (!hasYield)
            { return; }
            if (method.ReturnType is not INamedTypeSymbol named)
            { throw new InferenceException("Unresolved iterator return type."); }
            var ns = named.ContainingNamespace.ToDisplayString();
            if (method.IsAsync ? ns != "System.Collections.Generic" || named.Name is not ("IAsyncEnumerable" or "IAsyncEnumerator") || named.Arity != 1
                : (ns != "System.Collections.Generic" && ns != "System.Collections") || named.Name is not ("IEnumerable" or "IEnumerator") || named.Arity > 1)
            { throw new InferenceException("Unsupported iterator return type."); }
            iteratorReturn = named;
            iteratorDeclaredElement = named.Arity == 1 ? named.TypeArguments[0] : model.Compilation.GetSpecialType(SpecialType.System_Object);
            iteratorElement = named.Arity == 1 ? HmType.Variable() : CSharpTypes.Rigid(iteratorDeclaredElement);
        }

        private HmType IteratorBody(BlockSyntax body)
        {
            if (body is null)
            { throw new ArgumentNullException(nameof(body)); }
            var flow = Scoped(() => Statements(body.Statements.ToArray()));
            if (flow.ReturnType is not null || flow.Breaks || flow.Continues)
            { throw new InferenceException("Invalid iterator exit flow."); }
            return iteratorElement!;
        }

        private HmType WrapIteratorResult()
        {
            var declared = CSharpTypes.Rigid(iteratorReturn!);
            if (iteratorReturn!.Arity == 0)
            { return declared; }
            return declared.Name == CSharpTypes.NullableReference
                ? new(CSharpTypes.NullableReference, CSharpTypes.ReplaceOwnArguments(declared.Arguments[0], iteratorElement!))
                : CSharpTypes.ReplaceOwnArguments(declared, iteratorElement!);
        }

        private static void AddNamedArguments(INamedTypeSymbol named, Dictionary<ITypeParameterSymbol, HmType> mapping)
        {
            if (named is null)
            { throw new ArgumentNullException(nameof(named)); }
            if (mapping is null)
            { throw new ArgumentNullException(nameof(mapping)); }
            for (var owner = named; owner is not null; owner = owner.ContainingType)
            {
                for (var i = 0; i < owner.TypeArguments.Length; i++)
                { mapping[owner.OriginalDefinition.TypeParameters[i]] = CSharpTypes.Rigid(owner.TypeArguments[i], mapping); }
            }
        }

        private HmType EnumerationElement(CommonForEachStatementSyntax loop, ForEachStatementInfo info)
        {
            if (loop is null)
            { throw new ArgumentNullException(nameof(loop)); }
            if (info.ElementType is null || info.ElementType.TypeKind == TypeKind.Dynamic)
            { throw new InferenceException("Dynamic or unresolved enumeration."); }
            if (model.GetTypeInfo(loop.Expression, cancellationToken).Type is IArrayTypeSymbol array && !info.IsAsynchronous)
            {
                var element = HmType.Variable();
                HmType.Unify(Expression(loop.Expression), new("[" + new string(',', array.Rank - 1) + "]", element));
                return element;
            }
            if (info.GetEnumeratorMethod is null || info.GetEnumeratorMethod.ReturnType is not INamedTypeSymbol
                || info.CurrentProperty is null)
            {
                Anchor(loop.Expression);
                return CSharpTypes.Rigid(info.ElementType);
            }
            var mapping = EnumerationMapping(loop, info.GetEnumeratorMethod);
            var returned = info.GetEnumeratorMethod.OriginalDefinition.ReturnType as INamedTypeSymbol
                ?? throw new InferenceException("Unresolved enumerator type.");
            AddNamedArguments(returned, mapping);
            INamedTypeSymbol? ancestor = returned.OriginalDefinition;
            while (ancestor is not null && !SymbolEqualityComparer.Default.Equals(ancestor.OriginalDefinition, info.CurrentProperty.ContainingType.OriginalDefinition))
            { ancestor = ancestor.BaseType; }
            if (ancestor is null)
            {
                foreach (var implemented in returned.OriginalDefinition.AllInterfaces)
                {
                    if (SymbolEqualityComparer.Default.Equals(implemented.OriginalDefinition, info.CurrentProperty.ContainingType.OriginalDefinition))
                    { ancestor = implemented; break; }
                }
            }
            if (ancestor is null)
            { Anchor(loop.Expression); return CSharpTypes.Rigid(info.ElementType); }
            AddNamedArguments(ancestor, mapping);
            if (info.IsAsynchronous)
            {
                if (info.MoveNextMethod is not null)
                { MapEnumerationOwner(returned, info.MoveNextMethod.ContainingType, mapping); }
                BindImplicitAwait(loop, info.MoveNextMethod, mapping, true);
                if (info.DisposeMethod is not null)
                { MapEnumerationOwner(returned, info.DisposeMethod.ContainingType, mapping); }
                BindImplicitAwait(loop, info.DisposeMethod, mapping, false);
            }
            var current = CSharpTypes.Rigid(info.CurrentProperty.OriginalDefinition.Type, mapping);
            if (!info.CurrentConversion.IsIdentity)
            {
                HmType.Unify(current, CSharpTypes.Rigid(info.CurrentProperty.Type));
                return CSharpTypes.Rigid(info.ElementType);
            }
            return current;
        }

        private Flow ForEachFlow(CommonForEachStatementSyntax loop)
        {
            if (loop is null)
            { throw new ArgumentNullException(nameof(loop)); }
            return Scoped(() =>
            {
                var info = model.GetForEachStatementInfo(loop);
                var element = EnumerationElement(loop, info);
                if (loop is ForEachStatementSyntax simple)
                {
                    var iteration = element;
                    var iterationSyntax = simple.Type is RefTypeSyntax referenceType ? referenceType.Type : simple.Type;
                    if (!iterationSyntax.IsVar)
                    {
                        HmType.Unify(element, CSharpTypes.Rigid(info.ElementType!));
                        iteration = CSharpTypes.Rigid(model.GetTypeInfo(iterationSyntax, cancellationToken).Type!);
                    }
                    environment.Add(model.GetDeclaredSymbol(simple, cancellationToken)!, iteration);
                }
                else if (loop is ForEachVariableStatementSyntax deconstructed)
                {
                    BindDeconstruction(deconstructed.Variable, element, info.ElementType!, model.GetDeconstructionInfo(deconstructed));

                }
                return LoopFlow(Branch(loop.Statement));
            });
        }

        private bool PreservesForEachBindings(SyntaxNode before, SyntaxNode after, SemanticModel beforeModel, SemanticModel changedModel, MethodDeclarationSyntax changedMethod, SyntaxTree changedTree)
        {
            if (before is null)
            { throw new ArgumentNullException(nameof(before)); }
            if (after is null)
            { throw new ArgumentNullException(nameof(after)); }
            if (changedModel is null)
            { throw new ArgumentNullException(nameof(changedModel)); }
            if (beforeModel is null)
            { throw new ArgumentNullException(nameof(beforeModel)); }
            if (changedMethod is null)
            { throw new ArgumentNullException(nameof(changedMethod)); }
            if (changedTree is null)
            { throw new ArgumentNullException(nameof(changedTree)); }
            var originals = before.DescendantNodes().OfType<CommonForEachStatementSyntax>().ToArray();
            var candidates = after.DescendantNodes().OfType<CommonForEachStatementSyntax>().ToArray();
            if (originals.Length != candidates.Length)
            { return false; }
            for (var i = 0; i < originals.Length; i++)
            {
                var left = beforeModel.GetForEachStatementInfo(originals[i]);
                var right = changedModel.GetForEachStatementInfo(candidates[i]);
                if (!SameAwaitMethod(left.GetEnumeratorMethod, right.GetEnumeratorMethod, changedMethod, changedTree)
                    || !SameAwaitMethod(left.MoveNextMethod, right.MoveNextMethod, changedMethod, changedTree)
                    || ProtocolMember(left.CurrentProperty) != ProtocolMember(right.CurrentProperty)
                    || !SameAwaitMethod(left.DisposeMethod, right.DisposeMethod, changedMethod, changedTree)
                    || left.IsAsynchronous != right.IsAsynchronous
                    || !SameProtocolConversion(left.ElementConversion, right.ElementConversion)
                    || !SameProtocolConversion(left.CurrentConversion, right.CurrentConversion))
                { return false; }
                if (left.IsAsynchronous && (!SameImplicitAwait(beforeModel, changedModel, originals[i], candidates[i], left.MoveNextMethod, right.MoveNextMethod, changedMethod, changedTree)
                    || !SameImplicitAwait(beforeModel, changedModel, originals[i], candidates[i], left.DisposeMethod, right.DisposeMethod, changedMethod, changedTree)))
                { return false; }
            }
            return true;
        }

        private static string? ProtocolMember(ISymbol? symbol)
        { return symbol is null ? null : symbol.ContainingAssembly.Identity + ":" + symbol.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat); }

        private static bool SameProtocolConversion(Conversion left, Conversion right)
        {
            return left.IsIdentity == right.IsIdentity && left.IsImplicit == right.IsImplicit && left.IsNumeric == right.IsNumeric
            && left.IsReference == right.IsReference && left.IsBoxing == right.IsBoxing && left.IsUnboxing == right.IsUnboxing
            && left.IsNullable == right.IsNullable && ProtocolMember(left.MethodSymbol) == ProtocolMember(right.MethodSymbol);
        }
    }
}
