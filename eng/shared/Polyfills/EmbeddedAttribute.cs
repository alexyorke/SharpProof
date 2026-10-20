namespace Microsoft.CodeAnalysis;

// Hides this assembly's polyfills from friend assemblies, which otherwise
// see duplicate framework attribute types through InternalsVisibleTo.
[Embedded]
[AttributeUsage(AttributeTargets.All, Inherited = false)]
internal sealed class EmbeddedAttribute : Attribute
{
}
