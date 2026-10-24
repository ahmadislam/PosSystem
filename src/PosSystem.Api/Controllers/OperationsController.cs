using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PosSystem.Api.Controllers;

[ApiController, Route("api/operations"), Authorize(Roles = "Admin,Manager")]
public sealed class OperationsController : ControllerBase
{
    [HttpGet("jobs")]
    public IActionResult Jobs()
    {
        var monitoring = JobStorage.Current.GetMonitoringApi();
        return Ok(new
        {
            failed = monitoring.FailedCount(),
            enqueued = monitoring.EnqueuedCount("default"),
            scheduled = monitoring.ScheduledCount()
        });
    }
}
