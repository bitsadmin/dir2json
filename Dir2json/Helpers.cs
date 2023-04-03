using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Security;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

/*
Author: @bitsadmin
Website: https://github.com/bitsadmin/dir2json
License: BSD 3-Clause
*/

namespace Dir2json
{
    // FSNode structures
    [CollectionDataContract]
    public class FSNodes : List<FSNode>
    {
        ~FSNodes()
        {
            this.Clear();
        }
    }

    [DataContract]
    public class FSNode
    {
        public FSNode()
        {
            Attributes = 0;
            Size = 0;
            LastWriteTime = 0;
            SubNodes = new FSNodes();
            ParentNode = null;
        }

        ~FSNode()
        {
            SubNodes.Clear();
        }

        private static string GetModeFlags(FileAttributes f)
        {
            StringBuilder sb = new StringBuilder(6);

            sb.Append((f & FileAttributes.Directory) == FileAttributes.Directory ? "d" : "-");
            sb.Append((f & FileAttributes.Archive) == FileAttributes.Archive ? "a" : "-");
            sb.Append((f & FileAttributes.ReadOnly) == FileAttributes.ReadOnly ? "r" : "-");
            sb.Append((f & FileAttributes.Hidden) == FileAttributes.Hidden ? "h" : "-");
            sb.Append((f & FileAttributes.System) == FileAttributes.System ? "s" : "-");
            sb.Append((f & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint ? "l" : "-");

            return sb.ToString();
        }

        public override string ToString()
        {
            return string.Format(
                "{0} {1} {2}",
                GetModeFlags(this.Attributes),
                this.Name,
                this.Size > 0 ? this.Size.ToString() : ""
            );
        }

        // File or directory name
        [DataMember(Name = "N")]
        public string Name { get; set; }

        // Attributes
        [DataMember(Name = "A")]
        public FileAttributes Attributes { get; set; }
        //public uint Attributes { get; set; }

        // File size
        [DataMember(Name = "S")]
        public long Size { get; set; }

        // LastWriteTime
        [DataMember(Name = "T")]
        public long LastWriteTime { get; set; }

        // Subnodes
        [DataMember(Name = "C")]
        public FSNodes SubNodes { get; set; }

        // Parent node
        public FSNode ParentNode { get; set; }
    }

    static class Helpers
    {
        // Source: http://pinvoke.net/default.aspx/Structures/WIN32_FIND_DATA.html
        // The CharSet must match the CharSet of the corresponding PInvoke signature
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct WIN32_FIND_DATA
        {
            //public uint dwFileAttributes;
            public FileAttributes dwFileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint dwReserved0;
            public uint dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string cAlternateFileName;
            public uint dwFileType;
            public uint dwCreatorType;
            public uint wFinderFlags;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern SafeFindHandle FindFirstFile(string lpFileName, out WIN32_FIND_DATA lpFindFileData);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool FindNextFile(SafeFindHandle hFindFile, out WIN32_FIND_DATA lpFindFileData);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool FindClose(IntPtr hFindFile);

        [SecurityCritical]
        internal class SafeFindHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            [SecurityCritical]
            public SafeFindHandle() : base(true)
            { }

            [SecurityCritical]
            protected override bool ReleaseHandle()
            {
                return FindClose(base.handle);
            }
        }

        // Source: https://stackoverflow.com/a/5730893
        public static void CopyTo(this Stream input, Stream output)
        {
            byte[] buffer = new byte[16 * 1024]; // Fairly arbitrary size
            int bytesRead;

            while ((bytesRead = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, bytesRead);
            }
        }

        // Inspired by: https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.gzipstream?view=net-6.0#examples
        public static MemoryStream Compress(MemoryStream text)
        {
            MemoryStream gzcompressed = new MemoryStream();
            text.Seek(0, SeekOrigin.Begin);
            using (GZipStream gzcompressor = new GZipStream(gzcompressed, CompressionMode.Compress, true))
            {
                CopyTo(text, gzcompressor);
            }
            gzcompressed.Seek(0, SeekOrigin.Begin);

            return gzcompressed;
        }

        /*
        Path options:
            			        Local folder        Local drive     Network share           Notes
        Regular:                 C:\MyFolder         D:\               \\SRV01\MyShare	 Doesn't support long paths
        Relative:                 .\MyFolder         ..\
        DOS path:            \\?\C:\MyFolder     \\?\D:\         \\?\UNC\SRV01\MyShare   Used to circumvent MAX_PATH issues
        Local device path:   \\.\C:\MyFolder     \\.\D:\         \\.\UNC\SRV01\MyShare   Doesn't support long paths
        NT path:             \??\C:\MyFolder     \??\D:\         \??\UNC\SRV01\MyShare
        */
        public static string OptimizePath(string path)
        {
            string newPath = path;

            if (string.IsNullOrEmpty(path))
                return newPath;

            // Trim any whitespace characters
            newPath = path.Trim();

            // Make sure the path has a trailing backslash
            if (!newPath.EndsWith(@"\"))
                newPath += @"\";

            // Turn relative path into absolute paths
            // Options: ., .., .\, a, ab, \, \a
            if (newPath.Length <= 2)
            {
                newPath = Path.GetFullPath(newPath);
            }
            else
            {
                // Normalize the path if accidentally double backslashes are inserted
                // e.g. \\?\C:\Tmp\\MyPath
                if (newPath.StartsWith(@"\\"))
                    newPath = @"\\" + newPath.Substring(2).Replace(@"\\", @"\");
                else
                    newPath = newPath.Replace(@"\\", @"\");
            }

            // DOS/local device path or UNC path
            if (newPath.StartsWith(@"\\"))
            {
                // DOS or local device path has been specified -> leave as-is
                if (newPath[2] == '.' || newPath[2] == '?')
                    return newPath;

                // Regular network path has been specified
                // Turn into a DOS path network path
                else
                    return @"\\?\UNC\" + newPath.Substring(2);
            }
            // NT path -> Leave as-is
            else if (newPath.StartsWith(@"\??\"))
            {
                return newPath;
            }
            // Regular path -> Turn into DOS path
            else
            {
                return @"\\?\" + newPath;
            }
        }

        public static string GetCleanPath(string path)
        {
            return path
                .Replace(@"\\?\UNC\", @"\\")
                .Replace(@"\\?\", "")
                .Replace(@"\\.\", "")
                .Replace(@"\??\", "");
        }

        public static string GenerateJsonFileName(string path)
        {
            path = GetCleanPath(path);
            string filename = path
                .Trim(new char[] { '\\', '/', ' ', ':' })
                .Replace(@":\", "_") // Drive indicator
                .Replace(@"\", "_")
                .Replace("/", "_")
                .Replace(" ", "_");

            if (string.IsNullOrEmpty(filename))
                return "enum.json";
            else if (filename.Length == 1) // Most likely a drive letter
                return string.Format("Drive_{0}", filename);
            else
                return filename;
        }
    }

    // Inspired by: https://stackoverflow.com/a/4839265
    public class JsonHelper
    {
        public static MemoryStream To<T>(T obj)
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(obj.GetType());
            MemoryStream ms = new MemoryStream();
            serializer.WriteObject(ms, obj);
            ms.Seek(0, SeekOrigin.Begin);
            return ms;
        }
    }

    class StackList<T> : List<T>
    {
        public T Peek()
        {
            return this[this.Count - 1];
        }

        public T Pop()
        {
            T ret = Peek();
            this.RemoveAt(this.Count - 1);
            return ret;
        }
    }

    class Args
    {
        public Args(string[] args)
        {
            if (args.Length < 2)
                throw new ArgumentException("Insufficient parameters provided");

            // Path
            ListPath = Helpers.OptimizePath(args[0]);

            // Output
            string output = args[1];
            OutFileName = null;
            switch (output.ToLowerInvariant())
            {
                case "con":
                    OutputType = OutputTypes.Console | OutputTypes.Gzipped;
                    break;
                case "conraw":
                    OutputType = OutputTypes.Console;
                    break;
                case "cs":
                    OutputType = OutputTypes.CobaltStrikeDl | OutputTypes.Gzipped;
                    break;
                case "csraw":
                    OutputType = OutputTypes.CobaltStrikeDl;
                    break;
                default:
                    OutputType = OutputTypes.File;

                    // Check if an extension is provided to make sure a typo in the output parameter
                    // Doesn't accidentally write a file to disk
                    if (!output.Contains("."))
                        throw new ArgumentException(string.Format("To avoid accidental storage on disk when mistyping the output parameter, please provide an extension, e.g. {0}.json.gz", output));

                    // Determine whether JSON output needs to be saved plain or gzipped
                    Regex r = new Regex(@".*\.gz(ip)?");
                    if (r.IsMatch(output))
                        OutputType |= OutputTypes.Gzipped;

                    OutFileName = Path.GetFullPath(output);
                    break;
            }

            // Position-independent parameters
            FollowSymlinks = false;
            EntriesPerFile = 0;
            MaxDepth = 999;

            for (int i=2; i<args.Length; i++)
            {
                string arg = args[i].ToLowerInvariant();

                // --MaxDepth=10
                if (arg.StartsWith("/maxdepth="))
                {
                    string strMaxDepth = arg.Replace("/maxdepth=", "");
                    uint _maxDepth;
                    if (!uint.TryParse(strMaxDepth, out _maxDepth))
                        throw new ArgumentException("MaxDepth is not a valid unsigned integer");

                    if (_maxDepth == 0)
                        _maxDepth = 999;

                    MaxDepth = _maxDepth;
                }

                // --EntriesPerFile=100
                else if (arg.StartsWith("/entriesperfile="))
                {
                    string strMaxEntriesPerFile = arg.Replace("/entriesperfile=", "");
                    uint _entriesPerFile;
                    if (!uint.TryParse(strMaxEntriesPerFile, out _entriesPerFile))
                        throw new ArgumentException("EntriesPerFile is not a valid unsigned integer");

                    EntriesPerFile = _entriesPerFile * 1000;
                }

                // --FollowSymlinks
                else if (arg == "/followsymlinks")
                {
                    FollowSymlinks = true;
                }

                // Unknown
                else
                {
                    throw new ArgumentException("Unknown parameter: {0}", arg);
                }
            }
        }

        public string ListPath { get; }
        public uint MaxDepth { get; }
        public uint EntriesPerFile { get; }
        public OutputTypes OutputType { get; }
        public string OutFileName { get; }
        public bool FollowSymlinks { get; }
    }

    enum OutputTypes
    {
        File = 1,
        Console = 2,
        CobaltStrikeDl = 4,
        Gzipped = 8,
    }
}
