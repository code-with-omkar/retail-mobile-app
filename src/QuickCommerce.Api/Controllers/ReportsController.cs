using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Api.Controllers;

/// <summary>Sales and stock reports for store staff (their own store) and administrators (any store). Read only.</summary>
[ApiController]
[Route("api/reports")]
[Authorize(Policy = SecurityPolicies.StockManagement)]
public sealed class ReportsController(IReportService reports) : ControllerBase
{
    /// <summary>Days are India days: <c>from</c> and <c>to</c> are dates (2026-10-14), both included.</summary>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? storeId, CancellationToken cancellationToken)
    {
        var result = await reports.GetAsync(from, to, storeId, cancellationToken);
        return result.Status == ReportStatus.Succeeded ? Ok(new { success = true, data = result.Value }) : Failure(result.Status, result.Message!);
    }

    [HttpGet("orders.csv")]
    public async Task<IActionResult> OrdersCsv([FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? storeId, CancellationToken cancellationToken)
    {
        var result = await reports.OrdersCsvAsync(from, to, storeId, cancellationToken);
        return result.Status == ReportStatus.Succeeded
            ? File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(result.Value!)).ToArray(), "text/csv; charset=utf-8", $"orders-{from:yyyy-MM-dd}-to-{to:yyyy-MM-dd}.csv")
            : Failure(result.Status, result.Message!);
    }

    private IActionResult Failure(ReportStatus status, string message)
    {
        var body = new { success = false, message, errors = Array.Empty<string>() };
        return status switch
        {
            ReportStatus.Forbidden => StatusCode(StatusCodes.Status403Forbidden, body),
            ReportStatus.NotFound => NotFound(body),
            _ => BadRequest(body)
        };
    }
}
