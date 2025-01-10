using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LibraryOptimizer;

public class ConverterBackend
{
    public ConverterBackend(ConsoleLog console)
    {
        Console = console;
    }
    
    public ConsoleLog Console;
    
    //Builds list of files from input directories.
    public List<string> BuildFilesList(List<string> libraries, string? checkAll)
    {
        Console.WriteLine("Grabbing all files. Please wait...\nCan take a few minutes for large directories...");
        var allFiles = new List<string>();

        foreach (var library in libraries)
        {
            Console.WriteLine($"Grabbing {library}...");
            AppendFiles(library, allFiles);
        }

        Console.WriteLine($"{allFiles.Count} files grabbed.");

        //Based on the environment var passed into the container, will return recently added files from 3 days ago if "n".
        if (checkAll.ToLower() == "n")
        {
            Console.WriteLine("Grabbing recent files...");
            var recentFilesIEnumerable = allFiles.Where(file => File.GetLastWriteTime(file) >= DateTime.Now.AddDays(-7));
            var recentFiles = recentFilesIEnumerable.ToList();

            return recentFiles;
        }
        
        return allFiles;
    }
    
    private void AppendFiles(string? library, List<string> allFiles)
    {
        //Generates an enumerable of a directory and iterates through it to append each item to allFiles and logs the addition.
        var libraryIEnumerable = Directory.EnumerateFiles(library, "*.mkv", SearchOption.AllDirectories);
        foreach (var media in libraryIEnumerable)
        {
            allFiles.Add(media);
            Console.WriteLine(media);
        }
    }

    public bool IsProfile7(string fileInfo)
    {
        return fileInfo.Contains("DOVI configuration record: version: 1.0, profile: 7");
    }
    
    public bool CanEncodeAv1(string filePath, string fileInfo, double bitRate)
    {
        try
        {
            var av1 = fileInfo.ToLower().Contains("video: av1");
            return !fileInfo.ToLower().Contains("video: av1") &&
                    !fileInfo.Contains("DOVI configuration record");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error detecting encodability: {ex.Message}");
            return false;
        }
    }
    
    public bool CanEncodeHevc(string filePath, string fileInfo, double bitRate)
    {
        //Grabs file info.
        //Note: hide_banner hides program banner for easier readability and removing unnecessary text
        var encodeCheckCommand = $"ffprobe -i '{filePath}' -show_entries format=bit_rate -v quiet -of csv='p=0'";

        try
        {
            var bitRateCheck = true;//bitRate > 15;
            return (!fileInfo.Contains("DOVI configuration record, profile: 7") &&
                    fileInfo.Contains("DOVI configuration record"));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error detecting encodability: {ex.Message}");
            return false;
        }
    }

    public void DeleteFile(string filePath)
    {
        if (File.Exists(filePath))
        {
            try
            {
                File.Delete(filePath);
                Console.WriteLine($"Deleted: {filePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting file {filePath}: {ex.Message}");
            }
        }
        else
        {
            Console.WriteLine($"File not found, skipping delete: {filePath}");
        }
    }

    private string RunCommandInWindows(string command, string file, bool printOutput = true)
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

    private string RunCommandInDocker(string command, string file, bool printOutput = true)
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

    private string RunProccess(string file, Process process, bool printOutput = true)
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
                //Allowed outputs get logged to a logFile in the console object.
                if (!line.Contains("Last message repeated") 
                    && !line.Contains("Skipping NAL unit"))
                {
                    outputText += line;
                    if(printOutput)
                        Console.WriteLine($"{line} | File: {file}");
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
                //Allowed outputs get logged to a logFile in the console object.
                if (!line.Contains("Last message repeated") 
                    && !line.Contains("Skipping NAL unit"))
                {
                    errorText += line;
                    if(printOutput)
                        Console.WriteLine($"{line} | File: {file}");
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
                Console.WriteLine("Warning: Returned a minor error (ignored):");
                Console.WriteLine(errorText);

            }
        }
        else if (process.ExitCode != 0)
        {
            throw new Exception($"Command failed with exit code {process.ExitCode}: {combinedOutput}");
        }

        Console.WriteLine("Process completed successfully.");
        return combinedOutput;
    }

    public string RunCommand(string command, string file, bool printOutput = true)
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
    
    //Remuxes and will Encode file if above 75Mbps
    public bool RemuxAndEncodeHevc(string filePath)
    {
        var fileConverter = new FileConverter(this, filePath);
        var converted = fileConverter.RemuxAndEncodeHevc();
        fileConverter.AppendMetadata();

        return converted;
    }
    
    //Only remux file from Dolby Vision Profile 7 to Profile 8
    public bool Remux(string filePath)
    {
        var fileConverter = new FileConverter(this, filePath);
        var converted = fileConverter.Remux();
        fileConverter.AppendMetadata();

        return converted;
    }
    
    //Only encodes file from environment variable
    public bool EncodeHevc(string filePath)
    {
        var fileConverter = new FileConverter(this, filePath);
        var converted = fileConverter.EncodeHevc();
        fileConverter.AppendMetadata();

        return converted;
    }
    
    public bool EncodeAv1(string filePath, double bitRate)
    {
        var fileConverter = new FileConverter(this, filePath, bitRate);
        var converted = fileConverter.EncodeAv1();
        fileConverter.AppendMetadata();
        
        return converted;
    }
    
    public bool ShouldBeProcessed(string filePath, bool retryFailed)
    {
        var grabMetadataCommand = $"ffprobe -i '{filePath}' -show_entries format_tags=LIBRARY_OPTIMIZER_APP -of default=noprint_wrappers=1";
        var metadata = RunCommand(grabMetadataCommand, filePath, false);

        if (metadata.Contains("Converted=True.")
            || (metadata.Contains("Converted=False.") && !retryFailed))
            return false;
        if (metadata.Contains("Converted=False.") && retryFailed)
            return true;
        
        return true;
    }

    public string FileFormatToCommand(string file)
    {
        var formatedFile = file.Replace("'", "''");
        formatedFile = formatedFile.Replace("’", "'’");

        return formatedFile;
    }
    
    public string FileRemoveFormat(string file)
    {
        var originalFile = file.Replace("''", "'");
        originalFile = originalFile.Replace("'’", "’");

        return originalFile;
    }
}