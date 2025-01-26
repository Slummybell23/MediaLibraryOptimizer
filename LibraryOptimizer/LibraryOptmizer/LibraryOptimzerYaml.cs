namespace LibraryOptimizer.LibraryOptmizer;

public class LibraryOptimzerYaml
{
    public List<string> LibraryPaths { get; set; } = new List<string>() {"Replace Me", "Replace Me", "..."};
    public string? CheckAll { get; set; }  = "y";
    public int StartHour { get; set; } = 3;
    public bool EncodeHevc = false;
    public bool EncodeAv1 = false;
    public bool RemuxDolbyVision = false;
    public bool RetryFailed = false;
    public bool IsNvidia = false;
}