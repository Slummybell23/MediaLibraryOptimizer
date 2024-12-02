using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace DolbyVisionProject;

public abstract class Program
{
    private static ConsoleLog _console = new ConsoleLog();

    private static void Main(string[] args)
    {
        var libraryPath = string.Empty;
        var checkAll = string.Empty;
        
        if (Debugger.IsAttached)
        {
            checkAll = "y";
            libraryPath = "Z:\\Plex\\Movie";
            libraryPath = "Z:\\Plex\\Movie\\Coraline (2009)";
        }
        else
        {
            libraryPath = Environment.GetEnvironmentVariable("LIBRARY_PATH")!;
            checkAll = Environment.GetEnvironmentVariable("CHECK_ALL")!;
        }
        
        while (true)
        {
            var nonDolbyVision7 = 0;
            var failedFiles = new List<string>();
            var convertedFiles = new List<string>();
            
            _console.WriteLine("Grabbing all files. Please wait...");
            var filesIEnumerable = Directory.EnumerateFiles(libraryPath, "*.mkv", SearchOption.AllDirectories);
            var allFiles = filesIEnumerable.ToList();
            
            var recentFilesIEnumerable = allFiles.Where(file => File.GetCreationTime(file) >= DateTime.Now.AddDays(-3));
            var recentFiles = recentFilesIEnumerable.ToList();
            _console.WriteLine($"{allFiles.Count} files grabbed.");
            
            _console.WriteLine($"Check All Env Var: {checkAll}");
            _console.WriteLine(Environment.GetEnvironmentVariable("ENCODE")!);
            var directory = allFiles;
            if (checkAll.ToLower() == "n")
                directory = recentFiles;
        
            _console.WriteLine($"Processing {directory.Count} files...");
            foreach (var file in directory)
            {
                _console.LogFile(file);

                _console.LogText = new StringBuilder();
                _console.WriteLine($"Processing file: {file}");

                if (IsProfile7(file))
                {
                    _console.WriteLine($"Dolby Vision Profile 7 detected in: {file}");

                    var start = DateTime.Now;
                    var converted = ConvertToProfile8(file);

                    if (converted)
                        convertedFiles.Add(file);
                    else
                        failedFiles.Add(file);

                    var end = DateTime.Now;
                    var timeCost = end - start;
                    _console.WriteLine($"Conversion Time: {timeCost.ToString()}");

                    _console.LogFile(file);
                }
                else
                {
                    _console.WriteLine($"Skipping: {file} (not Dolby Vision Profile 7)");
                    nonDolbyVision7++;
                }
            }

            var endRunOutput = new StringBuilder();
            endRunOutput.AppendLine($"{allFiles.Count} files found");
            endRunOutput.AppendLine($"{directory.Count} files processed");
            endRunOutput.AppendLine($"{nonDolbyVision7} files skipped");
            endRunOutput.AppendLine($"{failedFiles.Count} files failed");
            endRunOutput.AppendLine($"{convertedFiles.Count} files converted");

            endRunOutput.AppendLine($"============= Converted Files ============");
            foreach (var converted in convertedFiles)
            {
                endRunOutput.AppendLine(converted);
            }
            endRunOutput.AppendLine($"=========================");
            
            endRunOutput.AppendLine($"============= Failed Files ============");
            foreach (var failed in failedFiles)
            {
                endRunOutput.AppendLine(failed);
            }
            endRunOutput.AppendLine($"=========================");

            _console.WriteLine(endRunOutput.ToString());
            
            checkAll = "n";
            _console.WriteLine("Waiting for new files... Setting to recent files");
            Thread.Sleep(TimeSpan.FromDays(1));
        }
    }

    private static bool IsProfile7(string filePath)
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
            _console.WriteLine($"Error detecting profile: {ex.Message}");
            return false;
        }
    }

    private static bool ConvertToProfile8(string filePath)
    {
        var movieName = Path.GetFileNameWithoutExtension(filePath);
        var directory = Path.GetDirectoryName(filePath);
        var hevcFile = Path.Combine(directory, $"{movieName}hevc.hevc");
        
        var profile8HevcFile = Path.Combine(directory, $"{movieName}profile8hevc.hevc");
        var rpuFile = Path.Combine(directory, $"{movieName}rpu.bin");
        var encodedHevc = Path.Combine(directory, $"{movieName}encodedHevc.hevc");
        var encodedProfile8HevcFile = Path.Combine(directory, $"{movieName}profile8encodedhevc.hevc");
        
        var outputFile = Path.Combine(Path.GetDirectoryName(filePath), "converted_" + Path.GetFileName(filePath));
        
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
            var bitRateOutput = RunCommand(encodeCheckCommand, filePath).Split().Last();
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
        
        
        var remuxCommand = string.Empty;
        if (needsEncoding) 
            remuxCommand = $"mkvmerge -o \"{outputFile}\" -D \"{filePath}\" \"{encodedProfile8HevcFile}\"";
        else
            remuxCommand = $"mkvmerge -o \"{outputFile}\" -D \"{filePath}\" \"{profile8HevcFile}\"";
        
        try
        {
            _console.WriteLine($"Extracting HEVC stream: {extractCommand}");
            RunCommand(extractCommand, filePath); //

            _console.WriteLine($"Converting to Profile 8: {convertCommand}");
            RunCommand(convertCommand, filePath); //
            
            if (needsEncoding)
            {
                _console.WriteLine($"Re Encoding {movieName}");
                _console.WriteLine($"Extracting RPU: {extractRpuCommand}");
                RunCommand(extractRpuCommand, filePath); //

                _console.WriteLine($"Encoding HEVC: {reEncodeHevcCommand}");
                RunCommand(reEncodeHevcCommand, filePath);
                DeleteFile(profile8HevcFile);

                _console.WriteLine($"Injecting RPU: {injectRpu}");
                RunCommand(injectRpu, filePath);
                DeleteFile(rpuFile);
                DeleteFile(encodedHevc);
            }
            
            _console.WriteLine($"Remuxing to MKV: {remuxCommand}");
            RunCommand(remuxCommand, filePath);
            DeleteFile(encodedProfile8HevcFile);
            DeleteFile(hevcFile); 
            DeleteFile(filePath);

            var renamedFilePath = filePath;
            File.Move(outputFile, renamedFilePath);

            _console.WriteLine($"Conversion complete: {outputFile}");
            return true;
        }
        catch (Exception ex)
        {
            _console.WriteLine($"Error during conversion: {ex.Message}");
            return false;
        }
        finally
        {
            _console.WriteLine("Cleaning up temporary files...");
            DeleteFile(hevcFile);
            DeleteFile(profile8HevcFile);
            DeleteFile(encodedProfile8HevcFile);
            DeleteFile(rpuFile);
            DeleteFile(encodedHevc);
        }
    }
    
    private static void DeleteFile(string filePath)
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
        
    private static string RunCommandAsBatch(string command, string file)
    {
        var tempBatFile = Path.Combine(Path.GetTempPath(), "temp_command.bat");
            
        if (File.Exists(tempBatFile))
            File.Delete(tempBatFile);
            
        File.WriteAllText(tempBatFile, command);

        if (!File.Exists(tempBatFile))
            throw new Exception($"Batch file not found: {tempBatFile}");
        
        _console.WriteLine($"Batch file contents:\n{File.ReadAllText(tempBatFile)}");

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
    
    private static string RunCommandAsShellScript(string command, string file)
    {
        var tempShFile = Path.Combine("/tmp", "temp_command.sh");
        
        if (File.Exists(tempShFile))
            File.Delete(tempShFile);
        
        File.WriteAllText(tempShFile, $"#!/bin/bash\n{command}");
        
        if (!File.Exists(tempShFile))
            throw new Exception($"Batch file not found: {tempShFile}");
        
        _console.WriteLine($"Batch file contents:\n{File.ReadAllText(tempShFile)}");
        
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

    private static string RunProccess(string file, ProcessStartInfo processInfo, string tempShFile)
    {
        using (var process = new Process { StartInfo = processInfo })
        {
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
                            _console.WriteLine($"{line} | File: {file}");
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
                            _console.WriteLine($"{line} | File: {file}");
                        }
                    }
                });

                process.WaitForExit();
                Task.WaitAll(outputTask, errorTask);
                
                var combinedOutput = outputText + errorText;

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
            finally
            {
                if (File.Exists(tempShFile))
                    File.Delete(tempShFile);
            }
        }
    }

    private static string RunCommand(string command, string file)
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