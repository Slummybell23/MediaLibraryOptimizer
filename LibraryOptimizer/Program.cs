﻿using System.Diagnostics;
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
       
        Console.CancelKeyPress += (sender, eventArgs) =>
        {
            Console.WriteLine("SIGTERM received. Shutting down...");
            eventArgs.Cancel = true; // Prevent immediate termination
            _cancellationToken.Cancel();
        };
        
        AppDomain.CurrentDomain.ProcessExit += (sender, eventArgs) =>
        {
            Console.WriteLine("Process exit detected. Running cleanup...");
            _cancellationToken.Cancel();
        };

        try
        {
            Task mainTask = Task.Run(() =>
            {
                // Simulate long-running work
                while (true)
                {
                    _cancellationToken.Token.ThrowIfCancellationRequested();
                    Console.WriteLine("Running...");
                    Thread.Sleep(2000);
                }

                // If you want to run the actual work:
                // wrapper.ProcessLibrary();
            }, _cancellationToken.Token);

            mainTask.Wait(); // Block until it completes or is cancelled
        }
        catch (AggregateException ex)
        {
            if (ex.InnerException is OperationCanceledException)
            {
                Cleanup();
            }
            else
            {
                throw;
            }
        }
    }

    private static void Cleanup()
    {
        Console.WriteLine("Cleaning...");
       
    }
   
}