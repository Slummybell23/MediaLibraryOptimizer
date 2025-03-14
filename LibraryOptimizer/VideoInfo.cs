
using System.Text.RegularExpressions;
using LibraryOptimizer.Enums;

namespace LibraryOptimizer;

public class VideoInfo
{
    private LibraryOptimizer.LibraryOptimizer _optimizerSettings;
    
    private double _inputBitRate;

    public double GetInputBitrate()
    {
        return _inputBitRate;
    }

    public void SetInputBitrate()
    { 
        _inputBitRate = ScanBitrate(_inputFfmpegVideoInfo);
    }

    private double _outputBitRate;

    public double GetOutputBitrate()
    {
        return _outputBitRate;
    }

    public void SetOutputBitrate()
    {
        _outputBitRate = ScanBitrate(_outputFfmpegVideoInfo);
    }
    
    public string _inputFfmpegVideoInfo;
    public string _outputFfmpegVideoInfo;

    private ConverterStatusEnum _converterStatusEnum = ConverterStatusEnum.NotConverted;
    
    private string _inputFilePath;
    private string _commandInputFilePath;
    private string _commandOutputFile;
    private string _outputFile;
    private string _tempDirectory;

    private long _inputFileSize;
    private long _outputFileSize;
    
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

    //private bool _converted;
    private string _failedReason = string.Empty;
    private string _commandVideoName;

    #region Constructors

    public VideoInfo(string inputFilePath, LibraryOptimizer.LibraryOptimizer optimizerSettings)
    {
        _optimizerSettings = optimizerSettings;
        
        _inputFilePath = inputFilePath;
        _commandInputFilePath = ConverterBackend.FileFormatToCommand(inputFilePath);
        
        _tempDirectory = Path.Combine(Path.GetDirectoryName(_inputFilePath)!, $"{_videoName}Incomplete");
        _commandOutputFile = Path.Combine(_tempDirectory, "converted_" + Path.GetFileName(_commandInputFilePath));
        _outputFile = ConverterBackend.FileRemoveFormat(_commandOutputFile);
        
        var command = $"ffmpeg -i '{_commandInputFilePath}' -hide_banner -loglevel info";
        _inputFfmpegVideoInfo = ConverterBackend.RunCommand(command, _commandInputFilePath, false);
    }
    
    public void SetVideoInfoCommands()
    {
        SetVideoInfoPaths();
        
        //======================Dolby Vision 7 -> 8 Remuxing=============================
        //Copies the HEVC stream of the mkv container to a seperate hevc file.
        _extractCommand = $"ffmpeg -i '{_commandInputFilePath}' -map 0:v:0 -c copy '{_hevcFile}'";

        //Converts the hevc file from Dolby Vision Profile 7 to Dolby Vision Profile 8.
        _convertCommand = $"dovi_tool -m 2 convert -i '{_hevcFile}' -o '{_profile8HevcFile}'";

        //Extracting rpu file which contains the Dolby Vision metadata. Otherwise, FFmpeg loses Dolby Vision metadata.
        _extractProfile8RpuCommand = $"dovi_tool extract-rpu -i '{_hevcFile}' -o '{_rpuFile}'";
        
        //After encoding, inject the rpu file back into the HEVC so Dolby Vision metadata is retained.
        _injectRpu = $"dovi_tool inject-rpu -i '{_encodedHevc}' -r '{_rpuFile}' -o '{_encodedProfile8HevcFile}'";
        
        //Remuxes the hevc file into new mkv container, overriding the original video stream with new encoded and/or converted hevc file.
        _remuxCommandEncoded = $"mkvmerge -o '{_commandOutputFile}' -D '{_commandInputFilePath}' '{_encodedProfile8HevcFile}'";
        _remuxCommand = $"mkvmerge -o '{_commandOutputFile}' -D '{_commandInputFilePath}' '{_profile8HevcFile}'";
        //======================Dolby Vision 7 -> 8 Remuxing=============================
        
        if(_optimizerSettings.IsNvidia)
            //NVIDIA NVENC
            _reEncodeHevcProfile8Command = $"ffmpeg -i '{_hevcFile}' -c:v hevc_nvenc -preset p7 -cq 3 -c:a copy '{_encodedHevc}'";
        else
            //INTEL ARC
            _reEncodeHevcProfile8Command = $"ffmpeg -hwaccel qsv -i '{_hevcFile}' -c:v hevc_qsv -preset 1 -global_quality 3 -c:a copy '{_encodedHevc}'";
        
        //Generate Temp Folder
        CreateTempFolder();
    }

    private void SetVideoInfoPaths()
    { 
        //Build file names (with directory path attatched)
        _videoName = Path.GetFileNameWithoutExtension(_inputFilePath);
        _commandVideoName = Path.GetFileNameWithoutExtension(_commandInputFilePath);
        
        var directory = _tempDirectory;
        _hevcFile = Path.Combine(directory, $"{_commandVideoName}hevc.hevc");
        
        _profile8HevcFile = Path.Combine(directory, $"{_commandVideoName}profile8hevc.hevc");
        _rpuFile = Path.Combine(directory, $"{_commandVideoName}rpu.bin");
        _encodedHevc = Path.Combine(directory, $"{_commandVideoName}encodedHevc.hevc");
        _encodedProfile8HevcFile = Path.Combine(directory, $"{_commandVideoName}profile8encodedhevc.hevc");
        
        _hevcFile = Path.Combine(directory, $"{_commandVideoName}hevc.hevc");
        
        _profile8HevcFile = Path.Combine(directory, $"{_commandVideoName}profile8hevc.hevc");
        _rpuFile = Path.Combine(directory, $"{_commandVideoName}rpu.bin");
        _encodedHevc = Path.Combine(directory, $"{_commandVideoName}encodedHevc.hevc");
        _encodedProfile8HevcFile = Path.Combine(directory, $"{_commandVideoName}profile8encodedhevc.hevc");
    }

    //AV1
    public void SetVideoInfoCommandsWithAv1()
    {
        SetVideoInfoCommands();
        
        if (_optimizerSettings.IsNvidia)
        {
            //NVIDIA NVENC
            if (_inputBitRate >= 12)
            {
                _encodeAv1Command = $"ffmpeg -i '{_commandInputFilePath}' -map 0:v:0 -map 0:a? -map 0:s? -c:v av1_nvenc -cq 25 -preset p7 -c:a copy -c:s copy -map_metadata 0 -map_chapters 0 '{_commandOutputFile}'";
            }
            else if (_inputBitRate <= 12 && _inputBitRate >= 7)
            {
                _encodeAv1Command = $"ffmpeg -i '{_commandInputFilePath}' -map 0:v:0 -map 0:a? -map 0:s? -c:v av1_nvenc -cq 29 -preset p7 -c:a copy -c:s copy -map_metadata 0 -map_chapters 0 '{_commandOutputFile}'";
            }
            else if (_inputBitRate <= 7)
            {
                _encodeAv1Command = $"ffmpeg -i '{_commandInputFilePath}' -map 0:v:0 -map 0:a? -map 0:s? -c:v av1_nvenc -cq 32 -preset p7 -c:a copy -c:s copy -map_metadata 0 -map_chapters 0 '{_commandOutputFile}'";
            }
        }
        else
        {
            //INTEL ARC
            if (_optimizerSettings.Quality == QualityEnum.NearLossless)
            {
                _encodeAv1Command = $"ffmpeg -hwaccel qsv -i '{_commandInputFilePath}' -map 0:v:0 -map 0:a? -map 0:s? -c:v av1_qsv -global_quality 1 -preset 1 -c:a copy -c:s copy -analyzeduration 1000000 -map_metadata 0 -map_chapters 0 '{_commandOutputFile}'";
            }
            else if (_inputBitRate >= 50 && (_optimizerSettings.Quality == QualityEnum.HighQuality))
            {
                _encodeAv1Command = $"ffmpeg -hwaccel qsv -i '{_commandInputFilePath}' -map 0:v:0 -map 0:a? -map 0:s? -c:v av1_qsv -global_quality 1 -preset 1 -c:a copy -c:s copy -analyzeduration 1000000 -map_metadata 0 -map_chapters 0 '{_commandOutputFile}'";
            }
            else if (_inputBitRate <= 50 && _inputBitRate >= 31 && (_optimizerSettings.Quality == QualityEnum.HighQuality))
            {
                _encodeAv1Command = $"ffmpeg -hwaccel qsv -i '{_commandInputFilePath}' -map 0:v:0 -map 0:a? -map 0:s? -c:v av1_qsv -global_quality 5 -preset 1 -c:a copy -c:s copy -analyzeduration 1000000 -map_metadata 0 -map_chapters 0 '{_commandOutputFile}'";
            }
            else if (_inputBitRate <= 31 && _inputBitRate >= 11 
                     || (_inputBitRate >= 11 && (_optimizerSettings.Quality == QualityEnum.Balanced)))
            {
                _encodeAv1Command = $"ffmpeg -hwaccel qsv -i '{_commandInputFilePath}' -map 0:v:0 -map 0:a? -map 0:s? -c:v av1_qsv -global_quality 20 -preset 1 -c:a copy -c:s copy -analyzeduration 1000000 -map_metadata 0 -map_chapters 0 '{_commandOutputFile}'";
            }
            else if (_inputBitRate <= 11 && _inputBitRate >= 6)
            {
                _encodeAv1Command = $"ffmpeg -hwaccel qsv -i '{_commandInputFilePath}' -map 0:v:0 -map 0:a? -map 0:s? -c:v av1_qsv -global_quality 21 -preset 1 -c:a copy -c:s copy -analyzeduration 1000000 -map_metadata 0 -map_chapters 0 '{_commandOutputFile}'";
            }
            else if (_inputBitRate <= 6)
            {
                _encodeAv1Command = $"ffmpeg -hwaccel qsv -i '{_commandInputFilePath}' -map 0:v:0 -map 0:a? -map 0:s? -c:v av1_qsv -global_quality 22 -preset 1 -c:a copy -c:s copy -analyzeduration 1000000 -map_metadata 0 -map_chapters 0 '{_commandOutputFile}'";
            }
        }
    }

    #endregion

    #region File Operations

    public ConverterStatusEnum RemuxAndEncodeHevc()
    {
        try
        {
            ConsoleLog.WriteLine($"Extracting HEVC stream: {_extractCommand}");
            ConverterBackend.RunCommand(_extractCommand, _inputFilePath);

            ConsoleLog.WriteLine($"Converting to Profile 8: {_convertCommand}");
            ConverterBackend.RunCommand(_convertCommand, _inputFilePath);

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
            
            ConsoleLog.WriteLine($"Injecting RPU: {_injectRpu}");
            ConverterBackend.RunCommand(_injectRpu, _inputFilePath);
            
            ConsoleLog.WriteLine($"Remuxing Encoded to MKV: {_remuxCommandEncoded}");
            ConverterBackend.RunCommand(_remuxCommandEncoded, _inputFilePath);
            
            SetFileSizes();

            if (_outputFileSize > _inputFileSize)
            {
                ConsoleLog.WriteLine("Encoded file larger than original file. Deleting encoded file.");
                ConverterBackend.DeleteFile(_outputFile);
            }
            else
            {
                //Renames new mkv container to the original file and deletes original file.
                //File.Move(_outputFile, _inputFilePath, true);

                ConsoleLog.WriteLine($"Conversion complete: {_outputFile}");

                _converterStatusEnum = ConverterStatusEnum.Success;
                return ConverterStatusEnum.Success;
            }

            ConsoleLog.WriteLine($"Remuxing Non Encoded to MKV: {_remuxCommand}");
            ConverterBackend.RunCommand(_remuxCommand, _inputFilePath);

            //Renames new mkv container to the original file and deletes original file.
            //File.Move(_outputFile, _inputFilePath, true);

            ConsoleLog.WriteLine($"Conversion complete: {_outputFile}");
            
            _converterStatusEnum = ConverterStatusEnum.Success;
            return ConverterStatusEnum.Success;
        }
        catch (Exception ex)
        {
            ConsoleLog.WriteLine($"Error during conversion: {ex.Message}");
            
            _converterStatusEnum = ConverterStatusEnum.Failed;
            return ConverterStatusEnum.Failed;
        }
    }

    public ConverterStatusEnum Remux()
    {
        try
        {
            ConsoleLog.WriteLine($"Extracting HEVC stream: {_extractCommand}");
            ConverterBackend.RunCommand(_extractCommand, _inputFilePath);

            ConsoleLog.WriteLine($"Converting to Profile 8: {_convertCommand}");
            ConverterBackend.RunCommand(_convertCommand, _inputFilePath);
            
            ConsoleLog.WriteLine($"Remuxing to MKV: {_remuxCommand}");
            ConverterBackend.RunCommand(_remuxCommand, _inputFilePath);

            //Renames new mkv container to the original file and deletes original file.
            //File.Move(_outputFile, _inputFilePath, true);

            ConsoleLog.WriteLine($"Conversion complete: {_outputFile}");
            
            _converterStatusEnum = ConverterStatusEnum.Success;
            return ConverterStatusEnum.Success;
        }
        catch (Exception ex)
        {
            ConsoleLog.WriteLine($"Error during conversion: {ex.Message}");
            
            _converterStatusEnum = ConverterStatusEnum.Failed;
            return ConverterStatusEnum.Failed;
        }
    }
    
    public ConverterStatusEnum EncodeHevc()
    {
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
            
            ConsoleLog.WriteLine($"Injecting RPU: {_injectRpu}");
            ConverterBackend.RunCommand(_injectRpu, _inputFilePath);

            ConsoleLog.WriteLine($"Remuxing to MKV: {_remuxCommandEncoded}");
            ConverterBackend.RunCommand(_remuxCommandEncoded, _inputFilePath);
            
            SetFileSizes();
            
            if (_outputFileSize > _inputFileSize)
            {
                ConsoleLog.WriteLine("Encoded file larger than original file. Deleting encoded file.");
                ConverterBackend.DeleteFile(_outputFile);

                _failedReason = "Output file larger than input.";
                _converterStatusEnum = ConverterStatusEnum.Failed;
                return ConverterStatusEnum.Failed;
            }
            else
            {
                //Renames new mkv container to the original file and deletes original file.
                //File.Move(_outputFile, _inputFilePath, true);

                ConsoleLog.WriteLine($"Conversion complete: {_outputFile}");
            }

            _converterStatusEnum = ConverterStatusEnum.Success;
            return ConverterStatusEnum.Success;
        }
        catch (Exception ex)
        {
            ConsoleLog.WriteLine($"Error during conversion: {ex.Message}");

            _converterStatusEnum = ConverterStatusEnum.Failed;
            return ConverterStatusEnum.Failed;
        }
    }
    
    public ConverterStatusEnum EncodeAv1()
    {
        //Runs command sequence
        try
        {
            ConsoleLog.WriteLine($"Re Encoding {_videoName} To AV1: {_encodeAv1Command}");
            ConverterBackend.RunCommand(_encodeAv1Command, _inputFilePath);
            
            SetFileSizes();
            
            if (_outputFileSize > _inputFileSize)
            {
                ConsoleLog.WriteLine("Encoded file larger than original file. Deleting encoded file.");
                ConverterBackend.DeleteFile(_outputFile);

                _failedReason = "Output file larger than input.";
                _converterStatusEnum = ConverterStatusEnum.Failed;
                return ConverterStatusEnum.Failed;
            }
            else
            {
                //Renames new mkv container to the original file and deletes original file.
                //File.Move(_outputFile, _inputFilePath, true);

                ConsoleLog.WriteLine($"Conversion complete: {_outputFile}");
            }

            _converterStatusEnum = ConverterStatusEnum.Success;
            return ConverterStatusEnum.Success;
        }
        catch (Exception ex)
        {
            ConsoleLog.WriteLine($"Error during conversion: {ex.Message}");

            _converterStatusEnum = ConverterStatusEnum.Failed;
            return ConverterStatusEnum.Failed;
        }
    }

    #endregion

    private void SetFileSizes()
    {
        _inputFileSize = new FileInfo(_inputFilePath).Length/1000000;
        _outputFileSize = new FileInfo(_outputFile).Length/1000000;

        ConsoleLog.WriteLine($"Old file size: {_inputFileSize} mb");
        ConsoleLog.WriteLine($"New file size: {_outputFileSize} mb");
    }
    
    private void CreateTempFolder()
    {
        var plexIgnoreFile = Path.Combine(_tempDirectory, ".plexIgnore");

        if(Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory,true);
        
        Directory.CreateDirectory(_tempDirectory);
        File.Create(plexIgnoreFile);
    }
    
    private double ScanBitrate(string ffmpegFileInfo)
    {
        var regex = new Regex("(bitrate:)(\\s)(\\d)*");

        var match = regex.Match(ffmpegFileInfo);
        var parsed = match.Value.Split("bitrate:")[1].Trim();
        return double.Parse(parsed);
    }

    public void AppendMetadata()
    {
        var fileToBuildMetadata = _inputFilePath;
        var command = $"ffmpeg -i '{_commandInputFilePath}' -hide_banner -loglevel info";
        var converted = false;
        if (_converterStatusEnum != ConverterStatusEnum.Failed)
        {
            command = $"ffmpeg -i '{_commandOutputFile}' -hide_banner -loglevel info";
            fileToBuildMetadata = _outputFile;
            converted = true;
        }

        _outputFfmpegVideoInfo = ConverterBackend.RunCommand(command, _commandOutputFile, false);
        SetOutputBitrate();
        
        var directory = Path.GetDirectoryName(fileToBuildMetadata)!;
        var customMetadataFile = Path.Combine(directory, $"converted {_videoName}Metadata.mkv");

        customMetadataFile = ConverterBackend.FileFormatToCommand(customMetadataFile);
        var insertMetadataCommand = $"ffmpeg -i '{fileToBuildMetadata}' -map 0 -c:v copy -c:a copy -c:s copy -metadata LIBRARY_OPTIMIZER_APP='Converted={converted}. Reason={_failedReason}' '{customMetadataFile}'";
        customMetadataFile = ConverterBackend.FileRemoveFormat(customMetadataFile);
        
        var failOutput = string.Empty;
        try
        {
            ConsoleLog.WriteLine($"Inserting metadata 'LIBRARY_OPTIMIZER_APP=Converted={converted}. Reason={_failedReason}' into {_inputFilePath}");
            failOutput = ConverterBackend.RunCommand(insertMetadataCommand, _inputFilePath, false);
            
            ConsoleLog.WriteLine("Writing file to original path...");
            ConsoleLog.WriteLine("DO NOT TURN OFF PROGRAM");
            File.Move(customMetadataFile, _inputFilePath, true);
        }
        catch
        {
            ConsoleLog.WriteLine($"Metadata fail: {failOutput}");
            ConsoleLog.WriteLine("Appending metadata failed. Continuing...");
        }
        
        if(Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory,true);
    }
}