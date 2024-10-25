# Dir2json
Dir2json is a .NET utility which recursively lists drives or network shares storing the directory listing including various attributes in a (gzipped) .json file. Attributes that are collected are:
- Name of the file or directory
- Size of the file in bytes
- Last Modified date of the file or directory
- Attributes of the file or directory ([.NET FileAttributes](https://learn.microsoft.com/en-us/dotnet/api/system.io.fileattributes#fields))

Dir2json can either be executed from the commandline, or from memory in a Cobalt Strike beacon using @CCob's [BOF.NET](https://github.com/CCob/BOF.NET) where Cobalt Strike's in-memory download functionality is used to retrieve the resulting file listing.

As a .json file is not as easy to search through, the `json2csv.ps1` or `json2csv.py` scripts convert the (hierarchical) JSON structure to a (flat) CSV file which can be easily queried using PowerShell (examples in `CheatSheet.ps1`) or using tools like `grep` (examples in `CheatSheet.sh`).

This utility has been developed for use in Red Team assignments to be able to efficiently perform offline searches for interesting files and directories. At the BITSADMIN blog an in-depth article on this tool is available: [Digging for Secrets on Corporate Shares](https://blog.bitsadmin.com/blog/digging-for-secrets).

Latest binaries available from the [Releases](https://github.com/bitsadmin/dir2json/releases) page.


## Demos
### In-memory execution in Cobalt Strike

[cobaltstrike.mp4](https://blog.bitsadmin.com/assets/img/20230403_digging-for-secrets/cobaltstrike.mp4)

### Convert JSON to CSV and query CSV

[powershell.mp4](https://blog.bitsadmin.com/assets/img/20230403_digging-for-secrets/powershell.mp4)

### Example queries
A cheat sheet with various PowerShell queries generating, importing and querying the directory listing can be found in the `CheatSheet.ps1` file in this repository. Additionally, `CheatSheet.sh` is available to perform similar queries in bash.


## Usage
### Cobalt Strike 
```shell
bofnet_init
bofnet_load /path/to/Dir2json/Dir2json.dll
bofnet_execute Dir2json.Program C:\ cs
bofnet_shutdown
```

### Commandline
```cmd
dir2json.exe C:\ drive_C.json.gz
```

### Usage
```
== Dir2json v1.0 ==
Twitter: @bitsadmin
Website: https://github.com/bitsadmin/dir2json


Usage: Dir2json.exe drive:path output [/MaxDepth=value] [/EntriesPerFile=value] [/FollowSymlinks]
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
            Maximum entries per file (x 1000). Makes sure that memory is periodically flushed. Default: 0 meaning GêP
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
    bofnet_execute Dir2json.Program C:\ C:\Temp\Drive_X.json.gz
```


## Notes
- Before the full directory listing is stored on disk, it is kept in memory. Even though the storage is quite efficient, on large filesystems it can use quite some memory and if the system does not have a lot of memory could lead to OutOfMemory exceptions. For that reason the `/EntriesPerFile=X` parameter is available which downloads the results thusfar and frees up memory before moving on. The `json2csv.ps1` and `json2csv.py` scripts recognize when a file has been split and merges the output back into a single .csv.
- For some reason in some circumstances (which I was not able to debug because it was a client environment) executing dir2json against `C:\` through BOF.NET just stops execution after a while without showing any message while the beacon just stays alive. In those cases the instead of collecting the files of the full `C:\` drive, create a file listing of the different folders (e.g. `C:\Users\`, `"C:\Program Files\"` individually and merge the outputs later.


## Future work
- Support for enumeration of folders and directories on SharePoint sites (`Microsoft.SharePoint.Client` .NET namespace)
- Support for the Windows registry
