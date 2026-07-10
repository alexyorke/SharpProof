using System;

namespace SharpProof.Attributes;

[AttributeUsage(AttributeTargets.All, Inherited = false)]
public sealed class ZeroAllocationsAttribute : Attribute
{
}