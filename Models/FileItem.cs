namespace  TestProject.Models;

public class FileItem
{
    public string Name { get; set; }
    public string Path { get; set; }
    public FileItemType Type { get; set; }
    public long? Size { get; set; }
    public DateTimeOffset LastModifiedDate { get; set; } 

}
