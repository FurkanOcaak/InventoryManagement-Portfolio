using Business.Features.Products.Abstractions;
using DataAccess.Persistence;
using Entities.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Business.Features.Products.Exceptions;

namespace DataAccess.Repositories;

public class ProductRepository : IProductRepository
{
    private readonly InventoryDbContext _context;

    public ProductRepository(InventoryDbContext context)
    {
        _context = context;
    }

    public async Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _context.Products
            .FirstOrDefaultAsync(product => product.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await _context.Products
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Product>> GetInactiveAsync(CancellationToken cancellationToken)
    {
        return await _context.Products
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(product => !product.IsActive)
            .ToListAsync(cancellationToken);
    }

    public async Task<Product?> GetByIdIncludingInactiveAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _context.Products
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(product => product.Id == id, cancellationToken);
    }

    public async Task<bool> ExistsByBarcodeAsync(string barcode, CancellationToken cancellationToken)
    {
        return await _context.Products
            .IgnoreQueryFilters()
            .AnyAsync(product => product.Barcode == barcode, cancellationToken);
    }

    public async Task AddAsync(Product product, CancellationToken cancellationToken)
    {
        await _context.Products.AddAsync(product, cancellationToken);
    }

    public void Remove(Product product)
    {
        _context.Products.Remove(product);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is SqlException { Number: 2601 or 2627 } sql
            && sql.Message.Contains("IX_Products_Barcode", StringComparison.Ordinal))
        {
            // The unique index also protects concurrent requests that passed the pre-check.
            var product = exception.Entries.Select(entry => entry.Entity).OfType<Product>().First();
            throw new DuplicateProductBarcodeException(product.Barcode);
        }
    }
}
