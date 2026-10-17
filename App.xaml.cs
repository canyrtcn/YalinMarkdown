using System.Windows;
using System.IO;

namespace YalinMarkdown;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            try
            {
                var directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "YalinMarkdown");
                Directory.CreateDirectory(directory);
                File.AppendAllText(
                    Path.Combine(directory, "error.log"),
                    $"[{DateTimeOffset.Now:O}] {e.Exception}\r\n\r\n");
            }
            catch
            {
                // Error logging must never mask the original exception.
            }

            MessageBox.Show(
                "An unexpected error occurred. Details were written to the application log.",
                "YalinMarkdown",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
        };
    }
}
