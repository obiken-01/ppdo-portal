using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using System.Net;

namespace PPDO.Functions.Functions
{
    /// <summary>
    /// Province-wide Investment Planning settings (<c>/api/config/investment-planning</c>) —
    /// v1.8.0, PPDO-136. Today that is one value: the default fiscal year every Investment
    /// Planning page opens on. Spec: <c>docs/v1.8/Default_Fiscal_Year_Spec.md</c>.
    ///
    /// <b>Both routes are gated on <c>CanManageInvestmentPlanningSettings</c></b> — CanManageConfig
    /// AND the host office (SuperAdmin from anywhere). Readers of the default do not come here:
    /// every Investment Planning page gets it from <c>GET /api/budget-planning/fiscal-years</c>,
    /// behind CanAccessBudgetPlanning. The write goes through <c>AuthorizeWriteAsync</c> for the
    /// reviewer write-denial guard, like every other config write.
    /// </summary>
    public sealed class ConfigInvestmentPlanningFunctions
    {
        private readonly IInvestmentPlanningSettingsService _settings;
        private readonly IJwtValidator _jwt;
        private readonly IPermissionService _permissions;

        public ConfigInvestmentPlanningFunctions(
            IInvestmentPlanningSettingsService settings, IJwtValidator jwt, IPermissionService permissions)
        {
            _settings    = settings;
            _jwt         = jwt;
            _permissions = permissions;
        }

        private Task<bool> CanManageSettings(User u) => _permissions.CanManageInvestmentPlanningSettingsAsync(u);

        // Creating a proposal reads the signatory defaults, so any Budget Planning user may read
        // them (PPDO-155, spec §4). Only the settings manager may change them.
        private async Task<bool> CanReadSignatoryDefaults(User u)
            => await _permissions.CanManageInvestmentPlanningSettingsAsync(u)
            || await _permissions.CanAccessBudgetPlanningAsync(u);

        // ── GET /api/config/investment-planning/default-fiscal-year ──
        [Function("InvestmentPlanningDefaultFiscalYearGet")]
        public async Task<HttpResponseData> GetDefaultFiscalYear(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "config/investment-planning/default-fiscal-year")]
            HttpRequestData req,
            CancellationToken ct)
        {
            (_, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageSettings, ct);
            if (denied is not null) return denied;

            DefaultFiscalYearDto data = await _settings.GetDefaultFiscalYearAsync(ct);

            return await ConfigHttp.EnvelopeAsync(
                req, HttpStatusCode.OK, ApiResponse<DefaultFiscalYearDto>.Ok(data), ct);
        }

        // ── PUT /api/config/investment-planning/default-fiscal-year ──
        // Body: { "defaultFiscalYear": 2028 } — null clears it. The property is required: an empty
        // {} body is rejected as malformed rather than silently clearing the setting.
        [Function("InvestmentPlanningDefaultFiscalYearUpdate")]
        public async Task<HttpResponseData> UpdateDefaultFiscalYear(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "config/investment-planning/default-fiscal-year")]
            HttpRequestData req,
            CancellationToken ct)
        {
            (User? caller, HttpResponseData? denied) =
                await ConfigHttp.AuthorizeWriteAsync(req, _jwt, _permissions, CanManageSettings, ct);
            if (denied is not null) return denied;

            UpdateDefaultFiscalYearDto? body = await ConfigHttp.ReadBodyAsync<UpdateDefaultFiscalYearDto>(req, ct);
            if (body is null)
                return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                    ApiResponse<DefaultFiscalYearDto>.Fail("Request body is missing or malformed."), ct);

            return await ConfigHttp.FromResultAsync(req,
                await _settings.UpdateDefaultFiscalYearAsync(body.DefaultFiscalYear, caller!.Id, ct), ct);
        }

        // ── GET /api/config/investment-planning/signatory-defaults (PPDO-155) ──
        [Function("InvestmentPlanningSignatoryDefaultsGet")]
        public async Task<HttpResponseData> GetSignatoryDefaults(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "config/investment-planning/signatory-defaults")]
            HttpRequestData req,
            CancellationToken ct)
        {
            (_, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanReadSignatoryDefaults, ct);
            if (denied is not null) return denied;

            SignatoryDefaultsDto data = await _settings.GetSignatoryDefaultsAsync(ct);
            return await ConfigHttp.EnvelopeAsync(
                req, HttpStatusCode.OK, ApiResponse<SignatoryDefaultsDto>.Ok(data), ct);
        }

        // ── PUT /api/config/investment-planning/signatory-defaults (PPDO-155) ──
        // Body: { ppdcName, ppdcPosition, lceName, lcePosition } — blank clears a value.
        [Function("InvestmentPlanningSignatoryDefaultsUpdate")]
        public async Task<HttpResponseData> UpdateSignatoryDefaults(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "config/investment-planning/signatory-defaults")]
            HttpRequestData req,
            CancellationToken ct)
        {
            (User? caller, HttpResponseData? denied) =
                await ConfigHttp.AuthorizeWriteAsync(req, _jwt, _permissions, CanManageSettings, ct);
            if (denied is not null) return denied;

            UpdateSignatoryDefaultsDto? body = await ConfigHttp.ReadBodyAsync<UpdateSignatoryDefaultsDto>(req, ct);
            if (body is null)
                return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                    ApiResponse<SignatoryDefaultsDto>.Fail("Request body is missing or malformed."), ct);

            return await ConfigHttp.FromResultAsync(req,
                await _settings.UpdateSignatoryDefaultsAsync(body, caller!.Id, ct), ct);
        }
    }
}
