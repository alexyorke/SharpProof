using System.Collections.Immutable;
using SharpProof.Ir;

namespace SharpProof.Specs;

internal static class ApiSpecReplayCallHost
{
    private const string Int32MathAbsIdentity =
        "M:System.Math.Abs(System.Int32)";

    internal static IrValue? TryInvoke(
        IrFactory factory,
        string callIdentity,
        string witnessIdentifier,
        IrCallInstruction call,
        IrValue? receiver,
        ImmutableArray<IrValue> arguments)
    {
        factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        callIdentity = ArgumentNullGuard.NotNull(
            callIdentity, nameof(callIdentity));
        witnessIdentifier = ArgumentNullGuard.NotNull(
            witnessIdentifier, nameof(witnessIdentifier));
        call = ArgumentNullGuard.NotNull(call, nameof(call));

        if (!string.Equals(
                callIdentity, Int32MathAbsIdentity, StringComparison.Ordinal) ||
            !ApiSpecTable.Default.TryGetByWitnessIdentifier(
                witnessIdentifier, out var template) ||
            template.Target.DocumentationCommentId != Int32MathAbsIdentity ||
            template.Target.ContainingTypeMetadataName != "System.Math" ||
            template.Target.MemberName != "Abs" ||
            !template.Target.IsStatic ||
            template.Target.ParameterTypes.Length != 1 ||
            template.Target.ParameterTypes[0] != IrTypeKind.Integer ||
            template.Target.ResultType != IrTypeKind.Integer ||
            template.Result == null ||
            template.Facets.Throws.Behavior != SpecThrowBehavior.MayThrow ||
            template.Facets.Throws.NormalCompletion == null ||
            receiver != null ||
            call.Receiver != null ||
            call.Target is not { } target ||
            arguments.Length != 1 ||
            arguments[0].Kind != IrValueKind.Integer ||
            arguments[0].Integer < int.MinValue ||
            arguments[0].Integer > int.MaxValue ||
            arguments[0].Integer == int.MinValue)
        {
            return null;
        }

        var member = factory.GetMemberInfo(call.Member);
        if (!member.IsStatic || member.ParameterTypes.Length != 1 ||
            member.ParameterTypes[0] != arguments[0].Type ||
            member.ReturnType != factory.GetVariableInfo(target).Type ||
            factory.GetTypeInfo(member.ParameterTypes[0]).Kind != IrTypeKind.Integer ||
            factory.GetTypeInfo(member.ReturnType).Kind != IrTypeKind.Integer)
        {
            return null;
        }

        return factory.CreateIntegerValue(Math.Abs((int)arguments[0].Integer));
    }
}
