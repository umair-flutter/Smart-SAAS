using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartSchool.Api.Security;
using SmartSchool.Application.Services;
using SmartSchool.Shared.Contracts;
using SmartSchool.Shared.Enums;
using SmartSchool.Shared.Security;

namespace SmartSchool.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/plans")]
public class PlansController(PlanService plans) : ControllerBase
{
    /// <summary>Managers see every plan; other viewers see active plans only.</summary>
    [HasPermission(Permissions.Subscriptions.View)]
    [HttpGet]
    public Task<List<PlanDto>> List(CancellationToken ct) =>
        plans.ListAsync(activeOnly: !User.HasClaim(AppClaims.Role, nameof(RoleType.SuperAdmin)) && !User.HasClaim(AppClaims.Permission, Permissions.Subscriptions.Manage), ct);

    [HasPermission(Permissions.Subscriptions.Manage)]
    [HttpGet("{id:guid}")]
    public Task<PlanEditDto> Get(Guid id, CancellationToken ct) => plans.GetAsync(id, ct);

    [HasPermission(Permissions.Subscriptions.Manage)]
    [HttpPost]
    public async Task<object> Create(PlanEditDto dto, CancellationToken ct) => new { id = await plans.CreateAsync(dto, ct) };

    [HasPermission(Permissions.Subscriptions.Manage)]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, PlanEditDto dto, CancellationToken ct)
    {
        await plans.UpdateAsync(id, dto, ct);
        return NoContent();
    }

    [HasPermission(Permissions.Subscriptions.Manage)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await plans.DeleteAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[Authorize]
[Route("api/subscriptions")]
public class SubscriptionsController(SubscriptionService subscriptions) : ControllerBase
{
    [HasPermission(Permissions.Subscriptions.View)]
    [HttpGet]
    public Task<PagedResult<SubscriptionDto>> List([FromQuery] PagedQuery query, [FromQuery] SubscriptionStatus? status, CancellationToken ct) =>
        subscriptions.ListAsync(query, status, ct);

    /// <summary>The caller's own tenant: current plan, usage, balance and history.</summary>
    [HasPermission(Permissions.Subscriptions.View)]
    [HttpGet("overview")]
    public Task<SubscriptionOverviewDto> Overview(CancellationToken ct) => subscriptions.GetOverviewAsync(ct);

    [HasPermission(Permissions.Subscriptions.Manage)]
    [HttpPost]
    public async Task<object> Create(SubscriptionCreateDto dto, CancellationToken ct) => new { id = await subscriptions.CreateAsync(dto, ct) };

    [HasPermission(Permissions.Subscriptions.Manage)]
    [HttpPost("{id:guid}/renew")]
    public async Task<object> Renew(Guid id, SubscriptionRenewDto dto, CancellationToken ct) => new { id = await subscriptions.RenewAsync(id, dto, ct) };

    [HasPermission(Permissions.Subscriptions.Manage)]
    [HttpPost("{id:guid}/status")]
    public async Task<IActionResult> ChangeStatus(Guid id, SubscriptionStatusChangeDto dto, CancellationToken ct)
    {
        await subscriptions.ChangeStatusAsync(id, dto, ct);
        return NoContent();
    }

    [HasPermission(Permissions.Subscriptions.Manage)]
    [HttpPost("{id:guid}/auto-renew")]
    public async Task<IActionResult> AutoRenew(Guid id, [FromQuery] bool enabled, CancellationToken ct)
    {
        await subscriptions.SetAutoRenewAsync(id, enabled, ct);
        return NoContent();
    }

    /// <summary>Runs the expiry / auto-renewal pass now instead of waiting for the hourly job.</summary>
    [HasPermission(Permissions.Subscriptions.Manage)]
    [HttpPost("maintenance")]
    public async Task<object> RunMaintenance(CancellationToken ct)
    {
        var (expired, renewed) = await subscriptions.RunMaintenanceAsync(ct);
        return new { expired, renewed };
    }
}

[ApiController]
[Authorize]
[Route("api/invoices")]
public class InvoicesController(InvoiceService invoices) : ControllerBase
{
    [HasPermission(Permissions.Billing.View)]
    [HttpGet]
    public Task<PagedResult<InvoiceDto>> List([FromQuery] PagedQuery query, [FromQuery] InvoiceStatus? status, [FromQuery] bool overdue, CancellationToken ct) =>
        invoices.ListAsync(query, status, overdue, ct);

    [HasPermission(Permissions.Billing.View)]
    [HttpGet("{id:guid}")]
    public Task<InvoiceDetailDto> Get(Guid id, CancellationToken ct) => invoices.GetAsync(id, ct);

    [HasPermission(Permissions.Billing.View)]
    [HttpGet("stats")]
    public Task<BillingStatsDto> Stats(CancellationToken ct) => invoices.GetStatsAsync(ct);

    [HasPermission(Permissions.Billing.Manage)]
    [HttpPost]
    public async Task<object> Create(InvoiceCreateDto dto, CancellationToken ct) => new { id = await invoices.CreateAsync(dto, ct) };

    [HasPermission(Permissions.Billing.Manage)]
    [HttpPost("{id:guid}/issue")]
    public async Task<IActionResult> Issue(Guid id, CancellationToken ct)
    {
        await invoices.IssueAsync(id, ct);
        return NoContent();
    }

    [HasPermission(Permissions.Billing.Manage)]
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        await invoices.CancelAsync(id, ct);
        return NoContent();
    }

    [HasPermission(Permissions.Billing.Manage)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await invoices.DeleteDraftAsync(id, ct);
        return NoContent();
    }

    [HasPermission(Permissions.Billing.Manage)]
    [HttpPost("{id:guid}/payments")]
    public async Task<object> AddPayment(Guid id, PaymentCreateDto dto, CancellationToken ct) => new { id = await invoices.AddPaymentAsync(id, dto, ct) };
}

[ApiController]
[Authorize]
[Route("api/payments")]
public class PaymentsController(InvoiceService invoices) : ControllerBase
{
    [HasPermission(Permissions.Billing.View)]
    [HttpGet]
    public Task<PagedResult<PaymentDto>> List([FromQuery] PagedQuery query, CancellationToken ct) => invoices.ListPaymentsAsync(query, ct);

    [HasPermission(Permissions.Billing.Manage)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await invoices.DeletePaymentAsync(id, ct);
        return NoContent();
    }
}
