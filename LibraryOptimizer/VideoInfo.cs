
using System.Text;
using System.Text.RegularExpressions;
using LibraryOptimizer.Enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LibraryOptimizer;

public class VideoInfo
{
    [JsonProperty("optimizerSettings")]
    private LibraryOptimizer.LibraryOptimizer _optimizerSettings;
        
    [JsonProperty("inputBitRate")]
    private double _inputBitRate;

    public double GetInputBitrate()
    {
        return _inputBitRate;
    }

    public void SetInputBitrate()
    { 
        _inputBitRate = ScanBitrate(InputFfmpegVideoInfo);
    }

    [JsonProperty("outputBitRate")]
    private double _outputBitRate;

    public double GetOutputBitrate()
    {
        return _outputBitRate;
    }

    public void SetOutputBitrate()
    {
        _outputBitRate = ScanBitrate(_outputFfmpegVideoInfo);
    }
        
    public string InputFfmpegVideoInfo;

    [JsonProperty("outputFfmpegVideoInfo")]
    private string _outputFfmpegVideoInfo;

    [JsonProperty("converterStatus")]
    private ConverterStatusEnum _converterStatusEnum = ConverterStatusEnum.NotConverted;
        
    [JsonProperty("inputFilePath")]
    private string _inputFilePath;

    [JsonProperty("commandInputFilePath")]
    private string _commandInputFilePath;

    [JsonProperty("commandOutputFile")]
    private string _commandOutputFile;

    [JsonProperty("outputFile")]
    private string _outputFile;

    [JsonProperty("tempDirectory")]
    private string _tempDirectory;

    [JsonProperty("commandTempDirectory")]
    private string _commandTempDirectory;

    [JsonProperty("inputFileSize")]
    private long _inputFileSize;

    [JsonProperty("outputFileSize")]
    private long _outputFileSize;
        
    public string VideoName;
        
    [JsonProperty("profile8HevcFile")]
    private string _profile8HevcFile;

    [JsonProperty("rpuFile")]
    private string _rpuFile;

    [JsonProperty("encodedHevc")]
    private string _encodedHevc;

    [JsonProperty("encodedProfile8HevcFile")]
    private string _encodedProfile8HevcFile;

    [JsonProperty("hevcFile")]
    private string _hevcFile;

    [JsonProperty("extractCommand")]
    private string _extractCommand;

    [JsonProperty("extractProfile8HevcCommand")]
    private string _extractProfile8HevcCommand;

    [JsonProperty("convertCommand")]
    private string _convertCommand;

    [JsonProperty("reEncodeHevcProfile8Command")]
    private string _reEncodeHevcProfile8Command;

    [JsonProperty("encodeAv1Command")]
    private string _encodeAv1Command;

    [JsonProperty("extractProfile8RpuCommand")]
    private string _extractProfile8RpuCommand;

    [JsonProperty("injectRpu")]
    private string _injectRpu;

    [JsonProperty("remuxCommandEncoded")]
    private string _remuxCommandEncoded;

    [JsonProperty("remuxCommand")]
    private string _remuxCommand;

    [JsonProperty("failedReason")]
    private string _failedReason = string.Empty;

    [JsonProperty("commandVideoName")]
    private string _commandVideoName;


    #region Constructors

    public VideoInfo(string inputFilePath, LibraryOptimizer.LibraryOptimizer optimizerSettings)
    {
        _optimizerSettings = optimizerSettings;
        
        _inputFilePath = inputFilePath;
        VideoName = Path.GetFileNameWithoutExtension(_inputFilePath);

        _commandInputFilePath = ConverterBackend.FileFormatToCommand(inputFilePath);
        
        _tempDirectory = Path.Combine(optimizerSettings._incompleteFolder!, $"{VideoName}Incomplete");
        _commandTempDirectory = ConverterBackend.FileFormatToCommand(_tempDirectory);
        
        _commandOutputFile = Path.Combine(_commandTempDirectory, "converted_" + Path.GetFileName(_commandInputFilePath));
        _outputFile = ConverterBackend.FileRemoveFormat(_commandOutputFile);
        
        var command = $"ffmpeg -i '{_commandInputFilePath}' -hide_banner -loglevel info";
        InputFfmpegVideoInfo = ConverterBackend.RunCommand(command, _commandInputFilePath, false);
    }
    
    public void SetVideoInfoCommands()
    {
        SetVideoInfoPaths();
        
        //======================Dolby Vision 7 -> 8 Remuxing=============================
        //Copies the HEVC stream of the mkv container to a seperate hevc file.
        _extractCommand = $"ffmpeg -i '{_commandInputFilePath}' -map 0:v:0 -c copy '{_hevcFile}'";

        //Copies the HEVC stream of the profile 8 mkv container to seperate hevc file.
        _extractProfile8HevcCommand = $"ffmpeg -i '{_commandInputFilePath}' -map 0:v:0 -c copy '{_profile8HevcFile}'";
        
        //Converts the hevc file from Dolby Vision Profile 7 to Dolby Vision Profile 8.
        _convertCommand = $"dovi_tool -m 2 convert -i '{_hevcFile}' -o '{_profile8HevcFile}'";

        //Extracting rpu file which contains the Dolby Vision metadata. Otherwise, FFmpeg loses Dolby Vision metadata.
        _extractProfile8RpuCommand = $"dovi_tool extract-rpu -i '{_profile8HevcFile}' -o '{_rpuFile}'";
        
        //After encoding, inject the rpu file back into the HEVC so Dolby Vision metadata is retained.
        _injectRpu = $"dovi_tool inject-rpu -i '{_encodedHevc}' -r '{_rpuFile}' -o '{_encodedProfile8HevcFile}'";
        
        //Remuxes the hevc file into new mkv container, overriding the original video stream with new encoded and/or converted hevc file.
        _remuxCommandEncoded = $"mkvmerge -o '{_commandOutputFile}' -D '{_commandInputFilePath}' '{_encodedProfile8HevcFile}'";
        _remuxCommand = $"mkvmerge -o '{_commandOutputFile}' -D '{_commandInputFilePath}' '{_profile8HevcFile}'";
        //======================Dolby Vision 7 -> 8 Remuxing=============================
        
        if(_optimizerSettings.IsNvidia)
            //NVIDIA NVENC
            _reEncodeHevcProfile8Command = $"ffmpeg -i '{_profile8HevcFile}' -c:v hevc_nvenc -preset p7 -cq 3 -c:a copy '{_encodedHevc}'";
        else
            //INTEL ARC Hopefully encodes well now
            _reEncodeHevcProfile8Command = $"ffmpeg -hwaccel qsv -i '{_profile8HevcFile}' -c:v hevc_qsv -preset 1 -global_quality 6 -scenario 3 -c:a copy '{_encodedHevc}'";
        
        //Generate Temp Folder
        CreateTempFolder();
    }

    private void SetVideoInfoPaths()
    { 
        //Build file names (with directory path attatched)
        _commandVideoName = Path.GetFileNameWithoutExtension(_commandInputFilePath);
        
        var directory = _commandTempDirectory;
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
            var qualityOffset = 0;
            if (_optimizerSettings.Quality == QualityEnum.HighQuality)
                qualityOffset = 2;
            
            //INTEL ARC
            if (_optimizerSettings.Quality == QualityEnum.NearLossless)
            {
                _encodeAv1Command = $"ffmpeg -hwaccel qsv -i '{_commandInputFilePath}' -map 0:v:0 -map 0:a? -map 0:s? -c:v av1_qsv -global_quality 1 -scenario 3 -preset 1 -c:a copy -c:s copy -analyzeduration 1000000 -map_metadata 0 -map_chapters 0 '{_commandOutputFile}'";
            }
            else if (_inputBitRate >= 50 && (_optimizerSettings.Quality == QualityEnum.HighQuality))
            {
                _encodeAv1Command = $"ffmpeg -hwaccel qsv -i '{_commandInputFilePath}' -map 0:v:0 -map 0:a? -map 0:s? -c:v av1_qsv -global_quality 1 -scenario 3 -preset 1 -c:a copy -c:s copy -analyzeduration 1000000 -map_metadata 0 -map_chapters 0 '{_commandOutputFile}'";
            }
            else if (_inputBitRate <= 50 && _inputBitRate >= 31 && (_optimizerSettings.Quality == QualityEnum.HighQuality))
            {
                _encodeAv1Command = $"ffmpeg -hwaccel qsv -i '{_commandInputFilePath}' -map 0:v:0 -map 0:a? -map 0:s? -c:v av1_qsv -global_quality 5 -scenario 3 -preset 1 -c:a copy -c:s copy -analyzeduration 1000000 -map_metadata 0 -map_chapters 0 '{_commandOutputFile}'";
            }
            else if (_inputBitRate <= 31 && _inputBitRate >= 11 
                     || (_inputBitRate >= 11 && (_optimizerSettings.Quality == QualityEnum.Balanced)))
            {
                _encodeAv1Command = $"ffmpeg -hwaccel qsv -i '{_commandInputFilePath}' -map 0:v:0 -map 0:a? -map 0:s? -c:v av1_qsv -global_quality {20 - qualityOffset} -scenario 3 -preset 1 -c:a copy -c:s copy -analyzeduration 1000000 -map_metadata 0 -map_chapters 0 '{_commandOutputFile}'";
            }
            else if (_inputBitRate <= 11 && _inputBitRate >= 6)
            {
                _encodeAv1Command = $"ffmpeg -hwaccel qsv -i '{_commandInputFilePath}' -map 0:v:0 -map 0:a? -map 0:s? -c:v av1_qsv -global_quality {21 - qualityOffset} -scenario 3 -preset 1 -c:a copy -c:s copy -analyzeduration 1000000 -map_metadata 0 -map_chapters 0 '{_commandOutputFile}'";
            }
            else if (_inputBitRate <= 6)
            {
                _encodeAv1Command = $"ffmpeg -hwaccel qsv -i '{_commandInputFilePath}' -map 0:v:0 -map 0:a? -map 0:s? -c:v av1_qsv -global_quality {22 - qualityOffset} -scenario 3 -preset 1 -c:a copy -c:s copy -analyzeduration 1000000 -map_metadata 0 -map_chapters 0 '{_commandOutputFile}'";
            }
        }
    }

    #endregion

    #region File Operations

    public ConverterStatusEnum RemuxAndEncodeHevc()
    {
        ConsoleLog.WriteLine("Remuxing and Encoding...");
        
        try
        {
            ConsoleLog.WriteLine($"Extracting HEVC stream: {_extractCommand}");
            ConverterBackend.RunCommand(_extractCommand, _inputFilePath, false, true);

            ConsoleLog.WriteLine($"Converting to Profile 8: {_convertCommand}");
            ConverterBackend.RunCommand(_convertCommand, _inputFilePath);
            
            ConsoleLog.WriteLine($"Extracting RPU: {_extractProfile8RpuCommand}");
            ConverterBackend.RunCommand(_extractProfile8RpuCommand, _inputFilePath);

            ConsoleLog.WriteLine($"Encoding HEVC: {_reEncodeHevcProfile8Command}");
            var failedOutput = string.Empty;
            try
            {
                failedOutput = ConverterBackend.RunCommand(_reEncodeHevcProfile8Command, _inputFilePath, false, true);
            }
            catch(Exception ex)
            {
                if (ex is OperationCanceledException)
                    throw;
                
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
                ConsoleLog.WriteLine($"Conversion complete: {_outputFile}");

                _converterStatusEnum = ConverterStatusEnum.Success;
                return ConverterStatusEnum.Success;
            }

            ConsoleLog.WriteLine($"Remuxing Non Encoded to MKV: {_remuxCommand}");
            ConverterBackend.RunCommand(_remuxCommand, _inputFilePath);

            ConsoleLog.WriteLine($"Conversion complete: {_outputFile}");
            
            _converterStatusEnum = ConverterStatusEnum.Failed;
            return ConverterStatusEnum.Failed;
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException)
                throw;
            
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
            ConverterBackend.RunCommand(_extractCommand, _inputFilePath, false, true);

            ConsoleLog.WriteLine($"Converting to Profile 8: {_convertCommand}");
            ConverterBackend.RunCommand(_convertCommand, _inputFilePath);
            
            ConverterBackend.DeleteFile(_hevcFile);
            
            ConsoleLog.WriteLine($"Remuxing to MKV: {_remuxCommand}");
            ConverterBackend.RunCommand(_remuxCommand, _inputFilePath);

            ConsoleLog.WriteLine($"Conversion complete: {_outputFile}");
            
            _converterStatusEnum = ConverterStatusEnum.Success;
            return ConverterStatusEnum.Success;
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException)
                throw;
            
            ConsoleLog.WriteLine($"Error during conversion: {ex.Message}");
            
            _converterStatusEnum = ConverterStatusEnum.Failed;
            return ConverterStatusEnum.Failed;
        }
    }
    
    public ConverterStatusEnum EncodeHevc()
    {
        try
        {
            ConsoleLog.WriteLine($"Extracting HEVC stream: {_extractProfile8HevcCommand}");
            ConverterBackend.RunCommand(_extractProfile8HevcCommand, _inputFilePath, false, true);
            
            ConsoleLog.WriteLine($"Extracting RPU: {_extractProfile8RpuCommand}");
            ConverterBackend.RunCommand(_extractProfile8RpuCommand, _inputFilePath);

            ConsoleLog.WriteLine($"Encoding HEVC: {_reEncodeHevcProfile8Command}");
            var failedOutput = string.Empty;
            try
            {
                failedOutput = ConverterBackend.RunCommand(_reEncodeHevcProfile8Command, _inputFilePath, false, true);
            }
            catch(Exception ex)
            {
                if (ex is OperationCanceledException)
                    throw;
                
                ConsoleLog.WriteLine(failedOutput);
                throw;
            }
            
            ConverterBackend.DeleteFile(_profile8HevcFile);
            
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
            
            ConsoleLog.WriteLine($"Conversion complete: {_outputFile}");

            _converterStatusEnum = ConverterStatusEnum.Success;
            return ConverterStatusEnum.Success;
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException)
                throw;
            
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
            ConsoleLog.WriteLine($"Re Encoding {VideoName} To AV1: {_encodeAv1Command}");

            var failedOutput = string.Empty;
            try
            {
                failedOutput = ConverterBackend.RunCommand(_encodeAv1Command, _inputFilePath, false, true);

            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException)
                    throw;
                
                ConsoleLog.WriteLine(failedOutput);
                throw;
            }
            
            SetFileSizes();
            
            if (_outputFileSize > _inputFileSize)
            {
                ConsoleLog.WriteLine("Encoded file larger than original file. Deleting encoded file.");
                ConverterBackend.DeleteFile(_outputFile);

                _failedReason = "Output file larger than input.";
                _converterStatusEnum = ConverterStatusEnum.Failed;
                return ConverterStatusEnum.Failed;
            }
            ConsoleLog.WriteLine($"Conversion complete: {_outputFile}");

            _converterStatusEnum = ConverterStatusEnum.Success;
            return ConverterStatusEnum.Success;
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException)
                throw;
            
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
        SafeDeleteDirectory(_tempDirectory);
        
        Directory.CreateDirectory(_tempDirectory);
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
        var fileToBuildMetadata = _commandInputFilePath;
        var command = $"ffmpeg -i '{_commandInputFilePath}' -hide_banner -loglevel info";
        var converted = false;
        if (_converterStatusEnum != ConverterStatusEnum.Failed)
        {
            command = $"ffmpeg -i '{_commandOutputFile}' -hide_banner -loglevel info";
            fileToBuildMetadata = _commandOutputFile;
            converted = true;
        }

        _outputFfmpegVideoInfo = ConverterBackend.RunCommand(command, _commandOutputFile, false);
        SetOutputBitrate();
        
        var directory = Path.GetDirectoryName(fileToBuildMetadata)!;
        var customMetadataFile = Path.Combine(directory, $"converted {_commandVideoName}Metadata.mkv");

        var insertMetadataCommand = $"ffmpeg -i '{fileToBuildMetadata}' -map 0 -c:v copy -c:a copy -c:s copy -metadata LIBRARY_OPTIMIZER_APP='Converted={converted}. Reason={_failedReason}' '{customMetadataFile}'";
        
        var failOutput = string.Empty;
        try
        {
            ConsoleLog.WriteLine($"Inserting metadata 'LIBRARY_OPTIMIZER_APP=Converted={converted}. Reason={_failedReason}' into {_inputFilePath}");
            ConsoleLog.WriteLine(insertMetadataCommand);
            failOutput = ConverterBackend.RunCommand(insertMetadataCommand, _inputFilePath, false, true);
            
            ConsoleLog.WriteLine("Writing file to original path...");
            ConsoleLog.WriteLine("DO NOT TURN OFF PROGRAM");
            File.Move(ConverterBackend.FileRemoveFormat(customMetadataFile), _inputFilePath, true);
        }
        catch(Exception ex)
        {
            if (ex is OperationCanceledException)
                throw;
            
            ConsoleLog.WriteLine($"Metadata fail: {failOutput}");
            ConsoleLog.WriteLine("Appending metadata failed. Continuing...");
        }
        
        SafeDeleteDirectory(_tempDirectory);
    }
    
    private void SafeDeleteDirectory(string path, int retries = 10, int delay = 10000)
    {
        var ex = new Exception();
        for (int i = 0; i < retries; i++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    var files = Directory.GetFiles(path);
                    foreach (var file in files)
                    {
                        if (file.Contains("plexignore"))
                            continue;
                        
                        ConsoleLog.WriteLine($"Deleting {file}");
                        Thread.Sleep(2000);
                        ConverterBackend.DeleteFile(file);
                    }
                    
                    Directory.Delete(path, true);
                    ConsoleLog.WriteLine($"Deleted: {path}");
                }
                return;
            }
            catch(Exception thrownEx)
            {
                ex = thrownEx;
                Thread.Sleep(delay);
            }
        }
        
        throw ex;
    }

    public void EndProgramCleanUp()
    {
        SafeDeleteDirectory(_tempDirectory);
    }

    public override string ToString()
    {
        var jObject = JObject.FromObject(this);
        return jObject.ToString();
    }
}