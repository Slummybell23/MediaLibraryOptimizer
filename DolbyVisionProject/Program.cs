using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace DolbyVisionProject;

public abstract class Program
{
    private static void Main(string[] args)
    {
        var consoleLog = new ConsoleLog();
        var converter = new Converter(consoleLog);
        
        string movieFolder;
        string tvShowFolder;
        string checkAll;
        var startHour = DateTime.Now.Hour;

        
        if (Debugger.IsAttached)
        {
            checkAll = "n";
            movieFolder = "Z:\\Plex\\Movie";
            //movieFolder = "Z:\\Plex\\Movie\\Coraline (2009)";
            tvShowFolder = "Z:\\Plex\\TV show";
        }
        else
        {
            movieFolder = Environment.GetEnvironmentVariable("MOVIE_FOLDER")!;
            tvShowFolder = Environment.GetEnvironmentVariable("TVSHOW_FOLDER")!;
            checkAll = Environment.GetEnvironmentVariable("CHECK_ALL")!;

            string startTimeStr = Environment.GetEnvironmentVariable("STARTTIME")!;
            var isParsed = int.TryParse(startTimeStr, CultureInfo.InvariantCulture, out startHour);
        }
        
        while (true)
        {
            var now = DateTime.Now;

            var hoursDifference = (startHour + 24) - now.Hour;
            if (hoursDifference >= 24)
                hoursDifference -= 24;

            var hoursTill5 = hoursDifference;
            if (hoursTill5 == 0)
            {
                var nonDolbyVision7 = 0;
                var failedFiles = new List<string>();
                var convertedFiles = new List<string>();

                var directory = converter.BuildFilesList(movieFolder, tvShowFolder, checkAll);
                consoleLog.WriteLine($"Processing {directory.Count} files...");
                foreach (var file in directory)
                {
                    consoleLog.LogText = new StringBuilder();
                    consoleLog.WriteLine($"Processing file: {file}");

                    if (converter.IsProfile7(file))
                    {
                        consoleLog.WriteLine($"Dolby Vision Profile 7 detected in: {file}");

                        var start = DateTime.Now;
                        var converted = converter.ConvertFile(file);

                        if (converted)
                            convertedFiles.Add(file);
                        else
                            failedFiles.Add(file);

                        var end = DateTime.Now;
                        var timeCost = end - start;
                        consoleLog.WriteLine($"Conversion Time: {timeCost.ToString()}");

                        consoleLog.LogFile(file);
                    }
                    else
                    {
                        consoleLog.WriteLine($"Skipping: {file} (not Dolby Vision Profile 7)");
                        nonDolbyVision7++;
                    }
                }

                var endRunOutput = new StringBuilder();
                endRunOutput.AppendLine($"{directory.Count} files processed");
                endRunOutput.AppendLine($"{nonDolbyVision7} files skipped");
                endRunOutput.AppendLine($"{failedFiles.Count} files failed");
                endRunOutput.AppendLine($"{convertedFiles.Count} files converted");

                endRunOutput.AppendLine($"============= Converted Files ============");
                foreach (var converted in convertedFiles)
                {
                    endRunOutput.AppendLine(converted);
                }
                endRunOutput.AppendLine($"==========================================");

                endRunOutput.AppendLine($"============= Failed Files ============");
                foreach (var failed in failedFiles)
                {
                    endRunOutput.AppendLine(failed);
                }
                endRunOutput.AppendLine($"=======================================");

                consoleLog.WriteLine(endRunOutput.ToString());
                
                checkAll = "n";
                consoleLog.WriteLine("Waiting for new files... Setting to recent files");
                
                now = DateTime.Now;
                hoursDifference = (startHour + 24) - now.Hour;
                if (hoursDifference > 24)
                   hoursDifference -= 24;

                hoursTill5 = hoursDifference;
                Thread.Sleep(TimeSpan.FromHours(hoursTill5));
            }
            else
            {
                consoleLog.WriteLine($"Waiting until {startHour}...\n{hoursTill5} hours remaining from time of log.");
                Thread.Sleep(TimeSpan.FromHours(hoursTill5));
            }
        }
    }
}