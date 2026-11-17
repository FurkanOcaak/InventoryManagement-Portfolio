using FluentValidation;
using WebAPI;
using WebAPI.Infrastructure.ExceptionHandling;
using Business.Features.Customers.Abstractions;
using Business.Features.Customers.DTOs;
using Business.Features.Customers.Services;
using Business.Features.Customers.Validators;
using Business.Features.Products.Abstractions;
using Business.Features.Products.DTOs;
using Business.Features.Products.Services;
using Business.Features.Products.Validators;
using Business.Features.Warehouses.Abstractions;
using Business.Features.Warehouses.DTOs;
using Business.Features.Warehouses.Services;
using Business.Features.Warehouses.Validators;
using DataAccess;
using Business.Features.Stock.Abstractions;
using Business.Features.Stock.DTOs;
using Business.Features.Stock.Services;
using Business.Features.Stock.Validators;
using Business.Common.Security;
using WebAPI.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddControllers(options => options.Filters.Add(
    new Microsoft.AspNetCore.Mvc.TypeFilterAttribute(typeof(ValidateCsrfFilter))));
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
builder.Services.AddDataAccess(builder.Configuration);
builder.Services.AddInventoryAuthentication(builder.Environment);
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<IValidator<CustomerCreateRequest>, CustomerCreateRequestValidator>();
builder.Services.AddScoped<IValidator<CustomerUpdateRequest>, CustomerUpdateRequestValidator>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IValidator<ProductCreateRequest>, ProductCreateRequestValidator>();
builder.Services.AddScoped<IValidator<ProductUpdateRequest>, ProductUpdateRequestValidator>();
builder.Services.AddScoped<IWarehouseService, WarehouseService>();
builder.Services.AddScoped<IValidator<WarehouseCreateRequest>, WarehouseCreateRequestValidator>();
builder.Services.AddScoped<IValidator<WarehouseUpdateRequest>, WarehouseUpdateRequestValidator>();
builder.Services.AddScoped<IStockService, StockService>();
builder.Services.AddScoped<
    IValidator<StockOperationRequest>,
    StockOperationRequestValidator>();

var app = builder.Build();

app.UseExceptionHandler(new ExceptionHandlerOptions
{
    AllowStatusCode404Response = true
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();
app.MapControllers();

app.Run();

public partial class Program
{
}
