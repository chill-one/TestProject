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
        try
        {
            List<FileItem> items = _fileService.BrowseDirectory(path);
            return Ok(items);
        }
        catch (DirectoryNotFoundException)
        {
            return NotFound(new
            {
                error = "The requested directory was not found."
            }
            );
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new
                {
                    error = "Access to the requested path is not allowed."
                }
                );
        }
    }

    [HttpGet("search")]
    public ActionResult<List<FileItem>> Search([FromQuery] string path = "", [FromQuery] string query = "")
    {
        try
        {
            List<FileItem> items = _fileService.SearchDirectory(path, query);
            return Ok(items);
        }
        catch (DirectoryNotFoundException)
        {
            return NotFound(new
            {
                error = "The requested directory was not found."
            }
            );
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new
                {
                    error = "Access to the requested path is not allowed."
                }
                );
        }
    }

    [HttpGet("download")]
    public IActionResult Download([FromQuery] string path)
    {
        try
        {
            FileStream stream = _fileService.OpenDownload(path);
            string fileName = Path.GetFileName(path);

            //application/octet-stream simply tells the browser this is an arbitrary binary file data
            return File(
                stream,
                "application/octet-stream",
                fileName
            );
        }
        catch (FileNotFoundException)
        {
            return NotFound(new
            {
                error = "The requested file was not found."
            }
            );
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new
                {
                    error = "Access to the requested path is not allowed."
                }
                );
        }
    }

}