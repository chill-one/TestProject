using Microsoft.AspNetCore.Mvc;
using TestProject.Models;
using TestProject.Services;

namespace TestProject.Controllers;

[ApiController]
[Route("api/files")]
public class FileController : ControllerBase
{
    private readonly FileService _fileService;

    public FileController(FileService fileService)
    {
        _fileService = fileService;
    }

    [HttpGet]
    public ActionResult<List<FileItem>> Browse([FromQuery] string path = "")
    {
        List<FileItem> items = _fileService.BrowseDirectory(path);
        return Ok(items);
    }
}