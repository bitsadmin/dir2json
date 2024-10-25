import gzip
import os
import json
import csv
import re
from datetime import datetime, timezone, timedelta
import argparse

# Banner
print('== json2csv.py v1.0 ==')
print('Twitter: @bitsadmin')
print('Website: https://github.com/bitsadmin/dir2json')

# Expand a gzip archive.
def expand_gzip_archive(in_file, out_file=None):
    if out_file is None:
        out_file = os.path.splitext(in_file)[0]
    with gzip.open(in_file, 'rb') as gz_file:
        with open(out_file, 'wb') as out_file_stream:
            out_file_stream.write(gz_file.read())
    print(f'[+] Decompressed "{in_file}" to "{out_file}"')

# Convert filesystem attribute flags to characters
def get_mode_flags(attr):
    d = 'd' if attr & 0x10 else '-'
    a = 'a' if attr & 0x20 else '-'
    r = 'r' if attr & 0x01 else '-'
    h = 'h' if attr & 0x02 else '-'
    s = 's' if attr & 0x04 else '-'
    l = 'l' if attr & 0x400 else '-'
    return f'{d}{a}{r}{h}{s}{l}'

# Collect file paths if sequence is used
def get_file_sequence(filepath):
    match = re.match(r'(.*_)(\d{3})(\..*)', filepath)

    if match:
        print('[+] Files sequence detected')
        prefix = match.group(1)
        postfix = match.group(3)
        input_files = []

        for i in range(1, 1000):
            test_path = f'{prefix}{i:03}{postfix}'
            if os.path.exists(test_path):
                input_files.append(test_path)
            else:
                break
    else:
        input_files = [filepath]
    
    return input_files

# Convert file time (ticks) to seconds
def filetime_to_datetime(file_time):
    # File time is in 100-nanosecond intervals, so divide by 10**7 to convert to seconds
    seconds_since_epoch = (file_time - 116444736000000000) // 10**7

    # Create a datetime object in UTC
    utc_datetime = datetime(1970, 1, 1, tzinfo=timezone.utc) + timedelta(seconds=seconds_since_epoch)

    # Format the datetime object as a string
    formatted_datetime = utc_datetime.strftime('%Y-%m-%d %H:%M:%S')

    return formatted_datetime

# Recursively process the JSON tree into a flat structure
def enumerate_json(path, nodes, csv_writer):
    for i, node in enumerate(nodes, start=1):
        # Compile CSV record
        full_path = os.path.join(path, node['N'])
        last_write_time = filetime_to_datetime(int(node['T']))
        csv_writer.writerow({
            'Name': node['N'],
            'Mode': get_mode_flags(int(node['A'])),
            'Length': node['S'],
            'LastWriteTime': last_write_time,
            'FullName': full_path
        })

        # Status update
        if i % 100000 == 0:
            print(f'[+] Processed entries: {i}')

        # Recurse if directory
        if node['A'] & 0x10:
            enumerate_json(full_path, node.get('C', []), csv_writer)

# Convert JSON file to CSV format
def json_to_csv(json_file, csv_out_file=None):
    # Check if input file exists
    if not os.path.exists(json_file):
        raise FileNotFoundError(f'[-] File "{json_file}" does not exist')

    # Determine input file(s)
    json_file_resolved = os.path.abspath(json_file)
    input_files = get_file_sequence(json_file_resolved)

    # Define output name if not defined
    if csv_out_file is None:
        filename = os.path.splitext(os.path.basename(json_file.replace('.gz','').replace('_001','')))[0] + '.csv'
        csv_out_file = os.path.join(os.path.dirname(json_file), filename)

    # Initiate CSV writer
    with open(csv_out_file, 'w', newline='', encoding='utf-8') as csvfile:
        fieldnames = ['Name', 'Mode', 'Length', 'LastWriteTime', 'FullName']
        writer = csv.DictWriter(csvfile, fieldnames=fieldnames)
        writer.writeheader()

        # Iterate over json(.gz) files
        for json_file in input_files:
            # First extract file if it ends with .gz
            if json_file.endswith('.gz'):
                json_out_file = os.path.splitext(json_file)[0]
                print(f'[+] File "{json_file}" is gzip compressed, decompressing...')
                expand_gzip_archive(json_file, json_out_file)
            else:
                json_out_file = json_file

            # Load JSON into memory
            with open(json_out_file, 'r', encoding='utf-8') as f:
                js = json.load(f)

            # Enumerate and store to CSV
            root = js['N'].rstrip('\\')
            print(f'[+] Converting JSON to CSV for "{json_file}"')
            enumerate_json(root, js['C'], writer)

            # Cleanup
            if json_file.endswith('.gz'):
                print(f'[+] Cleaning up extracted gzip file "{json_out_file}"')
                os.remove(json_out_file)

    print(f'[+] CSV output file writen to "{csv_out_file}"')

def main():
    parser = argparse.ArgumentParser(description='Convert a JSON file to CSV format')
    parser.add_argument('json_file', type=str, help='Path to the input JSON file (can be gzipped).')
    parser.add_argument('-o', '--output', type=str, help='Path to the output CSV file. If not provided, the output file will be generated based on the input file name.')

    args = parser.parse_args()
    json_to_csv(args.json_file, args.output)

if __name__ == '__main__':
    main()
