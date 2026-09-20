using System.Text.Json;

namespace iPhoneMirror.Core;

public enum ConnectionMode
{
    Auto,
    Usb,
    Wifi,
}

public enum MirrorState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Error,
}

public sealed record DeviceInfo(
    string Serial,
    string? Name,
    string Transport,
    bool Trusted,
    bool DeveloperMode,
    bool WifiPaired,
    string? StateError = null)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Serial : Name;

    public string DisplayDetails
    {
        get
        {
            if (StateError is not null) return ErrorCatalog.MessageFor(StateError);
            var state = !Trusted
                ? "Trust required"
                : !DeveloperMode
                    ? "Developer Mode required"
                    : WifiPaired
                        ? "Wi-Fi paired"
                        : "Trusted";
            return $"{Transport.ToUpperInvariant()} · {state}";
        }
    }
}

public sealed record SessionStatus(
    MirrorState State,
    ConnectionMode RequestedConnection,
    string? ActiveConnection,
    string? Serial,
    string? DeviceName,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record WorkerEvent(string Name, JsonElement Data);

public readonly record struct PhonePoint(ushort X, ushort Y);

public sealed record Viewport(double Left, double Top, double Width, double Height);
