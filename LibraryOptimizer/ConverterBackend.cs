using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
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
        var unSortedFiles = directory.GetFiles("*.mkv", SearchOption.AllDirectories);
        
        allFiles.AddRange(unSortedFiles);
    }

    #endregion

    #region File Operation Checks

    public static bool ShouldBeProcessed(VideoInfo videoInfo, bool retryFailed)
    {
        var regex = new Regex("(LIBRARY_OPTIMIZER_APP:)(.*)");
        var match = regex.Match(videoInfo.InputFfmpegVideoInfo).Value;
        
        if ((match.Contains("Converted=True.")
            || (match.Contains("Converted=False.") && !retryFailed)) 
            && !IsProfile7(videoInfo.InputFfmpegVideoInfo))
            return false;
        if (match.Contains("Converted=False.") && retryFailed)
            return true;
        
        return true;
    }
    
    public static bool IsProfile7(string fileInfo)
    {
        return fileInfo.Contains("DOVI configuration record: version: 1.0, profile: 7");
    }
    
    public static bool CanEncodeAv1(string fileInfo)
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
    
    public static bool CanEncodeHevc(string fileInfo)
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

    private static string RunCommandInWindows(string command, string file, bool saveOutput = true, bool printOutput = false)
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
        
        var output = RunProcess(file, process, saveOutput, printOutput);
        return output;
    }

    private static string RunCommandInDocker(string command, string file, bool saveOutput = true, bool printOutput = false)
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

        var output = RunProcess(file, process, saveOutput, printOutput);
        return output;
    }

    private static string RunProcess(string file, Process process, bool saveOutput = true, bool printOutput = false)
    {
        var token = Program._cancellationToken.Token;
        
        using var registration = token.Register(() => {
            try
            {
                process.Kill(entireProcessTree: true);
            } 
            catch { }
        });

        var outputBuilder = new StringBuilder();
        var errorBuilder  = new StringBuilder();

        process.OutputDataReceived += (_, programOutput) =>
        {
            if (programOutput.Data is null) return;
            if (programOutput.Data.Contains("Last message repeated") || programOutput.Data.Contains("Skipping NAL unit"))
                return;
            outputBuilder.AppendLine(programOutput.Data);
            if (saveOutput)
                ConsoleLog.WriteLine(programOutput.Data);
            else if (printOutput)
                Console.WriteLine($"{programOutput.Data} | File: {file}");
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            if (e.Data.Contains("Last message repeated") || e.Data.Contains("Skipping NAL unit"))
                return;
            errorBuilder.AppendLine(e.Data);
            if (saveOutput)
                ConsoleLog.WriteLine(e.Data);
            else if (printOutput)
                Console.WriteLine($"{e.Data} | File: {file}");
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            process.WaitForExitAsync(token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            } 
            catch { }
            throw;
        }

        // Combine captured output
        var combined = outputBuilder.ToString() + errorBuilder.ToString();

        // Handle FFmpeg warnings
        var err = errorBuilder.ToString();
        if (err.Contains("At least one output file must be specified") 
            || err.Contains("Error splitting the argument list: Option not found"))
        {
            if (saveOutput)
            {
                ConsoleLog.WriteLine("Warning: Returned a minor error (ignored):");
                ConsoleLog.WriteLine(err);
            }
        }
        else if (process.ExitCode != 0)
        {
            throw new Exception($"Command failed with exit code {process.ExitCode}: {combined}");
        }

        ConsoleLog.WriteLine("Process completed successfully.");
        return combined;
    }
    
    public static string RunCommand(string command, string file, bool saveOutput = true, bool printOutput = false)
    {
        Program._cancellationToken.Token.ThrowIfCancellationRequested();
        
        //Program is built to run in both windows and linux
        //(Although preferably in a linux based docker container)
        if (OperatingSystem.IsWindows())
        {
            return RunCommandInWindows(command, file, saveOutput, printOutput);
        }
        else if (OperatingSystem.IsLinux())
        {
            return RunCommandInDocker(command, file, saveOutput, printOutput);
        }
        else
        {
            throw new NotSupportedException("Unsupported operating system.");
        }
    }

    #endregion

    #region File Operations

    //Remuxes and will Encode file if above 75Mbps
    public static ConverterStatusEnum RemuxAndEncodeHevc(VideoInfo videoInfo)
    {
        videoInfo.SetVideoInfoCommands();
        var converted = videoInfo.RemuxAndEncodeHevc();
        videoInfo.AppendMetadata();

        return converted;
    }
    
    //Only remux file from Dolby Vision Profile 7 to Profile 8
    public static ConverterStatusEnum Remux(VideoInfo videoInfo)
    {
        videoInfo.SetVideoInfoCommands();
        var converted = videoInfo.Remux();
        videoInfo.AppendMetadata();

        return converted;
    }
    
    public static ConverterStatusEnum EncodeHevc(VideoInfo videoInfo)
    {
        videoInfo.SetVideoInfoCommands();
        var converted = videoInfo.EncodeHevc();
        videoInfo.AppendMetadata();

        return converted;
    }
    
    public static ConverterStatusEnum EncodeAv1(VideoInfo videoInfo)
    {
        videoInfo.SetVideoInfoCommandsWithAv1();
        var converted = videoInfo.EncodeAv1();
        videoInfo.AppendMetadata();
        
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