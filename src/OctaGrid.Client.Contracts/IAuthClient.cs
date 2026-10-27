using OctaGrid.Protocol.Users;

namespace OctaGrid.Client.Contracts;

public interface IAuthClient
{
    Task CreateUser(CreateUserRequest request, CancellationToken cancellationToken);

    Task SignIn(SignInRequest request, CancellationToken cancellationToken);
}
