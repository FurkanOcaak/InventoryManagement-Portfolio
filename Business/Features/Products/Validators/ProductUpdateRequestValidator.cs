using Business.Features.Products.DTOs;
using FluentValidation;

namespace Business.Features.Products.Validators;

public class ProductUpdateRequestValidator : AbstractValidator<ProductUpdateRequest>
{
    public ProductUpdateRequestValidator()
    {
        RuleFor(x => x.Barcode)
            .NotEmpty()
            .WithMessage("Ürün Barkodu boş olamaz.")
            .Must(value => !string.IsNullOrWhiteSpace(value))
            .WithMessage("Ürün Barkodu boşluklardan oluşamaz.")
            .MaximumLength(50)
            .WithMessage("Ürün Barkodu en fazla 50 karakter olabilir.");

        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Ürün adı boş olamaz.")
            .Must(value => !string.IsNullOrWhiteSpace(value))
            .WithMessage("Ürün adı boşluklardan oluşamaz.")
            .MaximumLength(200)
            .WithMessage("Ürün adı en fazla 200 karakter olabilir.");

        RuleFor(x => x.Description)
            .MaximumLength(1000)
            .WithMessage("Açıklama en fazla 1000 karakter olabilir.")
            .When(x => !string.IsNullOrWhiteSpace(x.Description));

        RuleFor(x => x.UnitPrice)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Birim fiyat negatif olamaz.");

        RuleFor(x => x.MinimumStock)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Minimum stok negatif olamaz.");
    }
}
