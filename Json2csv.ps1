param(
    [Parameter(Mandatory)]$JsonFile,
    $CsvOutFile
)

<#
Author: @bitsadmin
Website: https://github.com/bitsadmin/dir2json
License: BSD 3-Clause
#>


<#
.SYNOPSIS
Extract gzip archive

.PARAMETER InFile
Path to a gzipped file

.PARAMETER OutFile
Optional parameter specifying where to store the extracted file. By default uses the InFile parameter, removing the .gz extension.

.EXAMPLE
Expand-GZipArchive -InFile C:\tmp\file.json.gz
Expand-GZipArchive -InFile C:\tmp\file.json.gz -OutFile C:\extracted\file.json

.NOTES
Source: https://social.technet.microsoft.com/Forums/windowsserver/en-US/5aa53fef-5229-4313-a035-8b3a38ab93f5/unzip-gz-files-using-powershell#19df9b94-1704-4a19-a946-566c2b5d19c3
#>
Function Expand-GZipArchive {
    Param(
        [Parameter(Mandatory)]$InFile,
        $OutFile = ($InFile -replace '\.gz$','')
    )

    $inStream = New-Object System.IO.FileStream $InFile, ([IO.FileMode]::Open), ([IO.FileAccess]::Read), ([IO.FileShare]::Read)
    $outStream = New-Object System.IO.FileStream $OutFile, ([IO.FileMode]::Create), ([IO.FileAccess]::Write), ([IO.FileShare]::None)
    $gzipStream = New-Object System.IO.Compression.GzipStream $inStream, ([IO.Compression.CompressionMode]::Decompress)

    $buffer = New-Object byte[](1024)
    while($true)
    {
        $read = $gzipstream.Read($buffer, 0, 1024)
        
        if ($read -le 0)
            {break}
        
        $outStream.Write($buffer, 0, $read)
    }

    $gzipStream.Close()
    $outStream.Close()
    $inStream.Close()
}


<#
.SYNOPSIS
# Convert flags to characters

.PARAMETER attr
Unsigned integer specifying the filesystem attribute flags

.EXAMPLE
GetModeFlags(0x10)

.NOTES
List of all file attributes: https://learn.microsoft.com/en-us/dotnet/api/system.io.fileattributes#fields
#>
function GetModeFlags($attr)
{
	if($attr -band 0x10){$d='d'}else{$d='-'}    # Directory
	if($attr -band 0x20){$a='a'}else{$a='-'}    # Archive
	if($attr -band 0x01){$r='r'}else{$r='-'}    # ReadOnly
	if($attr -band 0x02){$h='h'}else{$h='-'}    # Hidden
	if($attr -band 0x04){$s='s'}else{$s='-'}    # System
	if($attr -band 0x400){$l='l'}else{$l='-'}   # ReparsePoint
    
    return @($d, $a, $r, $h, $s, $l) -join ''
}


<#
.SYNOPSIS
Recurse the JSON tree and turn it into a flat array of filesystem nodes

.PARAMETER Path
Build of of path carried into the recursion

.PARAMETER Nodes
Childnodes to enumerate

.NOTES
Node structure
- N: Name
- A: Attributes (e.g. 0x10 = Directory)
- S: Size
- T: LastWriteTime
- C: Children
#>
Function Enumerate($Path, $Nodes)
{
    foreach($node in $Nodes)
    {
        $fullPath = "{0}\{1}" -f $Path,$node.N

        # Recurse directories
        if($node.A -band 0x10)
        {
            Enumerate -Path $fullPath -nodes $node.C
        }

        # Convert LastWriteTime
        $lastWriteTime = [DateTime]::FromFileTimeUtc([Int64]$node.T).ToUniversalTime().ToString('yyyy-MM-dd HH:mm:ss')

        # Output both Directories and Files
        [PSCustomObject]@{
            Name=$node.N; Mode=GetModeFlags([int]$node.A); Length=$node.S; LastWriteTime=$lastWriteTime; FullName=$fullPath;
            # Directory=($attr -band 0x10) -shr 4;
            # Archive=($attr -band 0x20) -shr 5;
            # ReadOnly=($attr -band 0x01);
            # Hidden=($attr -band 0x02) -shr 1;
            # System=($attr -band 0x04) -shr 2;
            # ReparsePoint=($attr -band 0x400) -shr 10;
        }
        
        # Periodically provide update
        $global:i += 1
        if($global:i % 100000 -EQ 0)
        {
            Write-Verbose ([string]::Format($n, "Processed entries: {0,8:N0}", $global:i))
        }
    }
}


<#
.SYNOPSIS
Convert a relative path to an absolute path

.DESCRIPTION
Convert a relative path to an absolute path. Also supports non-existing paths as opposed to Resolve-Path which only supports existing paths.

.PARAMETER Path
Relative path to be resolved to an absolute path

.EXAMPLE
Resolve-NewPath -Path .\..\MyFile.csv

.NOTES
General notes
#>
Function Resolve-NewPath
{
    param($Path)

    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
}


# Banner
"== Json2csv v1.0 ==
Twitter: @bitsadmin
Website: https://github.com/bitsadmin/dir2json
"

# Make sure input JSON exists
if(-not (Test-Path $JsonFile))
{
    throw ("[-] File `"{0}`" does not exist" -f $JsonFile)
}

# Expand to full path
$JsonFileResolved = Resolve-NewPath $JsonFile

# Check if filename is part of sequence
$match = $JsonFileResolved | Select-String '(.*_)(\d{3})(\..*)'
if($match)
{
    "[+] Files sequence detected"
    $prefix = $match.Matches.Groups[1].Value
    $postfix = $match.Matches.Groups[3].Value

    $inputFiles = @()
    for($i=1; $i -le 999; $i++)
    {
        $testPath = '{0}{1:000}{2}' -f $prefix,$i,$postfix
        if(Test-Path $testPath)
            { $inputFiles += $testPath }
        else
            { break }
    }
}
else
{
    $inputFiles = @($JsonFileResolved)
}

# Configure number format for verbose display of processed records
$n = [System.Globalization.CultureInfo]::InvariantCulture.Clone()
$n.NumberFormat.NumberGroupSeparator = '.'

# Iterate over input files and convert them to CSV
$total = $inputFiles.Count
$n = 1
$outputFiles = @()
foreach($JsonFile in $inputFiles)
{
    "[+] Parsing file $n of $total"

    # Extract file first if it ends with .gz
    if([System.IO.Path]::GetExtension($JsonFile) -EQ '.gz')
    {
        # Compile output filename
        $path = Split-Path -Parent $JsonFile
        $filenamewithext = Split-Path -Leaf $JsonFile
        $filename = [System.IO.Path]::GetFileNameWithoutExtension($filenamewithext)
        if(-not [System.IO.Path]::GetExtension($filename) -EQ '.json')
            { $filename += '.json' }
        $JsonOutFile = Join-Path -Path $path -ChildPath $filename
        "[+] File `"{0}`" is gzip compressed, decompressing..." -f $filenamewithext

        # Extract
        Expand-GZipArchive $JsonFile $JsonOutFile
        "[+] Decompressed to `"{0}`"" -f $JsonOutFile
        
        # Update path to point to extracted .json file
        $JsonFile = $JsonOutFile
    }

    # If there is only one input file and an output filename is provided, use it
    if($inputFiles.Count -eq 1 -and -not [string]::IsNullOrEmpty($CsvOutFile))
    {
        $GeneratedCsvOutFile = $CsvOutFile
    }
    else
    {
        # Generate filename
        $path = Split-Path -Parent $JsonFile
        $filenamewithext = Split-Path -Leaf $JsonFile
        $filename = [System.IO.Path]::GetFileNameWithoutExtension($filenamewithext) + ".csv"
        $GeneratedCsvOutFile = Join-Path -Path $path -ChildPath $filename

            # Use generated name for final output file if no name is provided
        if([string]::IsNullOrEmpty($CsvOutFile))
        {
            $CsvOutFile = $GeneratedCsvOutFile -replace '_001',''
        }
    }
    $outputFiles += $GeneratedCsvOutFile

    # Load JSON
    $jsonFileSize = [Math]::Round((Get-ChildItem $JsonFile).Length / 1MB, 0)
    "[+] Loading JSON from `"{0}`" ({1} MB). This may take a while." -f $JsonFile,$jsonFileSize
    $js = Get-Content -Encoding UTF8 -Path $JsonFile | ConvertFrom-Json

    # Expand CSV output file to full path
    $GeneratedCsvOutFile = Resolve-NewPath $GeneratedCsvOutFile
    "[+] CSV output file will be written to `"{0}`"" -f $GeneratedCsvOutFile

    # Recursively transform the JSON into a CSV
    "[+] Converting JSON to CSV"
    $root = $js.N -replace '(.*)\\','$1'
    $global:i = 0
    Enumerate -Path $root -nodes $js.C | Export-Csv -NoTypeInformation -Encoding UTF8 $GeneratedCsvOutFile
    Write-Verbose ([string]::Format($n, "Processed entries: {0,8:N0}", $global:i))
    $csvFileSize = [Math]::Round((Get-ChildItem $GeneratedCsvOutFile).Length / 1MB, 0)
    "[+] Done! CSV output file size is {0}MB" -f $csvFileSize

    # If a gzip has been extracted, cleanup
    if($JsonOutFile)
    {
        "[+] Cleaning up extracted gzip file"
        Remove-Item $JsonOutFile
    }

    $n += 1
}

# Finish in case no files need to be merged
if($outputFiles.Count -eq 1)
{
    return
}

"[+] Merging CSV files"
$csv = @()
foreach($GeneratedCsvOutFile in $outputFiles)
{
    "[+] Loading file `"{0}`"" -f $GeneratedCsvOutFile
    $csv += Import-Csv $GeneratedCsvOutFile
    Remove-Item $GeneratedCsvOutFile
}

# When merging there are sometimes some duplicate directories,
# which are also immediately merged here
"[+] Writing to output file `"{0}`"" -f $CsvOutFile
$csv | Sort-Object -Unique FullName | Export-Csv -NoTypeInformation -Encoding UTF8 $CsvOutFile