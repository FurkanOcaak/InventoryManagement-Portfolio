using Business.Features.Warehouses.DTOs;
using FluentValidation;

namespace Business.Features.Warehouses.Validators;

public class WarehouseUpdateRequestValidator : AbstractValidator<WarehouseUpdateRequest>
{
    public WarehouseUpdateRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .WithMessage("Depo kodu boş olamaz.")
            .MaximumLength(50)
            .WithMessage("Depo kodu en fazla 50 karakter olabilir.");

        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Depo adı boş olamaz.")
            .MaximumLength(200)
            .WithMessage("Depo adı en fazla 200 karakter olabilir.");

        RuleFor(x => x.Location)
            .MaximumLength(300)
            .WithMessage("Konum en fazla 300 karakter olabilir.")
            .When(x => !string.IsNullOrWhiteSpace(x.Location));
    }
}
