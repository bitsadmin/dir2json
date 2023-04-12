# Author: @bitsadmin
# Website: https://github.com/bitsadmin/dir2json
# Blog: https://blog.bitsadmin.com/blog/digging-for-secrets
# License: BSD 3-Clause

# Statistics
tail -n +2 Drive_C.csv | wc -l # Total number of entries
awk -F',' '$2 !~ /"d....."/ { print }' Drive_C.csv | wc -l # Number of files
awk -F',' '$2 ~ /"d....."/ { print }' Drive_C.csv | wc -l # Number of directories

# List most common extensions
awk -F',' '$2 !~ "d....." { split($1, a, /[.]/); ext = a[length(a)]; gsub(/"/,"",ext); count[ext]++ } END { for (e in count) printf ".%s\t%d\n", e, count[e] | "sort -k2nr" }' Drive_C.csv | head -n25

# Search for string in filename
awk -F',' 'NR == 1 || $1 ~ /password/ { print }' Drive_C.csv

# Search files with interesting extensions
# Sysadmin files
admin=$(awk -F',' 'NR == 1 || $1 ~ /\.(kdbx?|pfx|p12|pem|p7b|key|ppk|crt|pub|config|cfg|ini|sln|\w{2}proj|sql|cmd|bat|ps1|vbs|log|rdp|rdg|ica|wim|vhdx?|vmdk)"$/ { print }' Drive_C.csv)
awk -F',' '$2 !~ "d....." { split($1, a, /[.]/); ext = a[length(a)]; gsub(/"/,"",ext); count[ext]++ } END { for (e in count) printf ".%s\t%d\n", e, count[e] | "sort -k2nr" }' <<< $admin | head -n25 | column -t -s,

# Office files
office=$(awk -F',' 'NR == 1 || $1 ~ /\.((doc|xls|ppt|pps|vsd)[xm]?|txt|csv|one|pst|url|lnk)"$/ { print }' Drive_C.csv)
awk -F',' '$2 !~ "d....." { split($1, a, /[.]/); ext = a[length(a)]; gsub(/"/,"",ext); count[ext]++ } END { for (e in count) printf ".%s\t%d\n", e, count[e] | "sort -k2nr" }' <<< $office | head -n25 | column -t -s,

# List .cmd extensions that are not in C:\Windows\WinSxS
awk -F',' 'NR == 1 || $1 ~ /\.cmd"$/ && $5 !~ /^"C:\\Windows\\WinSxS\\/ { print }' Drive_C.csv | column -t -s,

# List files in specific path
awk -F',' 'NR == 1 || $5 ~ /^"C:\\/ && split($5, a, "\\") && length(a) == 2 { print }' Drive_C.csv | column -t -s,
awk -F',' 'NR == 1 || $5 ~ /^"C:\\Users\\/ && split($5, a, "\\") && length(a) == 3 { print }' Drive_C.csv | column -t -s,

# Calculate size of a folder
awk -F ',' '$5 ~ /^"C:\\Data\\/{gsub(/"/, "", $3); sum+=$3} END {printf "%.2f\n", sum/(1024*1024*1024)}' Drive_C.csv

# Top 25 largest files on the filesystem
awk -F"," 'BEGIN {OFS=","} NR==1 {print} NR>1 {gsub(/"/,"",$3); print | "sort -t, -k3rn"}' Drive_C.csv | head -n25
