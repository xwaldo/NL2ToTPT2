using System.IO;
using System.Windows;

namespace Importer.InputTestTarget;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var sessionDirectory = ReadSessionDirectory(e.Args);
        var window = new MainWindow(sessionDirectory);
        MainWindow = window;
        window.Show();
    }

    private static string ReadSessionDirectory(IReadOnlyList<string> arguments)
    {
        for (var index = 0; index < arguments.Count - 1; index++)
        {
            if (string.Equals(
                    arguments[index],
                    "--session-dir",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFullPath(arguments[index + 1]);
            }
        }

        throw new ArgumentException("Expected --session-dir <path>.");
    }
}
