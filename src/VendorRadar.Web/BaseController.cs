using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using VendorRadar.Domain;

namespace VendorRadar.Web;

[Authorize]
public abstract class BaseController : Controller
{
    protected bool IsHtmx => Request.Headers.ContainsKey("HX-Request");

    protected async Task<IActionResult> Change(Func<Task<IActionResult>> operation, Func<IActionResult> retry)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Проверьте обязательные поля, даты и числа.";
            return Retry(retry);
        }
        try { return await operation(); }
        catch (MissingRecordException) { return NotFound(); }
        catch (OperationDeniedException) { return Forbid(); }
        catch (BusinessException ex) { TempData["Error"] = ex.Message; }
        catch (DbUpdateConcurrencyException) { TempData["Error"] = "Данные изменились. Повторите действие после проверки."; }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        { TempData["Error"] = "Такая запись уже существует или действие уже выполнено."; }
        catch (SqlException ex) when (ex.Number == 1205)
        { TempData["Error"] = "Другое изменение выполнялось одновременно. Повторите действие."; }
        return Retry(retry);
    }

    private IActionResult Retry(Func<IActionResult> retry)
    {
        var page = retry();
        if (!IsHtmx || page is not RedirectResult redirect) return page;
        Response.Headers["HX-Redirect"] = redirect.Url;
        return Ok();
    }
}
