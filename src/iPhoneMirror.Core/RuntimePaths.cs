namespace iPhoneMirror.Core;

public sealed class RuntimePaths
{
    public RuntimePaths(string? localAppData = null, string? roamingAppData = null)
    {
        localAppData ??= Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        roamingAppData ??= Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        LocalRoot = Path.Combine(localAppData, "iPhoneMirror");
        SettingsRoot = Path.Combine(roamingAppData, "iPhoneMirror");
    }

    public string LocalRoot { get; }
    public string SettingsRoot { get; }
    public string StateFile => Path.Combine(LocalRoot, "state.json");
    public string SettingsFile => Path.Combine(SettingsRoot, "settings.json");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(LocalRoot);
        Directory.CreateDirectory(SettingsRoot);
    }
}
