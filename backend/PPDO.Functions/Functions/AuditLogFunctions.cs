using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Functions.Functions;

/// <summary>
/// Audit Log config endpoints (<c>/api/config/audit-log</c>). Read-only, SuperAdmin-only
/// (via <see cref="IPermissionService.CanViewAuditLogAsync"/>, itself gated behind
/// <see cref="FeatureFlags.AuditLogPageEnabled"/>). Responses use the
/// <c>{ data, error, message }</c> envelope.
/// </summary>
public sealed class AuditLogFunctions
{
    private readonly IAuditLogService _auditLog;
    private readonly IJwtMiddleware _jwt;
    private readonly IPermissionService _permissions;

    public AuditLogFunctions(IAuditLogService auditLog, IJwtMiddleware jwt, IPermissionService permissions)
    {
        _auditLog    = auditLog;
        _jwt         = jwt;
        _permissions = permissions;
    }

    private Task<bool> CanViewAuditLog(User u) => _permissions.CanViewAuditLogAsync(u);

    // ── GET /api/config/audit-log?page=&pageSize=&tableName=&action=&actor=&from=&to= ──
    [Function("AuditLogList")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "config/audit-log")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanViewAuditLog, ct);
        if (denied is not null) return denied;

        int page = int.TryParse(req.Query["page"], out int p) && p > 0 ? p : 1;
        int pageSize = int.TryParse(req.Query["pageSize"], out int ps) && ps > 0 ? ps : 50;
        string? tableName = string.IsNullOrWhiteSpace(req.Query["tableName"]) ? null : req.Query["tableName"];
        string? action = string.IsNullOrWhiteSpace(req.Query["action"]) ? null : req.Query["action"];
        string? actor = string.IsNullOrWhiteSpace(req.Query["actor"]) ? null : req.Query["actor"];
        DateTime? from = DateTime.TryParse(req.Query["from"], out DateTime f) ? DateTime.SpecifyKind(f, DateTimeKind.Utc) : null;
        DateTime? to = DateTime.TryParse(req.Query["to"], out DateTime t) ? DateTime.SpecifyKind(t, DateTimeKind.Utc) : null;

        AuditLogFilterDto filter = new(page, pageSize, tableName, action, actor, from, to);
        AuditLogPageDto data = await _auditLog.GetPagedAsync(filter, ct);

        return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.OK, ApiResponse<AuditLogPageDto>.Ok(data), ct);
    }

    // ── GET /api/config/audit-log/tables ──
    [Function("AuditLogTableNames")]
    public async Task<HttpResponseData> TableNames(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "config/audit-log/tables")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanViewAuditLog, ct);
        if (denied is not null) return denied;

        IReadOnlyList<string> data = await _auditLog.GetTableNamesAsync(ct);
        return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.OK, ApiResponse<IReadOnlyList<string>>.Ok(data), ct);
    }
}
