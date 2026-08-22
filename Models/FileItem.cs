namespace  TestProject.Models;

public class FileItem
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public FileItemType Type { get; set; }
    public long? Size { get; set; }
    public DateTimeOffset LastModifiedDate { get; set; } 

}
