using Microsoft.AspNetCore.Identity;

namespace DataAccess.Identity;

public class ApplicationUser : IdentityUser<Guid>
{
    // Optional for existing accounts; only trusted administration may assign this link.
    public Guid? CustomerId { get; set; }
    public Entities.Entities.Customer? Customer { get; set; }
}
