using JackAll.Core.Format.Fcb;
using JackAll.Tools.World;

namespace JackAll.Tests;

/// <summary>
/// Sorting the mesh-less third of a world into the categories the map draws glyphs for.
/// </summary>
public class WorldEntityCategoryTests
{
    private static FcbObject Entity(params string[] components)
    {
        var node = new FcbObject { TypeHash = WorldHashes.Entity };
        var holder = new FcbObject { TypeHash = WorldHashes.Components };
        foreach (string component in components)
        {
            holder.Children.Add(new FcbObject { TypeHash = FcbClassDefinitions.Crc32Ascii(component) });
        }
        node.Children.Add(holder);
        return node;
    }

    [Fact]
    public void Each_component_picks_out_its_category()
    {
        Assert.Equal(EntityCategory.Light, WorldEntityCategories.Of(Entity("CDynamicLightComponent")));
        Assert.Equal(EntityCategory.Trigger, WorldEntityCategories.Of(Entity("CProximityTriggerComponent")));
        Assert.Equal(EntityCategory.Vegetation, WorldEntityCategories.Of(Entity("CRealtreeComponent")));
        Assert.Equal(EntityCategory.Emitter, WorldEntityCategories.Of(Entity("CNewParticlesComponent")));
        Assert.Equal(EntityCategory.Emitter, WorldEntityCategories.Of(Entity("CSoundComponent")));
        Assert.Equal(EntityCategory.Entrance, WorldEntityCategories.Of(Entity("CEntranceInfoComponent")));
        Assert.Equal(EntityCategory.Ai, WorldEntityCategories.Of(Entity("CFCXAIComponent")));
    }

    /// <summary>Anything naming none of the known components is a logic node - by count the largest
    /// group, so it must be the fallback rather than a special case.</summary>
    [Fact]
    public void Anything_unrecognised_is_an_event_node()
    {
        Assert.Equal(EntityCategory.Event, WorldEntityCategories.Of(Entity("CEventComponent")));
        Assert.Equal(EntityCategory.Event, WorldEntityCategories.Of(Entity()));
        Assert.Equal(EntityCategory.Event, WorldEntityCategories.Of(
            Entity("CEventComponent", "CPersistComponent")));
    }

    /// <summary>
    /// The ordering rule that earns its place: a building's entrance node carries an AI component
    /// too, and 240 of them in world 1 alone would otherwise be filed as plain AI points and lost
    /// among 3,000 cover markers.
    /// </summary>
    [Fact]
    public void An_entrance_that_also_carries_an_ai_component_stays_an_entrance()
    {
        Assert.Equal(EntityCategory.Entrance, WorldEntityCategories.Of(
            Entity("CBuildingInfoComponent", "CEventComponent", "CFCXAIComponent")));
    }

    /// <summary>The three categories another layer already draws must say so, or a light picks up a
    /// second glyph stacked on the one the Lights layer gives it.</summary>
    [Fact]
    public void Only_the_categories_another_layer_draws_claim_one()
    {
        Assert.True(EntityCategory.Light.HasOwnLayer());
        Assert.True(EntityCategory.Trigger.HasOwnLayer());
        Assert.True(EntityCategory.Vegetation.HasOwnLayer());

        Assert.False(EntityCategory.Emitter.HasOwnLayer());
        Assert.False(EntityCategory.Entrance.HasOwnLayer());
        Assert.False(EntityCategory.Ai.HasOwnLayer());
        Assert.False(EntityCategory.Event.HasOwnLayer());
    }
}
