<#
Author: @bitsadmin
Website: https://github.com/bitsadmin/dir2json
Blog: https://blog.bitsadmin.com/blog/efficient-directory-structure-enumeration-using-dir2json
License: BSD 3-Clause
#>

# --------------------------------
# Collection
# --------------------------------
# Create directory listing (Windows PowerShell)
# "-Depth 15" can also be used instead of "-Recurse" to specify a maximum recursion depth
# Path can also be a network share: "\\?\UNC\FS01\Share" instead of "\\?\C:\". FullName.Replace("\\?\","") then also needs to be updated to FullName.Replace("\?\UNC","")
Get-ChildItem -LiteralPath '\\?\C:\' -Depth 25 -Force | ForEach-Object { [PSCustomObject]@{Name=$_.Name; Mode=$_.Mode; Length=$_.Length; LastWriteTime=$_.LastWriteTime.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss'); FullName=$_.FullName.Replace('\\?\','') } } | Export-Csv -Encoding UTF8 -NoTypeInformation Drive_C.csv

# Create directory listing (PowerShell Core)
# Even though the Windows PowerShell oneliner can also be used in PowerShell Core, the PowerShell Core onlineliner is a bit shorter and looks a bit cleaner
# Dash is added to Mode attribute to have the PowerShell Core listing match the Windows PowerShell one. PowerShell core updates the first Mode flag ('d') to 'l' in case the folder is a symlink
# By default PowerShell Core does not enter into symlinked directories, but this can be forced using the -FollowSymlink parameter
Get-ChildItem -Recurse -Force -Path 'C:\' | % { [PSCustomObject]@{Name=$_.Name; Mode=$_.Mode+'-'; Length=$_.Length; LastWriteTime=$_.LastWriteTime.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss'); FullName=$_.FullName } } | Export-Csv -NoTypeInformation Drive_C.csv

# Create directory listing (Dir2json)
Dir2Json.exe C:\ Drive_C.json.gz
# Convert to csv
.\Json2csv.ps1 Drive_C.json.gz


# --------------------------------
# Import
# --------------------------------
# Different options for importing:
# D0: Plain CSV
# D1: Length attribute as integer
# D2: Extension attribute
# D3: Depth attribute
# D4: Mode attributes
$csv = Import-Csv -Path 'Drive_C.csv' | Select-Object `
	Name,@{n='Length';e={[int64]$_.Length}},Mode,LastWriteTime,FullName,`
	@{n='Extension';e={if($_.Mode[0] -ne 'd'){[System.IO.Path]::GetExtension($_.Name)}else{''}}},`
	@{n='Depth';e={$_.FullName.Split('\').Count - 1}}#,`
<#
	@{n='Directory';e={$_.Mode[0] -eq 'd'}},`
	@{n='Archive';e={$_.Mode[1] -eq 'a'}},`
	@{n='ReadOnly';e={$_.Mode[2] -eq 'r'}},`
	@{n='Hidden';e={$_.Mode[3] -eq 'h'}},`
	@{n='System';e={$_.Mode[4] -eq 's'}},`
	@{n='ReparsePoint';e={$_.Mode[5] -eq 'l' -or $_.Mode[0] -eq 'l'}}
#>


# --------------------------------
# Query
# --------------------------------
# Statistics
$csv | Measure-Object | Select-Object -ExpandProperty Count # Total number of entries
$csv | Where-Object Mode -NotMatch 'd.....' | Measure-Object | Select-Object -ExpandProperty Count # Number of files
$csv | Where-Object Mode -Match 'd.....' | Measure-Object | Select-Object -ExpandProperty Count # Number of directories
# In case D4 is used when importing, the following queries can also be used
#$csv | Where-Object -not Directory | Measure-Object | Select-Object -ExpandProperty Count # Number of files
#$csv | Where-Object Directory | Measure-Object | Select-Object -ExpandProperty Count # Number of directories

# List most common extensions
$exts = $csv | Where-Object Mode -NotMatch 'd.....' | Group-Object Extension -NoElement | Sort-Object -Descending Count,Name
$exts | Select-Object -First 25 | Format-Table Name,Count

# Search for string in filename
$csv | Where-Object Name -match 'password' | Format-Table -AutoSize Mode,LastWriteTime,Length,FullName

# Search files with interesting extensions
# Sysadmin files
$admin = $csv | Where-Object Extension -Match '^\.(kdbx?|pfx|p12|pem|p7b|key|ppk|crt|pub|config|cfg|ini|sln|\w{2}proj|sql|cmd|bat|ps1|vbs|log|rdp|rdg|ica|wim|vhdx?|vmdk)$'
$admin | Group-Object Extension -NoElement | Sort-Object -Descending Count,Name | Format-Table Name,Count
$admin | Foreach-Object { $f=$_.FullName -split '\\'; if($f.Count -gt 3){$f[0..2] -join '\'} } | Select-Object -Unique # List folders in which admin files are found
$admin | Sort-Object -Descending LastWriteTime | Format-Table Mode,LastWriteTime,Length,FullName | Out-Host -Paging

# Office files
$office = $csv | Where-Object Extension -Match '^\.((doc|xls|ppt|pps|vsd)[xm]?|txt|csv|one|pst|url|lnk)$'
$office | Group-Object Extension -NoElement | Sort-Object -Descending Count,Name | Format-Table Name,Count
$office | Sort-Object FullName | Select-Object Mode,LastWriteTime,Length,FullName | Export-Csv -NoTypeInformation C:\Tmp\office_files.csv

# List .cmd extensions that are not in C:\Windows\WinSxS
$admin | Where-Object Extension -EQ '.cmd' | Where-Object FullName -NotMatch 'C:\\Windows\\WinSxS\\' | Format-Table Mode,LastWriteTime,Length,FullName

# List files with specific extensions
$admin | Where-Object Extension -In @('.vbs','.cmd','.ps1','.bat') | Format-Table Mode,LastWriteTime,Length,FullName

# Recursively list all files and folders of a certain directory
$csv | Where-Object FullName -Like 'C:\Data\*' | Format-Table Mode,LastWriteTime,Length,FullName

# List files in specific path
$csv | Where-Object Depth -EQ 1 | Where-Object FullName -Like 'C:\*', | Format-Table -AutoSize Mode,LastWriteTime,Length,Name
$csv | Where-Object Depth -EQ 2 | Where-Object FullName -Like 'C:\Users\*' | Format-Table -AutoSize Mode,LastWriteTime,Length,Name

# Calculate size of a folder
($csv | Where-Object FullName -Like 'C:\Data\*' | Measure -Sum Length).Sum / 1GB

# Top 25 largest files on the filesystem
$csv | Sort-Object -Descending Length | Select-Object -First 25 | Select-Object Mode,LastWriteTime,@{n='LengthMB';e={[int][Math]::Truncate($_.Length/1MB)}},FullName | Format-Table Mode,LastWriteTime,LengthMB,FullName

# List of directory symlinks
# This query only works for CSVs created by Dir2json or Windows PowerShell; in PowerShell Core, the 'd' will have been substituted by an 'l',
# however then it is not clear anymore whether it is a symlinked file or directory
$csv | Where-Object Mode -Match 'd....l' | Format-Table Mode,LastWriteTime,Length,FullName

# List 25 most recently created hidden directories
$csv | Sort-Object -Descending LastWriteTime | Where-Object Mode -Match 'd..h..' | Select-Object -First 25 | Format-Table Mode,LastWriteTime,Length,FullName

# Recently created directories
$csv | Where-Object Mode -Match 'd.....' | Sort-Object -Descending LastWriteTime | Select-Object -First 20 | Format-Table Mode,LastWriteTime,Length,FullName