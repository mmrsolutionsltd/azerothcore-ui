using AzerothCore_UI.Api.Security;
using Xunit;

namespace AzerothCore_UI.Api.Tests.Security;

public sealed class AdministrationPermissionResolverTests
{
    [Theory]
    [InlineData("/api/accounts", "players.accounts")]
    [InlineData("/api/characters/42", "players.characters")]
    [InlineData("/api/quest-helper/add", "adventures.quests")]
    [InlineData("/api/server-administration/questing-companions/start", "adventures.quests")]
    [InlineData("/api/server-administration/dungeon-library/guide", "adventures.dungeons")]
    [InlineData("/api/server-administration/items/give", "players.actions")]
    [InlineData("/api/server-administration/characters/service/transfer", "players.services")]
    [InlineData("/api/server-administration/settings/rates", "server.settings")]
    [InlineData("/api/server-administration/restart", "server.control")]
    [InlineData("/api/database-backups/restore", "server.backups")]
    [InlineData("/api/administration-users/roles/Family", "security.roles")]
    [InlineData("/api/administration-users/audit", "security.audit")]
    public void MapsApiAreaToPermission(string path, string expected)
    {
        Assert.Equal(expected,
            AdministrationPermissionResolver.RequiredPermission("POST", path));
    }

    [Fact]
    public void DoesNotMapUnknownNonAdministrativeRoute()
    {
        Assert.Null(AdministrationPermissionResolver.RequiredPermission(
            "GET", "/health/ready"));
    }

    [Fact]
    public void AllowsAuthenticatedToolsToReadMinimalAvailability()
    {
        Assert.Null(AdministrationPermissionResolver.RequiredPermission(
            "GET", "/api/server-administration/availability"));
    }

    [Fact]
    public void AllowsAuthenticatedPagesToLoadTheirScopedCharacterPicker()
    {
        Assert.Null(AdministrationPermissionResolver.RequiredPermission(
            "GET", "/api/server-administration/players"));
    }

    [Theory]
    [InlineData("GET", "/api/server-administration/trainers")]
    [InlineData("POST", "/api/server-administration/trainers/teleport")]
    public void MapsTrainerFinderEndpointsToTrainingPermission(string method, string path)
    {
        Assert.Equal(
            "adventures.training",
            AdministrationPermissionResolver.RequiredPermission(method, path));
    }
}
