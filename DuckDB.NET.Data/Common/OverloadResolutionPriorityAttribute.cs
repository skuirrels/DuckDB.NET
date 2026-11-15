#if !NET9_0_OR_GREATER
namespace System.Runtime.CompilerServices;

/// <summary>
/// Polyfill for the .NET 9 attribute. The C# compiler reads it by name, so it also applies to
/// callers of the net8.0 build.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
internal sealed class OverloadResolutionPriorityAttribute(int priority) : Attribute
{
    public int Priority { get; } = priority;
}
#endif
