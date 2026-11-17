using Business.Common.Security;
using Microsoft.AspNetCore.Authorization;

namespace WebAPI.Infrastructure.Security;

public sealed class LinkedCustomerRequirement : IAuthorizationRequirement;

public sealed class LinkedCustomerHandler(ICurrentCustomer customer)
    : AuthorizationHandler<LinkedCustomerRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, LinkedCustomerRequirement requirement)
    {
        if (await customer.GetCustomerIdAsync() is not null) context.Succeed(requirement);
    }
}
