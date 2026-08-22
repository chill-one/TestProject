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
        
        //If the prefix of the current full path is not the same as home dir its not inside
        //If the current normalizedFullPath is the same as _homeDirectory except the last Separator its root
        if (normalizedFullPath == Path.TrimEndingDirectorySeparator(_homeDirectory)
            ||
            normalizedFullPath.StartsWith(_homeDirectory))
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

        List<FileItem> items = new List<FileItem>();

        //Get the directories info - Return folder first
        foreach (DirectoryInfo dir in directory.GetDirectories())
        {
            items.Add(
                new FileItem
                {
                    Name = dir.Name,
                    Path = Path.GetRelativePath(_homeDirectory, dir.FullName),
                    Type = FileItemType.Directory,
                    Size = null,
                    LastModifiedDate = dir.LastWriteTimeUtc
                }
            );
        }

        //Get the files info
        foreach (FileInfo file in directory.GetFiles())
        {
            items.Add(
                new FileItem
                {
                    Name = file.Name,
                    Size = file.Length,
                    Path = Path.GetRelativePath(_homeDirectory, file.FullName),
                    LastModifiedDate = file.LastWriteTimeUtc,
                    Type = FileItemType.File
                }
            );
        }

        return items;

    }

    /// <summary>Finds matching files and folders anywhere below a directory.</summary>
    /// <param name="relativePath">The directory to search, relative to the home directory.</param>
    /// <param name="query">Text to find in file and folder names.</param>
    /// <returns>All matching items, or an empty list for a blank query.</returns>
    public List<FileItem> SearchDirectory(string relativePath, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new List<FileItem>();
        }
        string normalizedFullPath = ResolvePath(relativePath);

        DirectoryInfo directory = new DirectoryInfo(normalizedFullPath);

        if (!directory.Exists)
        {
            throw new DirectoryNotFoundException(
                "Could not find the Directory with the given path."
            );
        }

        List<FileItem> result = new List<FileItem>();

        //Finds every files nested inside this directory recursively.
        foreach(FileInfo file in directory.EnumerateFiles("*", SearchOption.AllDirectories))
        {

            if(file.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(
                    new FileItem
                    {
                        Name = file.Name,
                        Size = file.Length,
                        Path = Path.GetRelativePath(_homeDirectory, file.FullName),
                        LastModifiedDate = file.LastWriteTimeUtc,
                        Type = FileItemType.File
                    }
                );
            }
    
        }
        //Get all the directory inside the given path
        foreach(DirectoryInfo dir in directory.EnumerateDirectories("*", SearchOption.AllDirectories))
        {
            if(dir.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(
                    new FileItem
                    {
                        Name = dir.Name,
                        Path = Path.GetRelativePath(_homeDirectory, dir.FullName),
                        Type = FileItemType.Directory,
                        Size = null,
                        LastModifiedDate = dir.LastWriteTimeUtc
                        
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

        //File already exists
        if (File.Exists(destinationPath))
        {
            throw new IOException(
                "A file with the same name already exists."
            );
        }

        await using FileStream destinationStream = File.Create(destinationPath);
        //Asynchronously copys the uploaded stream into the destinationStream
        await fileStream.CopyToAsync(destinationStream);

    }

}
