using System.Diagnostics;
using System.Globalization;

namespace LibraryOptimizer;

public abstract class Program
{ 
    public static CancellationTokenSource _cancellationToken = new CancellationTokenSource(); 
    private static Task? _optimizerTask;
    
    private static void Main(string[] args)
    {
        var wrapper = new LibraryOptimizer.LibraryOptimizer();
        
        if (Debugger.IsAttached)
        {
            //File paths specified to my Windows machine for debugging.
            wrapper.CheckAll = "y";
            wrapper.Libraries = new List<string>() { "Z:\\Plex\\Movie" };
            wrapper.RemuxDolbyVision = true;
            wrapper.EncodeHevc = true;
            wrapper.EncodeAv1 = true;
            wrapper.IsNvidia = true;
            wrapper.StartHour = DateTime.Now.Hour;
        }
       
        _optimizerTask = Task.Run(wrapper.ProcessLibrary);

        AppDomain.CurrentDomain.ProcessExit += (sender, eventArgs) =>
        {
            Console.WriteLine("Stopping Optimizer...");
            _cancellationToken.Cancel();

            if (_optimizerTask != null)
            {
                Console.WriteLine("Waiting for main work to finish...");
                try
                {
                    _optimizerTask.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("Optimizer Closed.");
                }
            }
        };
        
        _optimizerTask.Wait();
    }
}