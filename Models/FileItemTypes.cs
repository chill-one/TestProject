using System.Text.Json.Serialization;

namespace TestProject.Models;


// Tells ASP.NET's JSON serializer to use the enum name instead of its number.
[JsonConverter(typeof(JsonStringEnumConverter))]
// Classifies file items as either files or directories.
public enum FileItemType
{
    File,
    Directory
}
