using Microsoft.Extensions.DependencyInjection;
using OctaGrid.Application.Services;
using OctaGrid.Client.Contracts;
using OctaGrid.Protocol;

namespace OctaGrid.Client.Local;

/// <summary>
/// Runs one call against an application service in its own scope, as the current user. The Local adapters are
/// singletons while the services and their DbContext are scoped, so every call opens a scope, the way a server
/// handles one request, and calls from the UI and the agent never share a DbContext.
/// </summary>
internal sealed class LocalCalls
{
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IServiceScopeFactory _scopes;

    public LocalCalls(IServiceScopeFactory scopes, ICurrentUserAccessor currentUser)
    {
        _scopes = scopes;
        _currentUser = currentUser;
    }

    /// <summary>The single local user, created on first use.</summary>
    public async Task<UserResponse> GetOrCreateUserAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopes.CreateAsyncScope();
        return await GetOrCreateUserAsync(scope.ServiceProvider, cancellationToken);
    }

    public async Task<TResult> Run<TService, TResult>(Func<TService, Guid, Task<TResult>> call, CancellationToken cancellationToken)
        where TService : notnull
    {
        await using var scope = _scopes.CreateAsyncScope();
        var userId = await GetUserIdAsync(scope.ServiceProvider, cancellationToken);
        return await call(scope.ServiceProvider.GetRequiredService<TService>(), userId);
    }

    public async Task Run<TService>(Func<TService, Guid, Task> call, CancellationToken cancellationToken)
        where TService : notnull
    {
        await using var scope = _scopes.CreateAsyncScope();
        var userId = await GetUserIdAsync(scope.ServiceProvider, cancellationToken);
        await call(scope.ServiceProvider.GetRequiredService<TService>(), userId);
    }

    /// <summary>Runs a call the backend makes by itself, not on behalf of the user.</summary>
    public async Task RunAsBackend<TService>(Func<TService, Task> call)
        where TService : notnull
    {
        await using var scope = _scopes.CreateAsyncScope();
        await call(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private async Task<UserResponse> GetOrCreateUserAsync(IServiceProvider scoped, CancellationToken cancellationToken)
    {
        var user = await scoped.GetRequiredService<UsersService>().GetOrCreate(cancellationToken);
        _currentUser.SetCurrentUserId(user.Id);
        return user;
    }

    private async ValueTask<Guid> GetUserIdAsync(IServiceProvider scoped, CancellationToken cancellationToken)
    {
        if (_currentUser.GetCurrentUserId() is { } id)
        {
            return id;
        }

        return (await GetOrCreateUserAsync(scoped, cancellationToken)).Id;
    }
}
