#if !NET11_0_OR_GREATER
namespace System.Runtime.CompilerServices;

/// <summary>
/// The compiler marks a <c>union</c> declaration with this attribute. .NET 11 has it; this copy lets the
/// net10.0 build declare unions too.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
internal sealed class UnionAttribute : Attribute;

/// <summary>A union's value, whichever case it is. .NET 11 has it; this copy is for the net10.0 build.</summary>
internal interface IUnion
{
	/// <summary>The value, of one of the union's case types.</summary>
	object? Value { get; }
}
#endif
