namespace Business.Common.Security;

public interface ICurrentUser
{
    Guid? UserId { get; }
}
