using System.Runtime.CompilerServices;

namespace LibraryOptimizer;

public class FileConverter
{
    public FileConverter(string filePath)
    {
        _commandFilePath = filePath;
        
        //Builds command strings
        
        //Build file names (with directory path attatched)
        _videoName = Path.GetFileNameWithoutExtension(filePath);
        var directory = Path.GetDirectoryName(filePath)!;
        _hevcFile = Path.Combine(directory, $"{_videoName}hevc.hevc");
        
        _profile8HevcFile = Path.Combine(directory, $"{_videoName}profile8hevc.hevc");
        _rpuFile = Path.Combine(directory, $"{_videoName}rpu.bin");
        _encodedHevc = Path.Combine(directory, $"{_videoName}encodedHevc.hevc");
        _encodedProfile8HevcFile = Path.Combine(directory, $"{_videoName}profile8encodedhevc.hevc");
        
        _commandOutputFile = Path.Combine(Path.GetDirectoryName(filePath)!, "converted_" + Path.GetFileName(filePath));
        //===================================================
        
        //Build commands to execute in sequence
        
        //Copies the HEVC stream of the mkv container to a seperate hevc file.
        //Sets metadata level to 150 for easier proccessing on slower decoders in case hevc is level 153.
        //Difference is negligible unless you're doing 8k resolution files.
        _extractCommand = $"ffmpeg -i '{filePath}' -map 0:v:0 -bufsize 64M -c copy -bsf:v hevc_metadata=level=150 '{_hevcFile}'";
        
        //Converts the hevc file from Dolby Vision Profile 7 to Dolby Vision Profile 8.
        _convertCommand = $"dovi_tool -m 2 convert -i '{_hevcFile}' -o '{_profile8HevcFile}'";

        //Extracting rpu file which contains the Dolby Vision metadata. Otherwise, FFmpeg loses Dolby Vision metadata.
        _extractProfile8RpuCommand = $"dovi_tool extract-rpu -i '{_hevcFile}' -o '{_rpuFile}'";
        
        //Nvidia GPU Encoding
        _reEncodeHevcProfile8Command = $"ffmpeg -i '{_hevcFile}' -c:v hevc_nvenc -preset p7 -cq 3 -c:a copy '{_encodedHevc}'";
            
        //Nvidia GPU Encoding
        //_encodeAv1Command = $"ffmpeg -i '{filePath}' -map 0:v:0 -map 0:a? -map 0:s? -c:v av1_nvenc -cq 25 -preset p7 -c:a copy -c:s copy -map_metadata 0 -map_chapters 0 '{_commandOutputFile}'";
        
        //Intel Arc Encoding
        //_encodeAv1Command = $"ffmpeg -i '{filePath}' -map 0 -c:v av1_qsv -global_quality 20 -preset 1 -c:a copy -c:s copy -map_metadata 0 -map_chapters 0 '{_commandOutputFile}'";
        
        //CPU Software Encoding
        //EncodeAV1Command = $"ffmpeg -i '{filePath}' -c:v libsvtav1 -preset 6 -crf 15 -c:s copy -c:a copy -map_metadata 0 -map_chapters 0 '{OutputFile}'";
        
        //After encoding, inject the rpu file back into the HEVC so Dolby Vision metadata is retained.
        _injectRpu = $"dovi_tool inject-rpu -i '{_encodedHevc}' -r '{_rpuFile}' -o '{_encodedProfile8HevcFile}'";
        
        //Remuxes the hevc file into new mkv container, overriding the original video stream with new encoded and/or converted hevc file.
        _remuxCommandEncoded = $"mkvmerge -o '{_commandOutputFile}' -D '{filePath}' '{_encodedProfile8HevcFile}'";
        _remuxCommand = $"mkvmerge -o '{_commandOutputFile}' -D '{filePath}' '{_profile8HevcFile}'";
        //=====================================
        
        //Remove formatting for deleting.
        _filePath = ConverterBackend.FileRemoveFormat(filePath);
        _outputFile = ConverterBackend.FileRemoveFormat(_commandOutputFile);
        
        _videoName = Path.GetFileNameWithoutExtension(_filePath);
        directory = Path.GetDirectoryName(_filePath)!;
        _hevcFile = Path.Combine(directory, $"{_videoName}hevc.hevc");
        
        _profile8HevcFile = Path.Combine(directory, $"{_videoName}profile8hevc.hevc");
        _rpuFile = Path.Combine(directory, $"{_videoName}rpu.bin");
        _encodedHevc = Path.Combine(directory, $"{_videoName}encodedHevc.hevc");
        _encodedProfile8HevcFile = Path.Combine(directory, $"{_videoName}profile8encodedhevc.hevc");
    }
    
    //Constructor Chaining
    public FileConverter(string filePath, double bitRate, bool isNvidia) : this(filePath)
    {
        if (isNvidia)
        {
            //NVIDIA NVENC
            if (bitRate >= 12)
            {
                _encodeAv1Command = $"ffmpeg -i '{filePath}' -map 0:v:0 -map 0:a? -map 0:s? -c:v av1_nvenc -cq 25 -preset p7 -c:a copy -c:s copy -map_metadata 0 -map_chapters 0 '{_commandOutputFile}'";
            }
            else if (bitRate <= 12 && bitRate >= 7)
            {
                _encodeAv1Command = $"ffmpeg -i '{filePath}' -map 0:v:0 -map 0:a? -map 0:s? -c:v av1_nvenc -cq 29 -preset p7 -c:a copy -c:s copy -map_metadata 0 -map_chapters 0 '{_commandOutputFile}'";
            }
            else if (bitRate <= 7)
            {
                _encodeAv1Command = $"ffmpeg -i '{filePath}' -map 0:v:0 -map 0:a? -map 0:s? -c:v av1_nvenc -cq 32 -preset p7 -c:a copy -c:s copy -map_metadata 0 -map_chapters 0 '{_commandOutputFile}'";
            }
        }

        if (!isNvidia)
        {
            //INTEL ARC
            if (bitRate >= 12)
            {
                _encodeAv1Command = $"ffmpeg -i '{filePath}' -map 0 -c:v av1_qsv -global_quality 20 -preset 1 -c:a copy -c:s copy -map_metadata 0 -map_chapters 0 '{_commandOutputFile}'";
            }
            else if (bitRate <= 12 && bitRate >= 7)
            {
                _encodeAv1Command = $"ffmpeg -i '{filePath}' -map 0 -c:v av1_qsv -global_quality 22 -preset 1 -c:a copy -c:s copy -map_metadata 0 -map_chapters 0 '{_commandOutputFile}'";
            }
            else if (bitRate <= 7)
            {
                _encodeAv1Command = $"ffmpeg -i '{filePath}' -map 0 -c:v av1_qsv -global_quality 24 -preset 1 -c:a copy -c:s copy -map_metadata 0 -map_chapters 0 '{_commandOutputFile}'";
            }
        }
    }

    private ConverterBackend ConverterBackend;
    private string _filePath;
    private string _commandFilePath;

    private string _videoName;
    private string _profile8HevcFile;
    private string _rpuFile;
    private string _encodedHevc;
    private string _encodedProfile8HevcFile;
    private string _hevcFile;
    private string _commandOutputFile;
    private string _outputFile;

    private string _extractCommand;
    private string _convertCommand;

    private string _reEncodeHevcProfile8Command;
    private string _encodeAv1Command;
    private string _extractProfile8RpuCommand;
    private string _injectRpu;

    private string _remuxCommandEncoded;
    private string _remuxCommand;

    private bool? _converted = null;
    private string _failedReason = string.Empty;

    public ConverterStatus RemuxAndEncodeHevc()
    {
        //Runs command sequence
        try
        {
            ConsoleLog.WriteLine($"Extracting HEVC stream: {_extractCommand}");
            ConverterBackend.RunCommand(_extractCommand, _filePath);

            ConsoleLog.WriteLine($"Converting to Profile 8: {_convertCommand}");
            ConverterBackend.RunCommand(_convertCommand, _filePath);
            
            ConsoleLog.WriteLine($"Re Encoding {_videoName}");
            ConsoleLog.WriteLine($"Extracting RPU: {_extractProfile8RpuCommand}");
            ConverterBackend.RunCommand(_extractProfile8RpuCommand, _filePath);

            ConsoleLog.WriteLine($"Encoding HEVC: {_reEncodeHevcProfile8Command}");
            
            var failedOutput = string.Empty;
            try
            {
                failedOutput = ConverterBackend.RunCommand(_reEncodeHevcProfile8Command, _filePath, false);
            }
            catch
            {
                ConsoleLog.WriteLine(failedOutput);
                throw;
            }
            
            ConverterBackend.DeleteFile(_profile8HevcFile);

            ConsoleLog.WriteLine($"Injecting RPU: {_injectRpu}");
            ConverterBackend.RunCommand(_injectRpu, _filePath);
            ConverterBackend.DeleteFile(_rpuFile);
            ConverterBackend.DeleteFile(_encodedHevc);
            
            ConsoleLog.WriteLine($"Remuxing Encoded to MKV: {_remuxCommandEncoded}");
            ConverterBackend.RunCommand(_remuxCommandEncoded, _filePath);
            
            var oldFileSize = new FileInfo(_filePath).Length;
            var newFileSize = new FileInfo(_outputFile).Length;

            ConsoleLog.WriteLine($"Old file size: {oldFileSize}");
            ConsoleLog.WriteLine($"New file size: {newFileSize}");
        
            if (newFileSize > oldFileSize)
            {
                ConsoleLog.WriteLine("Encoded file larger than original file. Deleting encoded file.");
                ConverterBackend.DeleteFile(_outputFile);
            }
            else
            {
                ConverterBackend.DeleteFile(_filePath);
                ConverterBackend.DeleteFile(_encodedProfile8HevcFile);
                ConverterBackend.DeleteFile(_hevcFile);
                
                //Renames new mkv container to the original file and deletes original file.
                File.Move(_outputFile, _filePath, true);

                ConsoleLog.WriteLine($"Conversion complete: {_outputFile}");

                _converted = true;
                return ConverterStatus.Success;
            }

            ConsoleLog.WriteLine($"Remuxing Non Encoded to MKV: {_remuxCommand}");
            ConverterBackend.RunCommand(_remuxCommand, _filePath);
            
            ConverterBackend.DeleteFile(_encodedProfile8HevcFile);
            
            ConverterBackend.DeleteFile(_hevcFile);
            //Renames new mkv container to the original file and deletes original file.
            File.Move(_outputFile, _filePath, true);

            ConsoleLog.WriteLine($"Conversion complete: {_outputFile}");
            
            _converted = true;
            return ConverterStatus.Success;
        }
        catch (Exception ex)
        {
            ConsoleLog.WriteLine($"Error during conversion: {ex.Message}");
            
            _converted = false;
            return ConverterStatus.Failed;
        }
        finally
        {
            //No matter what, if a fail occurs or if a success, always clear out files generated during script runs.
            ConsoleLog.WriteLine("Cleaning up temporary files...");
            ConverterBackend.DeleteFile(_hevcFile);
            ConverterBackend.DeleteFile(_profile8HevcFile);
            ConverterBackend.DeleteFile(_encodedProfile8HevcFile);
            ConverterBackend.DeleteFile(_rpuFile);
            ConverterBackend.DeleteFile(_encodedHevc);
            ConverterBackend.DeleteFile(_outputFile);
        }
    }
    
    public ConverterStatus Remux()
    {
        //Runs command sequence
        try
        {
            ConsoleLog.WriteLine($"Extracting HEVC stream: {_extractCommand}");
            ConverterBackend.RunCommand(_extractCommand, _filePath);

            ConsoleLog.WriteLine($"Converting to Profile 8: {_convertCommand}");
            ConverterBackend.RunCommand(_convertCommand, _filePath);
            
            ConsoleLog.WriteLine($"Remuxing to MKV: {_remuxCommand}");
            ConverterBackend.RunCommand(_remuxCommand, _filePath);
            ConverterBackend.DeleteFile(_hevcFile);
            
            //Renames new mkv container to the original file and deletes original file.
            File.Move(_outputFile, _filePath, true);

            ConsoleLog.WriteLine($"Conversion complete: {_outputFile}");
            
            _converted = true;
            return ConverterStatus.Success;
        }
        catch (Exception ex)
        {
            ConsoleLog.WriteLine($"Error during conversion: {ex.Message}");
            
            _converted = false;
            return ConverterStatus.Failed;
        }
        finally
        {
            //No matter what, if a fail occurs or if a success, always clear out files generated during script runs.
            ConsoleLog.WriteLine("Cleaning up temporary files...");
            ConverterBackend.DeleteFile(_hevcFile);
            ConverterBackend.DeleteFile(_profile8HevcFile);
            ConverterBackend.DeleteFile(_outputFile);
        }
    }
    
    public ConverterStatus EncodeHevc()
    {
        //Runs command sequence
        try
        {
            ConsoleLog.WriteLine($"Extracting HEVC stream: {_extractCommand}");
            ConverterBackend.RunCommand(_extractCommand, _filePath);
            
            ConsoleLog.WriteLine($"Extracting RPU: {_extractProfile8RpuCommand}");
            ConverterBackend.RunCommand(_extractProfile8RpuCommand, _filePath);

            ConsoleLog.WriteLine($"Encoding HEVC: {_reEncodeHevcProfile8Command}");

            var failedOutput = string.Empty;
            try
            {
                failedOutput = ConverterBackend.RunCommand(_reEncodeHevcProfile8Command, _filePath, false);
            }
            catch
            {
                ConsoleLog.WriteLine(failedOutput);
                throw;
            }
            
            ConverterBackend.DeleteFile(_profile8HevcFile);

            ConsoleLog.WriteLine($"Injecting RPU: {_injectRpu}");
            ConverterBackend.RunCommand(_injectRpu, _filePath);
            ConverterBackend.DeleteFile(_rpuFile);
            ConverterBackend.DeleteFile(_encodedHevc);
            
            ConsoleLog.WriteLine($"Remuxing to MKV: {_remuxCommandEncoded}");
            ConverterBackend.RunCommand(_remuxCommandEncoded, _filePath);
            ConverterBackend.DeleteFile(_encodedProfile8HevcFile);
            ConverterBackend.DeleteFile(_hevcFile);
            
            var oldFileSize = new FileInfo(_filePath).Length;
            var newFileSize = new FileInfo(_outputFile).Length;

            ConsoleLog.WriteLine($"Old file size: {oldFileSize}");
            ConsoleLog.WriteLine($"New file size: {newFileSize}");
            
            if (newFileSize > oldFileSize)
            {
                ConsoleLog.WriteLine("Encoded file larger than original file. Deleting encoded file.");
                ConverterBackend.DeleteFile(_outputFile);

                _failedReason = "Output file larger than input.";
                _converted = false;
                return ConverterStatus.Failed;
            }
            else
            {
                //Renames new mkv container to the original file and deletes original file.
                File.Move(_outputFile, _filePath, true);

                ConsoleLog.WriteLine($"Conversion complete: {_outputFile}");
            }

            _converted = true;
            return ConverterStatus.Success;
        }
        catch (Exception ex)
        {
            ConsoleLog.WriteLine($"Error during conversion: {ex.Message}");

            _converted = false;
            return ConverterStatus.Failed;
        }
        finally
        {
            //No matter what, if a fail occurs or if a success, always clear out files generated during script runs.
            ConsoleLog.WriteLine("Cleaning up temporary files...");
            ConverterBackend.DeleteFile(_hevcFile);
            ConverterBackend.DeleteFile(_profile8HevcFile);
            ConverterBackend.DeleteFile(_encodedProfile8HevcFile);
            ConverterBackend.DeleteFile(_rpuFile);
            ConverterBackend.DeleteFile(_encodedHevc);
            ConverterBackend.DeleteFile(_outputFile);
        }
    }
    
    public ConverterStatus EncodeAv1()
    {
        //Runs command sequence
        try
        {
            ConsoleLog.WriteLine($"Re Encoding {_videoName} To AV1: {_encodeAv1Command}");
            ConverterBackend.RunCommand(_encodeAv1Command, _filePath);

            var oldFileSize = new FileInfo(_filePath).Length;
            var newFileSize = new FileInfo(_outputFile).Length;

            ConsoleLog.WriteLine($"Old file size: {oldFileSize}");
            ConsoleLog.WriteLine($"New file size: {newFileSize}");

            if (newFileSize > oldFileSize)
            {
                ConsoleLog.WriteLine("Encoded file larger than original file. Deleting encoded file.");
                ConverterBackend.DeleteFile(_outputFile);

                _failedReason = "Output file larger than input.";
                _converted = false;
                return ConverterStatus.Failed;
            }
            else
            {
                //Renames new mkv container to the original file and deletes original file.
                File.Move(_outputFile, _filePath, true);

                ConsoleLog.WriteLine($"Conversion complete: {_outputFile}");
            }

            _converted = true;
            return ConverterStatus.Success;
        }
        catch (Exception ex)
        {
            ConsoleLog.WriteLine($"Error during conversion: {ex.Message}");

            _converted = false;
            return ConverterStatus.Failed;
        }
        finally
        {
            ConverterBackend.DeleteFile(_outputFile);
        }
    }

    public void AppendMetadata()
    {
        var directory = Path.GetDirectoryName(_filePath)!;
        var customMetadataFile = Path.Combine(directory, $"{_videoName}Metadata.mkv");

        customMetadataFile = ConverterBackend.FileFormatToCommand(customMetadataFile);
        var insertMetadataCommand = $"ffmpeg -i '{_commandFilePath}' -map 0 -c:v copy -c:a copy -c:s copy -metadata LIBRARY_OPTIMIZER_APP='Converted={_converted}. Reason={_failedReason}' '{customMetadataFile}'";
        customMetadataFile = ConverterBackend.FileRemoveFormat(customMetadataFile);
        
        var failOutput = string.Empty;
        try
        {
            ConsoleLog.WriteLine($"Inserting metadata 'LIBRARY_OPTIMIZER_APP=Converted={_converted}. Reason={_failedReason}' into {_filePath}");
            failOutput = ConverterBackend.RunCommand(insertMetadataCommand, _filePath, false);
            
            File.Move(customMetadataFile, _filePath, true);
        }
        catch
        {
            ConsoleLog.WriteLine($"Metadata fail: {failOutput}");
            ConsoleLog.WriteLine("Appending metadata failed. Continuing...");
        }
    }
}