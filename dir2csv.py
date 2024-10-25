import csv
import re
import os
import argparse

# Banner: Display script version and contact information
print('== dir2csv.py v0.1 ==')
print('Twitter: @bitsadmin')
print('Website: https://github.com/bitsadmin/dir2json')

def dir_to_csv(txt_file, csv_out_file):
    # Define output name if not provided; uses the input filename to create the output filename
    if csv_out_file is None:
        filename = os.path.splitext(os.path.basename(txt_file))[0] + '.csv'
        csv_out_file = os.path.join(os.path.dirname(txt_file), filename)

    # Read the content of the input text file
    with open(txt_file, 'r', encoding='utf-8', errors='replace') as f:
        directory_listing = f.read()

    # Split the directory listing into lines for processing
    lines = directory_listing.split('\n')
    csv_data = []  # List to store processed CSV data
    current_dir = ''  # Variable to track the current directory being processed

    # Open the output CSV file for writing
    with open(csv_out_file, 'w', newline='', encoding='utf-8') as csvfile:
        fieldnames = ['Name', 'Mode', 'Length', 'LastWriteTime', 'FullName']  # Define CSV column headers
        writer = csv.DictWriter(csvfile, fieldnames=fieldnames)  # Create a CSV writer object
        writer.writeheader()  # Write the header row to the CSV file
        print()

        i = 0 
        for line in lines:
            i += 1
            # Print progress every 500.000 lines processed
            if i % 500000 == 0:
                print(f'Processed lines: {i} / {len(lines)}')

            # Check if the line indicates a new directory
            if 'Directory of' in line:
                current_dir = line.split('Directory of')[1].strip(' \\')  # Update current directory
            
            # Process lines that do not contain directories but have file information
            elif '<DIR>' not in line and current_dir:
                # Skip summary lines
                if re.match(r'\d+ File\(s\)', line.strip()):
                    continue

                # Split the line into components: date, time, length, and name
                parts = re.split(r'\s+', line.strip(), 3)
                if len(parts) == 4:
                    date, time, length, name = parts
                    # Write the file's details to the CSV
                    writer.writerow({
                        'Name': name,
                        'Mode': '------',
                        'Length': length,
                        'LastWriteTime': f'{date} {time}',
                        'FullName': f'{current_dir}\\{name}'
                    })
            
            # Process lines that indicate a directory
            elif '<DIR>' in line and current_dir:
                parts = re.split(r'\s+', line.strip(), 3)
                if len(parts) == 4:
                    date, time, _, name = parts
                    
                    # Skip current and parent directory indicators
                    if name in ('.', '..'):
                        continue
                    
                    # Write the directory's details to the CSV
                    writer.writerow({
                        'Name': name,
                        'Mode': 'd-----',
                        'Length': '1',
                        'LastWriteTime': f'{date} {time}',
                        'FullName': f'{current_dir}\\{name}'
                    })
        
        # Final progress report
        print(f'Processed lines: {len(lines)} / {len(lines)}')
        print(f'Output stored in "{csv_out_file}"')

def main():
    # Set up command-line argument parsing
    parser = argparse.ArgumentParser(description='Convert output of dir /s to CSV')
    parser.add_argument('txt_file', type=str, help='Path to the input .txt file containing the output of dir /s')
    parser.add_argument('-o', '--output', type=str, help='Path to the output CSV file. If not provided, the output file will be generated based on the input file name.')

    args = parser.parse_args()
    dir_to_csv(args.txt_file, args.output)

if __name__ == '__main__':
    main()
