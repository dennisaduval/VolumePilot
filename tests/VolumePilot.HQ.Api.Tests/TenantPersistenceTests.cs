using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VolumePilot.HQ.Api.Persistence;
using Xunit;

namespace VolumePilot.HQ.Api.Tests;

public sealed class TenantPersistenceTests
{
    [Fact]
    public async Task QueriesOnlyReturnRecordsForTheActiveTenant()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = await OpenDatabaseAsync(cancellationToken);
        var tenantA = NewId();
        var tenantB = NewId();
        var organizationA = NewId();
        var organizationB = NewId();

        await using (var tenantADb = CreateContext(connection, tenantA))
        {
            tenantADb.CompanyAccounts.Add(new CompanyAccount { Id = tenantA, Name = "Studio A" });
            tenantADb.ClientOrganizations.Add(new ClientOrganization
            {
                Id = organizationA,
                Name = "School A",
            });
            await tenantADb.SaveChangesAsync(cancellationToken);
        }

        await using (var tenantBDb = CreateContext(connection, tenantB))
        {
            tenantBDb.CompanyAccounts.Add(new CompanyAccount { Id = tenantB, Name = "Studio B" });
            tenantBDb.ClientOrganizations.Add(new ClientOrganization
            {
                Id = organizationB,
                Name = "School B",
            });
            await tenantBDb.SaveChangesAsync(cancellationToken);
        }

        await using var queryDb = CreateContext(connection, tenantA);
        var visibleOrganizations = await queryDb.ClientOrganizations
            .Select(organization => organization.Id)
            .ToListAsync(cancellationToken);

        Assert.Equal(new[] { organizationA }, visibleOrganizations);
        Assert.Equal("Studio A", (await queryDb.CompanyAccounts.SingleAsync(cancellationToken)).Name);
    }

    [Fact]
    public async Task SaveRejectsTenantIdThatDoesNotMatchTheActiveTenant()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = await OpenDatabaseAsync(cancellationToken);
        var tenantId = NewId();

        await using var db = CreateContext(connection, tenantId);
        db.CompanyAccounts.Add(new CompanyAccount { Id = tenantId, Name = "Studio" });
        db.ClientOrganizations.Add(new ClientOrganization
        {
            Id = NewId(),
            TenantId = NewId(),
            Name = "Other tenant's school",
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => db.SaveChangesAsync(cancellationToken));

        Assert.Contains("outside the active tenant", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompositeRelationshipPreventsCrossTenantJobReferences()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = await OpenDatabaseAsync(cancellationToken);
        var tenantA = NewId();
        var tenantB = NewId();
        var organizationB = NewId();

        await using (var tenantBDb = CreateContext(connection, tenantB))
        {
            tenantBDb.CompanyAccounts.Add(new CompanyAccount { Id = tenantB, Name = "Studio B" });
            tenantBDb.ClientOrganizations.Add(new ClientOrganization
            {
                Id = organizationB,
                Name = "School B",
            });
            await tenantBDb.SaveChangesAsync(cancellationToken);
        }

        await using var tenantADb = CreateContext(connection, tenantA);
        tenantADb.CompanyAccounts.Add(new CompanyAccount { Id = tenantA, Name = "Studio A" });
        tenantADb.Jobs.Add(new Job
        {
            Id = NewId(),
            ClientOrganizationId = organizationB,
            Name = "Cross-tenant job",
        });
        var error = await Assert.ThrowsAsync<DbUpdateException>(
            () => tenantADb.SaveChangesAsync(cancellationToken));

        Assert.NotNull(error);
    }

    private static async Task<SqliteConnection> OpenDatabaseAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(cancellationToken);
        await using var db = CreateContext(connection, tenantId: null);
        await db.Database.EnsureCreatedAsync(cancellationToken);
        return connection;
    }

    private static HqDbContext CreateContext(SqliteConnection connection, string? tenantId)
    {
        var options = new DbContextOptionsBuilder<HqDbContext>()
            .UseSqlite(connection)
            .Options;
        return new HqDbContext(options, new TestTenantContext(tenantId));
    }

    private static string NewId() => Ulid.NewUlid().ToString();

    private sealed record TestTenantContext(string? TenantId) : ITenantContext;
}
