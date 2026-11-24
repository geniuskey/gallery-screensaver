// .NET Framework lacks the marker type that C# records and init-only setters need.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
