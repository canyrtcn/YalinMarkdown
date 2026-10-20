using Microsoft.Win32;
using System.IO;
using System.Runtime.InteropServices;

namespace YalinMarkdown;

/// <summary>
/// Opt-in, per-user registration for portable YalinMarkdown. Never changes the
/// default handler or Windows' protected UserChoice registration.
/// </summary>
internal static class PortableFileAssociations
{
    internal static readonly string[] Extensions = [".md", ".markdown", ".txt", ".text"];

    private const string AppName = "YalinMarkdown";
    private const string MarkdownProgId = "YalinMarkdown.Markdown";
    private const string TextProgId = "YalinMarkdown.Text";
    private const string ApplicationKey = @"Software\Classes\Applications\YalinMarkdown.exe";
    private const string CapabilitiesKey = @"Software\YalinMarkdown\Capabilities";
    private const string RegisteredApplicationsKey = @"Software\RegisteredApplications";

    // SHCNE_ASSOCCHANGED / SHCNF_IDLIST: request shell association refresh.
    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);

    internal static bool SupportsPath(string? filePath) =>
        !string.IsNullOrWhiteSpace(filePath) &&
        Extensions.Contains(Path.GetExtension(filePath), StringComparer.OrdinalIgnoreCase);

    internal static string ProgIdFor(string extension) =>
        extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".markdown", StringComparison.OrdinalIgnoreCase)
            ? MarkdownProgId
            : extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
              extension.Equals(".text", StringComparison.OrdinalIgnoreCase)
                ? TextProgId
                : throw new ArgumentException("Unsupported file extension.", nameof(extension));

    internal static string BuildOpenCommand(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || executablePath.Contains('"'))
            throw new ArgumentException("Invalid executable path.", nameof(executablePath));
        return $"\"{executablePath}\" \"%1\"";
    }

    internal static string GetPortableExecutablePath()
    {
        var path = Environment.ProcessPath;
        // In development (dotnet run) don't register the dotnet host by mistake.
        if (string.IsNullOrWhiteSpace(path) ||
            !Path.GetFileName(path).Equals("YalinMarkdown.exe", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(path))
            throw new InvalidOperationException("Run the YalinMarkdown.exe application to register file types.");
        return Path.GetFullPath(path);
    }

    internal static bool IsRegisteredForCurrentExecutable()
    {
        try
        {
            var path = GetPortableExecutablePath();
            using var command = Registry.CurrentUser.OpenSubKey(ApplicationKey + @"\shell\open\command");
            using var apps = Registry.CurrentUser.OpenSubKey(RegisteredApplicationsKey);
            return string.Equals(command?.GetValue(null) as string, BuildOpenCommand(path),
                       StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(apps?.GetValue(AppName) as string, CapabilitiesKey,
                       StringComparison.OrdinalIgnoreCase);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    internal static void Register()
    {
        var path = GetPortableExecutablePath();
        var command = BuildOpenCommand(path);
        var icon = $"\"{path}\",0";

        // Only our namespaced keys and our own value in OpenWithProgids are modified.
        // No .extension default value and no UserChoice value is changed.
        using (var app = Registry.CurrentUser.CreateSubKey(ApplicationKey))
            app.SetValue("FriendlyAppName", AppName, RegistryValueKind.String);
        using (var iconKey = Registry.CurrentUser.CreateSubKey(ApplicationKey + @"\DefaultIcon"))
            iconKey.SetValue(null, icon, RegistryValueKind.String);
        using (var open = Registry.CurrentUser.CreateSubKey(ApplicationKey + @"\shell\open\command"))
            open.SetValue(null, command, RegistryValueKind.String);
        using (var supported = Registry.CurrentUser.CreateSubKey(ApplicationKey + @"\SupportedTypes"))
        {
            foreach (var extension in Extensions)
                supported.SetValue(extension, "", RegistryValueKind.String);
        }

        WriteProgId(MarkdownProgId, "YalinMarkdown Markdown document", command, icon);
        WriteProgId(TextProgId, "YalinMarkdown text document", command, icon);

        using (var capabilities = Registry.CurrentUser.CreateSubKey(CapabilitiesKey))
        {
            capabilities.SetValue("ApplicationName", AppName, RegistryValueKind.String);
            capabilities.SetValue("ApplicationDescription", "Portable Markdown and text editor", RegistryValueKind.String);
        }
        using (var associations = Registry.CurrentUser.CreateSubKey(CapabilitiesKey + @"\FileAssociations"))
        {
            foreach (var extension in Extensions)
                associations.SetValue(extension, ProgIdFor(extension), RegistryValueKind.String);
        }
        using (var registered = Registry.CurrentUser.CreateSubKey(RegisteredApplicationsKey))
            registered.SetValue(AppName, CapabilitiesKey, RegistryValueKind.String);

        foreach (var extension in Extensions)
        {
            using var openWith = Registry.CurrentUser.CreateSubKey(
                $@"Software\Classes\{extension}\OpenWithProgids");
            openWith.SetValue(ProgIdFor(extension), "", RegistryValueKind.String);
        }
        NotifyShell();
    }

    internal static bool Unregister()
    {
        using (var registered = Registry.CurrentUser.OpenSubKey(RegisteredApplicationsKey, writable: true))
        {
            if (!string.Equals(registered?.GetValue(AppName) as string, CapabilitiesKey,
                    StringComparison.OrdinalIgnoreCase))
                return false; // Do not remove associations owned by another registration.
            registered?.DeleteValue(AppName, throwOnMissingValue: false);
        }

        foreach (var extension in Extensions)
        {
            using var openWith = Registry.CurrentUser.OpenSubKey(
                $@"Software\Classes\{extension}\OpenWithProgids", writable: true);
            openWith?.DeleteValue(ProgIdFor(extension), throwOnMissingValue: false);
        }
        // Never delete whole .md / .txt keys or Windows user defaults.
        Registry.CurrentUser.DeleteSubKeyTree(ApplicationKey, throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\" + MarkdownProgId, throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\" + TextProgId, throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(CapabilitiesKey, throwOnMissingSubKey: false);
        NotifyShell();
        return true;
    }

    private static void WriteProgId(string progId, string displayName, string command, string icon)
    {
        var root = @"Software\Classes\" + progId;
        using (var key = Registry.CurrentUser.CreateSubKey(root))
            key.SetValue(null, displayName, RegistryValueKind.String);
        using (var iconKey = Registry.CurrentUser.CreateSubKey(root + @"\DefaultIcon"))
            iconKey.SetValue(null, icon, RegistryValueKind.String);
        using (var commandKey = Registry.CurrentUser.CreateSubKey(root + @"\shell\open\command"))
            commandKey.SetValue(null, command, RegistryValueKind.String);
    }

    private static void NotifyShell() => SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
}
