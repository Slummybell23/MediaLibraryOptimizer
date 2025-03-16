using System.Diagnostics;
using System.Globalization;

namespace LibraryOptimizer;

public abstract class Program
{
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
        
        wrapper.ProcessLibrary();
   }
}