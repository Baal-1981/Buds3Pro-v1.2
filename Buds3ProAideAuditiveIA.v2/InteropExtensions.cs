// InteropExtensions.cs
#nullable enable

namespace Buds3ProAideAuditiveIA.v2
{
    internal static class InteropExtensions
    {
        public static string ToClrString(this Java.Lang.String? s) => s?.ToString() ?? string.Empty;
        public static string ToClrString(this Java.Lang.ICharSequence? s) => s?.ToString() ?? string.Empty;
    }
}
