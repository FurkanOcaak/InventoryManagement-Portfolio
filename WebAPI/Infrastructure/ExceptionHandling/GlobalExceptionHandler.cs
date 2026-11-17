
using Business.Features.Customers.Exceptions;
using Business.Features.Products.Exceptions;
using Business.Features.Stock.Exceptions;
using Business.Features.Warehouses.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace WebAPI.Infrastructure.ExceptionHandling;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // 400 Bad Request - FluentValidation
        if (exception is Business.Features.Commerce.CommerceException commerce)
        {
            var status = commerce.Failure switch
            {
                Business.Features.Commerce.CommerceFailure.Invalid => StatusCodes.Status400BadRequest,
                Business.Features.Commerce.CommerceFailure.Forbidden => StatusCodes.Status403Forbidden,
                _ => StatusCodes.Status409Conflict
            };
            await Results.Problem(statusCode: status, title: commerce.Message).ExecuteAsync(httpContext);
            return true;
        }
        if (exception is ValidationException validationException)
        {
            var errors = validationException.Errors
                .GroupBy(x =>
                    string.IsNullOrWhiteSpace(x.PropertyName)
                        ? "request"
                        : x.PropertyName)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .Select(x => x.ErrorMessage)
                        .ToArray());

            var validationProblem = Results.ValidationProblem(errors);

            httpContext.Response.StatusCode =
                StatusCodes.Status400BadRequest;

            httpContext.Response.ContentType =
                "application/problem+json";

            await validationProblem.ExecuteAsync(httpContext);

            return true;
        }

        // 404 Not Found - Customer, Product, Warehouse, Stock
        if (exception is KeyNotFoundException)
        {
            var problemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Kayıt bulunamadı",
                Detail = "İstenen kayıt bulunamadı.",
                Type = "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.5"
            };

            httpContext.Response.StatusCode =
                StatusCodes.Status404NotFound;

            httpContext.Response.ContentType =
                "application/problem+json";

            await httpContext.Response.WriteAsJsonAsync(
                problemDetails,
                options: null,
                contentType: "application/problem+json",
                cancellationToken: cancellationToken);

            return true;
        }

        // 409 Conflict - Customer already active
        if (exception is CustomerAlreadyActiveException customerAlreadyActiveException)
        {
            var problemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Müşteri zaten aktif",
                Detail = "Bu müşteri zaten aktif olduğu için yeniden aktifleştirilemez.",
                Type = "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.10",
                Extensions =
                {
                    ["customerId"] = customerAlreadyActiveException.CustomerId
                }
            };

            httpContext.Response.StatusCode = StatusCodes.Status409Conflict;

            await httpContext.Response.WriteAsJsonAsync(
                problemDetails,
                options: null,
                contentType: "application/problem+json",
                cancellationToken: cancellationToken);

            return true;
        }

        // 409 Conflict - Duplicate Customer Code
        if (exception is DuplicateCustomerCodeException
            duplicateCustomerCodeException)
        {
            var problemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Müşteri kodu zaten kullanılıyor",
                Detail = "Bu müşteri kodu daha önce kullanılmış. Farklı bir kod giriniz.",
                Type = "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.10",
                Extensions =
                {
                    ["code"] = duplicateCustomerCodeException.Code
                }
            };

            httpContext.Response.StatusCode =
                StatusCodes.Status409Conflict;

            httpContext.Response.ContentType =
                "application/problem+json";

            await httpContext.Response.WriteAsJsonAsync(
                problemDetails,
                options: null,
                contentType: "application/problem+json",
                cancellationToken: cancellationToken);

            return true;
        }

        // 409 Conflict - Product already active
        if (exception is ProductAlreadyActiveException productAlreadyActiveException)
        {
            var problemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Ürün zaten aktif",
                Detail = "Bu ürün zaten aktif olduğu için yeniden aktifleştirilemez.",
                Type = "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.10",
                Extensions =
                {
                    ["productId"] = productAlreadyActiveException.ProductId
                }
            };

            httpContext.Response.StatusCode = StatusCodes.Status409Conflict;

            await httpContext.Response.WriteAsJsonAsync(
                problemDetails,
                options: null,
                contentType: "application/problem+json",
                cancellationToken: cancellationToken);

            return true;
        }

        // 409 Conflict - Duplicate Product Ürün Barkodu
        if (exception is DuplicateProductBarcodeException
            duplicateProductBarcodeException)
        {
            var problemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Ürün Barkodu zaten kullanılıyor",
                Detail = "Bu ürün barkodu daha önce kullanılmış. Farklı bir Ürün Barkodu giriniz.",
                Type = "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.10",
                Extensions =
                {
                    ["barcode"] = duplicateProductBarcodeException.Barcode
                }
            };

            httpContext.Response.StatusCode =
                StatusCodes.Status409Conflict;

            httpContext.Response.ContentType =
                "application/problem+json";

            await httpContext.Response.WriteAsJsonAsync(
                problemDetails,
                options: null,
                contentType: "application/problem+json",
                cancellationToken: cancellationToken);

            return true;
        }

        // 409 Conflict - Warehouse already active
        if (exception is WarehouseAlreadyActiveException warehouseAlreadyActiveException)
        {
            var problemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Depo zaten aktif",
                Detail = "Bu depo zaten aktif olduğu için yeniden aktifleştirilemez.",
                Type = "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.10",
                Extensions =
                {
                    ["warehouseId"] = warehouseAlreadyActiveException.WarehouseId
                }
            };

            httpContext.Response.StatusCode = StatusCodes.Status409Conflict;

            await httpContext.Response.WriteAsJsonAsync(
                problemDetails,
                options: null,
                contentType: "application/problem+json",
                cancellationToken: cancellationToken);

            return true;
        }

        // 409 Conflict - Duplicate Warehouse Code
        if (exception is DuplicateWarehouseCodeException
            duplicateWarehouseCodeException)
        {
            var problemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Depo kodu zaten kullanılıyor",
                Detail = "Bu depo kodu daha önce kullanılmış. Farklı bir kod giriniz.",
                Type = "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.10",
                Extensions =
                {
                    ["code"] = duplicateWarehouseCodeException.Code
                }
            };

            httpContext.Response.StatusCode =
                StatusCodes.Status409Conflict;

            httpContext.Response.ContentType =
                "application/problem+json";

            await httpContext.Response.WriteAsJsonAsync(
                problemDetails,
                options: null,
                contentType: "application/problem+json",
                cancellationToken: cancellationToken);

            return true;
        }

        // 409 Conflict - Insufficient Stock
        if (exception is InsufficientStockException)
        {
            var problemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Yetersiz stok",
                Detail = exception.Message,
                Type = "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.10"
            };

            httpContext.Response.StatusCode =
                StatusCodes.Status409Conflict;

            httpContext.Response.ContentType =
                "application/problem+json";

            await httpContext.Response.WriteAsJsonAsync(
                problemDetails,
                options: null,
                contentType: "application/problem+json",
                cancellationToken: cancellationToken);

            return true;
        }

        // 409 Conflict - Stock Concurrency / Inventory Conflict
        if (exception is StockConflictException)
        {
            var problemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Stok işlemi çakışması",
                Detail = exception.Message,
                Type = "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.10"
            };

            httpContext.Response.StatusCode =
                StatusCodes.Status409Conflict;

            httpContext.Response.ContentType =
                "application/problem+json";

            await httpContext.Response.WriteAsJsonAsync(
                problemDetails,
                options: null,
                contentType: "application/problem+json",
                cancellationToken: cancellationToken);

            return true;
        }

        // Unhandled exceptions are delegated to the ASP.NET Core pipeline.
        return false;
    }
}
