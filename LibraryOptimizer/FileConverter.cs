using System.Runtime.CompilerServices;

namespace LibraryOptimizer;

public class FileConverter
{
    public FileConverter(ConverterBackend converterBackend, string filePath)
    {
        _converterBackend = converterBackend;
        _console = converterBackend.Console;
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
        _extractProfile7RpuCommand = $"dovi_tool extract-rpu -i '{_profile8HevcFile}' -o '{_rpuFile}'";

        //Extracting rpu file which contains the Dolby Vision metadata. Otherwise, FFmpeg loses Dolby Vision metadata.
        _extractProfile8RpuCommand = $"dovi_tool extract-rpu -i '{_hevcFile}' -o '{_rpuFile}'";
        
        //Nvidia GPU Encoding
        _reEncodeHevcProfile7Command = $"ffmpeg -i '{_profile8HevcFile}' -c:v hevc_nvenc -preset p7 -cq 3 -c:a copy '{_encodedHevc}'";
        _reEncodeHevcProfile8Command = $"ffmpeg -i '{_hevcFile}' -c:v hevc_nvenc -preset p7 -cq 3 -c:a copy '{_encodedHevc}'";
            
        //Nvidia GPU Encoding
        _encodeAv1Command = $"ffmpeg -i '{filePath}' -map 0:v:0 -map 0:a? -map 0:s? -c:v av1_nvenc -cq 25 -preset p7 -c:a copy -c:s copy -map_metadata 0 -map_chapters 0 '{_commandOutputFile}'";
        
        //Intel Arc Encoding
        //EncodeAV1Command = $"ffmpeg -i '{filePath}' -map 0 -c:v av1_qsv -global_quality 20 -preset 1 -c:a copy -c:s copy -map_metadata 0 -map_chapters 0 '{OutputFile}'";
        
        //CPU Software Encoding
        //EncodeAV1Command = $"ffmpeg -i '{filePath}' -c:v libsvtav1 -preset 6 -crf 15 -c:s copy -c:a copy -map_metadata 0 -map_chapters 0 '{OutputFile}'";
        
        //After encoding, inject the rpu file back into the HEVC so Dolby Vision metadata is retained.
        _injectRpu = $"dovi_tool inject-rpu -i '{_encodedHevc}' -r '{_rpuFile}' -o '{_encodedProfile8HevcFile}'";
        
        //Remuxes the hevc file into new mkv container, overriding the original video stream with new encoded and/or converted hevc file.
        _remuxCommandEncoded = $"mkvmerge -o '{_commandOutputFile}' -D '{filePath}' '{_encodedProfile8HevcFile}'";
        _remuxCommand = $"mkvmerge -o '{_commandOutputFile}' -D '{filePath}' '{_profile8HevcFile}'";
        //=====================================
        
        //Remove formatting for deleting.
        _filePath = _converterBackend.FileRemoveFormat(filePath);
        _outputFile = _converterBackend.FileRemoveFormat(_commandOutputFile);
        
        _videoName = Path.GetFileNameWithoutExtension(_filePath);
        directory = Path.GetDirectoryName(_filePath)!;
        _hevcFile = Path.Combine(directory, $"{_videoName}hevc.hevc");
        
        _profile8HevcFile = Path.Combine(directory, $"{_videoName}profile8hevc.hevc");
        _rpuFile = Path.Combine(directory, $"{_videoName}rpu.bin");
        _encodedHevc = Path.Combine(directory, $"{_videoName}encodedHevc.hevc");
        _encodedProfile8HevcFile = Path.Combine(directory, $"{_videoName}profile8encodedhevc.hevc");
    }
    
    //Constructor Chaining
    public FileConverter(ConverterBackend converterBackend, string filePath, double bitRate) : this(converterBackend, filePath)
    {
        if (bitRate >= 12)
        {
            _encodeAv1Command = $"ffmpeg -i '{filePath}' -map 0:v:0 -map 0:a? -map 0:s? -c:v av1_nvenc -cq 25 -preset p7 -c:a copy -c:s copy -map_metadata 0 -map_chapters 0 '{_commandOutputFile}'";
        }
        else if (bitRate <= 11 && bitRate >= 7)
        {
            _encodeAv1Command = $"ffmpeg -i '{filePath}' -map 0:v:0 -map 0:a? -map 0:s? -c:v av1_nvenc -cq 29 -preset p7 -c:a copy -c:s copy -map_metadata 0 -map_chapters 0 '{_commandOutputFile}'";
        }
        else if (bitRate <= 6)
        {
            _encodeAv1Command = $"ffmpeg -i '{filePath}' -map 0:v:0 -map 0:a? -map 0:s? -c:v av1_nvenc -cq 32 -preset p7 -c:a copy -c:s copy -map_metadata 0 -map_chapters 0 '{_commandOutputFile}'";
        }
    }

    private ConverterBackend _converterBackend;
    private ConsoleLog _console;
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

    private string _reEncodeHevcProfile7Command;
    private string _reEncodeHevcProfile8Command;
    private string _encodeAv1Command;
    private string _extractProfile7RpuCommand;
    private string _extractProfile8RpuCommand;
    private string _injectRpu;

    private string _remuxCommandEncoded;
    private string _remuxCommand;

    private bool? _converted = null;
    private string _failedReason = string.Empty;

    public bool RemuxAndEncodeHevc()
    {
        //Runs command sequence
        try
        {
            _console.WriteLine($"Extracting HEVC stream: {_extractCommand}");
            _converterBackend.RunCommand(_extractCommand, _filePath);

            _console.WriteLine($"Converting to Profile 8: {_convertCommand}");
            _converterBackend.RunCommand(_convertCommand, _filePath);
            
            var encodeCheckCommand = $"ffprobe -i '{_filePath}' -show_entries format=bit_rate -v quiet -of csv='p=0'";
            var bitRateOutput = _converterBackend.RunCommand(encodeCheckCommand, _filePath).Split().Last();
            var bitRate = double.Parse(bitRateOutput) / 1000000.0;
            
            //If bitrate is above 75mbps, reencode.
            var needsEncoding = bitRate > 15;
            if (needsEncoding)
            {
                _console.WriteLine($"Re Encoding {_videoName}");
                _console.WriteLine($"Extracting RPU: {_extractProfile7RpuCommand}");
                _converterBackend.RunCommand(_extractProfile7RpuCommand, _filePath);

                _console.WriteLine($"Encoding HEVC: {_reEncodeHevcProfile7Command}");
                _converterBackend.RunCommand(_reEncodeHevcProfile7Command, _filePath);
                _converterBackend.DeleteFile(_profile8HevcFile);

                _console.WriteLine($"Injecting RPU: {_injectRpu}");
                _converterBackend.RunCommand(_injectRpu, _filePath);
                _converterBackend.DeleteFile(_rpuFile);
                _converterBackend.DeleteFile(_encodedHevc);
                
                _console.WriteLine($"Remuxing Encoded to MKV: {_remuxCommandEncoded}");
                _converterBackend.RunCommand(_remuxCommandEncoded, _filePath);
                
                var oldFileSize = new FileInfo(_filePath).Length;
                var newFileSize = new FileInfo(_outputFile).Length;

                _console.WriteLine($"Old file size: {oldFileSize}");
                _console.WriteLine($"New file size: {newFileSize}");
            
                if (newFileSize > oldFileSize)
                {
                    _console.WriteLine("Encoded file larger than original file. Deleting encoded file.");
                    _converterBackend.DeleteFile(_outputFile);
                }
                else
                {
                    _converterBackend.DeleteFile(_filePath);
                    _converterBackend.DeleteFile(_encodedProfile8HevcFile);
                    _converterBackend.DeleteFile(_hevcFile);
                    
                    //Renames new mkv container to the original file and deletes original file.
                    File.Move(_outputFile, _filePath, true);

                    _console.WriteLine($"Conversion complete: {_outputFile}");

                    _converted = true;
                    return true;
                }
                
            }

            _console.WriteLine($"Remuxing Non Encoded to MKV: {_remuxCommand}");
            _converterBackend.RunCommand(_remuxCommand, _filePath);
            
            _converterBackend.DeleteFile(_encodedProfile8HevcFile);
            
            _converterBackend.DeleteFile(_hevcFile);
            //Renames new mkv container to the original file and deletes original file.
            File.Move(_outputFile, _filePath, true);

            _console.WriteLine($"Conversion complete: {_outputFile}");
            
            _converted = true;
            return true;
        }
        catch (Exception ex)
        {
            _console.WriteLine($"Error during conversion: {ex.Message}");
            
            _converted = false;
            return false;
        }
        finally
        {
            //No matter what, if a fail occurs or if a success, always clear out files generated during script runs.
            _console.WriteLine("Cleaning up temporary files...");
            _converterBackend.DeleteFile(_hevcFile);
            _converterBackend.DeleteFile(_profile8HevcFile);
            _converterBackend.DeleteFile(_encodedProfile8HevcFile);
            _converterBackend.DeleteFile(_rpuFile);
            _converterBackend.DeleteFile(_encodedHevc);
            _converterBackend.DeleteFile(_outputFile);
        }
    }
    
    public bool Remux()
    {
        //Runs command sequence
        try
        {
            _console.WriteLine($"Extracting HEVC stream: {_extractCommand}");
            _converterBackend.RunCommand(_extractCommand, _filePath);

            _console.WriteLine($"Converting to Profile 8: {_convertCommand}");
            _converterBackend.RunCommand(_convertCommand, _filePath);
            
            _console.WriteLine($"Remuxing to MKV: {_remuxCommand}");
            _converterBackend.RunCommand(_remuxCommand, _filePath);
            _converterBackend.DeleteFile(_hevcFile);
            
            //Renames new mkv container to the original file and deletes original file.
            File.Move(_outputFile, _filePath, true);

            _console.WriteLine($"Conversion complete: {_outputFile}");
            
            _converted = true;
            return true;
        }
        catch (Exception ex)
        {
            _console.WriteLine($"Error during conversion: {ex.Message}");
            
            _converted = false;
            return false;
        }
        finally
        {
            //No matter what, if a fail occurs or if a success, always clear out files generated during script runs.
            _console.WriteLine("Cleaning up temporary files...");
            _converterBackend.DeleteFile(_hevcFile);
            _converterBackend.DeleteFile(_profile8HevcFile);
            _converterBackend.DeleteFile(_outputFile);
        }
    }
    
    public bool EncodeHevc()
    {
        //Runs command sequence
        try
        {
            _console.WriteLine($"Extracting HEVC stream: {_extractCommand}");
            _converterBackend.RunCommand(_extractCommand, _filePath);
            
            _console.WriteLine($"Extracting RPU: {_extractProfile7RpuCommand}");
            _converterBackend.RunCommand(_extractProfile8RpuCommand, _filePath);

            _console.WriteLine($"Encoding HEVC: {_reEncodeHevcProfile7Command}");
            _converterBackend.RunCommand(_reEncodeHevcProfile8Command, _filePath);
            _converterBackend.DeleteFile(_profile8HevcFile);

            _console.WriteLine($"Injecting RPU: {_injectRpu}");
            _converterBackend.RunCommand(_injectRpu, _filePath);
            _converterBackend.DeleteFile(_rpuFile);
            _converterBackend.DeleteFile(_encodedHevc);
            
            _console.WriteLine($"Remuxing to MKV: {_remuxCommandEncoded}");
            _converterBackend.RunCommand(_remuxCommandEncoded, _filePath);
            _converterBackend.DeleteFile(_encodedProfile8HevcFile);
            _converterBackend.DeleteFile(_hevcFile);
            
            var oldFileSize = new FileInfo(_filePath).Length;
            var newFileSize = new FileInfo(_outputFile).Length;

            _console.WriteLine($"Old file size: {oldFileSize}");
            _console.WriteLine($"New file size: {newFileSize}");
            
            if (newFileSize > oldFileSize)
            {
                _console.WriteLine("Encoded file larger than original file. Deleting encoded file.");
                _converterBackend.DeleteFile(_outputFile);

                _failedReason = "Output file larger than input.";
                _converted = false;
                return false;
            }
            else
            {
                //Renames new mkv container to the original file and deletes original file.
                File.Move(_outputFile, _filePath, true);

                _console.WriteLine($"Conversion complete: {_outputFile}");
            }

            _converted = true;
            return true;
        }
        catch (Exception ex)
        {
            _console.WriteLine($"Error during conversion: {ex.Message}");

            _converted = false;
            return false;
        }
        finally
        {
            //No matter what, if a fail occurs or if a success, always clear out files generated during script runs.
            _console.WriteLine("Cleaning up temporary files...");
            _converterBackend.DeleteFile(_hevcFile);
            _converterBackend.DeleteFile(_profile8HevcFile);
            _converterBackend.DeleteFile(_encodedProfile8HevcFile);
            _converterBackend.DeleteFile(_rpuFile);
            _converterBackend.DeleteFile(_encodedHevc);
            _converterBackend.DeleteFile(_outputFile);
        }
    }
    
    public bool EncodeAv1()
    {
        //Runs command sequence
        try
        {
            _console.WriteLine($"Re Encoding {_videoName} To AV1: {_encodeAv1Command}");
            _converterBackend.RunCommand(_encodeAv1Command, _filePath);

            var oldFileSize = new FileInfo(_filePath).Length;
            var newFileSize = new FileInfo(_outputFile).Length;

            _console.WriteLine($"Old file size: {oldFileSize}");
            _console.WriteLine($"New file size: {newFileSize}");

            if (newFileSize > oldFileSize)
            {
                _console.WriteLine("Encoded file larger than original file. Deleting encoded file.");
                _converterBackend.DeleteFile(_outputFile);

                _failedReason = "Output file larger than input.";
                _converted = false;
                return false;
            }
            else
            {
                //Renames new mkv container to the original file and deletes original file.
                File.Move(_outputFile, _filePath, true);

                _console.WriteLine($"Conversion complete: {_outputFile}");
            }

            _converted = true;
            return true;
        }
        catch (Exception ex)
        {
            _console.WriteLine($"Error during conversion: {ex.Message}");

            _converted = false;
            return false;
        }
        finally
        {
            _converterBackend.DeleteFile(_outputFile);
        }
    }

    public void AppendMetadata()
    {
        var directory = Path.GetDirectoryName(_filePath)!;
        var customMetadataFile = Path.Combine(directory, $"{_videoName}Metadata.mkv");

        customMetadataFile = _converterBackend.FileFormatToCommand(customMetadataFile);
        var insertMetadataCommand = $"ffmpeg -i '{_commandFilePath}' -map 0 -c:v copy -c:a copy -c:s copy -metadata LIBRARY_OPTIMIZER_APP='Converted={_converted}. Reason={_failedReason}' '{customMetadataFile}'";
        customMetadataFile = _converterBackend.FileRemoveFormat(customMetadataFile);
        
        var failOutput = string.Empty;
        try
        {
            _console.WriteLine($"Inserting metadata 'LIBRARY_OPTIMIZER_APP=Converted={_converted}. Reason={_failedReason}' into {_filePath}");
            failOutput = _converterBackend.RunCommand(insertMetadataCommand, _filePath, false);
            
            File.Move(customMetadataFile, _filePath, true);
        }
        catch
        {
            _console.WriteLine($"Metadata fail: {failOutput}");
            _console.WriteLine("Appending metadata failed. Continuing...");
        }
    }
}