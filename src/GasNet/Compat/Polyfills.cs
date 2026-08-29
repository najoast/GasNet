// Compiler polyfills for netstandard2.1 (Unity / Godot targets).
// These types exist in the .NET 7+/5+ BCL; when building against netstandard2.1 the C# compiler
// still emits references to them for `init` accessors and `required` members, so we provide
// internal stand-ins under their well-known metadata names.
#if !NET7_0_OR_GREATER

namespace System.Runtime.CompilerServices;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
internal sealed class RequiredMemberAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Event | AttributeTargets.Parameter | AttributeTargets.ReturnValue, AllowMultiple = false, Inherited = false)]
internal sealed class CompilerFeatureRequiredAttribute : Attribute
{
    public CompilerFeatureRequiredAttribute(string featureName) => FeatureName = featureName;

    public string FeatureName { get; }
    public bool IsOptional { get; init; }
}

internal static class IsExternalInit
{
}

#endif
