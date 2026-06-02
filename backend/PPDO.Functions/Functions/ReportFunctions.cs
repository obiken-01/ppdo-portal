using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PPDO.Application.Common;
using PPDO.Application.DTOs.PurchaseRequest;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Functions.Functions;

/// <summary>
/// HTTP-triggered Azure Functions for PR Report (<c>/api/purchase-requests/{id}/report|export</c>).
///
/// All endpoints require a valid JWT. Division-scope and permission enforcement
/// are delegated to <see cref="IPRReportService"/>.
///
/// Endpoints:
///   GET /api/purchase-requests/{id}/report  — PR Report JSON (Sections 1, 2, 3)
///   GET /api/purchase-requests/{id}/export  — PR Report as .xlsx download
/// </summary>
public sealed class ReportFunctions
{
    private readonly IPRReportService _report;
    private readonly IJwtMiddleware   _jwt;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public ReportFunctions(IPRReportService report, IJwtMiddleware jwt)
    {
        _report = report;
        _jwt    = jwt;
    }

    // ── GET /api/purchase-requests/{id}/report ────────────────────────────────

    [Function("GetPRReport")]
    public async Task<HttpResponseData> GetReport(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "purchase-requests/{id:guid}/report")]
        HttpRequestData req,
        Guid id,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        ServiceResult<PRReportDto> result =
            await _report.GetReportAsync(caller, id, cancellationToken);

        return await ToResponse(req, result, HttpStatusCode.OK, cancellationToken);
    }

    // ── GET /api/purchase-requests/{id}/export ────────────────────────────────

    [Function("ExportPRReport")]
    public async Task<HttpResponseData> ExportReport(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "purchase-requests/{id:guid}/export")]
        HttpRequestData req,
        Guid id,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        ServiceResult<byte[]> result =
            await _report.ExportReportAsync(caller, id, cancellationToken);

        if (!result.IsSuccess)
            return await ErrorResponse(req, result.Code, result.Error!, cancellationToken);

        HttpResponseData response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        response.Headers.Add("Content-Disposition",
            "attachment; filename=\"PR_Report.xlsx\"");
        await response.WriteBytesAsync(result.Value!);
        return response;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string? GetAuthHeader(HttpRequestData req)
        => req.Headers.TryGetValues("Authorization", out IEnumerable<string>? values)
            ? values.FirstOrDefault()
            : null;

    private static async Task<HttpResponseData> ToResponse<T>(
        HttpRequestData req,
        ServiceResult<T> result,
        HttpStatusCode successStatus,
        CancellationToken cancellationToken)
    {
        if (result.IsSuccess)
        {
            HttpResponseData ok = req.CreateResponse(successStatus);
            ok.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await ok.WriteStringAsync(
                JsonSerializer.Serialize(result.Value, _jsonOptions), cancellationToken);
            return ok;
        }

        return await ErrorResponse(req, result.Code, result.Error!, cancellationToken);
    }

    private static async Task<HttpResponseData> ErrorResponse(
        HttpRequestData req,
        ServiceErrorCode code,
        string message,
        CancellationToken cancellationToken)
    {
        HttpStatusCode status = code switch
        {
            ServiceErrorCode.NotFound   => HttpStatusCode.NotFound,
            ServiceErrorCode.Forbidden  => HttpStatusCode.Forbidden,
            ServiceErrorCode.Conflict   => HttpStatusCode.Conflict,
            ServiceErrorCode.BadRequest => HttpStatusCode.BadRequest,
            _                           => HttpStatusCode.InternalServerError,
        };

        HttpResponseData response = req.CreateResponse(status);
        await response.WriteStringAsync(message, cancellationToken);
        return response;
    }
}
