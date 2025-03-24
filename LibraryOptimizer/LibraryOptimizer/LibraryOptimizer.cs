using System.Reflection;
using System.Text;
using LibraryOptimizer.Enums;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace LibraryOptimizer.LibraryOptimizer;

public class LibraryOptimizer
{
    private string _dataFolder = "/data";
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

    public CancellationToken Token;

    #region Constructors

    public LibraryOptimizer(CancellationTokenSource tokenSource)
    {
        Token = tokenSource.Token;
        
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
                    Token.ThrowIfCancellationRequested();
                    
                    var inputFile = fileInfoEntry.FullName;
                    var commandFile = ConverterBackend.FileFormatToCommand(inputFile);
                    var videoInfo = new VideoInfo(inputFile, this);
                    var fileInfo = videoInfo.InputFfmpegVideoInfo;

                    ConsoleLog.ResetLogText();
                    ConsoleLog.WriteLine($"Processing file: {inputFile}");

                    // if (!ConverterBackend.ShouldBeProcessed(videoInfo, _retryFailed))
                    // {
                    //     ConsoleLog.WriteLine("Skipping file due to metadata check.");
                    //     continue;
                    // }

                    var converted = ConverterStatusEnum.NotConverted;

                    //Start timer to calculate time to convert file
                    var start = DateTime.Now;

                    try
                    {
                        File.Open(inputFile, FileMode.Open);
                    }
                    catch (UnauthorizedAccessException e)
                    {
                        ConsoleLog.WriteLine(e.ToString());
                    }
                    catch (IOException e)
                    {
                        ConsoleLog.WriteLine("Io Exception, file in use likely... skipping");
                        continue;
                    }
                    
                    if (EncodeAv1 || EncodeHevc)
                    {
                        videoInfo.SetInputBitrate();
                    }
                
                    if (EncodeAv1)
                    {
                        //Check if file is not av1, and not dolby vision
                        if (ConverterBackend.CanEncodeAv1(fileInfo))
                        {
                            converted = ConverterBackend.EncodeAv1(videoInfo);
                            
                            ConsoleLog.WriteLine($"Starting bitrate: {videoInfo.GetInputBitrate()} kbps");
                            ConsoleLog.WriteLine($"Ending bitrate: {videoInfo.GetOutputBitrate()} kbps");
                        }
                    }
                    if (RemuxDolbyVision && !EncodeHevc && converted == ConverterStatusEnum.NotConverted)
                    {
                        if (ConverterBackend.IsProfile7(fileInfo))
                        {
                            ConsoleLog.WriteLine($"Dolby Vision Profile 7 detected in: {inputFile}");
                             
                            converted = ConverterBackend.Remux(videoInfo);
                            
                            ConsoleLog.WriteLine($"Starting bitrate: {videoInfo.GetInputBitrate()} kbps");
                            ConsoleLog.WriteLine($"Ending bitrate: {videoInfo.GetOutputBitrate()} kbps");
                        }
                    }
                    if (RemuxDolbyVision && EncodeHevc && converted == ConverterStatusEnum.NotConverted)
                    {
                        if (ConverterBackend.IsProfile7(fileInfo))
                        {
                            ConsoleLog.WriteLine($"Dolby Vision Profile 7 detected in: {inputFile}");
                             
                            converted = ConverterBackend.RemuxAndEncodeHevc(videoInfo);
                            
                            ConsoleLog.WriteLine($"Starting bitrate: {videoInfo.GetInputBitrate()} kbps");
                            ConsoleLog.WriteLine($"Ending bitrate: {videoInfo.GetOutputBitrate()} kbps");
                        }
                    }
                    if (EncodeHevc && converted == ConverterStatusEnum.NotConverted)
                    {
                        if (ConverterBackend.CanEncodeHevc(commandFile, fileInfo, videoInfo.GetInputBitrate()))
                        {
                            converted = ConverterBackend.EncodeHevc(videoInfo);
                         
                            ConsoleLog.WriteLine($"Starting bitrate: {videoInfo.GetInputBitrate()} kbps");
                            ConsoleLog.WriteLine($"Ending bitrate: {videoInfo.GetOutputBitrate()} kbps");
                        }
                    }

                    if (converted != ConverterStatusEnum.NotConverted)
                    {                            
                        if (converted == ConverterStatusEnum.Success)
                        {
                            convertedFiles.Add(inputFile);
                        }
                        else
                        {
                            failedFiles.Add(inputFile);
                        }

                        var end = DateTime.Now;
                        var timeCost = end - start;
                        
                        ConsoleLog.WriteLine($"Conversion Time: {timeCost.ToString()}");
                        
                        if(converted == ConverterStatusEnum.Success)
                            ConsoleLog.LogFile(inputFile, true);
                        else
                            ConsoleLog.LogFile(inputFile, false);
                    }
                
                    if(converted == ConverterStatusEnum.NotConverted)
                    {
                        ConsoleLog.WriteLine($"Skipping: {inputFile}");
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