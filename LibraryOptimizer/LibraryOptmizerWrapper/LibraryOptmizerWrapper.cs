using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace LibraryOptimizer;

public class LibraryOptmizerWrapper
{
    //Creates objects necessary for logging and converting files.
    private static ConsoleLog _consoleLog = new ConsoleLog();
    private ConverterBackend _converterBackend = new ConverterBackend(_consoleLog);

    public string dataFolder = "/data";
    public string highSpeedPathFolder = "/fastDrive";
    public List<string> Libraries = new List<string>();
    public string? CheckAll = null;
    public int StartHour = DateTime.Now.Hour;
    public bool EncodeHevc = false;
    public bool EncodeAv1 = false;
    public bool RemuxDolbyVision = false;
    
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
            var yamlObj = deserializer.Deserialize<LibraryOptimzerWrapperYaml>(reader);

            RemuxDolbyVision = yamlObj.RemuxDolbyVision;
            EncodeHevc = yamlObj.EncodeHevc;
            EncodeAv1 = yamlObj.EncodeAv1;
            CheckAll = yamlObj.CheckAll;
            StartHour = yamlObj.StartHour;

            foreach (var library in yamlObj.LibraryPaths)
            {
                Libraries.Add(dataFolder + library);
            }
        }
        else
        {
            var wrapperJobject = new LibraryOptimzerWrapperYaml();

            var serializer = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
            var configStr = serializer.Serialize(wrapperJobject);
            
            File.WriteAllText(configFile, configStr);
        }
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

                highSpeedPathFolder = Path.Join(highSpeedPathFolder, "libraryOptimizerIncomplete");
                Directory.CreateDirectory(highSpeedPathFolder);
                
                
                //Grabs all files needed to check and iterate through them.
                var directory = _converterBackend.BuildFilesList(Libraries, CheckAll);
                _consoleLog.WriteLine($"Processing {directory.Count} files...");
                foreach (var file in directory)
                {
                    var modifiedFile = file.Replace("'", "''");
                    var fileName = Path.GetFileName(file);
                    var outputPathFile = Path.Combine(highSpeedPathFolder, $"{fileName}");
                    
                    
                    _consoleLog.LogText = new StringBuilder();
                    _consoleLog.WriteLine($"Processing file: {modifiedFile}");

                    if (!_converterBackend.ShouldBeProcessed(modifiedFile))
                    {
                        _consoleLog.WriteLine("Skipping file due to metadata check.");
                        continue;
                    }
                    
                    bool? converted = null;

                    //Start timer to calculate time to convert file
                    var start = DateTime.Now;
                    
                    var command = $"ffmpeg -i '{modifiedFile}' -hide_banner -loglevel info";
                    var fileInfo = _converterBackend.RunCommand(command, modifiedFile, false);

                    var startBitRate = 0.0;
                    var encodeCheckCommand = $"ffprobe -i '{modifiedFile}' -show_entries format=bit_rate -v quiet -of csv='p=0'";
                    if (EncodeAv1 || EncodeHevc)
                    {
                        var bitRateOutput = _converterBackend.RunCommand(encodeCheckCommand, modifiedFile).Split().Last();
                        startBitRate = double.Parse(bitRateOutput) / 1000000.0;
                    }
                    
                    if (EncodeAv1)
                    {
                        //Check if file is not av1, above 15mbps, and not dolby vision
                        if (_converterBackend.CanEncodeAv1(modifiedFile, fileInfo, startBitRate))
                        {
                            _consoleLog.WriteLine("Copying file for AV1 Encode...");
                            File.Copy(file, outputPathFile);

                            converted = _converterBackend.EncodeAv1(outputPathFile);

                            var bitRateOutput = _converterBackend.RunCommand(encodeCheckCommand, outputPathFile).Split().Last();
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
                            File.Copy(file, outputPathFile);

                            _consoleLog.WriteLine($"Dolby Vision Profile 7 detected in: {modifiedFile}");
                                
                            converted = _converterBackend.Remux(outputPathFile);
                        }
                    }
                    if (RemuxDolbyVision && EncodeHevc && converted == null)
                    {
                        if (_converterBackend.IsProfile7(fileInfo))
                        {
                            _consoleLog.WriteLine("Copying file for Dolby Vision Profile 7 Remuxing and HEVC Encoding...");
                            File.Copy(file, outputPathFile);

                            _consoleLog.WriteLine($"Dolby Vision Profile 7 detected in: {outputPathFile}");
                                
                            converted = _converterBackend.RemuxAndEncodeHevc(outputPathFile);
                        }
                    }
                    if (EncodeHevc && converted == null)
                    {
                        if (_converterBackend.CanEncodeHevc(modifiedFile, fileInfo, startBitRate))
                        {
                            _consoleLog.WriteLine("Copying file for HEVC Encode...");
                            File.Copy(file, outputPathFile);

                            converted = _converterBackend.EncodeHevc(outputPathFile);
                            
                            var bitRateOutput = _converterBackend.RunCommand(encodeCheckCommand, outputPathFile).Split().Last();
                            var endBitRate = double.Parse(bitRateOutput) / 1000000.0;
                        
                            _consoleLog.WriteLine($"Starting bitrate: {startBitRate} mbps");
                            _consoleLog.WriteLine($"Ending bitrate: {endBitRate} mbps");
                        }
                        
                    }

                    if (converted != null)
                    {
                        File.Move(outputPathFile, file, true);
                        
                        if (converted == true)
                            convertedFiles.Add(file);
                        else
                            failedFiles.Add(file);
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
                        _consoleLog.WriteLine($"Skipping: {modifiedFile}");
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