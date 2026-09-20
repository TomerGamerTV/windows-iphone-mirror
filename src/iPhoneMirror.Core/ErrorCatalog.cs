namespace iPhoneMirror.Core;

public static class ErrorCatalog
{
    private static readonly IReadOnlyDictionary<string, string> Messages = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["apple_device_support_missing"] = "Apple device support is not available. Install Apple Devices for Windows, then reconnect the iPhone.",
        ["usb_trust_required"] = "Unlock the iPhone and approve Trust This Computer before continuing.",
        ["trust_declined"] = "Trust was declined on the iPhone. Unlock it and approve Trust This Computer before continuing.",
        ["trust_pending"] = "The iPhone is waiting for a response to the Trust This Computer prompt. Unlock the iPhone and answer the prompt.",
        ["iphone_locked"] = "Unlock the iPhone before starting mirroring or setup.",
        ["developer_mode_required"] = "Developer Mode is required. Use Setup to reveal the option, enable it on the iPhone, restart, and confirm Turn On.",
        ["image_required"] = "A compatible developer image is not mounted. Use Setup to prepare it after reviewing the action.",
        ["unsupported_usb_version"] = "This iPhone iOS version does not support the required USB developer service.",
        ["display_service_missing"] = "The current developer image does not expose the required display service.",
        ["display_features_unavailable"] = "The iPhone does not advertise a supported display feature for mirroring.",
        ["wifi_pairing_required"] = "No saved CoreDevice Wi-Fi pairing matches this iPhone. Pair over USB first.",
        ["wifi_unreachable"] = "The paired iPhone could not be reached on the local Wi-Fi network.",
        ["multiple_devices"] = "Several iPhones match. Select one device before connecting.",
        ["stream_timeout"] = "Video stopped arriving. Reconnect after any phone call or network interruption ends.",
        ["stream_ended"] = "The iPhone video stream ended. Reconnect after the phone or network is ready.",
        ["stream_backlog"] = "Video could not be consumed quickly enough. Reconnect to start a clean stream.",
        ["player_disconnected"] = "The embedded video player stopped receiving the stream.",
        ["input_disconnected"] = "Input disconnected. The next fresh click inside the phone screen will reconnect input and continue the tap.",
        ["player_missing"] = "The bundled video player is missing or damaged. Reinstall iPhone Mirror.",
        ["worker_missing"] = "The bundled phone worker is missing or damaged. Reinstall iPhone Mirror.",
        ["worker_crashed"] = "The phone worker stopped unexpectedly. Reconnect to start a clean session.",
        ["connection_failed"] = "The iPhone connection failed. Check the cable or Wi-Fi, trust, Developer Mode, and developer image.",
    };

    public static string MessageFor(string? code) =>
        code is not null && Messages.TryGetValue(code, out var message)
            ? message
            : "The operation could not be completed. Check the iPhone connection and setup state.";
}
