namespace LibraryOptimizer;

public class VideoInfo
{
    public int height;
    public int width;

    public int bitrate;
    public int size;

    public string FilePath;
    public string filePath;

    public VideoInfo(string filePath)
    {
        FilePath = filePath;
        this.filePath = ConverterBackend.FileFormatToCommand(filePath);

        var ffprobeCommand = $"ffmpeg -i '{filePath}'";
        var ffprobeResults = ConverterBackend.RunCommand(ffprobeCommand, this.filePath);
        
        
    }
}