using System.Reflection;
using System.Text;

namespace LibraryOptimizer;

public class ConsoleLog
{
    //ConsoleLog meant to be used instead of Console since it's WriteLine method also adds to log file.
    public ConsoleLog()
    {
        //File systems differ between windows and linux dockerized
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
    
    //Allows for better string formatting
    public StringBuilder LogText { get; set; } = new StringBuilder();
    
    //Determines where to put log files
    private string _configDir;
    
    //Prints to the console and adds to the LogText object for logging purposes.
    public void WriteLine(string inputText)
    {
        LogText.AppendLine(inputText);
        Console.WriteLine(inputText);
    }

    //Generates a log file and folder containing info from WriteLine().
    public void LogFile(string file, bool? converted)
    {
        var formatedDate = DateTime.Today.ToString("MM-dd-yyyy");
        var logFolder = Path.Combine(_configDir, "logs", formatedDate);
        
        Directory.CreateDirectory(logFolder);

        var conversionStatus = string.Empty;
        if (converted == true)
            conversionStatus = "Converted Successfully";
        else
            conversionStatus = "Converted False";
        var video = Path.GetFileNameWithoutExtension(file) + " " + conversionStatus;
        
        var logFile = Path.Combine(logFolder, $"{video}.txt");
        
        //Overrides file if already existing.
        if (File.Exists(logFile))
            File.Delete(logFile);
        
        File.WriteAllText(logFile, LogText.ToString());
        
        LogText = new StringBuilder();
    }
}