
using System.Text.Json;
using Business.Features.Customers.Exceptions;
using Business.Features.Products.Exceptions;
using Business.Features.Stock.Exceptions;
using Business.Features.Warehouses.Exceptions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using WebAPI.Infrastructure.ExceptionHandling;

namespace Tests.Infrastructure;

public class GlobalExceptionHandlerTests
{
    private static ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddProblemDetails();

        return services.BuildServiceProvider();
    }

    private static HttpContext CreateHttpContext(
        ServiceProvider provider)
    {
        var httpContext = new DefaultHttpContext
        {
            RequestServices = provider
        };

        httpContext.Response.Body = new MemoryStream();

        return httpContext;
    }

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpContext httpContext)
    {
        httpContext.Response.Body.Position = 0;

        return await JsonDocument.ParseAsync(
            httpContext.Response.Body);
    }

    // TEST 1 - Validation: HTTP 400
    [Fact]
    public async Task TryHandleAsync_WhenValidationException_ShouldReturnBadRequestWithPropertyErrors()
    {
        using var provider = CreateServiceProvider();

        var handler = new GlobalExceptionHandler();
        var httpContext = CreateHttpContext(provider);

        var exception = new ValidationException(new[]
        {
            new ValidationFailure(
                "Code", "Code cannot be empty."),

            new ValidationFailure(
                "Code", "Code must be unique."),

            new ValidationFailure(
                "Name", "Name is required.")
        });

        var handled = await handler.TryHandleAsync(
            httpContext,
            exception,
            CancellationToken.None);

        Assert.True(handled);

        Assert.Equal(
            StatusCodes.Status400BadRequest,
            httpContext.Response.StatusCode);

        Assert.Equal(
            "application/problem+json",
            httpContext.Response.ContentType);

        using var json = await ReadJsonAsync(httpContext);

        var root = json.RootElement;

        Assert.True(
            root.TryGetProperty("errors", out var errors));

        Assert.True(
            errors.TryGetProperty("Code", out var codeErrors));

        var codeMessages = codeErrors
            .EnumerateArray()
            .Select(x => x.GetString())
            .ToArray();

        Assert.Equal(2, codeMessages.Length);

        Assert.Contains(
            "Code cannot be empty.",
            codeMessages);

        Assert.Contains(
            "Code must be unique.",
            codeMessages);

        Assert.True(
            errors.TryGetProperty("Name", out var nameErrors));

        var nameMessages = nameErrors
            .EnumerateArray()
            .Select(x => x.GetString())
            .ToArray();

        Assert.Contains(
            "Name is required.",
            nameMessages);
    }

    // TEST 2 - Not Found: HTTP 404
    [Fact]
    public async Task TryHandleAsync_WhenKeyNotFoundException_ShouldReturnNotFoundProblemDetails()
    {
        using var provider = CreateServiceProvider();

        var handler = new GlobalExceptionHandler();
        var httpContext = CreateHttpContext(provider);

        var exception = new KeyNotFoundException(
            "Customer not found: 42");

        var handled = await handler.TryHandleAsync(
            httpContext,
            exception,
            CancellationToken.None);

        Assert.True(handled);

        Assert.Equal(
            StatusCodes.Status404NotFound,
            httpContext.Response.StatusCode);

        Assert.Equal(
            "application/problem+json",
            httpContext.Response.ContentType);

        using var json = await ReadJsonAsync(httpContext);

        var root = json.RootElement;

        Assert.Equal(
            404,
            root.GetProperty("status").GetInt32());

        Assert.Equal(
            "Kayıt bulunamadı",
            root.GetProperty("title").GetString());

        Assert.Equal(
            "İstenen kayıt bulunamadı.",
            root.GetProperty("detail").GetString());

        var rawText = root.GetRawText();

        Assert.DoesNotContain(
            "Customer not found: 42",
            rawText);
    }

    // TEST 3 - Duplicate Customer Code: HTTP 409
    [Fact]
    public async Task TryHandleAsync_WhenDuplicateCustomerCodeException_ShouldReturnConflictProblemDetails()
    {
        using var provider = CreateServiceProvider();

        var handler = new GlobalExceptionHandler();
        var httpContext = CreateHttpContext(provider);

        var exception = new DuplicateCustomerCodeException(
            "C-100");

        var handled = await handler.TryHandleAsync(
            httpContext,
            exception,
            CancellationToken.None);

        Assert.True(handled);

        Assert.Equal(
            StatusCodes.Status409Conflict,
            httpContext.Response.StatusCode);

        Assert.Equal(
            "application/problem+json",
            httpContext.Response.ContentType);

        using var json = await ReadJsonAsync(httpContext);

        var root = json.RootElement;

        Assert.Equal(
            409,
            root.GetProperty("status").GetInt32());

        Assert.Equal(
            "Müşteri kodu zaten kullanılıyor",
            root.GetProperty("title").GetString());

        Assert.Equal(
            "Bu müşteri kodu daha önce kullanılmış. Farklı bir kod giriniz.",
            root.GetProperty("detail").GetString());

        Assert.True(
            root.TryGetProperty("code", out var codeProperty));

        Assert.Equal(
            "C-100",
            codeProperty.GetString());

        var rawText = root.GetRawText();

        Assert.DoesNotContain(
            "stackTrace",
            rawText,
            StringComparison.OrdinalIgnoreCase);
    }

    // TEST 4 - Duplicate Product Ürün Barkodu: HTTP 409
    [Fact]
    public async Task TryHandleAsync_WhenDuplicateProductBarcodeException_ShouldReturnConflictProblemDetails()
    {
        using var provider = CreateServiceProvider();

        var handler = new GlobalExceptionHandler();
        var httpContext = CreateHttpContext(provider);

        var exception = new DuplicateProductBarcodeException(
            "SKU-001");

        var handled = await handler.TryHandleAsync(
            httpContext,
            exception,
            CancellationToken.None);

        Assert.True(handled);

        Assert.Equal(
            StatusCodes.Status409Conflict,
            httpContext.Response.StatusCode);

        using var json = await ReadJsonAsync(httpContext);

        var root = json.RootElement;

        Assert.Equal(
            409,
            root.GetProperty("status").GetInt32());

        Assert.Equal(
            "Ürün Barkodu zaten kullanılıyor",
            root.GetProperty("title").GetString());

        Assert.Equal(
            "Bu ürün barkodu daha önce kullanılmış. Farklı bir Ürün Barkodu giriniz.",
            root.GetProperty("detail").GetString());

        Assert.Equal(
            "SKU-001",
            root.GetProperty("barcode").GetString());
    }

    // TEST 5 - Duplicate Warehouse Code: HTTP 409
    [Fact]
    public async Task TryHandleAsync_WhenDuplicateWarehouseCodeException_ShouldReturnConflictProblemDetails()
    {
        using var provider = CreateServiceProvider();

        var handler = new GlobalExceptionHandler();
        var httpContext = CreateHttpContext(provider);

        var exception = new DuplicateWarehouseCodeException(
            "WH-001");

        var handled = await handler.TryHandleAsync(
            httpContext,
            exception,
            CancellationToken.None);

        Assert.True(handled);

        Assert.Equal(
            StatusCodes.Status409Conflict,
            httpContext.Response.StatusCode);

        using var json = await ReadJsonAsync(httpContext);

        var root = json.RootElement;

        Assert.Equal(
            409,
            root.GetProperty("status").GetInt32());

        Assert.Equal(
            "Depo kodu zaten kullanılıyor",
            root.GetProperty("title").GetString());

        Assert.Equal(
            "Bu depo kodu daha önce kullanılmış. Farklı bir kod giriniz.",
            root.GetProperty("detail").GetString());

        Assert.Equal(
            "WH-001",
            root.GetProperty("code").GetString());
    }

    // TEST 6 - Insufficient Stock: HTTP 409
    [Fact]
    public async Task TryHandleAsync_WhenInsufficientStockException_ShouldReturnConflict()
    {
        using var provider = CreateServiceProvider();

        var handler = new GlobalExceptionHandler();
        var httpContext = CreateHttpContext(provider);

        var exception = new InsufficientStockException(
            Guid.NewGuid(),
            Guid.NewGuid());

        var handled = await handler.TryHandleAsync(
            httpContext,
            exception,
            CancellationToken.None);

        Assert.True(handled);

        Assert.Equal(
            StatusCodes.Status409Conflict,
            httpContext.Response.StatusCode);

        Assert.Equal(
            "application/problem+json",
            httpContext.Response.ContentType);

        using var json = await ReadJsonAsync(httpContext);

        var root = json.RootElement;

        Assert.Equal(
            409,
            root.GetProperty("status").GetInt32());

        Assert.Equal(
            "Yetersiz stok",
            root.GetProperty("title").GetString());

        Assert.Equal(
            exception.Message,
            root.GetProperty("detail").GetString());
    }

    // TEST 7 - Stock Concurrency Conflict: HTTP 409
    [Fact]
    public async Task TryHandleAsync_WhenStockConflictException_ShouldReturnConflict()
    {
        using var provider = CreateServiceProvider();

        var handler = new GlobalExceptionHandler();
        var httpContext = CreateHttpContext(provider);

        var exception = new StockConflictException(
            "Stok eşzamanlı güncelleme çakışması yaşandı.");

        var handled = await handler.TryHandleAsync(
            httpContext,
            exception,
            CancellationToken.None);

        Assert.True(handled);

        Assert.Equal(
            StatusCodes.Status409Conflict,
            httpContext.Response.StatusCode);

        Assert.Equal(
            "application/problem+json",
            httpContext.Response.ContentType);

        using var json = await ReadJsonAsync(httpContext);

        var root = json.RootElement;

        Assert.Equal(
            409,
            root.GetProperty("status").GetInt32());

        Assert.Equal(
            "Stok işlemi çakışması",
            root.GetProperty("title").GetString());

        Assert.Equal(
            exception.Message,
            root.GetProperty("detail").GetString());
    }

    // TEST 8 - Unhandled InvalidOperationException
    [Fact]
    public async Task TryHandleAsync_WhenInvalidOperationException_ShouldReturnFalseAndNotWrite409Response()
    {
        using var provider = CreateServiceProvider();

        var handler = new GlobalExceptionHandler();
        var httpContext = CreateHttpContext(provider);

        var exception = new InvalidOperationException(
            "This is not a handled exception");

        var handled = await handler.TryHandleAsync(
            httpContext,
            exception,
            CancellationToken.None);

        Assert.False(handled);

        Assert.Equal(
            StatusCodes.Status200OK,
            httpContext.Response.StatusCode);

        Assert.Equal(
            0,
            httpContext.Response.Body.Length);
    }

    // TEST 9 - Unknown Exception
    [Fact]
    public async Task TryHandleAsync_WhenUnknownException_ShouldReturnFalseAndNotWriteResponseBody()
    {
        using var provider = CreateServiceProvider();

        var handler = new GlobalExceptionHandler();
        var httpContext = CreateHttpContext(provider);

        var exception = new NotSupportedException(
            "Unexpected failure");

        var handled = await handler.TryHandleAsync(
            httpContext,
            exception,
            CancellationToken.None);

        Assert.False(handled);

        Assert.Equal(
            StatusCodes.Status200OK,
            httpContext.Response.StatusCode);

        Assert.Equal(
            0,
            httpContext.Response.Body.Length);
    }
}
