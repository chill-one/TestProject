using System.Text.Json.Serialization;

namespace TestProject.Models;


//Tells the ASP.NET's JSON serializer to use the enum's name instead of using number.
[JsonConverter(typeof(JsonStringEnumConverter))]
//The enum used by FileItems to classify file and directory in a controlled manner.
public enum FileItemType
{
    File,
    Directory
}