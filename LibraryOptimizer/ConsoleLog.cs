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

    //Generates a log file in a movies respective name and folder containing info from WriteLine().
    public void LogFile(string file)
    {
        var formatedDate = DateTime.Today.ToString("MM-dd-yyyy");
        var logFolder = Path.Combine(_configDir, "logs", formatedDate, "movies");
        
        Directory.CreateDirectory(logFolder);

        var movie = Path.GetFileNameWithoutExtension(file);
        
        var logFile = Path.Combine(logFolder, $"{movie}.txt");
        
        //Overrides file if already existing.
        if (File.Exists(logFile))
            File.Delete(logFile);
        
        File.WriteAllText(logFile, LogText.ToString());
        
        LogText = new StringBuilder();
    }
}