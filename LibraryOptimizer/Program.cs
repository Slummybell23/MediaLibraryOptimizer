using System.Diagnostics;
using System.Globalization;

namespace LibraryOptimizer;

public abstract class Program
{ 
    public static CancellationTokenSource _cancellationToken = new CancellationTokenSource(); 
    private static Task? _mainWorkTask;
    
    private static void Main(string[] args)
    {
        var wrapper = new LibraryOptimizer.LibraryOptimizer();
        
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
       
        _mainWorkTask = Task.Run(() => RunApp(_cancellationToken.Token, wrapper));

        AppDomain.CurrentDomain.ProcessExit += (sender, eventArgs) =>
        {
            Console.WriteLine("Process exit detected.");
            _cancellationToken.Cancel();

            if (_mainWorkTask != null)
            {
                Console.WriteLine("Waiting for main work to finish...");
                try
                {
                    _mainWorkTask.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("Operation Canceled...");
                }
            }

            Cleanup();
        };
        
        _mainWorkTask.Wait();
    }
    
    private static void RunApp(CancellationToken token, LibraryOptimizer.LibraryOptimizer wrapper)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            wrapper.ProcessLibrary();
        }
    }
    
    private static void Cleanup()
    {
        Console.WriteLine("Cleaning...");
       
    }
   
}