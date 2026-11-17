using Business.Common.Security;
﻿using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Tests.Integration;

public class CustomerApiSmokeTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public CustomerApiSmokeTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetAll_ShouldReturnOkWithEmptyArray()
    {
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var response = await client.GetAsync("/api/customers");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[]", content.Trim());
    }
}
