using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using YamlDotNet.Serialization;

namespace LibraryOptimizer;

public class LibraryOptmizerWrapper
{
    //Creates objects necessary for logging and converting files.
    private static ConsoleLog _consoleLog = new ConsoleLog();
    private ConverterBackend _converterBackend = new ConverterBackend(_consoleLog);
        
    public string? MovieFolder = null;
    public string? TvShowFolder = null;
    public string? CheckAll = null;
    public List<string> FilesToEncode = new List<string>();
    public int StartHour = DateTime.Now.Hour;
    public bool EncodeHevc = false;
    public bool EncodeAv1 = false;
    public bool Remux = false;
    
    private string _configDir;
    public void SetupWrapperVars()
    {
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
        
        //Check for config file
        var configFile = Path.Combine(_configDir, "Config.yml");

        if (File.Exists(configFile))
        {

            var reader = File.ReadAllText(configFile);

            var obj = new Deserializer();
            var blbbb = obj.Deserialize<LibraryOptimzerWrapperJSON>(reader);




        }
        else
        {
            var wrapperJobject = new LibraryOptimzerWrapperJSON();

            var configStr = JsonSerializer.Serialize(wrapperJobject);
            
            
            
            File.WriteAllText(configFile, configStr.ToString());
        }


        //If not found, build one.




    }

    public void ProcessLibrary()
    {
        while (true)
        {
            var now = DateTime.Now;

            //Calculates hours until start hour user specified.
            //TODO: Upgrade Scheduler
            var hoursDifference = (StartHour + 24) - now.Hour;
            if (hoursDifference >= 24)
                hoursDifference -= 24;

            var hoursTillStart = hoursDifference;
            if (hoursTillStart == 0)
            {
                var notProcessed = 0;
                var failedFiles = new List<string>();
                var convertedFiles = new List<string>();

                //Grabs all files needed to check and iterate through them.
                var directory = _converterBackend.BuildFilesList(MovieFolder, TvShowFolder, CheckAll);
                _consoleLog.WriteLine($"Processing {directory.Count} files...");
                foreach (var file in directory)
                {
                    var modifiedFile = file.Replace("'", "''");
                    
                    _consoleLog.LogText = new StringBuilder();
                    _consoleLog.WriteLine($"Processing file: {modifiedFile}");

                    bool? converted = null;

                    //Start timer to calculate time to convert file
                    var start = DateTime.Now;
                    
                    var command = $"ffmpeg -i '{modifiedFile}' -hide_banner -loglevel info";
                    var fileInfo = _converterBackend.RunCommand(command, modifiedFile);
                    
                    if (EncodeAv1)
                    {
                        //Check if file is not av1, above 15mbps, and not dolby vision
                        if (_converterBackend.CanEncodeAv1(modifiedFile, fileInfo))
                        {
                            var encodeCheckCommand = $"ffprobe -i '{modifiedFile}' -show_entries format=bit_rate -v quiet -of csv='p=0'";
                            var bitRateOutput = _converterBackend.RunCommand(encodeCheckCommand, modifiedFile).Split().Last();
                            var startBitRate = double.Parse(bitRateOutput) / 1000000.0;
                            
                            converted = _converterBackend.EncodeAv1(modifiedFile);

                            bitRateOutput = _converterBackend.RunCommand(encodeCheckCommand, modifiedFile).Split().Last();
                            var endBitRate = double.Parse(bitRateOutput) / 1000000.0;
                        
                            _consoleLog.WriteLine($"Starting bitrate: {startBitRate} mbps");
                            _consoleLog.WriteLine($"Ending bitrate: {endBitRate} mbps");
                        }
                    }
                    if (Remux && !EncodeHevc && converted == null)
                    {
                        if (_converterBackend.IsProfile7(fileInfo))
                        {
                            _consoleLog.WriteLine($"Dolby Vision Profile 7 detected in: {modifiedFile}");
                                
                            converted = _converterBackend.Remux(modifiedFile);
                        }
                    }
                    if (Remux && EncodeHevc && converted == null)
                    {
                        if (_converterBackend.IsProfile7(fileInfo))
                        {
                            _consoleLog.WriteLine($"Dolby Vision Profile 7 detected in: {modifiedFile}");
                                
                            converted = _converterBackend.RemuxAndEncodeHevc(modifiedFile);
                        }
                    }
                    if (EncodeHevc && converted == null)
                    {
                        //TODO: Create check for if file can be HEVC encoded
                        //converted = _converterBackend.Encode(file);
                    }
                    if(converted == null)
                    {
                        _consoleLog.WriteLine($"Skipping: {modifiedFile}");
                        notProcessed++;
                    }

                    if (converted != null)
                    {
                        if (converted == true)
                            convertedFiles.Add(file);
                        else
                            failedFiles.Add(file);
                    }
                    
                    var end = DateTime.Now;
                    var timeCost = end - start;

                    if (converted != null && converted == true)
                    {
                        _consoleLog.WriteLine($"Conversion Time: {timeCost.ToString()}");
                        _consoleLog.LogFile(modifiedFile);
                    }
                }

                //Build out quick view log of full run.
                var endRunOutput = new StringBuilder();
                endRunOutput.AppendLine($"{directory.Count} files processed");
                endRunOutput.AppendLine($"{notProcessed} files skipped");
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
}