using System.Diagnostics;
using System.Globalization;

namespace DolbyVisionProject;

public abstract class Program
{
   private static void Main(string[] args)
    {
        var wrapper = new DolbyVisionWrapper();
        
        if (Debugger.IsAttached)
        {
            //File paths specified to my Windows machine for debugging.
            wrapper.CheckAll = "y";
            wrapper.MovieFolder = "Z:\\Plex\\Movie";
            //movieFolder = "Z:\\Plex\\Movie\\Coraline (2009)";
            //wrapper.TvShowFolder = "Z:\\Plex\\TV show";
            wrapper.Remux = true;
            wrapper.Encode = false;
            //wrapper.FilesToEncode.Add("Coraline (2009)");
        }
        else
        {
            //Assumes if not in debug mode, running in docker environment.
            wrapper.MovieFolder = Environment.GetEnvironmentVariable("MOVIE_FOLDER")!;
            wrapper.TvShowFolder = Environment.GetEnvironmentVariable("TVSHOW_FOLDER")!;
            wrapper.CheckAll = Environment.GetEnvironmentVariable("CHECK_ALL")!;
            
            var encodeStr = Environment.GetEnvironmentVariable("ENCODE")!.ToLower();
            if (encodeStr == "y")
                wrapper.Encode = true;
            var remuxStr =  Environment.GetEnvironmentVariable("REMUX")!.ToLower();
            if (remuxStr == "y")
                wrapper.Remux = true;

            var encodeFiles = Environment.GetEnvironmentVariable("ENCODEFILES")!;
            if (encodeFiles.ToLower() != "n")
            {
                var filesToEncode = encodeFiles.Split(",");
                wrapper.FilesToEncode = filesToEncode.ToList();
            }
            
            var startTimeStr = Environment.GetEnvironmentVariable("STARTTIME")!;
            var isParsed = int.TryParse(startTimeStr, CultureInfo.InvariantCulture, out wrapper.StartHour);
        }
        
        if((wrapper.Encode && wrapper.Remux) || wrapper.Remux)
            wrapper.RemixAndEncodeFiles();
        else if (wrapper.Encode && !wrapper.Remux)
            wrapper.EncodeFiles();
    }

    
}