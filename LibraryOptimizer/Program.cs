using System.Diagnostics;
using System.Globalization;

namespace LibraryOptimizer;

public abstract class Program
{ 
    public static CancellationTokenSource _cancellationToken = new CancellationTokenSource(); 
    private static void Main(string[] args)
    {
        var wrapper = new LibraryOptimizer.LibraryOptimizer(_cancellationToken);
        
        if (Debugger.IsAttached)
        {
            //File paths specified to my Windows machine for debugging.
            wrapper.CheckAll = "y";
            wrapper.Libraries = new List<string>() { "Z:\\Plex\\Movie", "Z:\\Plex\\TV show" };
            wrapper.RemuxDolbyVision = true;
            wrapper.EncodeHevc = true;
            wrapper.EncodeAv1 = true;
            wrapper.StartHour = DateTime.Now.Hour;
        }
       
        AppDomain.CurrentDomain.ProcessExit += (sender, eventArgs) =>
        {
            Console.WriteLine("Process exit detected. Running cleanup...");
            _cancellationToken.Cancel();
        };

        try
        {
            wrapper.ProcessLibrary();

        }
        catch (OperationCanceledException ex)
        {
            Cleanup();
        }
    }

    private static void Cleanup()
    {
        Console.WriteLine("Cleaning...");
       
    }
   
}