using System.Text;

namespace DolbyVisionProject;

public class DolbyVisionWrapper
{
    //Creates objects necessary for logging and converting files.
    private static ConsoleLog _consoleLog = new ConsoleLog();
    private Converter _converter = new Converter(_consoleLog);
        
    public string? MovieFolder = null;
    public string? TvShowFolder = null;
    public string? CheckAll = null;
    public List<string> FilesToEncode = new List<string>();
    public int StartHour = DateTime.Now.Hour;
    public bool Encode = false;
    public bool Remux = false;
    
    public void RemixAndEncodeFiles()
    {
        while (true)
        {
            var now = DateTime.Now;

            //Calculates hours until start hour user specified.
            var hoursDifference = (StartHour + 24) - now.Hour;
            if (hoursDifference >= 24)
                hoursDifference -= 24;

            var hoursTillStart = hoursDifference;
            if (hoursTillStart == 0)
            {
                var nonDolbyVision7 = 0;
                var failedFiles = new List<string>();
                var convertedFiles = new List<string>();

                //Grabs all files needed to check and iterate through them.
                var directory = _converter.BuildFilesList(MovieFolder, TvShowFolder, CheckAll);
                _consoleLog.WriteLine($"Processing {directory.Count} files...");
                foreach (var file in directory)
                {
                    if (file.Contains("WALL"))
                    {
                        
                    }
                    _consoleLog.LogText = new StringBuilder();
                    _consoleLog.WriteLine($"Processing file: {file}");

                    //Check if file is Dolby Vision Profile 7.
                    if (_converter.IsProfile7(file))
                    {
                        _consoleLog.WriteLine($"Dolby Vision Profile 7 detected in: {file}");

                        //Start timer to calculate time to convert file
                        var start = DateTime.Now;
                        
                        //Convert file to Dolby Vision 8 and reencode file if user requested.
                        var converted = false;
                        if (Remux && Encode)
                            converted = _converter.RemuxAndEncode(file);
                        else if (Remux)
                            converted = _converter.Remux(file);

                        if (converted)
                            convertedFiles.Add(file);
                        else
                            failedFiles.Add(file);

                        var end = DateTime.Now;
                        var timeCost = end - start;
                        _consoleLog.WriteLine($"Conversion Time: {timeCost.ToString()}");

                        _consoleLog.LogFile(file);
                    }
                    else
                    {
                        _consoleLog.WriteLine($"Skipping: {file} (not Dolby Vision Profile 7)");
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

                _consoleLog.WriteLine(endRunOutput.ToString());
                
                CheckAll = "n";
                _consoleLog.WriteLine("Waiting for new files... Setting to recent files");
                
                //Setup to wait until start time.
                now = DateTime.Now;
                hoursDifference = (StartHour + 24) - now.Hour;
                if (hoursDifference > 24)
                    hoursDifference -= 24;

                hoursTillStart = hoursDifference;
                Thread.Sleep(TimeSpan.FromHours(hoursTillStart));
            }
            else
            {
                _consoleLog.WriteLine($"Waiting until {StartHour}...\n{hoursTillStart} hours remaining from time of log.");
                Thread.Sleep(TimeSpan.FromHours(hoursTillStart));
            }
        }
    }
    
    public void EncodeFiles()
    {
        var failedFiles = new List<string>();
        var convertedFiles = new List<string>();

        //Grabs all files needed to check and iterate through them.
        var directory = _converter.BuildFilesList(MovieFolder, TvShowFolder, FilesToEncode);
        _consoleLog.WriteLine($"Processing {directory.Count} files...");
        foreach (var file in directory)
        {
            _consoleLog.LogText = new StringBuilder();
            _consoleLog.WriteLine($"Processing file: {file}");

            //Start timer to calculate time to convert file
            var start = DateTime.Now;
            
            //Convert file to Dolby Vision 8 and reencode file if user requested.
            var converted = _converter.Encode(file);

            if (converted)
                convertedFiles.Add(file);
            else
                failedFiles.Add(file);

            var end = DateTime.Now;
            var timeCost = end - start;
            _consoleLog.WriteLine($"Conversion Time: {timeCost.ToString()}");

            _consoleLog.LogFile(file);
        }

        //Build out quick view log of full run.
        var endRunOutput = new StringBuilder();
        endRunOutput.AppendLine($"{directory.Count} files processed");
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

        _consoleLog.WriteLine(endRunOutput.ToString());
    }
    
}