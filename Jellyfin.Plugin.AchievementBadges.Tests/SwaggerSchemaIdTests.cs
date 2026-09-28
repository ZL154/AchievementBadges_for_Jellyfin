using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.AchievementBadges.Models;
using Xunit;

namespace Jellyfin.Plugin.AchievementBadges.Tests;

/// <summary>
/// Swashbuckle keys every schema in the server's OpenAPI document by the bare
/// type name, so a plugin type whose name Jellyfin already uses does not lose a
/// race, it throws: RegisterType refuses the second registration and the whole
/// document fails. The result is a server-wide 500 on /api-docs/openapi.json —
/// Swagger UI, the API browser and every generator that reads the spec, gone,
/// for a plugin that otherwise looks healthy and logs nothing at startup.
/// <para>
/// Our <c>Models.MediaType</c> did exactly that against
/// <c>Jellyfin.Data.Enums.MediaType</c> on 10.11 and 12 alike, which is why it
/// is now <see cref="BadgeMediaType"/>. Nothing in the build caught it: the
/// break lives in the host's document, not ours.
/// </para>
/// </summary>
public class SwaggerSchemaIdTests
{
    /// <summary>
    /// The assemblies whose type names share the document with ours. Loaded
    /// through types we reference, so the test follows whichever Jellyfin
    /// package line this target framework builds against.
    /// </summary>
    private static IEnumerable<Assembly> JellyfinAssemblies() => new[]
    {
        typeof(MediaBrowser.Controller.Library.IUserManager).Assembly,
        typeof(MediaBrowser.Model.Dto.BaseItemDto).Assembly,
        typeof(Jellyfin.Data.Enums.MediaType).Assembly,
    }.Distinct();

    [Fact]
    public void NoPluginTypeShadowsAJellyfinTypeName()
    {
        var jellyfin = JellyfinAssemblies()
            .SelectMany(SafeExportedTypes)
            .Where(t => !t.IsGenericTypeParameter)
            .GroupBy(t => t.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().FullName!, StringComparer.Ordinal);

        var clashes = SafeExportedTypes(typeof(Plugin).Assembly)
            .Where(t => !t.IsNested)
            .Select(t => (Ours: t.FullName!, Name: t.Name))
            .Where(x => jellyfin.ContainsKey(x.Name))
            .Select(x => $"{x.Ours} collides with {jellyfin[x.Name]} (schemaId \"{x.Name}\")")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(clashes);
    }

    /// <summary>
    /// The enum's stored form is its number, and profiles on disk hold those
    /// numbers, so the rename had to leave every ordinal where it was.
    /// </summary>
    [Theory]
    [InlineData(BadgeMediaType.Film, 0)]
    [InlineData(BadgeMediaType.TV, 1)]
    [InlineData(BadgeMediaType.Music, 2)]
    [InlineData(BadgeMediaType.Book, 3)]
    [InlineData(BadgeMediaType.Anime, 4)]
    [InlineData(BadgeMediaType.Multi, 5)]
    [InlineData(BadgeMediaType.Game, 6)]
    public void TheRenameMovedNoStoredOrdinal(BadgeMediaType value, int ordinal)
    {
        Assert.Equal(ordinal, (int)value);
    }

    /// <summary>
    /// And the property carrying it kept its name, which is what the stored
    /// JSON and the client both key on.
    /// </summary>
    [Fact]
    public void TheWireNameOfThePropertyIsUnchanged()
    {
        Assert.Equal(typeof(BadgeMediaType), typeof(AchievementDefinition).GetProperty("Media")!.PropertyType);
        Assert.Equal(typeof(BadgeMediaType), typeof(CustomBadge).GetProperty("Media")!.PropertyType);

        // Unrelated to the enum: Tracearr's own string field is still MediaType.
        Assert.Equal(typeof(string), typeof(TracearrPlay).GetProperty("MediaType")!.PropertyType);
    }

    private static IEnumerable<Type> SafeExportedTypes(Assembly assembly)
    {
        try { return assembly.GetExportedTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }
}
