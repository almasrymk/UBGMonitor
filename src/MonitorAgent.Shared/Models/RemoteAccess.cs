namespace MonitorAgent.Shared.Models;
public sealed record RemoteAccessStatus(bool Enabled, bool AllowAdministration, bool OpenFirewall, bool HasViewerKey, bool HasAdminKey, string? Fingerprint);
public sealed record RemoteAccessAction(string Action, bool Enabled = false, bool AllowAdministration = false, bool OpenFirewall = false, string? Pfx = null, string? PfxPassword = null);
public sealed record RemoteAccessResult(RemoteAccessStatus Status, string? Key = null);
