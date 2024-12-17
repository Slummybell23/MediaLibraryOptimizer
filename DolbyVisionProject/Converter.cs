using System.Diagnostics;

namespace DolbyVisionProject;

public class Converter
{
    public Converter(ConsoleLog console)
    {
        _console = console;
    }
    
    public ConsoleLog _console;
    
    
    //Builds list of files from input directories.
    public List<string> BuildFilesList(string? movies, string? tvShows, string? checkAll)
    {
        _console.WriteLine("Grabbing all files. Please wait...\nCan take a few minutes for large directories...");
        var allFiles = new List<string>();

        if (!String.IsNullOrWhiteSpace(movies))
        {
            _console.WriteLine("Grabbing movies...");
            AppendFiles(movies, allFiles);
        }

        if (!String.IsNullOrWhiteSpace(tvShows))
        {
            _console.WriteLine("Grabbing tv shows...");
            AppendFiles(tvShows, allFiles);
        }

        _console.WriteLine($"{allFiles.Count} files grabbed.");

        //Based on the environment var passed into the container, will return recently added files from 3 days ago if "n".
        if (checkAll.ToLower() == "n")
        {
            _console.WriteLine("Grabbing recent files...");
            var recentFilesIEnumerable = allFiles.Where(file => File.GetCreationTime(file) >= DateTime.Now.AddDays(-3));
            var recentFiles = recentFilesIEnumerable.ToList();

            return recentFiles;
        }
        
        return allFiles;
    }
    
    public List<string> BuildFilesList(string? movies, string? tvShows, List<string> encodeFiles)
    {
        _console.WriteLine("Grabbing all files. Please wait...\nCan take a few minutes for large directories...");
        var allFiles = new List<string>();

        if (!String.IsNullOrWhiteSpace(movies))
        {
            _console.WriteLine("Grabbing movies...");
            FindFiles(movies, allFiles, encodeFiles);
        }
        
        if (!String.IsNullOrWhiteSpace(tvShows) && allFiles.Count != encodeFiles.Count)
        {
            _console.WriteLine("Grabbing tv shows...");
            FindFiles(tvShows, allFiles, encodeFiles);
        }
        
        _console.WriteLine($"{allFiles.Count} files grabbed.");
        
        return allFiles;
    }
    
    
    private void AppendFiles(string? library, List<string> allFiles)
    {
        //Generates an enumerable of a directory and iterates through it to append each item to allFiles and logs the addition.
        var libraryIEnumerable = Directory.EnumerateFiles(library, "*.mkv", SearchOption.AllDirectories);
        foreach (var media in libraryIEnumerable)
        {
            allFiles.Add(media);
            _console.WriteLine(media);
        }
    }
    
    private void FindFiles(string? library, List<string> foundFiles, List<string> filesToFind)
    {
        //Generates an enumerable of a directory and iterates through it to append each item to allFiles and logs the addition.
        var libraryIEnumerable = Directory.EnumerateFiles(library, "*.mkv", SearchOption.AllDirectories);
        var foundFileCount = 0;
        foreach (var media in libraryIEnumerable)
        {
            foreach (var fileToFind in filesToFind)
            {
                if (media.Contains(fileToFind + ".mkv"))
                {
                    foundFileCount++;
                    foundFiles.Add(media);
                    _console.WriteLine(media);
                    break;
                }
            }

            if (foundFileCount >= filesToFind.Count)
                break;
        }
    }

    public bool IsProfile7(string filePath)
    {
        //Grabs file info.
        //Note: hide_banner hides program banner for easier readability and removing unnecessary text
        var command = $"ffmpeg -i '{filePath}' -hide_banner -loglevel info";

        try
        {
            var result = RunCommand(command, filePath);
            
            var configRecord = result.Contains("DOVI configuration record: version: 1.0, profile: 7");
            return configRecord;
        }
        catch (Exception ex)
        {
            _console.WriteLine($"Error detecting profile: {ex.Message}");
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
                _console.WriteLine($"Deleted: {filePath}");
            }
            catch (Exception ex)
            {
                _console.WriteLine($"Error deleting file {filePath}: {ex.Message}");
            }
        }
        else
        {
            _console.WriteLine($"File not found, skipping delete: {filePath}");
        }
    }

    private string RunCommandInWindows(string command, string file)
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
        
        var output = RunProccess(file, process);
        return output;
    }

    private string RunCommandInDocker(string command, string file)
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

        var output = RunProccess(file, process);
        return output;
    }

    private string RunProccess(string file, Process process)
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
                    _console.WriteLine($"{line} | File: {file}");
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
                    _console.WriteLine($"{line} | File: {file}");
                }
            }
        });

        //Does not execute next lines until script finishes running.
        process.WaitForExit();
        
        //Waits for the output and error monitors to end.
        Task.WaitAll(outputTask, errorTask);
            
        var combinedOutput = outputText + errorText;

        //Ignores FFmpeg warning.
        if (errorText.Contains("At least one output file must be specified"))
        {
            _console.WriteLine("Warning: FFmpeg returned a minor error (ignored):");
            _console.WriteLine(errorText);
        }
        else if (process.ExitCode != 0)
        {
            throw new Exception($"Command failed with exit code {process.ExitCode}: {errorText}");
        }

        _console.WriteLine("Process completed successfully.");
        return combinedOutput;
    }

    public string RunCommand(string command, string file)
    {
        //Program is built to run in both windows and linux
        //(Although preferably in a linux based docker container)
        if (OperatingSystem.IsWindows())
        {
            return RunCommandInWindows(command, file);
        }
        else if (OperatingSystem.IsLinux())
        {
            return RunCommandInDocker(command, file);
        }
        else
        {
            throw new NotSupportedException("Unsupported operating system.");
        }
    }
    
    //Remuxes and will Encode file if above 75Mbps
    public bool RemuxAndEncode(string filePath)
    {
        var fileConverter = new FileConverter(this, filePath);
        var converted = fileConverter.RemuxAndEncode();
        
        return converted;
    }
    
    //Only remux file from Dolby Vision Profile 7 to Profile 8
    public bool Remux(string filePath)
    {
        var fileConverter = new FileConverter(this, filePath);
        var converted = fileConverter.Remux();
        
        return converted;
    }
    
    //Only encodes file from environment variable
    public bool Encode(string filePath)
    {
        var fileConverter = new FileConverter(this, filePath);
        var converted = fileConverter.Encode();
        
        return converted;
    }
}