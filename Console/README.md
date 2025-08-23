# GARbro.Console

A command-line interface for GARbro, enabling automated extraction and conversion of game resources. This tool provides programmatic access to GARbro's extensive format support, making it ideal for batch processing, integration into build pipelines, and automated content extraction workflows.

## Features

- Extract files from 400+ game archive formats
- Batch convert images between formats while preserving quality where possible
- Convert proprietary audio formats to standard WAV
- Filter extraction by file type or regular expression patterns
- Automatic format detection and identification
- Preserve or convert proprietary formats based on your needs

## Installation

Download the latest release and ensure all GARbro dependencies are present in the same directory as `GARbro.Console.exe`.

## Usage

```
GARbro.Console.exe <command> [<switches>...] <archive_name>
```

### Commands

| Command | Description |
|---------|-------------|
| `i` | **Identify** - Detects and displays the archive format |
| `l` | **List** - Shows all files contained within the archive with offsets and sizes |
| `x` | **Extract** - Extracts files from the archive to the specified directory |
| `f` | **Formats** - Displays all supported archive formats with descriptions |

### Switches

#### Output Control

| Switch | Description |
|--------|-------------|
| `-o <directory>` | Sets the output directory for extracted files (default: current directory) |
| `-y` | Overwrites existing files without prompting |

#### Filtering Options

| Switch | Description |
|--------|-------------|
| `-f <regex>` | Extracts only files matching the regular expression pattern |
| `-na` | Skips audio files during extraction |
| `-ni` | Skips image files during extraction |
| `-ns` | Skips script files during extraction |

#### Image Processing

| Switch | Description |
|--------|-------------|
| `-if <format>` | Converts all images to the specified format (`png`, `jpg`, `bmp`, etc.) |
| `-ocu` | **Only Convert Unknown** - When used with `-if`, preserves common formats (PNG, JPEG, BMP) and only converts proprietary formats |
| `-aio` | **Adjust Image Offset** - Corrects image positioning for formats that store offset data |

#### Audio Processing

| Switch | Description |
|--------|-------------|
| `-ca` | Converts audio files to WAV format (preserves original format if not specified) |

## Examples

### Basic Extraction
```bash
# Extract all files from an archive
GARbro.Console.exe x game.pak

# Extract to a specific directory
GARbro.Console.exe x -o "extracted_files" game.pak
```

### Filtered Extraction
```bash
# Extract only PNG images
GARbro.Console.exe x -f "\.png$" game.pak

# Extract everything except audio files
GARbro.Console.exe x -na game.pak
```

### Format Conversion
```bash
# Convert all images to PNG
GARbro.Console.exe x -if png game.pak

# Convert only proprietary image formats to PNG, keep standard formats as-is
GARbro.Console.exe x -if png -ocu game.pak

# Extract and convert audio to WAV
GARbro.Console.exe x -ca game.pak
```

### Archive Information
```bash
# Identify archive format
GARbro.Console.exe i game.pak

# List archive contents
GARbro.Console.exe l game.pak

# Show all supported formats
GARbro.Console.exe f
```

## Advanced Usage

### Batch Processing
Process multiple archives using shell scripting:

```bash
# Windows batch file
for %%f in (*.pak) do GARbro.Console.exe x -o "%%~nf" "%%f"

# PowerShell
Get-ChildItem *.pak | ForEach-Object { 
    & GARbro.Console.exe x -o $_.BaseName $_.FullName 
}
```

### Integration Examples

Extract specific resources for asset pipeline:
```bash
# Extract only textures and convert proprietary formats
GARbro.Console.exe x -f "\.(png|jpg|bmp|dds)$" -if png -ocu assets.pak
```

## Notes

- The tool automatically detects archive formats - no need to specify the format manually
- When converting images, be aware that some conversions may result in quality loss or transparency issues
- Use `-ocu` with image conversion to preserve quality of common formats
- Regular expressions in the `-f` filter are case-sensitive
- Output directory structure preserves the internal archive structure

## Troubleshooting

- **"Input file has an unknown format"** - The file is not a recognized archive format or is corrupted
- **"No files match the given filter"** - Check your regular expression syntax and ensure matching files exist
- **Quality loss during conversion** - Use `-ocu` to avoid converting already-standard formats

## License

GARbro.Console is part of the GARbro project and is distributed under the same license terms.