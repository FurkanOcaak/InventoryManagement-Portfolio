
using Business.Features.Stock.Abstractions;
using Business.Features.Stock.DTOs;
using Business.Features.Stock.Exceptions;
using Entities.Entities;
using FluentValidation;

namespace Business.Features.Stock.Services;

public class StockService : IStockService
{
    private readonly IStockRepository _stockRepository;
    private readonly IValidator<StockOperationRequest> _validator;

    public StockService(
        IStockRepository stockRepository,
        IValidator<StockOperationRequest> validator)
    {
        _stockRepository = stockRepository;
        _validator = validator;
    }

    public Task<StockOperationResponse> StockInAsync(
        StockOperationRequest request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            request,
            MovementType.StockIn,
            cancellationToken);
    }

    public Task<StockOperationResponse> StockOutAsync(
        StockOperationRequest request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            request,
            MovementType.StockOut,
            cancellationToken);
    }

    private async Task<StockOperationResponse> ExecuteAsync(
        StockOperationRequest request,
        MovementType movementType,
        CancellationToken cancellationToken)
    {
        var validationResult = await _validator.ValidateAsync(
            request,
            cancellationToken);

        if (!validationResult.IsValid)
        {
            throw new ValidationException(validationResult.Errors);
        }

        var product = await _stockRepository.GetProductByIdAsync(
            request.ProductId,
            cancellationToken);

        if (product is null)
        {
            throw new KeyNotFoundException("Ürün bulunamadı.");
        }

        var warehouse = await _stockRepository.GetWarehouseByIdAsync(
            request.WarehouseId,
            cancellationToken);

        if (warehouse is null)
        {
            throw new KeyNotFoundException("Depo bulunamadı.");
        }

        var inventory =
            await _stockRepository.GetInventoryByProductAndWarehouseAsync(
                request.ProductId,
                request.WarehouseId,
                cancellationToken);

        bool isNewInventory = inventory is null;

        if (inventory is null)
        {
            if (movementType == MovementType.StockOut)
            {
                throw new InsufficientStockException(
                    request.ProductId,
                    request.WarehouseId);
            }

            inventory = new Inventory
            {
                ProductId = request.ProductId,
                WarehouseId = request.WarehouseId,
                Quantity = 0,
                Version = Guid.NewGuid(),
                IsActive = true
            };
        }

        int quantityBefore = inventory.Quantity;
        int quantityAfter;

        if (movementType == MovementType.StockIn)
        {
            try
            {
                quantityAfter = checked(
                    quantityBefore + request.Quantity);
            }
            catch (OverflowException)
            {
                throw new StockConflictException(
                    "Stok miktarı izin verilen sınırı aşıyor.");
            }
        }
        else
        {
            if (quantityBefore < request.Quantity)
            {
                throw new InsufficientStockException(
                    request.ProductId,
                    request.WarehouseId);
            }

            quantityAfter = quantityBefore - request.Quantity;
        }

        inventory.Quantity = quantityAfter;
        inventory.Version = Guid.NewGuid();

        var movement = new StockMovement
        {
            MovementType = movementType,
            Quantity = request.Quantity,
            QuantityBefore = quantityBefore,
            QuantityAfter = quantityAfter,
            Note = request.Note,
            IsActive = true
        };

        await _stockRepository.SaveStockOperationAsync(
            inventory,
            movement,
            isNewInventory,
            cancellationToken);

        return new StockOperationResponse
        {
            StockMovementId = movement.Id,
            ProductId = request.ProductId,
            WarehouseId = request.WarehouseId,
            MovementType = movementType,
            Quantity = request.Quantity,
            QuantityBefore = quantityBefore,
            QuantityAfter = quantityAfter,
            CreatedAtUtc = movement.CreatedAtUtc
        };
    }

    public Task<IReadOnlyList<InventoryStockItemResponse>> GetStockAsync(
        Guid? productId,
        Guid? warehouseId,
        CancellationToken cancellationToken)
    {
        return _stockRepository.GetStockAsync(
            productId,
            warehouseId,
            cancellationToken);
    }

    public Task<IReadOnlyList<StockMovementHistoryResponse>> GetMovementsAsync(
        Guid? productId,
        Guid? warehouseId,
        int limit,
        CancellationToken cancellationToken)
    {
        return _stockRepository.GetMovementsAsync(
            productId,
            warehouseId,
            limit,
            cancellationToken);
    }

    public Task<IReadOnlyList<LowStockItemResponse>> GetLowStockAsync(
        CancellationToken cancellationToken)
    {
        return _stockRepository.GetLowStockAsync(cancellationToken);
    }

}
