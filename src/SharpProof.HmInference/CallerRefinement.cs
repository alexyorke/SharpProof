using System.Globalization;
using Microsoft.CodeAnalysis;

namespace SharpProof.HmInference;

public static partial class Inference
{
    private sealed partial class Session
    {
        private readonly List<(HmType Variable, (int Parameter, string Path) Key)> signatureOrigins = [];

        private void RegisterSignatureOrigins(HmType inferred, HmType declared, int parameter, string path, HashSet<(int Parameter, string Path)> pins)
        {
            if (inferred is null)
            { throw new ArgumentNullException(nameof(inferred)); }
            if (declared is null)
            { throw new ArgumentNullException(nameof(declared)); }
            if (parameter < 0)
            { throw new ArgumentOutOfRangeException(nameof(parameter)); }
            if (path is null)
            { throw new ArgumentNullException(nameof(path)); }
            if (pins is null)
            { throw new ArgumentNullException(nameof(pins)); }
            cancellationToken.ThrowIfCancellationRequested();
            if (inferred.IsVariable)
            {
                var key = (parameter, path);
                signatureOrigins.Add((inferred, key));
                if (pins.Contains(key))
                { HmType.Unify(inferred, declared); }
                return;
            }
            if (inferred.Arguments.Length != declared.Arguments.Length)
            { return; }
            for (var i = 0; i < inferred.Arguments.Length; i++)
            { RegisterSignatureOrigins(inferred.Arguments[i], declared.Arguments[i], parameter, path + "/" + i.ToString(CultureInfo.InvariantCulture), pins); }
        }

        private static bool SameRigidSignature(HmType before, HmType after)
        {
            if (before is null)
            { throw new ArgumentNullException(nameof(before)); }
            if (after is null)
            { throw new ArgumentNullException(nameof(after)); }
            before = before.Root;
            after = after.Root;
            if (before.CSharpSymbol is { } original && after.CSharpSymbol is { } changed)
            { return CSharpTypes.SameSignatureType(original, changed); }
            if (before.Name != after.Name || before.NestedTypeName != after.NestedTypeName || before.Arguments.Length != after.Arguments.Length)
            { return false; }
            for (var i = 0; i < before.Arguments.Length; i++)
            { if (!SameRigidSignature(before.Arguments[i], after.Arguments[i])) { return false; } }
            return true;
        }

        private bool CollectSignaturePins(HmType inferred, HmType before, HmType after, int? parameter, HashSet<(int Parameter, string Path)> pins)
        {
            if (inferred is null)
            { throw new ArgumentNullException(nameof(inferred)); }
            if (before is null)
            { throw new ArgumentNullException(nameof(before)); }
            if (after is null)
            { throw new ArgumentNullException(nameof(after)); }
            if (parameter is < 0)
            { throw new ArgumentOutOfRangeException(nameof(parameter)); }
            if (pins is null)
            { throw new ArgumentNullException(nameof(pins)); }
            cancellationToken.ThrowIfCancellationRequested();
            if (SameRigidSignature(before, after))
            { return false; }
            var added = false;
            var direct = false;
            foreach (var origin in signatureOrigins)
            {
                if (ReferenceEquals(origin.Variable, inferred) && (parameter is null || origin.Key.Parameter == parameter))
                { direct = true; added |= pins.Add(origin.Key); }
            }
            if (direct)
            { return added; }
            inferred = inferred.Root;
            before = before.Root;
            after = after.Root;
            if (inferred.IsVariable || before.Name != after.Name || inferred.Arguments.Length != before.Arguments.Length || before.Arguments.Length != after.Arguments.Length)
            {
                var free = inferred.FreeVariables();
                foreach (var origin in signatureOrigins)
                {
                    if ((parameter is null || origin.Key.Parameter == parameter) && free.Contains(origin.Variable.Root))
                    { added |= pins.Add(origin.Key); }
                }
                return added;
            }
            for (var i = 0; i < inferred.Arguments.Length; i++)
            { added |= CollectSignaturePins(inferred.Arguments[i], before.Arguments[i], after.Arguments[i], parameter, pins); }
            return added;
        }

        private void RefineSuppliedSignature(IMethodSymbol before, IMethodSymbol after, HmType[] parameters, HmType result, HashSet<(int Parameter, string Path)> pins)
        {
            if (before is null)
            { throw new ArgumentNullException(nameof(before)); }
            if (after is null)
            { throw new ArgumentNullException(nameof(after)); }
            if (parameters is null)
            { throw new ArgumentNullException(nameof(parameters)); }
            if (result is null)
            { throw new ArgumentNullException(nameof(result)); }
            if (pins is null)
            { throw new ArgumentNullException(nameof(pins)); }
            var originalOffset = before.ReducedFrom is null ? 0 : 1;
            var changedOffset = after.ReducedFrom is null ? 0 : 1;
            if (before.Parameters.Length + originalOffset != parameters.Length || after.Parameters.Length + changedOffset != parameters.Length)
            { return; }
            for (var i = 0; i < parameters.Length; i++)
            {
                var original = i == 0 && originalOffset != 0 ? before.ReceiverType : before.Parameters[i - originalOffset].Type;
                var changed = i == 0 && changedOffset != 0 ? after.ReceiverType : after.Parameters[i - changedOffset].Type;
                if (original is not null && changed is not null)
                { CollectSignaturePins(parameters[i], CSharpTypes.Rigid(original), CSharpTypes.Rigid(changed), i, pins); }
            }
            CollectSignaturePins(result, CSharpTypes.Rigid(before.ReturnType), CSharpTypes.Rigid(after.ReturnType), null, pins);
        }
    }
}
