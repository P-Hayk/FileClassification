using FileClassification.Application.Commands.CancelFile;
using FileClassification.Application.Commands.DeleteFile;
using FileClassification.Application.Commands.ResumeFile;
using FileClassification.Application.Commands.UploadFile;
using FileClassification.Application.Enums;
using FileClassification.Application.Queries.GetAllFiles;
using FileClassification.Application.Queries.GetFileById;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace FileClassification.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FilesController(IMediator mediator) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest("No file provided.");

        if (!file.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Only .txt files are accepted.");

        await using var stream = file.OpenReadStream();
        var result = await mediator.Send(new UploadFileCommand(file.FileName, file.Length, stream), ct);
        return Ok(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        return Ok(await mediator.Send(new GetAllFilesQuery(), ct));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var result = await mediator.Send(new GetFileByIdQuery(id), ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPatch("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct)
    {
        return await mediator.Send(new CancelFileCommand(id), ct) switch
        {
            FileOperationResult.Success => Ok(),
            FileOperationResult.NotFound => NotFound(),
            FileOperationResult.InvalidState => Conflict("File can only be cancelled while it is processing."),
            _ => StatusCode(500)
        };
    }

    [HttpPatch("{id:int}/resume")]
    public async Task<IActionResult> Resume(int id, CancellationToken ct)
    {
        return await mediator.Send(new ResumeFileCommand(id), ct) switch
        {
            FileOperationResult.Success => Ok(),
            FileOperationResult.NotFound => NotFound(),
            FileOperationResult.InvalidState => Conflict("File can only be resumed when it is canceled or failed."),
            _ => StatusCode(500)
        };
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        return await mediator.Send(new DeleteFileCommand(id), ct) switch
        {
            FileOperationResult.Success => NoContent(),
            FileOperationResult.NotFound => NotFound(),
            FileOperationResult.InvalidState => Conflict("File cannot be deleted while it is processing. Cancel it first."),
            _ => StatusCode(500)
        };
    }
}
