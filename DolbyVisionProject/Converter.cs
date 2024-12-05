using System.Diagnostics;

namespace DolbyVisionProject;

public class Converter(ConsoleLog console)
{
    public List<string> BuildFilesList(string movies,string tvShows, string checkAll)
    {
        console.WriteLine("Grabbing all files. Please wait...\nCan take a few minutes for large directories...");
        var allFiles = new List<string>();

        if (!String.IsNullOrWhiteSpace(movies))
        {
            console.WriteLine("Grabbing movies...");
            AppendFiles(movies, allFiles);
        }

        if (!String.IsNullOrWhiteSpace(tvShows))
        {
            console.WriteLine("Grabbing tv shows...");
            AppendFiles(tvShows, allFiles);
        }

        console.WriteLine($"{allFiles.Count} files grabbed.");

        var directory = allFiles;
        if (checkAll.ToLower() == "n")
        {
            console.WriteLine("Grabbing recent files...");
            var recentFilesIEnumerable = allFiles.Where(file => File.GetCreationTime(file) >= DateTime.Now.AddDays(-3));
            var recentFiles = recentFilesIEnumerable.ToList();

            directory = recentFiles;
        }
        
        return directory;
    }

    private void AppendFiles(string library, List<string> allFiles)
    {
        var libraryIEnumerable = Directory.EnumerateFiles(library, "*.mkv", SearchOption.AllDirectories);
        foreach (var media in libraryIEnumerable)
        {
            allFiles.Add(media);
            console.WriteLine(media);
        }
    }

    public bool IsProfile7(string filePath)
    {
        var command = $"ffmpeg -i \"{filePath}\" -hide_banner -loglevel info";

        try
        {
            var result = RunCommand(command, filePath);
            
            var configRecord = result.Contains("DOVI configuration record: version: 1.0, profile: 7");
            return configRecord;
        }
        catch (Exception ex)
        {
            console.WriteLine($"Error detecting profile: {ex.Message}");
            return false;
        }
    }

    public bool ConvertFile(string filePath)
    {
        var movieName = Path.GetFileNameWithoutExtension(filePath);
        var directory = Path.GetDirectoryName(filePath)!;
        var hevcFile = Path.Combine(directory, $"{movieName}hevc.hevc");
        
        var profile8HevcFile = Path.Combine(directory, $"{movieName}profile8hevc.hevc");
        var rpuFile = Path.Combine(directory, $"{movieName}rpu.bin");
        var encodedHevc = Path.Combine(directory, $"{movieName}encodedHevc.hevc");
        var encodedProfile8HevcFile = Path.Combine(directory, $"{movieName}profile8encodedhevc.hevc");
        
        var outputFile = Path.Combine(Path.GetDirectoryName(filePath)!, "converted_" + Path.GetFileName(filePath));
        
        var extractCommand = $"ffmpeg -i \"{filePath}\" -map 0:v:0 -bufsize 64M -c copy -bsf:v hevc_metadata=level=150 \"{hevcFile}\"";
        var convertCommand = $"dovi_tool -m 2 convert -i \"{hevcFile}\" -o \"{profile8HevcFile}\"";

        var needsEncoding = false;
        var encodingEnvVar = Environment.GetEnvironmentVariable("ENCODE");
        if(encodingEnvVar != null && encodingEnvVar.ToLower() == "y")
            needsEncoding = true;

        var reEncodeHevcCommand = string.Empty;
        var extractRpuCommand = string.Empty;
        var injectRpu = string.Empty;
        if (needsEncoding)
        {
            var encodeCheckCommand = $"ffprobe -i \"{filePath}\" -show_entries format=bit_rate -v quiet -of csv=\"p=0\"";
            var bitRateOutput = Enumerable.Last<string>(RunCommand(encodeCheckCommand, filePath).Split());
            var bitRate = int.Parse(bitRateOutput);
            needsEncoding = bitRate > 75000;
            
            extractRpuCommand = $"dovi_tool extract-rpu -i \"{profile8HevcFile}\" -o \"{rpuFile}\"";

            var nvencCheckCommand = "ffmpeg -hide_banner -encoders | findstr nvenc";
            var nvencOutput = RunCommand(nvencCheckCommand, filePath);
            if(nvencOutput.Contains("NVIDIA NVENC hevc encoder"))
                reEncodeHevcCommand = $"ffmpeg -i \"{profile8HevcFile}\" -c:v hevc_nvenc -preset fast -cq 19 -maxrate 80M -bufsize 25M -rc-lookahead 32 -c:a copy \"{encodedHevc}\"";
            else
                reEncodeHevcCommand = $"ffmpeg -i \"{profile8HevcFile}\" -c:v libx265 -preset slow -b:v 60000k -maxrate 60000k -vbv-bufsize 20000 -x265-params \"keyint=48:min-keyint=24\" -an \"{encodedHevc}\"";
        
            injectRpu = $"dovi_tool inject-rpu -i \"{encodedHevc}\" -r \"{rpuFile}\" -o \"{encodedProfile8HevcFile}\"\n";
        }
        
        
        string remuxCommand;
        if (needsEncoding) 
            remuxCommand = $"mkvmerge -o \"{outputFile}\" -D \"{filePath}\" \"{encodedProfile8HevcFile}\"";
        else
            remuxCommand = $"mkvmerge -o \"{outputFile}\" -D \"{filePath}\" \"{profile8HevcFile}\"";
        
        try
        {
            console.WriteLine($"Extracting HEVC stream: {extractCommand}");
            RunCommand(extractCommand, filePath); //

            console.WriteLine($"Converting to Profile 8: {convertCommand}");
            RunCommand(convertCommand, filePath); //
            
            if (needsEncoding)
            {
                console.WriteLine($"Re Encoding {movieName}");
                console.WriteLine($"Extracting RPU: {extractRpuCommand}");
                RunCommand(extractRpuCommand, filePath); //

                console.WriteLine($"Encoding HEVC: {reEncodeHevcCommand}");
                RunCommand(reEncodeHevcCommand, filePath);
                DeleteFile(profile8HevcFile);

                console.WriteLine($"Injecting RPU: {injectRpu}");
                RunCommand(injectRpu, filePath);
                DeleteFile(rpuFile);
                DeleteFile(encodedHevc);
            }

            console.WriteLine($"Remuxing to MKV: {remuxCommand}");
            RunCommand(remuxCommand, filePath);
            DeleteFile(encodedProfile8HevcFile);
            DeleteFile(hevcFile); 
            DeleteFile(filePath);

            var renamedFilePath = filePath;
            File.Move(outputFile, renamedFilePath);

            console.WriteLine($"Conversion complete: {outputFile}");
            return true;
        }
        catch (Exception ex)
        {
            console.WriteLine($"Error during conversion: {ex.Message}");
            return false;
        }
        finally
        {
            console.WriteLine("Cleaning up temporary files...");
            DeleteFile(hevcFile);
            DeleteFile(profile8HevcFile);
            DeleteFile(encodedProfile8HevcFile);
            DeleteFile(rpuFile);
            DeleteFile(encodedHevc);
        }
    }

    private void DeleteFile(string filePath)
    {
        if (File.Exists(filePath))
        {
            try
            {
                File.Delete(filePath);
                console.WriteLine($"Deleted: {filePath}");
            }
            catch (Exception ex)
            {
                console.WriteLine($"Error deleting file {filePath}: {ex.Message}");
            }
        }
        else
        {
            console.WriteLine($"File not found, skipping delete: {filePath}");
        }
    }

    private string RunCommandAsBatch(string command, string file)
    {
        var tempBatFile = Path.Combine(Path.GetTempPath(), "temp_command.bat");
            
        if (File.Exists(tempBatFile))
            File.Delete(tempBatFile);
            
        File.WriteAllText(tempBatFile, command);

        if (!File.Exists(tempBatFile))
            throw new Exception($"Batch file not found: {tempBatFile}");

        console.WriteLine($"Batch file contents:\n{File.ReadAllText(tempBatFile)}");

        var processInfo = new ProcessStartInfo
        {
            FileName = tempBatFile,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var output = RunProccess(file, processInfo, tempBatFile);
        return output;
    }

    private string RunCommandAsShellScript(string command, string file)
    {
        var tempShFile = Path.Combine("/tmp", "temp_command.sh");
        
        if (File.Exists(tempShFile))
            File.Delete(tempShFile);
        
        File.WriteAllText(tempShFile, $"#!/bin/bash\n{command}");
        
        if (!File.Exists(tempShFile))
            throw new Exception($"Batch file not found: {tempShFile}");

        console.WriteLine($"Batch file contents:\n{File.ReadAllText(tempShFile)}");
        
        var chmodProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "/bin/chmod",
                Arguments = $"+x {tempShFile}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        chmodProcess.Start();
        chmodProcess.WaitForExit();

        var processInfo = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = tempShFile,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var output = RunProccess(file, processInfo, tempShFile);
        return output;
    }

    private string RunProccess(string file, ProcessStartInfo processInfo, string tempShFile)
    {
        using var process = new Process();
        process.StartInfo = processInfo;
        try
        {
            process.Start();

            var outputText = "";
            var outputTask = Task.Run(() =>
            {
                string line;
                while ((line = process.StandardOutput.ReadLine()) != null)
                {
                    if (!line.Contains("Last message repeated") 
                        && !line.Contains("Skipping NAL unit")
                        && !line.Contains("q=-1.0 size="))
                    {
                        outputText += line;
                        console.WriteLine($"{line} | File: {file}");
                    }
                }
            });

            var errorText = "";
            var errorTask = Task.Run(() =>
            {
                string line;
                while ((line = process.StandardError.ReadLine()!) != null)
                {
                    if (!line.Contains("Last message repeated") 
                        && !line.Contains("Skipping NAL unit")
                        && !line.Contains("q=-1.0 size="))
                    {
                        errorText += line;
                        console.WriteLine($"{line} | File: {file}");
                    }
                }
            });

            process.WaitForExit();
            Task.WaitAll(outputTask, errorTask);
                
            var combinedOutput = outputText + errorText;

            if (errorText.Contains("At least one output file must be specified"))
            {
                console.WriteLine("Warning: FFmpeg returned a minor error (ignored):");
                console.WriteLine(errorText);
            }
            else if (process.ExitCode != 0)
            {
                throw new Exception($"Command failed with exit code {process.ExitCode}: {errorText}");
            }

            console.WriteLine("Process completed successfully.");
            return combinedOutput;
        }
        finally
        {
            if (File.Exists(tempShFile))
                File.Delete(tempShFile);
        }
    }

    private string RunCommand(string command, string file)
    {
        if (OperatingSystem.IsWindows())
        {
            return RunCommandAsBatch(command, file);
        }
        else if (OperatingSystem.IsLinux())
        {
            return RunCommandAsShellScript(command, file);
        }
        else
        {
            throw new NotSupportedException("Unsupported operating system.");
        }
    }
}