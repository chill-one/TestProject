using TestProject.Models;

namespace TestProject.Services;

public class FileService
{

    // Defines the part of the filesystem this service may access.
    private readonly string _homeDirectory;

    /// <summary>Creates a service rooted at the configured home directory.</summary>
    /// <param name="homeDirectory">The only part of the filesystem this service may access.</param>
    public FileService(string homeDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(homeDirectory);
        string normalizedHomeDirectory = Path.GetFullPath(homeDirectory);

        // Check that the configured home directory exists.
        if (!Directory.Exists(normalizedHomeDirectory))
        {
            throw new DirectoryNotFoundException(
                "The configured home directory does not exist."
            );
        }

        if (!normalizedHomeDirectory.EndsWith(Path.DirectorySeparatorChar))
        {
            normalizedHomeDirectory += Path.DirectorySeparatorChar;
        }

        _homeDirectory = normalizedHomeDirectory;

    }

    /// <summary>
    /// Converts a relative path to an absolute path inside the home directory.
    /// </summary>
    /// <param name="relativePath">The path to the file or directory.</param>
    /// <returns>The normalized absolute path.</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown when the path leaves the home directory.</exception>
    private string ResolvePath(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        string normalizedPath = Path.Combine(_homeDirectory, relativePath);
        string normalizedFullPath = Path.GetFullPath(normalizedPath);

        StringComparison comparison = OperatingSystem.IsWindows()
                                        ? StringComparison.OrdinalIgnoreCase
                                            : StringComparison.Ordinal;

        string rootPath =  Path.TrimEndingDirectorySeparator(_homeDirectory);
        
        
        // Reject paths outside the home directory, while still allowing the root itself.
        if (normalizedFullPath.Equals(rootPath, comparison)
            ||
            normalizedFullPath.StartsWith(_homeDirectory, comparison))
        {
            return normalizedFullPath;
        }

        throw new UnauthorizedAccessException(
            "The requested path is outside the configured home directory."
        );
    }

    /// <summary>
    /// Lists the files and folders directly inside a directory.
    /// </summary>
    /// <param name="relativePath">The directory path relative to the home directory.</param>
    /// <param name="cancellationToken">Token used to stop a long-running browse.</param>
    /// <returns>The directory contents, with folders listed before files.</returns>
    public List<FileItem> BrowseDirectory(string relativePath, CancellationToken cancellationToken)
    {

        string normalizedFullPath = ResolvePath(relativePath);

        DirectoryInfo directory = new DirectoryInfo(normalizedFullPath);
        
        //Pre check
        if (!directory.Exists)
        {
            throw new DirectoryNotFoundException(
                "Could not find the Directory with the given path."
            );
        }

        List<FileItem> directories = new();
        List<FileItem> files = new();

        //Ignore files with higher persmisson and symlinks
        EnumerationOptions options = CreateEnumerationOptions(false);

        foreach (FileSystemInfo item in directory.EnumerateFileSystemInfos("*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item is DirectoryInfo dir)
            {
                directories.Add(
                    CreateDirectoryItem(
                        dir,
                        CalculateDirectorySize(dir, cancellationToken)
                    )
                );
            }
            else if (item is FileInfo file)
            {
                files.Add(
                    CreateFileItem(file)
                );
            }
        }

        directories.AddRange(files);

        return directories;
    }

    /// <summary>Adds a file's size to each parent directory up to the search root.</summary>
    /// <param name="file">The file whose size should be counted.</param>
    /// <param name="searchRoot">The directory where size accumulation stops.</param>
    /// <param name="directorySizes">The directory size totals being updated.</param>
    /// <param name="pathComparer">The comparer used for filesystem paths.</param>
    private void AccumulateDirectorySizes(FileInfo file, string searchRoot, Dictionary<string, long> directorySizes, StringComparer pathComparer)
    {
        DirectoryInfo? currentDirectory = file.Directory;

        while (currentDirectory != null)
        {
            string currentPath =
                Path.TrimEndingDirectorySeparator(
                    currentDirectory.FullName
                );

            if (directorySizes.TryGetValue(
                currentPath,
                out long currentSize))
            {
                directorySizes[currentPath] =
                    currentSize + file.Length;
            }
            else
            {
                directorySizes[currentPath] = file.Length;
            }

            if (pathComparer.Equals(currentPath, searchRoot))
            {
                break;
            }

            currentDirectory = currentDirectory.Parent;
        }
    }

    /// <summary>Finds matching files and folders anywhere below a directory.</summary>
    /// <param name="relativePath">The directory to search, relative to the home directory.</param>
    /// <param name="query">Text to find in file and folder names.</param>
    /// <param name="cancellationToken">Token used to stop a long-running search.</param>
    /// <returns>All matching items, or an empty list for a blank query.</returns>
    public List<FileItem> SearchDirectory(string relativePath, string query, CancellationToken cancellationToken)
    {
        string normalizedFullPath = ResolvePath(relativePath);

        DirectoryInfo directory = new DirectoryInfo(normalizedFullPath);

        if (!directory.Exists)
        {
            throw new DirectoryNotFoundException(
                "Could not find the Directory with the given path."
            );
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return new List<FileItem>();
        }

        List<FileItem> result = new List<FileItem>();
        List<DirectoryInfo> matchingDirectories = new();

        StringComparer pathComparer =
                OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        // Trim the trailing separator to mark where the search should stop.
        string searchRoot = Path.TrimEndingDirectorySeparator(normalizedFullPath);

        Dictionary<string, long> directorySizes = new Dictionary<string, long>(pathComparer);


        // Ignore inaccessible entries and skip symbolic links and junctions.
        EnumerationOptions options = CreateEnumerationOptions(true);

        // Find every file and folder nested inside this directory recursively.
        foreach (FileSystemInfo item in directory.EnumerateFileSystemInfos("*", options))
        {
            // Allow the caller to cancel a long-running search.
            cancellationToken.ThrowIfCancellationRequested();
            
            // Handle files.
            if (item is FileInfo file)
            {

                AccumulateDirectorySizes(file,searchRoot,directorySizes,pathComparer);

                if(file.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(
                        CreateFileItem(file)
                    );
                }
            }
            else if (item is DirectoryInfo dir)
            {
                // Handle directories.
                string directoryPath = Path.TrimEndingDirectorySeparator(dir.FullName);

                // Include empty directories with a size of zero.
                directorySizes.TryAdd(directoryPath, 0);

                // Save matching directories until their sizes are known.
                if(dir.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    matchingDirectories.Add(dir);
                }

            }
        
        }

        // Add matching directories after their sizes have been calculated.
        foreach (DirectoryInfo dir in matchingDirectories)
        {
            // There may be thousands of matching directories.
            cancellationToken.ThrowIfCancellationRequested();
            string directoryPath =
                Path.TrimEndingDirectorySeparator(
                    dir.FullName
                );

            // Get the calculated size for this directory.
            directorySizes.TryGetValue(
                directoryPath,
                out long size
            );

            result.Add(
                CreateDirectoryItem(dir, size)
            );
        }


        return result;
    }


    /// <summary>Opens a file for downloading.</summary>
    /// <param name="relativePath">The file path relative to the home directory.</param>
    /// <returns>A readable stream for the requested file.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the file does not exist.</exception>
    public FileStream OpenDownload(string relativePath)
    {
        string normalizedFullPath = ResolvePath(relativePath);

        if (!File.Exists(normalizedFullPath))
        {
            throw new FileNotFoundException(
                "Could not find the file with the given path."
            );
        }

        // Returns the file stream for reading
        return File.OpenRead(normalizedFullPath);
    }


    /// <summary>Saves an uploaded file in an existing directory.</summary>
    /// <param name="relativeDirectoryPath">The destination directory relative to the home directory.</param>
    /// <param name="fileName">The name to use for the uploaded file.</param>
    /// <param name="fileStream">The stream containing the file data.</param>
    public async Task UploadFile(string relativeDirectoryPath, string fileName, Stream fileStream)
    {
        string normalizedDirectoryPath = ResolvePath(relativeDirectoryPath);

        DirectoryInfo directory = new DirectoryInfo(normalizedDirectoryPath);

        // Check that the destination directory exists.
        if (!directory.Exists)
        {
            throw new DirectoryNotFoundException(
                "Could not find the Directory with the given path."
            );
        }


        // Keep only the filename and discard any client-provided directory segments.
        string safeFileName = Path.GetFileName(fileName);

        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            throw new ArgumentException(
                "The uploaded file must have a valid filename."
            );
        }

        // Build the destination path.
        string destinationPath = Path.Combine(normalizedDirectoryPath, safeFileName);

        await using FileStream destinationStream =
            new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None
            );

        await fileStream.CopyToAsync(destinationStream);

    }

    /// <summary>Converts a server path into a browser-friendly relative path.</summary>
    /// <param name="fullPath">The absolute path inside the home directory.</param>
    /// <returns>A relative path using forward slashes.</returns>
    private string GetRelativeClientPath(string fullPath)
    {
        string relativePath =
            Path.GetRelativePath(_homeDirectory, fullPath);

        return relativePath.Replace(
            Path.DirectorySeparatorChar,
            '/'
        );
    }

    /// <summary>Builds the API model for a file.</summary>
    /// <param name="file">The filesystem file to represent.</param>
    /// <returns>A file item with client-safe path and metadata.</returns>
    private FileItem CreateFileItem(FileInfo file)
    {
        return new FileItem
        {
            Name = file.Name,
            Path = GetRelativeClientPath(file.FullName),
            Type = FileItemType.File,
            LastModifiedDate = file.LastWriteTimeUtc,
            Size = file.Length
        };
    }


    /// <summary>Builds the API model for a directory.</summary>
    /// <param name="directory">The filesystem directory to represent.</param>
    /// <param name="size">The directory's calculated size in bytes.</param>
    /// <returns>A directory item with client-safe path and metadata.</returns>
    private FileItem CreateDirectoryItem(DirectoryInfo directory, long size)
    {
        return new FileItem
        {
            Name = directory.Name,
            Path = GetRelativeClientPath(directory.FullName),
            Type = FileItemType.Directory,
            LastModifiedDate = directory.LastWriteTimeUtc,
            Size = size
        };
    }

    /// <summary>Creates safe filesystem enumeration settings.</summary>
    /// <param name="recursive">Whether nested directories should be included.</param>
    /// <returns>Enumeration settings that skip inaccessible entries and links.</returns>
    private EnumerationOptions CreateEnumerationOptions(bool recursive)
    {
        return new EnumerationOptions
        {
            RecurseSubdirectories = recursive,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
    }

    /// <summary>Calculates the total size of all files below a directory.</summary>
    /// <param name="directory">The directory whose contents should be measured.</param>
    /// <param name="cancellationToken">Token used to stop the calculation.</param>
    /// <returns>The total size of the directory's files in bytes.</returns>
    private long CalculateDirectorySize(DirectoryInfo directory, CancellationToken cancellationToken)
    {
        long totalSize = 0;

        EnumerationOptions options = CreateEnumerationOptions(true);

        // Recursively walk through the nested directories.
        foreach(FileInfo file in directory.EnumerateFiles("*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            totalSize += file.Length;
        }

        return totalSize;
    }

}
