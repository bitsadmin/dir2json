using System.IO;
#if BOFBUILD
using BOFNET;
#else
using System;
#endif

/*
Author: @bitsadmin
Website: https://github.com/bitsadmin/dir2json
License: BSD 3-Clause
*/

namespace Dir2json
{
#if BOFBUILD
    partial class Program : BeaconObject
    {
        public Program(BeaconApi api) : base(api)
        {
        }
#else
    partial class Program
    {
#endif
        public volatile static Program bofnet = null;

        public static void WriteLine(string format, params object[] arg)
        {
#if BOFBUILD
            bofnet.BeaconConsole.WriteLine(format, arg);
#else
            if (arg.Length > 0)
                Console.WriteLine(format, arg);
            else
                Console.WriteLine(format);
#endif
        }

        public static void WriteErrorLine(string format, params object[] arg)
        {
#if BOFBUILD
            bofnet.BeaconConsole.WriteLine(format, arg);
#else
            if (arg.Length > 0)
                Console.Error.WriteLine(format, arg);
            else
                Console.Error.WriteLine(format);
#endif
        }

        public static void CobaltStrikeDownload(string fileName, Stream fileData)
        {
#if BOFBUILD
            bofnet.DownloadFile(fileName, fileData);
#else
            throw new ArgumentException("Attempting to request a Cobalt Strike in-memory download from a non-beacon context.");
#endif
        }
    }
}
