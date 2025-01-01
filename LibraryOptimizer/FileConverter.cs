using System.Runtime.CompilerServices;

namespace LibraryOptimizer;

public class FileConverter
{
    public FileConverter(ConverterBackend converterBackend, string filePath)
    {
        _converterBackend = converterBackend;
        Console = converterBackend._console;
        this.FilePath = filePath;
        
        //Builds command strings
        
        //Build file names (with directory path attatched)
        MovieName = Path.GetFileNameWithoutExtension(filePath);
        var directory = Path.GetDirectoryName(filePath)!;
        HevcFile = Path.Combine(directory, $"{MovieName}hevc.hevc");
        
        Profile8HevcFile = Path.Combine(directory, $"{MovieName}profile8hevc.hevc");
        RpuFile = Path.Combine(directory, $"{MovieName}rpu.bin");
        EncodedHevc = Path.Combine(directory, $"{MovieName}encodedHevc.hevc");
        EncodedProfile8HevcFile = Path.Combine(directory, $"{MovieName}profile8encodedhevc.hevc");
        
        OutputFile = Path.Combine(Path.GetDirectoryName(filePath)!, "converted_" + Path.GetFileName(filePath));
        //===================================================
        
        //Build commands to execute in sequence
        
        //Copies the HEVC stream of the mkv container to a seperate hevc file.
        //Sets metadata level to 150 for easier proccessing on slower decoders in case hevc is level 153.
        //Difference is negligible unless you're doing 8k resolution movies.
        ExtractCommand = $"ffmpeg -i '{filePath}' -map 0:v:0 -bufsize 64M -c copy -bsf:v hevc_metadata=level=150 '{HevcFile}'";
        
        //Converts the hevc file from Dolby Vision Profile 7 to Dolby Vision Profile 8.
        ConvertCommand = $"dovi_tool -m 2 convert -i '{HevcFile}' -o '{Profile8HevcFile}'";

        //Extracting rpu file which contains the Dolby Vision metadata. Otherwise, FFmpeg loses Dolby Vision metadata.
        ExtractProfile7RpuCommand = $"dovi_tool extract-rpu -i '{Profile8HevcFile}' -o '{RpuFile}'";

        //Extracting rpu file which contains the Dolby Vision metadata. Otherwise, FFmpeg loses Dolby Vision metadata.
        ExtractProfile8RpuCommand = $"dovi_tool extract-rpu -i '{HevcFile}' -o '{RpuFile}'";
        
        //Uses nvidia gpu accelerated encoding.
        //WARNING: Slow, but far faster than cpu encoding.
        //cq of 19 provides good quality without extreme bitrates. Goal is to lower bitrate to around 60mbps
        //while retaining high quality and making decoding easier on slower decoders.
        ReEncodeHevcProfile7Command = $"ffmpeg -i '{Profile8HevcFile}' -c:v hevc_nvenc -preset p7 -cq 18 -c:a copy '{EncodedHevc}'";
        ReEncodeHevcProfile8Command = $"ffmpeg -i '{HevcFile}' -c:v hevc_nvenc -preset p7 -cq 18 -c:a copy '{EncodedHevc}'";
            
        // EncodeAV1Command = 
        //     $"ffmpeg -i '{filePath}' " +
        //     $"-map 0 " +
        //     $"-c:v libsvtav1 -preset 12 -crf 27 -svtav1-params tune=1 " +
        //     $"-c:a copy -c:s copy " +
        //     $"-map_metadata 0 -map_chapters 0 " +
        //     $"'{OutputFile}'";
        EncodeAV1Command = $"ffmpeg -i '{filePath}' -map 0 -c:v av1_nvenc -cq 27 -preset p7 -c:a copy -c:s copy -map_metadata 0 -map_chapters 0 '{OutputFile}'";
        
        //After encoding, inject the rpu file back into the HEVC so Dolby Vision metadata is retained.
        InjectRpu = $"dovi_tool inject-rpu -i '{EncodedHevc}' -r '{RpuFile}' -o '{EncodedProfile8HevcFile}'";
        
        //Remuxes the hevc file into new mkv container, overriding the original video stream with new encoded and/or converted hevc file.
        RemuxCommandEncoded = $"mkvmerge -o '{OutputFile}' -D '{filePath}' '{EncodedProfile8HevcFile}'";
        RemuxCommand = $"mkvmerge -o '{OutputFile}' -D '{filePath}' '{Profile8HevcFile}'";
        //=====================================
        
        //Unformat File names for deleting.
        HevcFile = HevcFile.Replace("''", "'");
        
        Profile8HevcFile = Profile8HevcFile.Replace("''", "'");
        RpuFile = RpuFile.Replace("''", "'");
        EncodedHevc = EncodedHevc.Replace("''", "'");
        EncodedProfile8HevcFile = EncodedProfile8HevcFile.Replace("''", "'");
        
        FilePath = FilePath.Replace("''", "'");
        OutputFile = OutputFile.Replace("''", "'");
    }
    
    private ConverterBackend _converterBackend;
    private ConsoleLog Console;
    private string FilePath;
    
    private string MovieName;
    private string Profile8HevcFile;
    private string RpuFile;
    private string EncodedHevc;
    private string EncodedProfile8HevcFile;
    private string HevcFile;
    private string OutputFile;

    private string ExtractCommand;
    private string ConvertCommand;

    private string ReEncodeHevcProfile7Command;
    private string ReEncodeHevcProfile8Command;
    private string EncodeAV1Command;
    private string ExtractProfile7RpuCommand;
    private string ExtractProfile8RpuCommand;
    private string InjectRpu;
    
    private string RemuxCommandEncoded;
    private string RemuxCommand;

    private bool? Converted = null;
    private string FailedReason = String.Empty;

    public bool RemuxAndEncodeHevc()
    {
        //Runs command sequence
        try
        {
            Console.WriteLine($"Extracting HEVC stream: {ExtractCommand}");
            _converterBackend.RunCommand(ExtractCommand, FilePath);

            Console.WriteLine($"Converting to Profile 8: {ConvertCommand}");
            _converterBackend.RunCommand(ConvertCommand, FilePath);
            
            var encodeCheckCommand = $"ffprobe -i '{FilePath}' -show_entries format=bit_rate -v quiet -of csv='p=0'";
            var bitRateOutput = _converterBackend.RunCommand(encodeCheckCommand, FilePath).Split().Last();
            var bitRate = double.Parse(bitRateOutput) / 1000000.0;
            
            //If bitrate is above 75mbps, reencode.
            var needsEncoding = bitRate > 15;
            if (needsEncoding)
            {
                Console.WriteLine($"Re Encoding {MovieName}");
                Console.WriteLine($"Extracting RPU: {ExtractProfile7RpuCommand}");
                _converterBackend.RunCommand(ExtractProfile7RpuCommand, FilePath);

                Console.WriteLine($"Encoding HEVC: {ReEncodeHevcProfile7Command}");
                _converterBackend.RunCommand(ReEncodeHevcProfile7Command, FilePath);
                _converterBackend.DeleteFile(Profile8HevcFile);

                Console.WriteLine($"Injecting RPU: {InjectRpu}");
                _converterBackend.RunCommand(InjectRpu, FilePath);
                _converterBackend.DeleteFile(RpuFile);
                _converterBackend.DeleteFile(EncodedHevc);
                
                Console.WriteLine($"Remuxing Encoded to MKV: {RemuxCommandEncoded}");
                _converterBackend.RunCommand(RemuxCommandEncoded, FilePath);
                
                var oldFileSize = new FileInfo(FilePath).Length;
                var newFileSize = new FileInfo(OutputFile).Length;

                Console.WriteLine($"Old file size: {oldFileSize}");
                Console.WriteLine($"New file size: {newFileSize}");
            
                if (newFileSize > oldFileSize)
                {
                    Console.WriteLine("Encoded file larger than original file. Deleting encoded file.");
                    _converterBackend.DeleteFile(OutputFile);
                }
                else
                {
                    _converterBackend.DeleteFile(FilePath);
                    _converterBackend.DeleteFile(EncodedProfile8HevcFile);
                    _converterBackend.DeleteFile(HevcFile);
                    
                    //Renames new mkv container to the original file and deletes original file.
                    File.Move(OutputFile, FilePath, true);

                    Console.WriteLine($"Conversion complete: {OutputFile}");

                    Converted = true;
                    return true;
                }
                
            }

            Console.WriteLine($"Remuxing Non Encoded to MKV: {RemuxCommand}");
            _converterBackend.RunCommand(RemuxCommand, FilePath);
            
            _converterBackend.DeleteFile(EncodedProfile8HevcFile);
            
            _converterBackend.DeleteFile(HevcFile);
            //Renames new mkv container to the original file and deletes original file.
            File.Move(OutputFile, FilePath, true);

            Console.WriteLine($"Conversion complete: {OutputFile}");
            
            Converted = true;
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during conversion: {ex.Message}");
            
            Converted = false;
            return false;
        }
        finally
        {
            //No matter what, if a fail occurs or if a success, always clear out files generated during script runs.
            Console.WriteLine("Cleaning up temporary files...");
            _converterBackend.DeleteFile(HevcFile);
            _converterBackend.DeleteFile(Profile8HevcFile);
            _converterBackend.DeleteFile(EncodedProfile8HevcFile);
            _converterBackend.DeleteFile(RpuFile);
            _converterBackend.DeleteFile(EncodedHevc);
            _converterBackend.DeleteFile(OutputFile);
        }
    }
    
    public bool Remux()
    {
        //Runs command sequence
        try
        {
            Console.WriteLine($"Extracting HEVC stream: {ExtractCommand}");
            _converterBackend.RunCommand(ExtractCommand, FilePath);

            Console.WriteLine($"Converting to Profile 8: {ConvertCommand}");
            _converterBackend.RunCommand(ConvertCommand, FilePath);
            
            Console.WriteLine($"Remuxing to MKV: {RemuxCommand}");
            _converterBackend.RunCommand(RemuxCommand, FilePath);
            _converterBackend.DeleteFile(HevcFile);
            
            //Renames new mkv container to the original file and deletes original file.
            File.Move(OutputFile, FilePath, true);

            Console.WriteLine($"Conversion complete: {OutputFile}");
            
            Converted = true;
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during conversion: {ex.Message}");
            
            Converted = false;
            return false;
        }
        finally
        {
            //No matter what, if a fail occurs or if a success, always clear out files generated during script runs.
            Console.WriteLine("Cleaning up temporary files...");
            _converterBackend.DeleteFile(HevcFile);
            _converterBackend.DeleteFile(Profile8HevcFile);
            _converterBackend.DeleteFile(OutputFile);
        }
    }
    
    public bool EncodeHevc()
    {
        //Runs command sequence
        try
        {
            Console.WriteLine($"Extracting HEVC stream: {ExtractCommand}");
            _converterBackend.RunCommand(ExtractCommand, FilePath);
            
            Console.WriteLine($"Extracting RPU: {ExtractProfile7RpuCommand}");
            _converterBackend.RunCommand(ExtractProfile8RpuCommand, FilePath);

            Console.WriteLine($"Encoding HEVC: {ReEncodeHevcProfile7Command}");
            _converterBackend.RunCommand(ReEncodeHevcProfile8Command, FilePath);
            _converterBackend.DeleteFile(Profile8HevcFile);

            Console.WriteLine($"Injecting RPU: {InjectRpu}");
            _converterBackend.RunCommand(InjectRpu, FilePath);
            _converterBackend.DeleteFile(RpuFile);
            _converterBackend.DeleteFile(EncodedHevc);
            
            Console.WriteLine($"Remuxing to MKV: {RemuxCommandEncoded}");
            _converterBackend.RunCommand(RemuxCommandEncoded, FilePath);
            _converterBackend.DeleteFile(EncodedProfile8HevcFile);
            _converterBackend.DeleteFile(HevcFile);
            
            var oldFileSize = new FileInfo(FilePath).Length;
            var newFileSize = new FileInfo(OutputFile).Length;

            Console.WriteLine($"Old file size: {oldFileSize}");
            Console.WriteLine($"New file size: {newFileSize}");
            
            if (newFileSize > oldFileSize)
            {
                Console.WriteLine("Encoded file larger than original file. Deleting encoded file.");
                _converterBackend.DeleteFile(OutputFile);

                FailedReason = "Output file larger than input.";
                Converted = false;
                return false;
            }
            else
            {
                //Renames new mkv container to the original file and deletes original file.
                File.Move(OutputFile, FilePath, true);

                Console.WriteLine($"Conversion complete: {OutputFile}");
            }

            Converted = true;
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during conversion: {ex.Message}");

            Converted = false;
            return false;
        }
        finally
        {
            //No matter what, if a fail occurs or if a success, always clear out files generated during script runs.
            Console.WriteLine("Cleaning up temporary files...");
            _converterBackend.DeleteFile(HevcFile);
            _converterBackend.DeleteFile(Profile8HevcFile);
            _converterBackend.DeleteFile(EncodedProfile8HevcFile);
            _converterBackend.DeleteFile(RpuFile);
            _converterBackend.DeleteFile(EncodedHevc);
            _converterBackend.DeleteFile(OutputFile);
        }
    }
    
    public bool EncodeAv1()
    {
        //Runs command sequence
        try
        {
            Console.WriteLine($"Re Encoding {MovieName} To AV1");
            _converterBackend.RunCommand(EncodeAV1Command, FilePath);

            var oldFileSize = new FileInfo(FilePath).Length;
            var newFileSize = new FileInfo(OutputFile).Length;

            Console.WriteLine($"Old file size: {oldFileSize}");
            Console.WriteLine($"New file size: {newFileSize}");

            if (newFileSize > oldFileSize)
            {
                Console.WriteLine("Encoded file larger than original file. Deleting encoded file.");
                _converterBackend.DeleteFile(OutputFile);

                FailedReason = "Output file larger than input.";
                Converted = false;
                return false;
            }
            else
            {
                //Renames new mkv container to the original file and deletes original file.
                File.Move(OutputFile, FilePath, true);

                Console.WriteLine($"Conversion complete: {OutputFile}");
            }

            Converted = true;
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during conversion: {ex.Message}");

            Converted = false;
            return false;
        }
        finally
        {
            _converterBackend.DeleteFile(OutputFile);
        }
    }

    public void AppendMetadata()
    {
        var directory = Path.GetDirectoryName(FilePath)!;
        var customMetadataFile = Path.Combine(directory, $"{MovieName.Replace("''","'")}Metadata.mkv");
        
        FilePath = FilePath.Replace("'", "''");
        customMetadataFile = customMetadataFile.Replace("'", "''");
        
        //mkvpropedit AvatarCustom.mkv --set "title=Your New Title" --add "tag:LIBRARY_OPTIMIZER_APP=Success"

        var insertMetadataCommand = $"ffmpeg -i '{FilePath}' -map 0 -c:v copy -c:a copy -c:s copy -metadata LIBRARY_OPTIMIZER_APP='Converted={Converted}. Reason={FailedReason}' '{customMetadataFile}'";

        FilePath = FilePath.Replace("''", "'");
        customMetadataFile = customMetadataFile.Replace("''", "'");
        
        var failOutput = string.Empty;
        try
        {
            Console.WriteLine($"Inserting metadata 'LIBRARY_OPTIMIZER_APP=Converted={Converted}. Reason={FailedReason}' into {FilePath}");
            failOutput = _converterBackend.RunCommand(insertMetadataCommand, FilePath, false);
            
            File.Move(customMetadataFile, FilePath, true);
        }
        catch
        {
            Console.WriteLine($"Metadata fail: {failOutput}");
            Console.WriteLine("Appending metadata failed. Continuing...");
        }
        
    }
}