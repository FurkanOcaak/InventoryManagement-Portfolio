using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Business.Features.Customers.Abstractions;
using Business.Features.Products.Abstractions;
using Business.Features.Products.Services;
using Business.Features.Products.Validators;
using Business.Features.Customers.Services;
using Business.Features.Customers.Validators;
using Business.Features.Customers.DTOs;
using Business.Features.Products.DTOs;
using Business.Features.Warehouses.Abstractions;
using Business.Features.Warehouses.DTOs;
using Business.Features.Warehouses.Services;
using Business.Features.Warehouses.Validators;
using DataAccess.Interceptors;
using DataAccess.Persistence;
using DataAccess.Repositories;
using FluentValidation;
using Business.Features.Stock.Abstractions;
using DataAccess.Identity;
using Microsoft.AspNetCore.Identity;

namespace DataAccess;

public static class DependencyInjection
{
	public static IServiceCollection AddDataAccess(
		this IServiceCollection services,
		IConfiguration configuration)
	{
		var connectionString =
			configuration.GetConnectionString("InventoryDatabase");

		if (string.IsNullOrWhiteSpace(connectionString))
		{
			throw new InvalidOperationException(
				"InventoryManagement database connection string is not configured.");
		}

		services.AddScoped<AuditSaveChangesInterceptor>();

		services.AddDbContext<InventoryDbContext>(
			(serviceProvider, options) =>
			{
				options.UseSqlServer(connectionString);

				options.AddInterceptors(
					serviceProvider.GetRequiredService<
						AuditSaveChangesInterceptor>());
			});

		services.AddIdentityCore<ApplicationUser>()
			.AddRoles<IdentityRole<Guid>>()
			.AddEntityFrameworkStores<InventoryDbContext>();

		services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<Business.Common.Security.ICurrentCustomer, CurrentCustomer>();
        services.AddScoped<Business.Features.Commerce.ICommerceStore, CommerceStore>();
        services.AddScoped<Business.Features.Commerce.IAdminOrderAccess, CurrentAdminOrderAccess>();
        services.AddScoped<Business.Features.Commerce.CommerceService>();
		services.AddScoped<IProductRepository, ProductRepository>();
		services.AddScoped<IWarehouseRepository, WarehouseRepository>();
        services.AddScoped<IStockRepository, StockRepository>();
        services.AddScoped<ICustomerService, CustomerService>();
		services.AddScoped<IProductService, ProductService>();
		services.AddScoped<IWarehouseService, WarehouseService>();
		services.AddScoped<IValidator<CustomerCreateRequest>, CustomerCreateRequestValidator>();
		services.AddScoped<IValidator<CustomerUpdateRequest>, CustomerUpdateRequestValidator>();
		services.AddScoped<IValidator<ProductCreateRequest>, ProductCreateRequestValidator>();
		services.AddScoped<IValidator<ProductUpdateRequest>, ProductUpdateRequestValidator>();
		services.AddScoped<IValidator<WarehouseCreateRequest>, WarehouseCreateRequestValidator>();
		services.AddScoped<IValidator<WarehouseUpdateRequest>, WarehouseUpdateRequestValidator>();

		return services;
	}
}
