using System.IO;

namespace iPhoneMirror.App;

internal static class RuntimeLocator
{
    public static string? FindPython()
    {
        if (Environment.GetEnvironmentVariable("IPHONE_MIRROR_TEST_MODE") == "1")
        {
            var overridePath = Environment.GetEnvironmentVariable("IPHONE_MIRROR_TEST_PYTHON");
            if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath)) return overridePath;
        }
        var bundled = Path.Combine(AppContext.BaseDirectory, "runtime", "python", "python.exe");
        if (File.Exists(bundled)) return bundled;
#if DEBUG
        var repo = FindRepoRoot();
        if (repo is not null)
        {
            var venv = Path.Combine(repo, "worker", ".venv", "Scripts", "python.exe");
            if (File.Exists(venv)) return venv;
        }
#endif
        return null;
    }

    public static string? FindWorker()
    {
        if (Environment.GetEnvironmentVariable("IPHONE_MIRROR_TEST_MODE") == "1")
        {
            var overridePath = Environment.GetEnvironmentVariable("IPHONE_MIRROR_TEST_WORKER");
            if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath)) return overridePath;
        }
        var copied = Path.Combine(AppContext.BaseDirectory, "worker", "worker.py");
        if (File.Exists(copied)) return copied;
#if DEBUG
        var repo = FindRepoRoot();
        var source = repo is null ? null : Path.Combine(repo, "worker", "worker.py");
        if (source is not null && File.Exists(source)) return source;
#endif
        return null;
    }

    public static string? FindMpv()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "runtime", "mpv", "mpv.exe");
        if (File.Exists(bundled)) return bundled;
#if DEBUG
        var repo = FindRepoRoot();
        if (repo is not null)
        {
            var local = Path.Combine(repo, "third_party", "mpv", "mpv.exe");
            if (File.Exists(local)) return local;
        }
#endif
        return null;
    }

    private static string? FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && directory is not null; i++, directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "iPhoneMirror.slnx"))) return directory.FullName;
        }
        return null;
    }
}
