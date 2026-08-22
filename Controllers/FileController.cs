using Microsoft.AspNetCore.Mvc;
using TestProject.Models;
using TestProject.Services;

namespace TestProject.Controllers;

[ApiController]
[Route("api/files")]
public class FileController : ControllerBase
{
    private readonly FileService _fileService;

    /// <summary>Creates the controller with its file service dependency.</summary>
    /// <param name="fileService">Service used for filesystem operations.</param>
    public FileController(FileService fileService)
    {
        _fileService = fileService;
    }

    /// <summary>Returns the contents of the requested directory.</summary>
    /// <param name="path">Directory path relative to the configured home directory.</param>
    /// <param name="cancellationToken">Token used to stop the browse if the request is cancelled.</param>
    [HttpGet]
    public ActionResult<List<FileItem>> Browse([FromQuery] string path = "", CancellationToken cancellationToken = default)
    {
        try
        {
            List<FileItem> items = _fileService.BrowseDirectory(path, cancellationToken);
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

    /// <summary>Searches for files and folders below the requested directory.</summary>
    /// <param name="path">Directory path relative to the configured home directory.</param>
    /// <param name="query">Text to find in item names.</param>
    /// <param name="cancellationToken">Token used to stop the search if the request is cancelled.</param>
    [HttpGet("search")]
    public ActionResult<List<FileItem>> Search([FromQuery] string path = "", [FromQuery] string query = "", CancellationToken cancellationToken = default)
    {
        try
        {
            List<FileItem> items = _fileService.SearchDirectory(path, query, cancellationToken);
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

    /// <summary>Downloads a file from the configured home directory.</summary>
    /// <param name="path">File path relative to the configured home directory.</param>
    [HttpGet("download")]
    public IActionResult Download([FromQuery] string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return BadRequest(new
            {
                error = "A file path is required."
            });
        }

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

    /// <summary>Uploads a file into the requested directory.</summary>
    /// <param name="path">Destination directory relative to the configured home directory.</param>
    /// <param name="file">The file received from the request.</param>
    [HttpPost("upload")]
    public async Task<IActionResult> Upload([FromQuery] string? path, IFormFile? file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new
            {
                error = "No file was provided."
            });
        }


        try
        {
            string relativePath = path ?? "";

            string fileName = Path.GetFileName(file.FileName);

            using Stream stream = file.OpenReadStream();

            await _fileService.UploadFile(relativePath, fileName, stream);

            return Ok();

        }
        catch (DirectoryNotFoundException)
        {
            return NotFound(new
            {
                error = "The requested directory was not found."
            }
            );
        }
        catch (ArgumentException)
        {
            return StatusCode(
                StatusCodes.Status400BadRequest,
                new
                {
                    error = "The uploaded file must have a valid filename."
                }
                );
        }
        catch (IOException)
        {
            return StatusCode(
                StatusCodes.Status409Conflict,
                new
                {
                    error = "A file with the same name already exists."
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
