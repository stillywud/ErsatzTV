using ErsatzTV.Application.CopyPrep.Commands;
using ErsatzTV.Application.CopyPrep.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace ErsatzTV.Controllers.Api;

[ApiController]
[EndpointGroupName("general")]
public class CopyPrepController(IMediator mediator)
{
    [HttpGet("/api/copy-prep")]
    [Tags("CopyPrep")]
    [EndpointSummary("List copy-prep queue items")]
    public async Task<IActionResult> GetQueueItems([FromQuery] int limit = 100) =>
        new OkObjectResult(await mediator.Send(new GetCopyPrepQueueItems(limit)));

    [HttpPost("/api/copy-prep/{id:int}/retry")]
    [Tags("CopyPrep")]
    [EndpointSummary("Retry a copy-prep queue item")]
    public async Task<IActionResult> RetryQueueItem(int id) =>
        await mediator.Send(new RetryCopyPrepQueueItem(id))
            ? new OkResult()
            : new NotFoundResult();
}
