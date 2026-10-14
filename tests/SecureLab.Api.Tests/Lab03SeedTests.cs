using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Tests;

public sealed class Lab03SeedTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    [Fact]
    public async Task Seed_PreservesGuidRolesOwnersAndStandardPasswordVerifier()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        foreach (var (id, name, role) in new[]
        {
            (DbSeeder.AliceId, "alice", DbSeeder.ReporterRole),
            (DbSeeder.BobId, "bob", DbSeeder.ReporterRole),
            (DbSeeder.MorganId, "morgan", DbSeeder.AnalystRole),
            (DbSeeder.AdminId, "admin", DbSeeder.AdministratorRole)
        })
        {
            var user = await users.FindByIdAsync(id.ToString());
            Assert.NotNull(user);
            Assert.Equal(name, user.UserName);
            Assert.True(await users.IsInRoleAsync(user, role));
            Assert.True(await users.CheckPasswordAsync(user, CookieClient.SeedPassword));
        }
        Assert.Equal(DbSeeder.AliceId, (await db.Incidents.SingleAsync(item => item.Id == DbSeeder.AliceIncidentId)).OwnerUserId);
        Assert.Equal(DbSeeder.BobId, (await db.Incidents.SingleAsync(item => item.Id == DbSeeder.BobIncidentId)).OwnerUserId);
        Assert.DoesNotContain(db.Model.GetEntityTypes(), entity => entity.ClrType == typeof(StudyUser));
        Assert.Equal("identity_users", db.Model.FindEntityType(typeof(ApplicationUser))?.GetTableName());
    }
}
