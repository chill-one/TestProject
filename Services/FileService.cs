using TestProject.Models;

namespace TestProject.Services;

public class FileService
{

    //Where am i allowed to access
    private readonly string _homeDirectory;

    /// <summary>Creates a service rooted at the configured home directory.</summary>
    /// <param name="homeDirectory">The only part of the filesystem this service may access.</param>
    public FileService(string homeDirectory)
    {
        string normalizedHomeDirectory = Path.GetFullPath(homeDirectory);

        //Check if the given home directory exists.
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
        string normalizedPath = Path.Combine(_homeDirectory, relativePath);
        string normalizedFullPath = Path.GetFullPath(normalizedPath);

        StringComparison comparison = OperatingSystem.IsWindows()
                                        ? StringComparison.OrdinalIgnoreCase
                                            : StringComparison.Ordinal;

        string rootPath =  Path.TrimEndingDirectorySeparator(_homeDirectory);
        
        
        //If the prefix of the current full path is not the same as home dir its not inside
        //If the current normalizedFullPath is the same as _homeDirectory except the last Separator its root
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
    /// <returns>The directory contents, with folders listed before files.</returns>
    public List<FileItem> BrowseDirectory(string relativePath)
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
        EnumerationOptions options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        foreach (FileSystemInfo item in directory.EnumerateFileSystemInfos("*", options))
        {
            if (item is DirectoryInfo dir)
            {
                directories.Add(
                    new FileItem
                    {
                        Name = dir.Name,
                        Path = GetRelativeClientPath(dir.FullName),
                        Type = FileItemType.Directory,
                        LastModifiedDate = dir.LastWriteTimeUtc,
                        Size = null
                    }
                );
            }
            else if (item is FileInfo file)
            {
                files.Add(
                    new FileItem
                    {
                        Name = file.Name,
                        Path = GetRelativeClientPath(file.FullName),
                        Type = FileItemType.File,
                        LastModifiedDate = file.LastWriteTimeUtc,
                        Size = file.Length
                    }
                );
            }
        }

        directories.AddRange(files);

        return directories;
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


        //Ignore files that have permission requriement and skip symbolic links/junction-like entries
        EnumerationOptions options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        //Finds every files and folders nested inside this directory recursively.
        foreach (FileSystemInfo item in directory.EnumerateFileSystemInfos("*", options))
        {
            //User can cancle if they wish too.
            cancellationToken.ThrowIfCancellationRequested();
            // 1. Does item.Name match query?
            if(!item.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            
            // 2. Is item a FileInfo?
            if (item is FileInfo file)
            {
                result.Add(
                    new FileItem
                    {
                        Name = file.Name,
                        Path = GetRelativeClientPath(file.FullName),
                        Type = FileItemType.File,
                        LastModifiedDate = file.LastWriteTimeUtc,
                        Size = file.Length
                    }
                );
            }
            else if (item is DirectoryInfo dir)
            {
            // 3. Is item a DirectoryInfo?
                result.Add(
                    new FileItem
                    {
                        Name = dir.Name,
                        Path = GetRelativeClientPath(dir.FullName),
                        Type = FileItemType.Directory,
                        LastModifiedDate = dir.LastWriteTimeUtc,
                        Size = null
                    }
                );

            }
        
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

        // Check directory exists
        if (!directory.Exists)
        {
            throw new DirectoryNotFoundException(
                "Could not find the Directory with the given path."
            );
        }


        // Sanitize filename
        string safeFileName = Path.GetFileName(fileName);

        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            throw new ArgumentException(
                "The uploaded file must have a valid filename."
            );
        }

        // Build destination path
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

}
