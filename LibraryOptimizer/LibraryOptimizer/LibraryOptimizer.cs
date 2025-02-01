using System.Reflection;
using System.Text;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace LibraryOptimizer.LibraryOptmizer;

public class LibraryOptimizer
{
    private string _dataFolder = "/data";
    private string _incompleteFolder = "/incomplete";
    private bool _retryFailed;
    private string _configDir;
    private bool _forceStart = false;

    public List<string> Libraries = new List<string>();
    public string CheckAll = "y";
    public int StartHour = DateTime.Now.Hour;
    public bool EncodeHevc;
    public bool EncodeAv1;
    public bool RemuxDolbyVision;
    public QualityEnum Quality;
    public bool IsNvidia;

    #region Constructors

    public LibraryOptimizer()
    {
        if (OperatingSystem.IsWindows())
        {
            _configDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
            _incompleteFolder = "Y:\\";
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
            var yamlStr = File.ReadAllText(configFile);
            
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
            try
            {
                //yaml contains a string containing your YAML
                var yamlObj = deserializer.Deserialize<LibraryOptimizerYaml>(yamlStr);
                
                foreach (var library in yamlObj.LibraryPaths)
                {
                    Libraries.Add(_dataFolder + library);
                }
                
                RemuxDolbyVision = yamlObj.RemuxDolbyVision;
                EncodeHevc = yamlObj.EncodeHevc;
                EncodeAv1 = yamlObj.EncodeAv1;
                CheckAll = yamlObj.CheckAll;
                StartHour = yamlObj.StartHour;
                _retryFailed = yamlObj.RetryFailed;
                IsNvidia = yamlObj.IsNvidia;
                Quality = yamlObj.Quality;
                
                BuildConfigFile(yamlObj, configFile);
            }
            catch (Exception e)
            {
                ConsoleLog.WriteLine(e.Message);
                ConsoleLog.WriteLine("Re Writing Config File");
                
                var libraryOptimizerYaml = new LibraryOptimizerYaml();
                libraryOptimizerYaml.LibraryPaths = Libraries;
                
                BuildConfigFile(libraryOptimizerYaml, configFile);
            }
        }
        else
        {
            var libraryOptimzerYaml = new LibraryOptimizerYaml();
            BuildConfigFile(libraryOptimzerYaml, configFile);
        }
        
        _incompleteFolder = Path.Join(_incompleteFolder, "libraryOptimizerIncomplete");
        
        //Clean up old files
        if(Directory.Exists(_incompleteFolder))
            Directory.Delete(_incompleteFolder, true);
        
        Directory.CreateDirectory(_incompleteFolder);
    }

    private void BuildConfigFile(LibraryOptimizerYaml libraryOptimizerYaml, string configFile)
    {
        var serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
        var configStr = serializer.Serialize(libraryOptimizerYaml);
            
        File.WriteAllText(configFile, configStr);
    }

    #endregion

    public void ProcessLibrary()
    {
        while (true)
        {
            if (Environment.GetEnvironmentVariable("FORCE_START") == "y")
                _forceStart = true;
            
            var now = DateTime.Now;

            //Calculates hours until start hour user specified.
            //TODO: Upgrade Scheduler
            var hoursDifference = (StartHour + 24) - now.Hour;
            if (hoursDifference >= 24)
                hoursDifference -= 24;
            
            var hoursTillStart = hoursDifference;
            if (hoursTillStart == 0 || _forceStart)
            {
                _forceStart = false;

                var notProcessed = 0;
                var failedFiles = new List<string>();
                var convertedFiles = new List<string>();
                
                //Grabs all files needed to check and iterate through them.
                var directory = ConverterBackend.BuildFilesList(Libraries, CheckAll);
                ConsoleLog.WriteLine($"Processing files...");
                foreach (var fileInfoEntry in directory)
                {
                    var file = fileInfoEntry.FullName;

                    var commandFile = ConverterBackend.FileFormatToCommand(file);
                
                    var fileName = Path.GetFileName(file);
                    var outputPathFile = Path.Combine(_incompleteFolder, $"{fileName}");
                    var commandOutputFile = ConverterBackend.FileFormatToCommand(outputPathFile);

                    ConsoleLog.ResetLogText();
                    ConsoleLog.WriteLine($"Processing file: {file}");

                    if (!ConverterBackend.ShouldBeProcessed(commandFile, _retryFailed))
                    {
                        ConsoleLog.WriteLine("Skipping file due to metadata check.");
                        continue;
                    }

                    var converted = ConverterStatus.NotConverted;

                    //Start timer to calculate time to convert file
                    var start = DateTime.Now;
                
                    var command = $"ffmpeg -i '{commandFile}' -hide_banner -loglevel info";
                    var fileInfo = ConverterBackend.RunCommand(command, commandFile, false);

                    var startBitRate = 0.0;
                    var encodeCheckCommand = $"ffprobe -i '{commandFile}' -show_entries format=bit_rate -v quiet -of csv='p=0'";

                    if (EncodeAv1 || EncodeHevc)
                    {
                        var bitRateOutput = ConverterBackend.RunCommand(encodeCheckCommand, commandFile).Split().Last();
                        startBitRate = double.Parse(bitRateOutput) / 1000000.0;
                    }
                
                    if (EncodeAv1)
                    {
                        //Check if file is not av1, and not dolby vision
                        if (ConverterBackend.CanEncodeAv1(commandFile, fileInfo, startBitRate))
                        {
                            ConsoleLog.WriteLine("Copying file for AV1 Encode...");
                            try
                            {
                                File.Copy(file, outputPathFile);
                            }
                            catch (IOException e)
                            {
                                ConsoleLog.WriteLine("File in use. Skipping.");
                                
                                continue;
                            }

                            converted = ConverterBackend.EncodeAv1(commandOutputFile, startBitRate, this);

                            encodeCheckCommand = $"ffprobe -i '{commandOutputFile}' -show_entries format=bit_rate -v quiet -of csv='p=0'";
                            var bitRateOutput = ConverterBackend.RunCommand(encodeCheckCommand, commandOutputFile).Split().Last();
                            var endBitRate = double.Parse(bitRateOutput) / 1000000.0;
                    
                            ConsoleLog.WriteLine($"Starting bitrate: {startBitRate} mbps");
                            ConsoleLog.WriteLine($"Ending bitrate: {endBitRate} mbps");
                        }
                    }
                    if (RemuxDolbyVision && !EncodeHevc && converted == ConverterStatus.NotConverted)
                    {
                        if (ConverterBackend.IsProfile7(fileInfo))
                        {
                            ConsoleLog.WriteLine("Copying file for Dolby Vision Profile 7 Remuxing...");
                            try
                            {
                                File.Copy(file, outputPathFile);
                            }
                            catch (IOException e)
                            {
                                ConsoleLog.WriteLine("File in use. Skipping.");
                                
                                continue;
                            }
                            
                            ConsoleLog.WriteLine($"Dolby Vision Profile 7 detected in: {file}");
                            
                            converted = ConverterBackend.Remux(commandOutputFile, this);
                        }
                    }
                    if (RemuxDolbyVision && EncodeHevc && converted == ConverterStatus.NotConverted)
                    {
                        if (ConverterBackend.IsProfile7(fileInfo))
                        {
                            ConsoleLog.WriteLine("Copying file for Dolby Vision Profile 7 Remuxing and HEVC Encoding...");
                            try
                            {
                                File.Copy(file, outputPathFile);
                            }
                            catch (IOException e)
                            {
                                ConsoleLog.WriteLine("File in use. Skipping.");
                                
                                continue;
                            }
                            
                            ConsoleLog.WriteLine($"Dolby Vision Profile 7 detected in: {file}");
                            
                            converted = ConverterBackend.RemuxAndEncodeHevc(commandOutputFile, this);
                        
                            encodeCheckCommand = $"ffprobe -i '{commandOutputFile}' -show_entries format=bit_rate -v quiet -of csv='p=0'";
                            var bitRateOutput = ConverterBackend.RunCommand(encodeCheckCommand, outputPathFile).Split().Last();
                            var endBitRate = double.Parse(bitRateOutput) / 1000000.0;
                    
                            ConsoleLog.WriteLine($"Starting bitrate: {startBitRate} mbps");
                            ConsoleLog.WriteLine($"Ending bitrate: {endBitRate} mbps");
                        }
                    }
                    if (EncodeHevc && converted == ConverterStatus.NotConverted)
                    {
                        if (ConverterBackend.CanEncodeHevc(commandFile, fileInfo, startBitRate))
                        {
                            ConsoleLog.WriteLine("Copying file for HEVC Encode...");
                            try
                            {
                                File.Copy(file, outputPathFile);
                            }
                            catch (IOException e)
                            {
                                ConsoleLog.WriteLine("File in use. Skipping.");
                                
                                continue;
                            }
                            
                            converted = ConverterBackend.EncodeHevc(commandOutputFile, this);
                        
                            encodeCheckCommand = $"ffprobe -i '{commandOutputFile}' -show_entries format=bit_rate -v quiet -of csv='p=0'";
                            var bitRateOutput = ConverterBackend.RunCommand(encodeCheckCommand, outputPathFile).Split().Last();
                            var endBitRate = double.Parse(bitRateOutput) / 1000000.0;
                    
                            ConsoleLog.WriteLine($"Starting bitrate: {startBitRate} mbps");
                            ConsoleLog.WriteLine($"Ending bitrate: {endBitRate} mbps");
                        }
                    }

                    if (converted != ConverterStatus.NotConverted)
                    {                            
                        ConsoleLog.WriteLine("Processing done. Moving output file to library... /n DO NOT TURN OFF PROGRAM DURING THIS");

                        if (converted == ConverterStatus.Success)
                        {
                            convertedFiles.Add(file);
                        }
                        else
                        {
                            failedFiles.Add(file);
                        }
                    
                        File.Move(outputPathFile, file, true);

                        ConverterBackend.DeleteFile(outputPathFile);

                        var end = DateTime.Now;
                        var timeCost = end - start;
                        
                        ConsoleLog.WriteLine($"Conversion Time: {timeCost.ToString()}");
                        
                        if(converted == ConverterStatus.Success)
                            ConsoleLog.LogFile(file, true);
                        else
                            ConsoleLog.LogFile(file, false);
                    }
                
                    if(converted == ConverterStatus.NotConverted)
                    {
                        ConsoleLog.WriteLine($"Skipping: {file}");
                        notProcessed++;
                    }
                }

                //Build out quick view log of full run.
                var endRunOutput = new StringBuilder();
                endRunOutput.AppendLine($"NULL files processed");
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

                ConsoleLog.WriteLine(endRunOutput.ToString());
                
                CheckAll = "n";
                ConsoleLog.WriteLine("Waiting for new files... Setting to recent files");
                
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
                ConsoleLog.WriteLine($"Waiting until {StartHour}...\n{hoursTillStart} hours remaining from time of log.");
                Thread.Sleep(TimeSpan.FromHours(hoursTillStart));
            }
        }
    }
}