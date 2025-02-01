
namespace LibraryOptimizer;

public class FileConverter
{
    private string _inputFilePath;
    private string _commandInputFilePath;
    private string _commandOutputFile;
    private string _outputFile;
    
    private string _videoName;
    private string _profile8HevcFile;
    private string _rpuFile;
    private string _encodedHevc;
    private string _encodedProfile8HevcFile;
    private string _hevcFile;

    private string _extractCommand;
    private string _convertCommand;

    private string _reEncodeHevcProfile8Command;
    private string _encodeAv1Command;
    private string _extractProfile8RpuCommand;
    private string _injectRpu;

    private string _remuxCommandEncoded;
    private string _remuxCommand;

    private bool _converted;
    private string _failedReason = string.Empty;
    
    #region Constructors

    public FileConverter(string inputFilePath)
    {
        //inputFilePath inserted here is already formated for commands
        _commandInputFilePath = inputFilePath;
        
        //Build file names (with directory path attatched)
        _videoName = Path.GetFileNameWithoutExtension(inputFilePath);
        var directory = Path.GetDirectoryName(inputFilePath)!;
        _hevcFile = Path.Combine(directory, $"{_videoName}hevc.hevc");
        
        _profile8HevcFile = Path.Combine(directory, $"{_videoName}profile8hevc.hevc");
        _rpuFile = Path.Combine(directory, $"{_videoName}rpu.bin");
        _encodedHevc = Path.Combine(directory, $"{_videoName}encodedHevc.hevc");
        _encodedProfile8HevcFile = Path.Combine(directory, $"{_videoName}profile8encodedhevc.hevc");
        
        _commandOutputFile = Path.Combine(Path.GetDirectoryName(inputFilePath)!, "converted_" + Path.GetFileName(inputFilePath));
        
        //======================Dolby Vision 7 -> 8 Remuxing=============================
        //Copies the HEVC stream of the mkv container to a seperate hevc file.
        //Sets metadata level to 150 for easier proccessing on slower decoders in case hevc is level 153.
        //Difference is negligible unless you're doing 8k resolution files.
        _extractCommand = $"ffmpeg -i '{inputFilePath}' -map 0:v:0 -bufsize 64M -c copy -bsf:v hevc_metadata=level=150 '{_hevcFile}'";
        
        //Converts the hevc file from Dolby Vision Profile 7 to Dolby Vision Profile 8.
        _convertCommand = $"dovi_tool -m 2 convert -i '{_hevcFile}' -o '{_profile8HevcFile}'";

        //Extracting rpu file which contains the Dolby Vision metadata. Otherwise, FFmpeg loses Dolby Vision metadata.
        _extractProfile8RpuCommand = $"dovi_tool extract-rpu -i '{_hevcFile}' -o '{_rpuFile}'";
        
        //After encoding, inject the rpu file back into the HEVC so Dolby Vision metadata is retained.
        _injectRpu = $"dovi_tool inject-rpu -i '{_encodedHevc}' -r '{_rpuFile}' -o '{_encodedProfile8HevcFile}'";
        
        //Remuxes the hevc file into new mkv container, overriding the original video stream with new encoded and/or converted hevc file.
        _remuxCommandEncoded = $"mkvmerge -o '{_commandOutputFile}' -D '{inputFilePath}' '{_encodedProfile8HevcFile}'";
        _remuxCommand = $"mkvmerge -o '{_commandOutputFile}' -D '{inputFilePath}' '{_profile8HevcFile}'";
        //======================Dolby Vision 7 -> 8 Remuxing=============================
        
        //Remove formatting for deleting.
        _inputFilePath = ConverterBackend.FileRemoveFormat(inputFilePath);
        _outputFile = ConverterBackend.FileRemoveFormat(_commandOutputFile);
        
        _videoName = Path.GetFileNameWithoutExtension(_inputFilePath);
        directory = Path.GetDirectoryName(_inputFilePath)!;
        _hevcFile = Path.Combine(directory, $"{_videoName}hevc.hevc");
        
        _profile8HevcFile = Path.Combine(directory, $"{_videoName}profile8hevc.hevc");
        _rpuFile = Path.Combine(directory, $"{_videoName}rpu.bin");
        _encodedHevc = Path.Combine(directory, $"{_videoName}encodedHevc.hevc");
        _encodedProfile8HevcFile = Path.Combine(directory, $"{_videoName}profile8encodedhevc.hevc");
    }
    
    //AV1
    public FileConverter(string inputFilePath, double bitRate, bool isNvidia) : this(inputFilePath)
    {
        if (isNvidia)
        {
            //NVIDIA NVENC
            if (bitRate >= 12)
            {
                _encodeAv1Command = $"ffmpeg -i '{inputFilePath}' -map 0:v:0 -map 0:a? -map 0:s? -c:v av1_nvenc -cq 25 -preset p7 -c:a copy -c:s copy -map_metadata 0 -map_chapters 0 '{_commandOutputFile}'";
            }
            else if (bitRate <= 12 && bitRate >= 7)
            {
                _encodeAv1Command = $"ffmpeg -i '{inputFilePath}' -map 0:v:0 -map 0:a? -map 0:s? -c:v av1_nvenc -cq 29 -preset p7 -c:a copy -c:s copy -map_metadata 0 -map_chapters 0 '{_commandOutputFile}'";
            }
            else if (bitRate <= 7)
            {
                _encodeAv1Command = $"ffmpeg -i '{inputFilePath}' -map 0:v:0 -map 0:a? -map 0:s? -c:v av1_nvenc -cq 32 -preset p7 -c:a copy -c:s copy -map_metadata 0 -map_chapters 0 '{_commandOutputFile}'";
            }
        }
        else
        {
            //INTEL ARC
            if (bitRate >= 27)
            {
                _encodeAv1Command = $"ffmpeg -i '{inputFilePath}' -map 0:v:0 -map 0:a? -map 0:s? -c:v av1_qsv -global_quality 3 -preset 1 -c:a copy -c:s copy -map_metadata 0 -map_chapters 0 '{_commandOutputFile}'";
            }
            else if (bitRate <= 27 && bitRate >= 12)
            {
                _encodeAv1Command = $"ffmpeg -i '{inputFilePath}' -map 0:v:0 -map 0:a? -map 0:s? -c:v av1_qsv -global_quality 18 -preset 1 -c:a copy -c:s copy -map_metadata 0 -map_chapters 0 '{_commandOutputFile}'";
            }
            else if (bitRate <= 12 && bitRate >= 7)
            {
                _encodeAv1Command = $"ffmpeg -i '{inputFilePath}' -map 0:v:0 -map 0:a? -map 0:s? -c:v av1_qsv -global_quality 21 -preset 1 -c:a copy -c:s copy -map_metadata 0 -map_chapters 0 '{_commandOutputFile}'";
            }
            else if (bitRate <= 7)
            {
                _encodeAv1Command = $"ffmpeg -i '{inputFilePath}' -map 0:v:0 -map 0:a? -map 0:s? -c:v av1_qsv -global_quality 23 -preset 1 -c:a copy -c:s copy -map_metadata 0 -map_chapters 0 '{_commandOutputFile}'";
            }
        }
    }

    //HEVC
    public FileConverter(string inputFilePath, bool isNvidia) : this(inputFilePath)
    { 
        var directory = Path.GetDirectoryName(inputFilePath)!;
        _hevcFile = Path.Combine(directory, $"{_videoName}hevc.hevc");
        _encodedProfile8HevcFile = Path.Combine(directory, $"{_videoName}profile8encodedhevc.hevc");

        if(isNvidia)
            //NVIDIA NVENC
            _reEncodeHevcProfile8Command = $"ffmpeg -i '{_hevcFile}' -c:v hevc_nvenc -preset p7 -cq 3 -c:a copy '{_encodedHevc}'";
        else
            //INTEL ARC
            _reEncodeHevcProfile8Command = $"ffmpeg -i '{_hevcFile}' -c:v hevc_qsv -preset 1 -global_quality 3 -c:a copy '{_encodedHevc}'";
        
        directory = Path.GetDirectoryName(_inputFilePath)!;
        _encodedHevc = Path.Combine(directory, $"{_videoName}encodedHevc.hevc");
        _hevcFile = Path.Combine(directory, $"{_videoName}hevc.hevc");
    }

    #endregion

    #region File Operations

    public ConverterStatus RemuxAndEncodeHevc()
    {
        //Runs command sequence
        try
        {
            ConsoleLog.WriteLine($"Extracting HEVC stream: {_extractCommand}");
            ConverterBackend.RunCommand(_extractCommand, _inputFilePath);

            ConsoleLog.WriteLine($"Converting to Profile 8: {_convertCommand}");
            ConverterBackend.RunCommand(_convertCommand, _inputFilePath);
            
            ConsoleLog.WriteLine($"Re Encoding {_videoName}");
            ConsoleLog.WriteLine($"Extracting RPU: {_extractProfile8RpuCommand}");
            ConverterBackend.RunCommand(_extractProfile8RpuCommand, _inputFilePath);

            ConsoleLog.WriteLine($"Encoding HEVC: {_reEncodeHevcProfile8Command}");
            
            var failedOutput = string.Empty;
            try
            {
                failedOutput = ConverterBackend.RunCommand(_reEncodeHevcProfile8Command, _inputFilePath, false);
            }
            catch
            {
                ConsoleLog.WriteLine(failedOutput);
                throw;
            }
            
            ConverterBackend.DeleteFile(_profile8HevcFile);

            ConsoleLog.WriteLine($"Injecting RPU: {_injectRpu}");
            ConverterBackend.RunCommand(_injectRpu, _inputFilePath);
            ConverterBackend.DeleteFile(_rpuFile);
            ConverterBackend.DeleteFile(_encodedHevc);
            
            ConsoleLog.WriteLine($"Remuxing Encoded to MKV: {_remuxCommandEncoded}");
            ConverterBackend.RunCommand(_remuxCommandEncoded, _inputFilePath);
            
            var oldFileSize = new FileInfo(_inputFilePath).Length/1000000;
            var newFileSize = new FileInfo(_outputFile).Length/1000000;

            ConsoleLog.WriteLine($"Old file size: {oldFileSize} mb");
            ConsoleLog.WriteLine($"New file size: {newFileSize} mb");
        
            if (newFileSize > oldFileSize)
            {
                ConsoleLog.WriteLine("Encoded file larger than original file. Deleting encoded file.");
                ConverterBackend.DeleteFile(_outputFile);
            }
            else
            {
                ConverterBackend.DeleteFile(_inputFilePath);
                ConverterBackend.DeleteFile(_encodedProfile8HevcFile);
                ConverterBackend.DeleteFile(_hevcFile);
                
                //Renames new mkv container to the original file and deletes original file.
                File.Move(_outputFile, _inputFilePath, true);

                ConsoleLog.WriteLine($"Conversion complete: {_outputFile}");

                _converted = true;
                return ConverterStatus.Success;
            }

            ConsoleLog.WriteLine($"Remuxing Non Encoded to MKV: {_remuxCommand}");
            ConverterBackend.RunCommand(_remuxCommand, _inputFilePath);
            
            ConverterBackend.DeleteFile(_encodedProfile8HevcFile);
            
            ConverterBackend.DeleteFile(_hevcFile);
            //Renames new mkv container to the original file and deletes original file.
            File.Move(_outputFile, _inputFilePath, true);

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
            ConverterBackend.RunCommand(_extractCommand, _inputFilePath);

            ConsoleLog.WriteLine($"Converting to Profile 8: {_convertCommand}");
            ConverterBackend.RunCommand(_convertCommand, _inputFilePath);
            
            ConsoleLog.WriteLine($"Remuxing to MKV: {_remuxCommand}");
            ConverterBackend.RunCommand(_remuxCommand, _inputFilePath);
            ConverterBackend.DeleteFile(_hevcFile);
            
            //Renames new mkv container to the original file and deletes original file.
            File.Move(_outputFile, _inputFilePath, true);

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
            ConverterBackend.RunCommand(_extractCommand, _inputFilePath);
            
            ConsoleLog.WriteLine($"Extracting RPU: {_extractProfile8RpuCommand}");
            ConverterBackend.RunCommand(_extractProfile8RpuCommand, _inputFilePath);

            ConsoleLog.WriteLine($"Encoding HEVC: {_reEncodeHevcProfile8Command}");

            var failedOutput = string.Empty;
            try
            {
                failedOutput = ConverterBackend.RunCommand(_reEncodeHevcProfile8Command, _inputFilePath, false);
            }
            catch
            {
                ConsoleLog.WriteLine(failedOutput);
                throw;
            }
            
            ConverterBackend.DeleteFile(_profile8HevcFile);

            ConsoleLog.WriteLine($"Injecting RPU: {_injectRpu}");
            ConverterBackend.RunCommand(_injectRpu, _inputFilePath);
            ConverterBackend.DeleteFile(_rpuFile);
            ConverterBackend.DeleteFile(_encodedHevc);
            
            ConsoleLog.WriteLine($"Remuxing to MKV: {_remuxCommandEncoded}");
            ConverterBackend.RunCommand(_remuxCommandEncoded, _inputFilePath);
            ConverterBackend.DeleteFile(_encodedProfile8HevcFile);
            ConverterBackend.DeleteFile(_hevcFile);
            
            var oldFileSize = new FileInfo(_inputFilePath).Length/1000000;
            var newFileSize = new FileInfo(_outputFile).Length/1000000;

            ConsoleLog.WriteLine($"Old file size: {oldFileSize} mb");
            ConsoleLog.WriteLine($"New file size: {newFileSize} mb");
            
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
                File.Move(_outputFile, _inputFilePath, true);

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
            ConverterBackend.RunCommand(_encodeAv1Command, _inputFilePath);

            var oldFileSize = new FileInfo(_inputFilePath).Length/1000000;
            var newFileSize = new FileInfo(_outputFile).Length/1000000;

            ConsoleLog.WriteLine($"Old file size: {oldFileSize} mb");
            ConsoleLog.WriteLine($"New file size: {newFileSize} mb");

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
                File.Move(_outputFile, _inputFilePath, true);

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

    #endregion

    public void AppendMetadata()
    {
        var directory = Path.GetDirectoryName(_inputFilePath)!;
        var customMetadataFile = Path.Combine(directory, $"{_videoName}Metadata.mkv");

        customMetadataFile = ConverterBackend.FileFormatToCommand(customMetadataFile);
        var insertMetadataCommand = $"ffmpeg -i '{_commandInputFilePath}' -map 0 -c:v copy -c:a copy -c:s copy -metadata LIBRARY_OPTIMIZER_APP='Converted={_converted}. Reason={_failedReason}' '{customMetadataFile}'";
        customMetadataFile = ConverterBackend.FileRemoveFormat(customMetadataFile);
        
        var failOutput = string.Empty;
        try
        {
            ConsoleLog.WriteLine($"Inserting metadata 'LIBRARY_OPTIMIZER_APP=Converted={_converted}. Reason={_failedReason}' into {_inputFilePath}");
            failOutput = ConverterBackend.RunCommand(insertMetadataCommand, _inputFilePath, false);
            
            File.Move(customMetadataFile, _inputFilePath, true);
        }
        catch
        {
            ConsoleLog.WriteLine($"Metadata fail: {failOutput}");
            ConsoleLog.WriteLine("Appending metadata failed. Continuing...");
        }
    }
}