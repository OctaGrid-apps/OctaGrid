using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OctaGrid.Application.Identity;
using OctaGrid.Data;
using OctaGrid.Data.Models;
using OctaGrid.Extensions.Models.Exceptions;

namespace OctaGrid.Application.Tests;

/// <summary>Runs user provisioning against a real (in-memory) SQLite database.</summary>
public sealed class UserProvisioningTests : IDisposable
{
    private static readonly ExternalIdentity Alice = new("https://id.example.com", "alice-1", "Alice", "alice@example.com");

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public UserProvisioningTests()
    {
        _connection.Open();
        using var context = NewContext();
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private OctaGridContext NewContext() =>
        new(new DbContextOptionsBuilder<OctaGridContext>().UseSqlite(_connection).Options);

    private async Task<Protocol.UserResponse> Ensure(ExternalIdentity identity)
    {
        await using var context = NewContext();
        return await new UserProvisioning(context).EnsureUserAsync(identity, CancellationToken.None);
    }

    [Fact]
    public async Task A_first_sign_in_creates_a_user_with_the_login()
    {
        var user = await Ensure(Alice);

        Assert.Equal("Alice", user.Name);
        Assert.Equal("alice@example.com", user.Email);

        await using var context = NewContext();
        var login = Assert.Single(context.ExternalLogins.AsNoTracking());
        Assert.Equal((user.Id, Alice.Issuer, Alice.Subject), (login.UserId, login.Issuer, login.Subject));
    }

    [Fact]
    public async Task The_same_account_signs_in_as_the_same_user()
    {
        var first = await Ensure(Alice);
        var second = await Ensure(Alice);

        Assert.Equal(first.Id, second.Id);
        await using var context = NewContext();
        Assert.Single(context.Users.AsNoTracking());
    }

    [Fact]
    public async Task The_profile_follows_what_the_provider_reports()
    {
        var first = await Ensure(Alice);
        var renamed = await Ensure(Alice with { Name = "Alice B.", Email = "ab@example.com" });

        Assert.Equal(first.Id, renamed.Id);
        Assert.Equal("Alice B.", renamed.Name);
        Assert.Equal("ab@example.com", renamed.Email);
    }

    [Fact]
    public async Task The_same_subject_from_another_issuer_is_another_user()
    {
        var first = await Ensure(Alice);
        var other = await Ensure(Alice with { Issuer = "https://login.other.example" });

        Assert.NotEqual(first.Id, other.Id);
    }

    [Fact]
    public async Task Two_users_may_report_the_same_email()
    {
        await Ensure(Alice);
        await Ensure(Alice with { Subject = "alice-2" });

        await using var context = NewContext();
        Assert.Equal(2, context.Users.AsNoTracking().Count(u => u.Email == Alice.Email));
    }

    [Fact]
    public async Task An_account_without_a_subject_is_refused()
    {
        await Assert.ThrowsAsync<ValidationException>(() => Ensure(Alice with { Subject = " " }));
    }

    [Fact]
    public void A_long_name_is_cut_but_a_long_subject_is_refused()
    {
        var user = User.Create(new string('n', User.MaxNameLength + 10), "");

        Assert.Equal(User.MaxNameLength, user.Name.Length);
        Assert.Throws<ValidationException>(() => user.AddLogin("issuer", new string('s', ExternalLogin.MaxLength + 1)));
    }
}
