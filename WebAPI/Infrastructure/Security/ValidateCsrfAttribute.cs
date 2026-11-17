using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace WebAPI.Infrastructure.Security;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class ValidateCsrfAttribute() : TypeFilterAttribute(typeof(ValidateCsrfFilter));

public sealed class ValidateCsrfFilter(IAntiforgery antiforgery) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var method = context.HttpContext.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method)) return;

        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "İstek doğrulanamadı.",
                Detail = "Güvenlik doğrulamasını yenileyip tekrar deneyin."
            }) { StatusCode = StatusCodes.Status400BadRequest };
        }
    }
}
