using Business.Features.Stock.DTOs;
using FluentValidation;

namespace Business.Features.Stock.Validators;

public class StockOperationRequestValidator : AbstractValidator<StockOperationRequest>
{
    public StockOperationRequestValidator()
    {
        RuleFor(x => x.ProductId)
            .NotEmpty()
            .WithMessage("ProductId boş olamaz.");

        RuleFor(x => x.WarehouseId)
            .NotEmpty()
            .WithMessage("WarehouseId boş olamaz.");

        RuleFor(x => x.Quantity)
            .GreaterThan(0)
            .WithMessage("Quantity sıfır veya negatif olamaz.");

        RuleFor(x => x.Note)
            .MaximumLength(500)
            .WithMessage("Note en fazla 500 karakter olabilir.")
            .When(x => !string.IsNullOrWhiteSpace(x.Note));
    }
}
