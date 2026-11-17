using FluentValidation;
using Business.Features.Customers.DTOs;

namespace Business.Features.Customers.Validators;

public class CustomerCreateRequestValidator : AbstractValidator<CustomerCreateRequest>
{
    public CustomerCreateRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .WithMessage("MÃ¼ÅŸteri kodu boÅŸ olamaz.")
            .Must(value => !string.IsNullOrWhiteSpace(value))
            .WithMessage("MÃ¼ÅŸteri kodu boÅŸluklardan oluÅŸamaz.")
            .MaximumLength(32)
            .WithMessage("MÃ¼ÅŸteri kodu en fazla 32 karakter olabilir.");

        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("MÃ¼ÅŸteri adÄ± boÅŸ olamaz.")
            .Must(value => !string.IsNullOrWhiteSpace(value))
            .WithMessage("MÃ¼ÅŸteri adÄ± boÅŸluklardan oluÅŸamaz.")
            .MaximumLength(200)
            .WithMessage("MÃ¼ÅŸteri adÄ± en fazla 200 karakter olabilir.");

        RuleFor(x => x.Email)
            .MaximumLength(254)
            .WithMessage("E-posta en fazla 254 karakter olabilir.")
            .EmailAddress()
            .When(x => !string.IsNullOrWhiteSpace(x.Email))
            .WithMessage("GeÃ§erli bir e-posta adresi giriniz.");

        RuleFor(x => x.Phone)
            .MaximumLength(32)
            .WithMessage("Telefon en fazla 32 karakter olabilir.");

        RuleFor(x => x.TaxNumber)
            .MaximumLength(32)
            .WithMessage("Vergi numarasÄ± en fazla 32 karakter olabilir.");

        RuleFor(x => x.Address)
            .MaximumLength(500)
            .WithMessage("Adres en fazla 500 karakter olabilir.");
    }
}
