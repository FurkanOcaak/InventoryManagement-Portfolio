using Business.Features.Warehouses.Abstractions;
using DataAccess.Persistence;
using Entities.Entities;
using Microsoft.EntityFrameworkCore;

namespace DataAccess.Repositories;

public class WarehouseRepository : IWarehouseRepository
{
    private readonly InventoryDbContext _context;

    public WarehouseRepository(InventoryDbContext context)
    {
        _context = context;
    }

    public async Task<Warehouse?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _context.Warehouses
            .FirstOrDefaultAsync(warehouse => warehouse.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Warehouse>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await _context.Warehouses
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Warehouse>> GetInactiveAsync(CancellationToken cancellationToken)
    {
        return await _context.Warehouses
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(warehouse => !warehouse.IsActive)
            .ToListAsync(cancellationToken);
    }

    public async Task<Warehouse?> GetByIdIncludingInactiveAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _context.Warehouses
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(warehouse => warehouse.Id == id, cancellationToken);
    }

    public async Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken)
    {
        return await _context.Warehouses
            .IgnoreQueryFilters()
            .AnyAsync(warehouse => warehouse.Code == code, cancellationToken);
    }

    public async Task AddAsync(Warehouse warehouse, CancellationToken cancellationToken)
    {
        await _context.Warehouses.AddAsync(warehouse, cancellationToken);
    }

    public void Remove(Warehouse warehouse)
    {
        _context.Warehouses.Remove(warehouse);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        await _context.SaveChangesAsync(cancellationToken);
    }
}
