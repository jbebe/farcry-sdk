namespace JackAll.Tests;

/// <summary>The retail Domino scripts under <c>Fixtures\Domino\</c> the fixture-backed tests read.</summary>
public sealed class DominoFixtures
{
    // A mid-sized release graph whose debug twin traces box-to-box connections.
    public const string FastTravel = "Domino/user/fasttravel/fasttravel.fasttravel.lua";

    // FastTravel's debug twin: TraceConnection calls and named instance boxes.
    public const string FastTravelTwin = "Domino/user/fasttravel/fasttravel.fasttravel.debug.lua";

    // The largest release graph, with a debug twin, preloaded animations and sounds.
    public const string TaxiRide = "Domino/user/openingsequence/openingsequence.taxiride.lua";

    // TaxiRide's debug twin, which the twin tests reach through DominoDebugTwin.TwinPathFor.
    public const string TaxiRideTwin = "Domino/user/openingsequence/openingsequence.taxiride.debug.lua";

    // Sets a bark bank from a literal mission tag.
    public const string ReapSewBriefing = "Domino/user/a1lm02_reapsew.a1lm02_briefingsubvpawnbrief.lua";

    // Pooled boxes read in their own continuation, a pooled en_0, a MultipleAND fired by slot, two entry pins.
    public const string HealthEven = "Domino/user/a1bu00_tutorial.a1bu00_healtheven.lua";

    // A pooled box fired twice in one handler, en_ prologues with unwired outs, pooled boxes only the twin names.
    public const string SafehouseTut = "Domino/user/a1bu00_tutorial.a1bu00_safehousetut.lua";

    // ex_32 shared by two handlers, a Globals write, entry pins In and In_Story.
    public const string PrisonMission = "Domino/user/a1bu01_prison.a1bu01_mission.lua";

    // A system node with every pin kind and a delayed control out.
    public const string ProximityTrigger = "Domino/system/proximitytrigger.lua";

    // A system node with 35 data ins, only two of them described.
    public const string BypassMissionStatus = "Domino/system/bypass_setmissionsstatus.lua";

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found() => Fixture.AssertPresent(
        FastTravel, FastTravelTwin, TaxiRide, TaxiRideTwin, ReapSewBriefing, HealthEven, SafehouseTut, PrisonMission,
        ProximityTrigger, BypassMissionStatus);
}
