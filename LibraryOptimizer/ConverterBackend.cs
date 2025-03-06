using System.Diagnostics;
using LibraryOptimizer.Enums;

namespace LibraryOptimizer;

public abstract class ConverterBackend
{
    #region DirectoryBuilder

    //Builds list of files from input directories.
    public static IEnumerable<FileInfo> BuildFilesList(List<string> libraries, string checkAll)
    {
        ConsoleLog.WriteLine("Grabbing all files. Please wait...\nCan take a few minutes for large directories...");
        var allFiles = new List<FileInfo>();
        foreach (var library in libraries)
        {
            ConsoleLog.WriteLine($"Grabbing {library}..."); 
            GetFiles(library, allFiles);
        }
        
        var sortedFiles = allFiles.OrderByDescending(r => r.CreationTime);
        
        //Based on the environment var passed into the container, will return recently added files from 3 days ago if "n".
        if (checkAll.ToLower() != "n") 
            return sortedFiles;
        
        ConsoleLog.WriteLine("Grabbing recent files...");

        var recentInDir = sortedFiles.Where(file => file.LastWriteTime >= DateTime.Now.AddDays(-7));
            
        return recentInDir;
    }
    
    private static void GetFiles(string library, List<FileInfo> allFiles)
    {
        //Generates an enumerable of a directory and iterates through it to append each item to allFiles and logs the addition.
        var directory = new DirectoryInfo(library);
        //Old directory.GetFiles("*.mkv", SearchOption.AllDirectories);
        var unSortedFiles = directory.GetFiles("*.*", SearchOption.AllDirectories)
            .Where(f => f.FullName.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase));
        
        allFiles.AddRange(unSortedFiles);
    }

    #endregion

    #region File Operation Checks

    public static bool ShouldBeProcessed(string filePath, bool retryFailed)
    {
        var grabMetadataCommand = $"ffprobe -i '{filePath}' -show_entries format_tags=LIBRARY_OPTIMIZER_APP -of default=noprint_wrappers=1";
        
        var metadataOrFail = string.Empty;
        try
        {
            metadataOrFail = RunCommand(grabMetadataCommand, filePath, false);
        }
        catch(Exception ex)
        {
            Console.WriteLine(ex.Message);
            
            return false;
        } 

        if (metadataOrFail.Contains("Converted=True.")
            || (metadataOrFail.Contains("Converted=False.") && !retryFailed))
            return false;
        if (metadataOrFail.Contains("Converted=False.") && retryFailed)
            return true;
        
        return true;
    }
    
    public static bool IsProfile7(string fileInfo)
    {
        return fileInfo.Contains("DOVI configuration record: version: 1.0, profile: 7");
    }
    
    public static bool CanEncodeAv1(string filePath, string fileInfo, double bitRate)
    {
        try
        {
            return !fileInfo.ToLower().Contains("video: av1") &&
                   !fileInfo.Contains("DOVI configuration record");
        }
        catch (Exception ex)
        {
            ConsoleLog.WriteLine($"Error detecting encodability: {ex.Message}");
            return false;
        }
    }
    
    public static bool CanEncodeHevc(string filePath, string fileInfo, double bitRate)
    {
        try
        {
            return (!fileInfo.Contains("DOVI configuration record, profile: 7") &&
                    fileInfo.Contains("DOVI configuration record"));
        }
        catch (Exception ex)
        {
            ConsoleLog.WriteLine($"Error detecting encodability: {ex.Message}");
            return false;
        }
    }

    #endregion

    #region Run Command

    private static string RunCommandInWindows(string command, string file, bool printOutput = true)
    {
        //Specifies starting arguments for running powershell script
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "pwsh.exe",
                Arguments = $"-Command \"{command}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        
        var output = RunProccess(file, process, printOutput);
        return output;
    }

    private static string RunCommandInDocker(string command, string file, bool printOutput = true)
    {
        //Specifies starting arguments for running bash script
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "pwsh",
                Arguments = $"-Command \"{command}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        var output = RunProccess(file, process, printOutput);
        return output;
    }

    private static string RunProccess(string file, Process process, bool printOutput = true)
    {
        process.Start();
        
        //Generates 2 threaded tasks to monitor the output stream of the script file.
        
        //Monitors non erroring output.
        var outputText = "";
        var outputTask = Task.Run(() =>
        {
            string line;
            while ((line = process.StandardOutput.ReadLine()!) != null)
            {
                //Ignores specific outputs
                //Allowed outputs get logged to a logFile in the ConsoleLog object.
                if (!line.Contains("Last message repeated") 
                    && !line.Contains("Skipping NAL unit"))
                {
                    outputText += line;
                    if(printOutput)
                        ConsoleLog.WriteLine($"{line} | File: {file}");
                }
            }
        });
        
        //Monitors error output.
        var errorText = "";
        var errorTask = Task.Run(() =>
        {
            string line;
            while ((line = process.StandardError.ReadLine()!) != null)
            {
                //Ignores specific outputs
                //Allowed outputs get logged to a logFile in the ConsoleLog object.
                if (!line.Contains("Last message repeated") 
                    && !line.Contains("Skipping NAL unit"))
                {
                    errorText += line;
                    if(printOutput)
                        ConsoleLog.WriteLine($"{line} | File: {file}");
                }
            }
        });

        //Does not execute next lines until script finishes running.
        process.WaitForExit();
        
        //Waits for the output and error monitors to end.
        Task.WaitAll(outputTask, errorTask);
            
        var combinedOutput = outputText + errorText;

        //Ignores FFmpeg warning.
        if (errorText.Contains("At least one output file must be specified")
            || errorText.Contains("Error splitting the argument list: Option not found"))
        {
            if (printOutput)
            {
                ConsoleLog.WriteLine("Warning: Returned a minor error (ignored):");
                ConsoleLog.WriteLine(errorText);

            }
        }
        else if (process.ExitCode != 0)
        {
            throw new Exception($"Command failed with exit code {process.ExitCode}: {combinedOutput}");
        }

        ConsoleLog.WriteLine("Process completed successfully.");
        return combinedOutput;
    }

    public static string RunCommand(string command, string file, bool printOutput = true)
    {
        //Program is built to run in both windows and linux
        //(Although preferably in a linux based docker container)
        if (OperatingSystem.IsWindows())
        {
            return RunCommandInWindows(command, file, printOutput);
        }
        else if (OperatingSystem.IsLinux())
        {
            return RunCommandInDocker(command, file, printOutput);
        }
        else
        {
            throw new NotSupportedException("Unsupported operating system.");
        }
    }

    #endregion

    #region File Operations

    //Remuxes and will Encode file if above 75Mbps
    public static ConverterStatusEnum RemuxAndEncodeHevc(string filePath, LibraryOptimizer.LibraryOptimizer optimizerSettings)
    {
        var fileConverter = new FileConverter(filePath, optimizerSettings);
        var converted = fileConverter.RemuxAndEncodeHevc();
        fileConverter.AppendMetadata();

        return converted;
    }
    
    //Only remux file from Dolby Vision Profile 7 to Profile 8
    public static ConverterStatusEnum Remux(string filePath, LibraryOptimizer.LibraryOptimizer optimizerSettings)
    {
        var fileConverter = new FileConverter(filePath, optimizerSettings);
        var converted = fileConverter.Remux();
        fileConverter.AppendMetadata();

        return converted;
    }
    
    public static ConverterStatusEnum EncodeHevc(string filePath, LibraryOptimizer.LibraryOptimizer optimizerSettings)
    {
        var fileConverter = new FileConverter(filePath, optimizerSettings);
        var converted = fileConverter.EncodeHevc();
        fileConverter.AppendMetadata();

        return converted;
    }
    
    public static ConverterStatusEnum EncodeAv1(string filePath, double bitRate, LibraryOptimizer.LibraryOptimizer optimizerSettings)
    {
        var fileConverter = new FileConverter(filePath, bitRate, optimizerSettings);
        var converted = fileConverter.EncodeAv1();
        fileConverter.AppendMetadata();
        
        return converted;
    }

    #endregion

    public static string FileFormatToCommand(string file)
    {
        var formatedFile = file.Replace("'", "''");
        formatedFile = formatedFile.Replace("’", "'’");

        return formatedFile;
    }
    
    public static string FileRemoveFormat(string file)
    {
        var originalFile = file.Replace("''", "'");
        originalFile = originalFile.Replace("'’", "’");

        return originalFile;
    }
    
    public static void DeleteFile(string filePath)
    {
        if (File.Exists(filePath))
        {
            try
            {
                File.Delete(filePath);
                ConsoleLog.WriteLine($"Deleted: {filePath}");
            }
            catch (Exception ex)
            {
                ConsoleLog.WriteLine($"Error deleting file {filePath}: {ex.Message}");
            }
        }
        else
        {
            ConsoleLog.WriteLine($"File not found, skipping delete: {filePath}");
        }
    }
}