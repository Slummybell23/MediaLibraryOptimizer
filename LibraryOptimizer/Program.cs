using System.Diagnostics;
using System.Globalization;

namespace LibraryOptimizer;

public abstract class Program
{
   private static void Main(string[] args)
    {
        var wrapper = new LibraryOptmizerWrapper();
        
        //wrapper.SetupWrapperVars();
        
        if (Debugger.IsAttached)
        {
            //File paths specified to my Windows machine for debugging.
            wrapper.CheckAll = "y";
            wrapper.MovieFolder = "Z:\\Plex\\Movie";
            //movieFolder = "Z:\\Plex\\Movie\\Coraline (2009)";
            //wrapper.TvShowFolder = "Z:\\Plex\\TV show";
            wrapper.Remux = false;
            wrapper.EncodeHevc = false;
            wrapper.EncodeAv1 = true;
            //wrapper.FilesToEncode.Add("Coraline (2009)");
        }
        else
        {
            wrapper.CheckAll = "y";
            wrapper.Remux = false;
            wrapper.EncodeHevc = false;
            wrapper.EncodeAv1 = true;

            wrapper.TvShowFolder = string.Empty;
            //Assumes if not in debug mode, running in docker environment.
            // wrapper.TvShowFolder = "/tvShows";
            wrapper.MovieFolder = "/movies";
            // wrapper.CheckAll = Environment.GetEnvironmentVariable("CHECK_ALL")!;
            //
            // var encodeStr = Environment.GetEnvironmentVariable("ENCODE")!.ToLower();
            // if (encodeStr == "y")
            //     wrapper.Encode = true;
            // var remuxStr =  Environment.GetEnvironmentVariable("REMUX")!.ToLower();
            // if (remuxStr == "y")
            //     wrapper.Remux = true;
            //
            // var encodeFiles = Environment.GetEnvironmentVariable("ENCODEFILES")!;
            // if (encodeFiles.ToLower() != "n")
            // {
            //     var filesToEncode = encodeFiles.Split(",");
            //     wrapper.FilesToEncode = filesToEncode.ToList();
            // }
            //
            // var startTimeStr = Environment.GetEnvironmentVariable("STARTTIME")!;
            // var isParsed = int.TryParse(startTimeStr, CultureInfo.InvariantCulture, out wrapper.StartHour);
        }
        
        wrapper.ProcessLibrary();
    }
}