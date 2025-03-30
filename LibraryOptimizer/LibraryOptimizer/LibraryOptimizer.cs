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
    public string _incompleteFolder = "/incomplete";
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

    public LibraryOptimizer()
    {
        Token = Program._cancellationToken.Token;
        
        if (OperatingSystem.IsWindows())
        {
            _configDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
            _incompleteFolder = "E:\\";
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
            var lastResetDate = DateTime.Now;
            
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
                var directory = ConverterBackend.BuildFilesList(Libraries, CheckAll).ToList();
                ConsoleLog.WriteLine($"Processing files...");

                var directoryStartIndex = 0;
                for (int mainIndex = 0; mainIndex < directory.Count(); mainIndex = directoryStartIndex + 1)
                {
                    var resetDiff = (DateTime.Now - lastResetDate).Days;
                    if (resetDiff > 7)
                    {
                        Console.WriteLine("7 Days since last refresh.");
                        Console.WriteLine("Refreshing Directories...");
                        directory = ConverterBackend.BuildFilesList(Libraries, CheckAll).ToList();
                        mainIndex = 0;
                        directoryStartIndex = 0;

                        lastResetDate = DateTime.Now;
                    }
                    
                    var listOfFilesToProcess = new List<FileInfo>();
                    var cancelationToken = new CancellationTokenSource();
                    var taskOne = Task.Run(() => FindFile(directoryStartIndex, directory, cancelationToken,
                        listOfFilesToProcess, ref directoryStartIndex));
                    var taskTwo = Task.Run(() => FindFile(directoryStartIndex + 1, directory, cancelationToken,
                        listOfFilesToProcess, ref directoryStartIndex));
                    var taskThree = Task.Run(() => FindFile(directoryStartIndex + 2, directory, cancelationToken,
                        listOfFilesToProcess, ref directoryStartIndex));
                    Task.WaitAll(taskOne, taskTwo, taskThree);
                    directoryStartIndex++;

                   foreach (var fileInfoEntry in listOfFilesToProcess)
                   {
                       var locked = IsFileLocked(fileInfoEntry);
                       if (locked)
                       {
                           ConsoleLog.WriteLine("File in use. Skipping...");
                           continue;
                       }

                       var inputFile = fileInfoEntry.FullName;
                       var commandFile = ConverterBackend.FileFormatToCommand(inputFile);
                       var videoInfo = new VideoInfo(inputFile, this);
                       var fileInfo = videoInfo.InputFfmpegVideoInfo;
                       
                       if (inputFile.Contains($"Incomplete/{videoInfo.VideoName}"))
                           continue;

                       try
                       {
                           Token.ThrowIfCancellationRequested();

                           ConsoleLog.ResetLogText();
                           ConsoleLog.WriteLine($"Processing file: {inputFile}");

                           if (!ConverterBackend.ShouldBeProcessed(videoInfo, _retryFailed))
                           {
                               ConsoleLog.WriteLine("Skipping file due to metadata check.");
                               continue;
                           }

                           var converted = ConverterStatusEnum.NotConverted;

                           //Start timer to calculate time to convert file
                           var start = DateTime.Now;

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
                               if (ConverterBackend.CanEncodeHevc(fileInfo))
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
                                   ConsoleLog.WriteLine(videoInfo.ToString());
                                   
                                   failedFiles.Add(inputFile);
                               }

                               var end = DateTime.Now;
                               var timeCost = end - start;

                               ConsoleLog.WriteLine($"Conversion Time: {timeCost.ToString()}");

                               if (converted == ConverterStatusEnum.Success)
                                   ConsoleLog.LogFile(inputFile, true);
                               else
                                   ConsoleLog.LogFile(inputFile, false);
                           }

                           if (converted == ConverterStatusEnum.NotConverted)
                           {
                               ConsoleLog.WriteLine($"Skipping: {inputFile}");
                               notProcessed++;
                           }
                       }
                       catch (OperationCanceledException ex)
                       {
                           videoInfo.EndProgramCleanUp();
                           throw;
                       }
                   }
                }

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

    private void FindFile(int directoryStartIndex, List<FileInfo> directory, CancellationTokenSource cancelationToken, List<FileInfo> listOfFilesToProcess, ref int dirStartCount)
    {
        Thread.Sleep(3000);
        var foundFile = string.Empty;
        for (var directoryIndex = directoryStartIndex; directoryIndex < directory.Count(); directoryIndex+= 3)
        {
            dirStartCount++;
            try
            {
                var fileInfoEntry = directory[directoryIndex];
                
                var locked = IsFileLocked(fileInfoEntry);
                if (locked)
                {
                    Console.WriteLine($"Skipping {fileInfoEntry.FullName}. File in use.");
                    continue;
                }

                VideoInfo videoInfo;
                try
                {
                    videoInfo = new VideoInfo(fileInfoEntry.FullName, this);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    Console.WriteLine($"Skipping {fileInfoEntry.FullName}");
                    continue;
                }
                
                var fileInfo = videoInfo.InputFfmpegVideoInfo;

                if (!ConverterBackend.ShouldBeProcessed(videoInfo, _retryFailed))
                {
                    Console.WriteLine($"Skipping {fileInfoEntry.FullName} due to metadata.");
                    continue;
                }

                if (EncodeAv1)
                {
                    //Check if file is not av1, and not dolby vision
                    if (ConverterBackend.CanEncodeAv1(fileInfo))
                    {
                        cancelationToken.Cancel();
                        listOfFilesToProcess.Add(fileInfoEntry);
                        foundFile = fileInfoEntry.FullName;
                        break;
                    }
                }

                if (RemuxDolbyVision && !EncodeHevc)
                {
                    if (ConverterBackend.IsProfile7(fileInfo))
                    {
                        cancelationToken.Cancel();
                        listOfFilesToProcess.Add(fileInfoEntry);
                        foundFile = fileInfoEntry.FullName;
                        break;
                    }
                }

                if (RemuxDolbyVision && EncodeHevc)
                {
                    if (ConverterBackend.IsProfile7(fileInfo))
                    {
                        cancelationToken.Cancel();
                        listOfFilesToProcess.Add(fileInfoEntry);
                        foundFile = fileInfoEntry.FullName;
                        break;
                    }
                }

                if (EncodeHevc)
                {
                    if (ConverterBackend.CanEncodeHevc(fileInfo))
                    {
                        cancelationToken.Cancel();
                        listOfFilesToProcess.Add(fileInfoEntry);
                        foundFile = fileInfoEntry.FullName;
                        break;
                    }
                }

                Console.WriteLine($"Skipping {fileInfoEntry.FullName}");
                cancelationToken.Token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException ex)
            {
                Console.WriteLine($"Thread Canceled.");
                break;
            }
        }
        
        Console.WriteLine($"Found File to Optimize: {foundFile}");
    }

    private bool IsFileLocked(FileInfo file)
    {
        try
        {
            var originalName = file.FullName;
            var modifiedName = file.FullName + "rename";
            
            File.Move(originalName, modifiedName); 
            File.Move(modifiedName, originalName);
        }
        catch
        {
            return true;
        }

        return false;
    }
}