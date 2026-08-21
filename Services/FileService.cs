namespace TestProject.Services;

public class FileService
{

    //_homeDirectory is a common C# convention for private fields.
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

}