using TestProject.Models;

namespace TestProject.Services;

public class FileService
{

    //Where am i allowed to access
    private readonly string _homeDirectory;

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
    /// Validates the given relativePath to make sure its either the root or
    /// inside the root directory.
    /// </summary>
    /// <param name="relativePath">The path to the file or directory.</param>
    /// <returns>The normalized absolute path of the given relative path.</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown when the requested path is outside the configured home direcotry.</exception>
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
    /// Browses the filesystem at relativePath and grabs filesystem items inside the 
    /// relativepath.
    /// </summary>
    /// <param name="relativePath">The location of the file/directory</param>
    /// <returns>list of fileItem</returns>
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

}