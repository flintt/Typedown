using System;

namespace Typedown.Core.Utilities
{
    /// <summary>The native bridge belongs to one document, not every URL beginning with its address.</summary>
    public static class WebViewPagePolicy
    {
        public static bool Matches(string address, string expectedAddress)
        {
            if (!Uri.TryCreate(address, UriKind.Absolute, out var actual) ||
                !Uri.TryCreate(expectedAddress, UriKind.Absolute, out var expected) ||
                !string.IsNullOrEmpty(actual.UserInfo) || !string.IsNullOrEmpty(expected.UserInfo))
                return false;

            return string.Equals(actual.Scheme, expected.Scheme, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(actual.IdnHost, expected.IdnHost, StringComparison.OrdinalIgnoreCase) &&
                actual.Port == expected.Port &&
                string.Equals(actual.GetComponents(UriComponents.PathAndQuery, UriFormat.UriEscaped),
                    expected.GetComponents(UriComponents.PathAndQuery, UriFormat.UriEscaped),
                    expected.IsFile ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }

        public static bool CanReceiveMessage(string source, string currentAddress, string expectedAddress) =>
            Matches(source, expectedAddress) && Matches(currentAddress, expectedAddress);
    }
}
