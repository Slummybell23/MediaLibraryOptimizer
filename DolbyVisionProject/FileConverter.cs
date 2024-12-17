using System.Runtime.CompilerServices;

namespace DolbyVisionProject;

public class FileConverter
{
    public FileConverter(Converter converter, string filePath)
    {
        //filePath = filePath.Replace("(", "`(");
        //filePath = filePath.Replace(")", "`)");
        
        Converter = converter;
        Console = converter._console;
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
        ExtractRpuCommand = $"dovi_tool extract-rpu -i '{Profile8HevcFile}' -o '{RpuFile}'";

        //Checks if system has nvenc for hardware encoding.
        var nvencCheckCommand = "ffmpeg -hide_banner -encoders";
        var nvencOutput = Converter.RunCommand(nvencCheckCommand, filePath);
        if(nvencOutput.Contains("NVIDIA NVENC hevc encoder"))
            //Uses nvidia gpu accelerated encoding.
            //WARNING: Slow, but far faster than cpu encoding.
            //cq of 19 provides good quality without extreme bitrates. Goal is to lower bitrate to around 60mbps
            //while retaining high quality and making decoding easier on slower decoders.
            ReEncodeHevcCommand = $"ffmpeg -i '{Profile8HevcFile}' -c:v hevc_nvenc -preset fast -cq 19 -maxrate 80M -bufsize 25M -rc-lookahead 32 -c:a copy '{EncodedHevc}'";
        else
            //Uses cpu encoding.
            //WARNING: Highly advised against due to being extremely slow on cpu.
            ReEncodeHevcCommand = $"ffmpeg -i '{Profile8HevcFile}' -c:v libx265 -preset slow -b:v 60000k -maxrate 60000k -vbv-bufsize 20000 -x265-params 'keyint=48:min-keyint=24' -an '{EncodedHevc}'";
    
        //After encoding, inject the rpu file back into the HEVC so Dolby Vision metadata is retained.
        InjectRpu = $"dovi_tool inject-rpu -i '{EncodedHevc}' -r '{RpuFile}' -o '{EncodedProfile8HevcFile}'";
        
        //Remuxes the hevc file into new mkv container, overriding the original video stream with new encoded and/or converted hevc file.
        RemuxCommandEncoded = $"mkvmerge -o '{OutputFile}' -D '{filePath}' '{EncodedProfile8HevcFile}'";
        RemuxCommand = $"mkvmerge -o '{OutputFile}' -D '{filePath}' '{Profile8HevcFile}'";
        //=====================================
        
        //Unformat File names for deleting.
        HevcFile = HevcFile.Replace("`", "");
        
        Profile8HevcFile = Profile8HevcFile.Replace("`", "");
        RpuFile = RpuFile.Replace("`", "");
        EncodedHevc = EncodedHevc.Replace("`", "");
        EncodedProfile8HevcFile = EncodedProfile8HevcFile.Replace("`", "");
        
        OutputFile = OutputFile.Replace("`", "");
    }
    
    private Converter Converter;
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

    private string ReEncodeHevcCommand;
    private string ExtractRpuCommand;
    private string InjectRpu;
    
    private string RemuxCommandEncoded;
    private string RemuxCommand;

    public bool RemuxAndEncode()
    {
        //Runs command sequence
        try
        {
            Console.WriteLine($"Extracting HEVC stream: {ExtractCommand}");
            Converter.RunCommand(ExtractCommand, FilePath);

            Console.WriteLine($"Converting to Profile 8: {ConvertCommand}");
            Converter.RunCommand(ConvertCommand, FilePath);
            
            var encodeCheckCommand = $"ffprobe -i '{FilePath}' -show_entries format=bit_rate -v quiet -of csv='p=0'";
            var bitRateOutput = Converter.RunCommand(encodeCheckCommand, FilePath).Split().Last();
            var bitRate = int.Parse(bitRateOutput);
            
            //If bitrate is above 75mbps, reencode.
            var needsEncoding = bitRate > 75000;
            if (needsEncoding)
            {
                Console.WriteLine($"Re Encoding {MovieName}");
                Console.WriteLine($"Extracting RPU: {ExtractRpuCommand}");
                Converter.RunCommand(ExtractRpuCommand, FilePath);

                Console.WriteLine($"Encoding HEVC: {ReEncodeHevcCommand}");
                Converter.RunCommand(ReEncodeHevcCommand, FilePath);
                Converter.DeleteFile(Profile8HevcFile);

                Console.WriteLine($"Injecting RPU: {InjectRpu}");
                Converter.RunCommand(InjectRpu, FilePath);
                Converter.DeleteFile(RpuFile);
                Converter.DeleteFile(EncodedHevc);
            }

            if (needsEncoding)
            {
                Console.WriteLine($"Remuxing to MKV: {RemuxCommandEncoded}");
                Converter.RunCommand(RemuxCommandEncoded, FilePath);
            }
            else
            {
                Console.WriteLine($"Remuxing to MKV: {RemuxCommand}");
                Converter.RunCommand(RemuxCommand, FilePath);
            }
            
            Converter.DeleteFile(EncodedProfile8HevcFile);
            Converter.DeleteFile(HevcFile);
            Converter.DeleteFile(FilePath);
            
            //Renames new mkv container to the original file and deletes original file.
            var renamedFilePath = FilePath;
            File.Move(OutputFile, renamedFilePath);

            Console.WriteLine($"Conversion complete: {OutputFile}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during conversion: {ex.Message}");
            return false;
        }
        finally
        {
            //No matter what, if a fail occurs or if a success, always clear out files generated during script runs.
            Console.WriteLine("Cleaning up temporary files...");
            Converter.DeleteFile(HevcFile);
            Converter.DeleteFile(Profile8HevcFile);
            Converter.DeleteFile(EncodedProfile8HevcFile);
            Converter.DeleteFile(RpuFile);
            Converter.DeleteFile(EncodedHevc);
        }
    }
    
    public bool Remux()
    {
        //Runs command sequence
        try
        {
            Console.WriteLine($"Extracting HEVC stream: {ExtractCommand}");
            Converter.RunCommand(ExtractCommand, FilePath);

            Console.WriteLine($"Converting to Profile 8: {ConvertCommand}");
            Converter.RunCommand(ConvertCommand, FilePath);
            
            Console.WriteLine($"Remuxing to MKV: {RemuxCommand}");
            Converter.RunCommand(RemuxCommand, FilePath);
            Converter.DeleteFile(HevcFile);
            Converter.DeleteFile(FilePath);
            
            //Renames new mkv container to the original file and deletes original file.
            var renamedFilePath = FilePath;
            File.Move(OutputFile, renamedFilePath);

            Console.WriteLine($"Conversion complete: {OutputFile}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during conversion: {ex.Message}");
            return false;
        }
        finally
        {
            //No matter what, if a fail occurs or if a success, always clear out files generated during script runs.
            Console.WriteLine("Cleaning up temporary files...");
            Converter.DeleteFile(HevcFile);
            Converter.DeleteFile(Profile8HevcFile);
        }
    }
    
    public bool Encode()
    {
        //Runs command sequence
        try
        {
            Console.WriteLine($"Extracting HEVC stream: {ExtractCommand}");
            Converter.RunCommand(ExtractCommand, FilePath); //
            
            Console.WriteLine($"Re Encoding {MovieName}");
            Console.WriteLine($"Extracting RPU: {ExtractRpuCommand}");
            Converter.RunCommand(ExtractRpuCommand, FilePath); //

            Console.WriteLine($"Encoding HEVC: {ReEncodeHevcCommand}");
            Converter.RunCommand(ReEncodeHevcCommand, FilePath);
            Converter.DeleteFile(Profile8HevcFile);

            Console.WriteLine($"Injecting RPU: {InjectRpu}");
            Converter.RunCommand(InjectRpu, FilePath);
            Converter.DeleteFile(RpuFile);
            Converter.DeleteFile(EncodedHevc);
            
            Console.WriteLine($"Remuxing to MKV: {RemuxCommandEncoded}");
            Converter.RunCommand(RemuxCommandEncoded, FilePath);
            Converter.DeleteFile(EncodedProfile8HevcFile);
            Converter.DeleteFile(HevcFile);
            Converter.DeleteFile(FilePath);
            
            //Renames new mkv container to the original file and deletes original file.
            var renamedFilePath = FilePath;
            File.Move(OutputFile, renamedFilePath);

            Console.WriteLine($"Conversion complete: {OutputFile}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during conversion: {ex.Message}");
            return false;
        }
        finally
        {
            //No matter what, if a fail occurs or if a success, always clear out files generated during script runs.
            Console.WriteLine("Cleaning up temporary files...");
            Converter.DeleteFile(HevcFile);
            Converter.DeleteFile(Profile8HevcFile);
            Converter.DeleteFile(EncodedProfile8HevcFile);
            Converter.DeleteFile(RpuFile);
            Converter.DeleteFile(EncodedHevc);
        }
    }
}