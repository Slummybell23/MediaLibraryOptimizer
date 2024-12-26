namespace LibraryOptimizer;

public class LibraryOptimzerWrapperJSON
{
    public List<string> LibraryPaths { get; set; } = new List<string>();
    public string? CheckAll { get; set; }  = "yes";
    public List<string> FilesToEncode { get; set; }  = new List<string>();
    public int StartHour { get; set; } = DateTime.Now.Hour;
    public bool Encode { get; set; } = false;
    public bool Remux { get; set; } = false;
    public string? nvidiaDevice { get; set; } = null;
}