import os
import sys
from pathlib import Path
from filetranslate.service_fn import read_csv_list

def escape_csharp_string(s):
    """Escape special characters for C# string literals"""
    if s is None:
        return ""
    return s.replace('\\', '\\\\').replace('"', '\\"').replace('\n', '\\n').replace('\r', '\\r')

def generate_csharp_localization(csv_path, output_path):
    """Generate C# localization file from CSV"""

    # Read CSV data
    data = read_csv_list(csv_path)

    # Get file size for the constant
    file_size = os.path.getsize(csv_path)

    # Start building the C# file
    cs_content = """using System.Collections.Concurrent;
using System.Collections.Generic;

public static partial class Localization
{
    private static readonly ConcurrentDictionary<string, LocalizationEntry> _csvResources = InitializeResources();

    private const int DEFAULT_CSV_SIZE = """ + str(file_size) + """; // Only load GARbro.en-US.csv if the size is not this

    private static ConcurrentDictionary<string, LocalizationEntry> InitializeResources()
    {
        return new ConcurrentDictionary<string, LocalizationEntry>(
            new Dictionary<string, LocalizationEntry> {
"""

    # Process each row
    for i, row in enumerate(data):
        if len(row) < 5 or i == 0:
            continue

        original = row[0]
        singular = escape_csharp_string(row[1])
        plural1 = escape_csharp_string(row[2]) if row[2] else None
        plural2 = escape_csharp_string(row[3]) if row[3] else None
        plural3 = escape_csharp_string(row[4]) if row[4] else None

        # Build the entry
        entry = f'                {{ "{original}", new LocalizationEntry {{ '

        parts = []
        parts.append(f'Singular = "{singular}"')

        if plural1:
            parts.append(f'Plural1 = "{plural1}"')
        if plural2:
            parts.append(f'Plural2 = "{plural2}"')
        if plural3:
            parts.append(f'Plural3 = "{plural3}"')

        entry += ', '.join(parts) + ' } }'

        # Add comma except for last entry
        if i < len(data) - 1:
            entry += ','

        cs_content += entry + '\n'

    # Close the C# structure
    cs_content += """            });
    }
}"""

    # Write to output file
    with open(output_path, 'w', encoding='utf-8') as f:
        f.write(cs_content)

    print(f"Generated {output_path} with {len(data)} entries")
    print(f"File size constant set to: {file_size}")

if __name__ == "__main__":

    csv_path = "GARbro.en-US.csv"
    output_path = "..\..\GameRes\Main\LocalizationEN.cs"

    if len(sys.argv) > 1:
        csv_path = sys.argv[1]
    if len(sys.argv) > 2:
        output_path = sys.argv[2]

    generate_csharp_localization(csv_path, output_path)