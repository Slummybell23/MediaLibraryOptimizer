﻿using System.Diagnostics;
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
            //wrapper.Libraries = new List<string>() { "Z:\\Plex\\Movie" };
            wrapper.Libraries = new List<string>() { "E:\\test" };
            //wrapper.MovieFolder = "E:\\ssdMovie\\Avatar Test";
            //"Z:\\Plex\\Movie", 
            //movieFolder = "Z:\\Plex\\Movie\\Coraline (2009)";
            //wrapper.TvShowFolder = "Z:\\Plex\\TV show";
            wrapper.RemuxDolbyVision = false;
            wrapper.EncodeHevc = false;
            wrapper.EncodeAv1 = true;
            wrapper.StartHour = DateTime.Now.Hour;
            //wrapper.FilesToEncode.Add("Coraline (2009)");
        }
        
        wrapper.ProcessLibrary();
   }
}