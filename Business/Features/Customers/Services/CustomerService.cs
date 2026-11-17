using FluentValidation;
using Business.Features.Customers.Abstractions;
using Business.Features.Customers.DTOs;
using Business.Features.Customers.Exceptions;
using Entities.Entities;

namespace Business.Features.Customers.Services;

public class CustomerService : ICustomerService
{
    private readonly ICustomerRepository _customerRepository;
    private readonly IValidator<CustomerCreateRequest> _createValidator;
    private readonly IValidator<CustomerUpdateRequest> _updateValidator;

    public CustomerService(
        ICustomerRepository customerRepository,
        IValidator<CustomerCreateRequest> createValidator,
        IValidator<CustomerUpdateRequest> updateValidator)
    {
        _customerRepository = customerRepository;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    public async Task<CustomerResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _customerRepository.GetByIdAsync(id, cancellationToken);

        return entity is null ? null : ToResponse(entity);
    }

    public async Task<IReadOnlyList<CustomerResponse>> GetAllAsync(CancellationToken cancellationToken)
    {
        var entities = await _customerRepository.GetAllAsync(cancellationToken);

        return entities
            .Select(ToResponse)
            .ToList();
    }

    public async Task<IReadOnlyList<CustomerResponse>> GetInactiveAsync(CancellationToken cancellationToken)
    {
        var entities = await _customerRepository.GetInactiveAsync(cancellationToken);

        return entities.Select(ToResponse).ToList();
    }

    public async Task<CustomerResponse> ReactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var customer = await _customerRepository.GetByIdIncludingInactiveAsync(id, cancellationToken);

        if (customer is null)
        {
            throw new KeyNotFoundException("Yeniden aktifleştirilecek müşteri bulunamadı.");
        }

        if (customer.IsActive)
        {
            throw new CustomerAlreadyActiveException(id);
        }

        customer.IsActive = true;
        customer.DeactivatedAtUtc = null;
        customer.DeactivatedByUserId = null;

        await _customerRepository.SaveChangesAsync(cancellationToken);

        return ToResponse(customer);
    }

    public async Task<CustomerResponse> CreateAsync(CustomerCreateRequest request, CancellationToken cancellationToken)
    {
        var validationResult = await _createValidator.ValidateAsync(request, cancellationToken);

        if (!validationResult.IsValid)
        {
            throw new ValidationException(validationResult.Errors);
        }

        var exists = await _customerRepository.ExistsByCodeAsync(request.Code, cancellationToken);

        if (exists)
        {
            throw new DuplicateCustomerCodeException(request.Code);
        }

        var customer = new Customer
        {
            Code = request.Code,
            Name = request.Name,
            Email = request.Email,
            Phone = request.Phone,
            TaxNumber = request.TaxNumber,
            Address = request.Address,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = null,
            DeactivatedAtUtc = null,
            CreatedByUserId = null,
            UpdatedByUserId = null,
            DeactivatedByUserId = null,
        };

        await _customerRepository.AddAsync(customer, cancellationToken);
        await _customerRepository.SaveChangesAsync(cancellationToken);

        return ToResponse(customer);
    }

    public async Task<CustomerResponse> UpdateAsync(Guid id, CustomerUpdateRequest request, CancellationToken cancellationToken)
    {
        var validationResult = await _updateValidator.ValidateAsync(request, cancellationToken);

        if (!validationResult.IsValid)
        {
            throw new ValidationException(validationResult.Errors);
        }

        var existingCustomer = await _customerRepository.GetByIdAsync(id, cancellationToken);

        if (existingCustomer is null)
        {
            throw new KeyNotFoundException("GÃ¼ncellenecek mÃ¼ÅŸteri bulunamadÄ±.");
        }

        var codeIsChanged = !string.Equals(existingCustomer.Code, request.Code, StringComparison.Ordinal);

        if (codeIsChanged)
        {
            var exists = await _customerRepository.ExistsByCodeAsync(request.Code, cancellationToken);

            if (exists)
            {
                throw new DuplicateCustomerCodeException(request.Code);
            }
        }

        existingCustomer.Code = request.Code;
        existingCustomer.Name = request.Name;
        existingCustomer.Email = request.Email;
        existingCustomer.Phone = request.Phone;
        existingCustomer.TaxNumber = request.TaxNumber;
        existingCustomer.Address = request.Address;

        await _customerRepository.SaveChangesAsync(cancellationToken);

        return ToResponse(existingCustomer);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var customer = await _customerRepository.GetByIdAsync(id, cancellationToken);

        if (customer is null)
        {
            throw new KeyNotFoundException("PasifleÅŸtirilecek mÃ¼ÅŸteri bulunamadÄ±.");
        }

        _customerRepository.Remove(customer);
        await _customerRepository.SaveChangesAsync(cancellationToken);
    }

    private static CustomerResponse ToResponse(Customer customer)
    {
        return new CustomerResponse
        {
            Id = customer.Id,
            Code = customer.Code,
            Name = customer.Name,
            Email = customer.Email,
            Phone = customer.Phone,
            TaxNumber = customer.TaxNumber,
            Address = customer.Address,
            IsActive = customer.IsActive,
            CreatedAtUtc = customer.CreatedAtUtc,
            UpdatedAtUtc = customer.UpdatedAtUtc
        };
    }
}
