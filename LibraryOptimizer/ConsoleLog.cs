using System.Reflection;
using System.Text;

namespace LibraryOptimizer;

public static class ConsoleLog
{
    // Determines where to put log files (static field).
    private static string _configDir;

    // Allows for better string formatting (static property).
    private static StringBuilder _logText = new StringBuilder();

    // Static constructor to initialize the static class.
    static ConsoleLog()
    {
        // File systems differ between Windows and Linux Dockerized environments.
        if (OperatingSystem.IsWindows())
        {
            _configDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        }
        else if (OperatingSystem.IsLinux())
        {
            _configDir = "/config";
        }
        else
        {
            throw new NotSupportedException("Unsupported operating system.");
        }
    }

    public static void ResetLogText()
    {
        _logText = new StringBuilder();
    }

    // Prints to the console and adds to the LogText object for logging purposes (static method).
    public static void WriteLine(string inputText)
    {
        _logText.AppendLine(inputText);
        Console.WriteLine(inputText);
    }

    // Generates a log file and folder containing info from WriteLine() (static method).
    public static void LogFile(string file, bool? converted)
    {
        var formattedDate = DateTime.Today.ToString("MM-dd-yyyy");
        var logFolder = Path.Combine(_configDir, "logs", formattedDate);

        Directory.CreateDirectory(logFolder);

        var conversionStatus = converted == true ? "Converted Successfully" : "Converted False";
        var video = Path.GetFileNameWithoutExtension(file) + " " + conversionStatus;

        var logFile = Path.Combine(logFolder, $"{video}.txt");

        // Overrides file if already existing.
        if (File.Exists(logFile))
            File.Delete(logFile);

        File.WriteAllText(logFile, _logText.ToString());

        // Reset LogText for future use.
        _logText = new StringBuilder();
    }
}
