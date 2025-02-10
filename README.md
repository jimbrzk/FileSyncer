# FileSyncer

## Overview
FileSyncer is a command-line tool for synchronizing files between directories. It efficiently copies files, preserves metadata (optional), and removes orphaned files from the target directory.

## Features
- Synchronizes files between a source and a target directory
- Option to include/exclude specific files
- Removes orphaned files from the target directory
- Supports dry-run mode for testing before execution
- Copies file metadata such as ACLs and timestamps (optional)
- Logs the synchronization process
- Displays real-time progress and transfer speed

## Installation
To use FileSyncer, you need to have .NET installed on your system. Download or clone the repository, then build the project using the following command:

```sh
 dotnet build
```

## Usage
Run the program from the command line with the required parameters:

```sh
FileSyncer.exe Source="C:\source" Target="D:\target"
```

### Optional Parameters:
- `Include="*.txt,*.jpg"` - Synchronizes only specified file types.
- `Ignore="*.log,temp"` - Ignores specified files or directories.
- `LogFile="sync.log"` - Logs operations to a specified file.
- `DryRun` - Simulates the synchronization process without making changes.
- `CopyAcl` - Copies file access control lists (Windows only).
- `CopyDates` - Copies file creation and modification timestamps.

## Example Usage
### Basic Synchronization
```sh
FileSyncer.exe Source="C:\source" Target="D:\target"
```

### Synchronization with Logging and Metadata Copying
```sh
FileSyncer.exe Source="C:\source" Target="D:\target" LogFile="sync.log" CopyAcl CopyDates
```

### Dry-Run Mode (No Changes Applied)
```sh
FileSyncer.exe Source="C:\source" Target="D:\target" DryRun
```

## Output Example
```
Starting sync 2025-02-11 Source: C:\source Target: D:\target
 - DryRun: False
 - Include: ALL
 - Ignore: NONE

Removing files that are not present on source but found at target
Syncing files from source to target

Operation completed. Synced: 120 Removed: 5 Errors: 0. Elapsed: 00:02:45
```

## Error Handling
If an error occurs, the tool logs the issue and continues processing other files. The number of errors encountered is displayed in the final report.

## License
This project is open-source and available under the MIT License.
