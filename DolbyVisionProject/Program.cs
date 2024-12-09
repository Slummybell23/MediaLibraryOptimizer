using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace DolbyVisionProject;

public abstract class Program
{
    private static void Main(string[] args)
    {
        //Creates objects necessary for logging and converting files.
        var consoleLog = new ConsoleLog();
        var converter = new Converter(consoleLog);
        
        string movieFolder;
        string tvShowFolder;
        string checkAll;
        var startHour = DateTime.Now.Hour;
        
        if (Debugger.IsAttached)
        {
            //These are file paths specified to my Windows machine for debugging.
            checkAll = "n";
            movieFolder = "Z:\\Plex\\Movie";
            //movieFolder = "Z:\\Plex\\Movie\\Coraline (2009)";
            tvShowFolder = "Z:\\Plex\\TV show";
        }
        else
        {
            //Assumes if not in debug mode, running in docker environment.
            movieFolder = Environment.GetEnvironmentVariable("MOVIE_FOLDER")!;
            tvShowFolder = Environment.GetEnvironmentVariable("TVSHOW_FOLDER")!;
            checkAll = Environment.GetEnvironmentVariable("CHECK_ALL")!;

            string startTimeStr = Environment.GetEnvironmentVariable("STARTTIME")!;
            var isParsed = int.TryParse(startTimeStr, CultureInfo.InvariantCulture, out startHour);
        }
        
        while (true)
        {
            var now = DateTime.Now;

            //Calculates hours until start hour user specified.
            var hoursDifference = (startHour + 24) - now.Hour;
            if (hoursDifference >= 24)
                hoursDifference -= 24;

            var hoursTillStart = hoursDifference;
            if (hoursTillStart == 0)
            {
                var nonDolbyVision7 = 0;
                var failedFiles = new List<string>();
                var convertedFiles = new List<string>();

                //Grabs all files needed to check and iterate through them.
                var directory = converter.BuildFilesList(movieFolder, tvShowFolder, checkAll);
                consoleLog.WriteLine($"Processing {directory.Count} files...");
                foreach (var file in directory)
                {
                    consoleLog.LogText = new StringBuilder();
                    consoleLog.WriteLine($"Processing file: {file}");

                    //Check if file is Dolby Vision Profile 7.
                    if (converter.IsProfile7(file))
                    {
                        consoleLog.WriteLine($"Dolby Vision Profile 7 detected in: {file}");

                        //Start timer to calculate time to convert file
                        var start = DateTime.Now;
                        
                        //Convert file to Dolby Vision 8 and reencode file if user requested.
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

                //Build out quick view log of full run.
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
                
                //Setup to wait until start time.
                now = DateTime.Now;
                hoursDifference = (startHour + 24) - now.Hour;
                if (hoursDifference > 24)
                   hoursDifference -= 24;

                hoursTillStart = hoursDifference;
                Thread.Sleep(TimeSpan.FromHours(hoursTillStart));
            }
            else
            {
                consoleLog.WriteLine($"Waiting until {startHour}...\n{hoursTillStart} hours remaining from time of log.");
                Thread.Sleep(TimeSpan.FromHours(hoursTillStart));
            }
        }
    }
}