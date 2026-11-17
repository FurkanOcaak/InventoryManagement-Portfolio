using Business.Features.Warehouses.Abstractions;
using Business.Features.Warehouses.DTOs;
using Business.Features.Warehouses.Exceptions;
using Entities.Entities;
using FluentValidation;

namespace Business.Features.Warehouses.Services;

public class WarehouseService : IWarehouseService
{
    private readonly IWarehouseRepository _warehouseRepository;
    private readonly IValidator<WarehouseCreateRequest> _createValidator;
    private readonly IValidator<WarehouseUpdateRequest> _updateValidator;

    public WarehouseService(
        IWarehouseRepository warehouseRepository,
        IValidator<WarehouseCreateRequest> createValidator,
        IValidator<WarehouseUpdateRequest> updateValidator)
    {
        _warehouseRepository = warehouseRepository;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    public async Task<WarehouseResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _warehouseRepository.GetByIdAsync(id, cancellationToken);

        return entity is null ? null : ToResponse(entity);
    }

    public async Task<IReadOnlyList<WarehouseResponse>> GetAllAsync(CancellationToken cancellationToken)
    {
        var entities = await _warehouseRepository.GetAllAsync(cancellationToken);

        return entities
            .Select(ToResponse)
            .ToList();
    }

    public async Task<IReadOnlyList<WarehouseResponse>> GetInactiveAsync(CancellationToken cancellationToken)
    {
        var entities = await _warehouseRepository.GetInactiveAsync(cancellationToken);

        return entities.Select(ToResponse).ToList();
    }

    public async Task<WarehouseResponse> ReactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var warehouse = await _warehouseRepository.GetByIdIncludingInactiveAsync(id, cancellationToken);

        if (warehouse is null)
        {
            throw new KeyNotFoundException("Yeniden aktifleştirilecek depo bulunamadı.");
        }

        if (warehouse.IsActive)
        {
            throw new WarehouseAlreadyActiveException(id);
        }

        warehouse.IsActive = true;
        warehouse.DeactivatedAtUtc = null;
        warehouse.DeactivatedByUserId = null;

        await _warehouseRepository.SaveChangesAsync(cancellationToken);

        return ToResponse(warehouse);
    }

    public async Task<WarehouseResponse> CreateAsync(WarehouseCreateRequest request, CancellationToken cancellationToken)
    {
        var validationResult = await _createValidator.ValidateAsync(request, cancellationToken);

        if (!validationResult.IsValid)
        {
            throw new ValidationException(validationResult.Errors);
        }

        var exists = await _warehouseRepository.ExistsByCodeAsync(request.Code, cancellationToken);

        if (exists)
        {
            throw new DuplicateWarehouseCodeException(request.Code);
        }

        var warehouse = new Warehouse
        {
            Code = request.Code,
            Name = request.Name,
            Location = request.Location,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = null,
            DeactivatedAtUtc = null,
            CreatedByUserId = null,
            UpdatedByUserId = null,
            DeactivatedByUserId = null,
        };

        await _warehouseRepository.AddAsync(warehouse, cancellationToken);
        await _warehouseRepository.SaveChangesAsync(cancellationToken);

        return ToResponse(warehouse);
    }

    public async Task<WarehouseResponse> UpdateAsync(Guid id, WarehouseUpdateRequest request, CancellationToken cancellationToken)
    {
        var validationResult = await _updateValidator.ValidateAsync(request, cancellationToken);

        if (!validationResult.IsValid)
        {
            throw new ValidationException(validationResult.Errors);
        }

        var existingWarehouse = await _warehouseRepository.GetByIdAsync(id, cancellationToken);

        if (existingWarehouse is null)
        {
            throw new KeyNotFoundException("Güncellenecek depo bulunamadı.");
        }

        var codeChanged = !string.Equals(existingWarehouse.Code, request.Code, StringComparison.Ordinal);

        if (codeChanged)
        {
            var exists = await _warehouseRepository.ExistsByCodeAsync(request.Code, cancellationToken);

            if (exists)
            {
                throw new DuplicateWarehouseCodeException(request.Code);
            }
        }

        existingWarehouse.Code = request.Code;
        existingWarehouse.Name = request.Name;
        existingWarehouse.Location = request.Location;

        await _warehouseRepository.SaveChangesAsync(cancellationToken);

        return ToResponse(existingWarehouse);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var warehouse = await _warehouseRepository.GetByIdAsync(id, cancellationToken);

        if (warehouse is null)
        {
            throw new KeyNotFoundException("Pasifleştirilecek depo bulunamadı.");
        }

        _warehouseRepository.Remove(warehouse);
        await _warehouseRepository.SaveChangesAsync(cancellationToken);
    }

    private static WarehouseResponse ToResponse(Warehouse warehouse)
    {
        return new WarehouseResponse
        {
            Id = warehouse.Id,
            Code = warehouse.Code,
            Name = warehouse.Name,
            Location = warehouse.Location,
            IsActive = warehouse.IsActive,
            CreatedAtUtc = warehouse.CreatedAtUtc,
            UpdatedAtUtc = warehouse.UpdatedAtUtc
        };
    }
}
