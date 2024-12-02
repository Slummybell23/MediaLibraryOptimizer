using System.Reflection;
using System.Text;

namespace DolbyVisionProject;

public class ConsoleLog
{
    public ConsoleLog()
    {
        if (OperatingSystem.IsWindows())
        {
            ConfigDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        }
        else if (OperatingSystem.IsLinux())
        {
            ConfigDir = "/config";
        }
    }
    
    public StringBuilder LogText { get; set; } = new StringBuilder();
    private string ConfigDir = string.Empty;
    
    public void WriteLine(string inputText)
    {
        LogText.AppendLine(inputText);
        Console.WriteLine(inputText);
    }

    public void LogFile(string file)
    {
        var formatedDate = DateTime.Today.ToString("MM-dd-yyyy");
        var logFolder = Path.Combine(ConfigDir, "logs", formatedDate, "movies");
        
        Directory.CreateDirectory(logFolder);

        var movie = Path.GetFileNameWithoutExtension(file);
        
        var logFile = Path.Combine(logFolder, $"{movie}.txt");
        
        if (File.Exists(logFile))
            File.Delete(logFile);
        
        File.WriteAllText(logFile, LogText.ToString());
        
        LogText = new StringBuilder();
    }
}