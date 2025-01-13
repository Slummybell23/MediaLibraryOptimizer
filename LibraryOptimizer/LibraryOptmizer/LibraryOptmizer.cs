using System.Reflection;
using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace LibraryOptimizer.LibraryOptmizer;

public class LibraryOptmizer
{
    //Creates objects necessary for logging and converting files.
    private static ConsoleLog _consoleLog = new ConsoleLog();
    private ConverterBackend _converterBackend = new ConverterBackend(_consoleLog);

    public string dataFolder = "/data";
    public string highSpeedPathFolder = "/tmp";
    public List<string> Libraries = new List<string>();
    public string? CheckAll = null;
    public int StartHour = DateTime.Now.Hour;
    public bool EncodeHevc = false;
    public bool EncodeAv1 = false;
    public bool RemuxDolbyVision = false;
    public bool RetryFailed = false;
    
    private string _configDir;
    public void SetupWrapperVars()
    {
        if (OperatingSystem.IsWindows())
        {
            _configDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
            highSpeedPathFolder = "Y:\\";
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
            
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
            
            //yaml contains a string containing your YAML
            var yamlObj = deserializer.Deserialize<LibraryOptimzerYaml>(reader);

            RemuxDolbyVision = yamlObj.RemuxDolbyVision;
            EncodeHevc = yamlObj.EncodeHevc;
            EncodeAv1 = yamlObj.EncodeAv1;
            CheckAll = yamlObj.CheckAll;
            StartHour = yamlObj.StartHour;
            RetryFailed = yamlObj.RetryFailed;

            foreach (var library in yamlObj.LibraryPaths)
            {
                Libraries.Add(dataFolder + library);
            }
        }
        else
        {
            var wrapperJobject = new LibraryOptimzerYaml();

            var serializer = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
            var configStr = serializer.Serialize(wrapperJobject);
            
            File.WriteAllText(configFile, configStr);
        }
        
        highSpeedPathFolder = Path.Join(highSpeedPathFolder, "libraryOptimizerIncomplete");
        Directory.CreateDirectory(highSpeedPathFolder);
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
                var directory = _converterBackend.BuildFilesList(Libraries, CheckAll);
                _consoleLog.WriteLine($"Processing {directory.Count} files...");
                foreach (var file in directory)
                {
                    if (file.Contains("Love Is War"))
                    {
                       continue; 
                    }

                    var commandFile = _converterBackend.FileFormatToCommand(file);
                    
                    var fileName = Path.GetFileName(file);
                    var outputPathFile = Path.Combine(highSpeedPathFolder, $"{fileName}");
                    var commandOutputFile = _converterBackend.FileFormatToCommand(outputPathFile);

                    _consoleLog.LogText = new StringBuilder();
                    _consoleLog.WriteLine($"Processing file: {file}");

                    if (!_converterBackend.ShouldBeProcessed(commandFile, RetryFailed))
                    {
                        _consoleLog.WriteLine("Skipping file due to metadata check.");
                        continue;
                    }
                    
                    bool? converted = null;

                    //Start timer to calculate time to convert file
                    var start = DateTime.Now;
                    
                    var command = $"ffmpeg -i '{commandFile}' -hide_banner -loglevel info";
                    var fileInfo = _converterBackend.RunCommand(command, commandFile, false);

                    var startBitRate = 0.0;
                    var encodeCheckCommand = $"ffprobe -i '{commandFile}' -show_entries format=bit_rate -v quiet -of csv='p=0'";

                    if (EncodeAv1 || EncodeHevc)
                    {
                        var bitRateOutput = _converterBackend.RunCommand(encodeCheckCommand, commandFile).Split().Last();
                        startBitRate = double.Parse(bitRateOutput) / 1000000.0;
                    }
                    
                    if (EncodeAv1)
                    {
                        //Check if file is not av1, above 15mbps, and not dolby vision
                        if (_converterBackend.CanEncodeAv1(commandFile, fileInfo, startBitRate))
                        {
                            _consoleLog.WriteLine("Copying file for AV1 Encode...");
                            File.Copy(file, outputPathFile);

                            converted = _converterBackend.EncodeAv1(commandOutputFile, startBitRate);

                            encodeCheckCommand = $"ffprobe -i '{commandOutputFile}' -show_entries format=bit_rate -v quiet -of csv='p=0'";
                            var bitRateOutput = _converterBackend.RunCommand(encodeCheckCommand, commandOutputFile).Split().Last();
                            var endBitRate = double.Parse(bitRateOutput) / 1000000.0;
                        
                            _consoleLog.WriteLine($"Starting bitrate: {startBitRate} mbps");
                            _consoleLog.WriteLine($"Ending bitrate: {endBitRate} mbps");
                        }
                    }
                    if (RemuxDolbyVision && !EncodeHevc && converted == null)
                    {
                        if (_converterBackend.IsProfile7(fileInfo))
                        {
                            _consoleLog.WriteLine("Copying file for Dolby Vision Profile 7 Remuxing...");
                            File.Copy(file, commandOutputFile);
                    
                            _consoleLog.WriteLine($"Dolby Vision Profile 7 detected in: {file}");
                                
                            converted = _converterBackend.Remux(commandOutputFile);
                        }
                    }
                    if (RemuxDolbyVision && EncodeHevc && converted == null)
                    {
                        if (_converterBackend.IsProfile7(fileInfo))
                        {
                            _consoleLog.WriteLine("Copying file for Dolby Vision Profile 7 Remuxing and HEVC Encoding...");
                            File.Copy(file, outputPathFile);
                    
                            _consoleLog.WriteLine($"Dolby Vision Profile 7 detected in: {file}");
                                
                            converted = _converterBackend.RemuxAndEncodeHevc(commandOutputFile);
                            
                            encodeCheckCommand = $"ffprobe -i '{commandOutputFile}' -show_entries format=bit_rate -v quiet -of csv='p=0'";
                            var bitRateOutput = _converterBackend.RunCommand(encodeCheckCommand, outputPathFile).Split().Last();
                            var endBitRate = double.Parse(bitRateOutput) / 1000000.0;
                        
                            _consoleLog.WriteLine($"Starting bitrate: {startBitRate} mbps");
                            _consoleLog.WriteLine($"Ending bitrate: {endBitRate} mbps");
                        }
                    }
                    if (EncodeHevc && converted == null)
                    {
                        if (_converterBackend.CanEncodeHevc(commandFile, fileInfo, startBitRate))
                        {
                            _consoleLog.WriteLine("Copying file for HEVC Encode...");
                            File.Copy(file, outputPathFile);
                    
                            converted = _converterBackend.EncodeHevc(commandOutputFile);
                            
                            encodeCheckCommand = $"ffprobe -i '{commandOutputFile}' -show_entries format=bit_rate -v quiet -of csv='p=0'";
                            var bitRateOutput = _converterBackend.RunCommand(encodeCheckCommand, outputPathFile).Split().Last();
                            var endBitRate = double.Parse(bitRateOutput) / 1000000.0;
                        
                            _consoleLog.WriteLine($"Starting bitrate: {startBitRate} mbps");
                            _consoleLog.WriteLine($"Ending bitrate: {endBitRate} mbps");
                        }
                    }

                    if (converted != null)
                    {
                        if (converted == true)
                        {
                            convertedFiles.Add(file);
                            _consoleLog.WriteLine("Processing done. Moving output file to library.");
                            File.Move(outputPathFile, file, true);
                        }
                        else
                        {
                            failedFiles.Add(file);
                        }
                        
                        _converterBackend.DeleteFile(outputPathFile);
                    }
                    
                    var end = DateTime.Now;
                    var timeCost = end - start;

                    if (converted != null)
                    {
                        _consoleLog.WriteLine($"Conversion Time: {timeCost.ToString()}");
                        
                        _consoleLog.LogFile(file, converted);
                    }
                    
                    if(converted == null)
                    {
                        _consoleLog.WriteLine($"Skipping: {file}");
                        notProcessed++;
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