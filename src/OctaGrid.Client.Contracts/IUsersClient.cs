using OctaGrid.Protocol;

namespace OctaGrid.Client.Contracts;

public interface IUsersClient
{
    Task<UserResponse> Me(CancellationToken cancellationToken);
}
