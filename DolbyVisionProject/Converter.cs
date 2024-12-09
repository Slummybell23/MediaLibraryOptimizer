using System.Diagnostics;

namespace DolbyVisionProject;

public class Converter(ConsoleLog console)
{
    //Builds list of files from input directories.
    public List<string> BuildFilesList(string movies, string tvShows, string checkAll)
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

        //Based on the environment var passed into the container, will return recently added files from 3 days ago if "n".
        if (checkAll.ToLower() == "n")
        {
            console.WriteLine("Grabbing recent files...");
            var recentFilesIEnumerable = allFiles.Where(file => File.GetCreationTime(file) >= DateTime.Now.AddDays(-3));
            var recentFiles = recentFilesIEnumerable.ToList();

            return recentFiles;
        }
        
        return allFiles;
    }

    private void AppendFiles(string library, List<string> allFiles)
    {
        //Generates an enumerable of a directory and iterates through it to append each item to allFiles and logs the addition.
        var libraryIEnumerable = Directory.EnumerateFiles(library, "*.mkv", SearchOption.AllDirectories);
        foreach (var media in libraryIEnumerable)
        {
            allFiles.Add(media);
            console.WriteLine(media);
        }
    }

    public bool IsProfile7(string filePath)
    {
        //Grabs file info.
        //Note: hide_banner hides program banner for easier readability and removing unnecessary text
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
        //Builds command strings
        
        //Build file names (with directory path attatched)
        var movieName = Path.GetFileNameWithoutExtension(filePath);
        var directory = Path.GetDirectoryName(filePath)!;
        var hevcFile = Path.Combine(directory, $"{movieName}hevc.hevc");
        
        var profile8HevcFile = Path.Combine(directory, $"{movieName}profile8hevc.hevc");
        var rpuFile = Path.Combine(directory, $"{movieName}rpu.bin");
        var encodedHevc = Path.Combine(directory, $"{movieName}encodedHevc.hevc");
        var encodedProfile8HevcFile = Path.Combine(directory, $"{movieName}profile8encodedhevc.hevc");
        
        var outputFile = Path.Combine(Path.GetDirectoryName(filePath)!, "converted_" + Path.GetFileName(filePath));
        //===================================================
        
        //Build commands to execute in sequence
        
        //Copies the HEVC stream of the mkv container to a seperate hevc file.
        //Sets metadata level to 150 for easier proccessing on slower decoders in case hevc is level 153.
        //Difference is negligible unless you're doing 8k resolution movies.
        var extractCommand = $"ffmpeg -i \"{filePath}\" -map 0:v:0 -bufsize 64M -c copy -bsf:v hevc_metadata=level=150 \"{hevcFile}\"";
        
        //Converts the hevc file from Dolby Vision Profile 7 to Dolby Vision Profile 8.
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
            //Checks if file meets bitrate threshold to be reencoded.
            //Command displays just the bitrate of the file in kbps.
            var encodeCheckCommand = $"ffprobe -i \"{filePath}\" -show_entries format=bit_rate -v quiet -of csv=\"p=0\"";
            var bitRateOutput = RunCommand(encodeCheckCommand, filePath).Split().Last();
            var bitRate = int.Parse(bitRateOutput);
            
            //If bitrate is above 75mbps, reencode.
            needsEncoding = bitRate > 75000;
            
            //Extracting rpu file which contains the Dolby Vision metadata. Otherwise, FFmpeg loses Dolby Vision metadata.
            extractRpuCommand = $"dovi_tool extract-rpu -i \"{profile8HevcFile}\" -o \"{rpuFile}\"";

            //Checks if system has nvenc for hardware encoding.
            var nvencCheckCommand = "ffmpeg -hide_banner -encoders | findstr nvenc";
            var nvencOutput = RunCommand(nvencCheckCommand, filePath);
            if(nvencOutput.Contains("NVIDIA NVENC hevc encoder"))
            //Uses nvidia gpu accelerated encoding.
            //WARNING: Slow, but far faster than cpu encoding.
            //cq of 19 provides good quality without extreme bitrates. Goal is to lower bitrate to around 60mbps
            //while retaining high quality and making decoding easier on slower decoders.
                reEncodeHevcCommand = $"ffmpeg -i \"{profile8HevcFile}\" -c:v hevc_nvenc -preset fast -cq 19 -maxrate 80M -bufsize 25M -rc-lookahead 32 -c:a copy \"{encodedHevc}\"";
            else
            //Uses cpu encoding.
            //WARNING: Highly advised against due to being extremely slow on cpu.
                reEncodeHevcCommand = $"ffmpeg -i \"{profile8HevcFile}\" -c:v libx265 -preset slow -b:v 60000k -maxrate 60000k -vbv-bufsize 20000 -x265-params \"keyint=48:min-keyint=24\" -an \"{encodedHevc}\"";
        
            //After encoding, inject the rpu file back into the HEVC so Dolby Vision metadata is retained.
            injectRpu = $"dovi_tool inject-rpu -i \"{encodedHevc}\" -r \"{rpuFile}\" -o \"{encodedProfile8HevcFile}\"\n";
        }
        
        //Remuxes the hevc file into new mkv container, overriding the original video stream with new encoded and/or converted hevc file.
        string remuxCommand;
        if (needsEncoding) 
            remuxCommand = $"mkvmerge -o \"{outputFile}\" -D \"{filePath}\" \"{encodedProfile8HevcFile}\"";
        else
            remuxCommand = $"mkvmerge -o \"{outputFile}\" -D \"{filePath}\" \"{profile8HevcFile}\"";
        //=====================================
        
        //Runs command sequence
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
            
            //Renames new mkv container to the original file and deletes original file.
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
            //No matter what, if a fail occurs or if a success, always clear out files generated during script runs.
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

    private string RunCommandAsPowershell(string command, string file)
    {
        //Specifies starting arguments for running powershell script
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
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

    private string RunCommandAsBash(string command, string file)
    {
        //Specifies starting arguments for running bash script
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"{command}\"",
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
                    && !line.Contains("Skipping NAL unit")
                    && !line.Contains("q=-1.0 size="))
                {
                    outputText += line;
                    console.WriteLine($"{line} | File: {file}");
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
                    && !line.Contains("Skipping NAL unit")
                    && !line.Contains("q=-1.0 size="))
                {
                    errorText += line;
                    console.WriteLine($"{line} | File: {file}");
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

    private string RunCommand(string command, string file)
    {
        //Program is built to run in both windows and linux
        //(Although preferably in a linux based docker container)
        if (OperatingSystem.IsWindows())
        {
            return RunCommandAsPowershell(command, file);
        }
        else if (OperatingSystem.IsLinux())
        {
            return RunCommandAsBash(command, file);
        }
        else
        {
            throw new NotSupportedException("Unsupported operating system.");
        }
    }
}