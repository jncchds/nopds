using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Nopds.Infrastructure.Data;
using Nopds.Infrastructure.Identity;

namespace Nopds.Tests.Integration;

[Collection(AppCollection.Name)]
public class AdminForceTests(AppFixture app)
{
    private const string Name = "forced-admin";

    [Fact]
    public async Task Force_creates_then_restores_demoted_and_locked_admin()
    {
        await InitAsync("first-pass-1", force: true);
        await using (var scope = app.Factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var u = await users.FindByNameAsync(Name);
            Assert.True(u!.IsAdmin);

            u.IsAdmin = false;
            u.IsApproved = false;
            u.LockoutEnabled = true;
            u.LockoutEnd = DateTimeOffset.MaxValue;
            await users.UpdateAsync(u);
        }

        // Without the flag the existing account is left alone.
        await InitAsync("second-pass-2", force: false);
        await using (var scope = app.Factory.Services.CreateAsyncScope())
        {
            var u = await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByNameAsync(Name);
            Assert.False(u!.IsAdmin);
        }

        await InitAsync("second-pass-2", force: true);
        await using (var scope = app.Factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var u = await users.FindByNameAsync(Name);
            Assert.True(u!.IsAdmin);
            Assert.True(u.CanSignIn(DateTimeOffset.UtcNow));
            Assert.True(await users.CheckPasswordAsync(u, "second-pass-2"));
            Assert.False(await users.CheckPasswordAsync(u, "first-pass-1"));
        }
    }

    private async Task InitAsync(string password, bool force)
    {
        await using var scope = app.Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(false, Name, password, force);
    }
}
