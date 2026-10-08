using System.Text.RegularExpressions;

namespace MonitorAgent.Shared.Security;

public static partial class LogRedaction
{
    public static string Text(string text) => CredentialFields().Replace(UrlCredentials().Replace(text, "$1[redacted]@"), "$1=[redacted]");
    [GeneratedRegex(@"(?i)(password|pwd|clientsecret|token|accesskey)\s*[=:]\s*(?:""[^""]*""|'[^']*'|[^;\s]+)")]
    private static partial Regex CredentialFields();
    [GeneratedRegex(@"(https?://)[^\s/@]+:[^\s/@]+@", RegexOptions.IgnoreCase)]
    private static partial Regex UrlCredentials();
}
