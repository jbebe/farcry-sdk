using JackAll.Tools.Domino;
using JackAll.Tools.Domino.Nodes;

namespace JackAll.Core.Tests;

public class DominoNodeCatalogTests
{
    /// <summary>A catalog backed by an in-memory path→source map, keyed the way the real VFS is
    /// (lowercase, backslashes).</summary>
    private static DominoNodeCatalog CatalogOf(params (string Path, string Source)[] files)
    {
        var map = files.ToDictionary(f => f.Path, f => f.Source, StringComparer.OrdinalIgnoreCase);
        return new DominoNodeCatalog(path => map.TryGetValue(path, out string? src) ? src : null);
    }

    private const string DelayNode = """

        -- DOMINO REFLECTION BOX START
        --
        -- <Display Category="Script Flow" Text="Delay"/>
        --
        -- <ControlIn  Name="Start"/>
        -- <ControlIn  Name="Stop"/>
        -- <DataIn     Name="Seconds"      Type="Core|float"/>
        --
        -- <ControlOut Name="TimeElapsed"  Delayed="true"/>
        --
        -- DOMINO REFLECTION BOX END

        Delay = { }
        function Delay:Start() end
        export = Delay;
        """;

    [Fact]
    public void Resolves_a_system_node_from_its_reflection_box()
    {
        var catalog = CatalogOf((@"domino\system\delay.lua", DelayNode));

        NodeSignature? signature = catalog.Resolve("Domino/System/Delay.lua");

        Assert.NotNull(signature);
        Assert.Equal(SignatureOrigin.Declared, signature.Origin);
        Assert.Equal("Delay", signature.DisplayName);
        Assert.Equal("Script Flow", signature.Category);
        Assert.Equal(["Start", "Stop"], signature.ControlIns.Select(p => p.Name));
        Assert.Equal("Seconds", signature.DataIns.Single().Name);
        Assert.Equal("Core|float", signature.DataIns.Single().Type);
        Assert.True(signature.ControlOuts.Single().Delayed);
    }

    [Fact]
    public void Maps_a_node_type_path_to_the_lowercase_backslash_vfs_path()
    {
        Assert.Equal(@"domino\system\delay.lua", DominoNodeCatalog.ToVfsPath("Domino/System/Delay.lua"));
        Assert.Equal(
            @"domino\user\common_missionbriefings.basebrief_convo.lua",
            DominoNodeCatalog.ToVfsPath("Domino/User/Common_MissionBriefings.BASEBRIEF_CONVO.lua"));
    }

    [Fact]
    public void Returns_null_when_the_referenced_script_cannot_be_read()
    {
        var catalog = CatalogOf();

        Assert.Null(catalog.Resolve("Domino/System/NotInstalled.lua"));
    }

    [Fact]
    public void Reads_each_script_once_and_caches_the_result_including_misses()
    {
        int reads = 0;
        var catalog = new DominoNodeCatalog(_ => { reads++; return null; });

        catalog.Resolve("Domino/System/Delay.lua");
        catalog.Resolve("Domino/System/Delay.lua");
        catalog.Resolve("Domino/System/Delay.lua");

        Assert.Equal(1, reads);
    }

    [Fact]
    public void Infers_a_sub_graph_signature_from_its_generated_code()
    {
        // The out anchors are the DummyFunction fields in Init; BlackBox also emits a matching empty
        // `function export:Out()` for each, which must not be counted as an in anchor.
        const string subGraph = """
            export = { };
            function export:Create(cbox)
                cbox:RegisterBox("Domino/System/Delay.lua");
            end;
            function export:Init(cbox)
                self.Accepted = DummyFunction;
                self.Finished = DummyFunction;
                self[0] = cbox:CreateBox("Domino/System/Delay.lua");
                self[0].TimeElapsed = self._type.f_0_TimeElapsed;
            end;
            function export:ShutDown() end;
            function export:Start()
                self[0].Seconds = self.WaitTime;
                self[0]._type.Start(self[0]);
            end;
            function export:Cancel()
                self[0]._type.Stop(self[0]);
            end;
            function export:en_1()
                self[0].Seconds = self.WaitTime;
            end;
            function export:ex_2() end;
            function export:f_0_TimeElapsed()
                self = self._graph;
                self:Finished();
            end;
            function export:Accepted() end;
            function export:Finished() end;
            """;
        var catalog = CatalogOf((@"domino\user\test.sub.lua", subGraph));

        NodeSignature? signature = catalog.Resolve("Domino/User/Test.SUB.lua");

        Assert.NotNull(signature);
        Assert.Equal(SignatureOrigin.Inferred, signature.Origin);
        Assert.Equal("SUB", signature.DisplayName);
        // Start and Cancel only: lifecycle, en_N/ex_N/f_* internals and the out-anchor definitions
        // are all excluded.
        Assert.Equal(["Start", "Cancel"], signature.ControlIns.Select(p => p.Name));
        Assert.Equal(["Accepted", "Finished"], signature.ControlOuts.Select(p => p.Name));
        // WaitTime is read but never produced here, so it has to come from the parent graph.
        Assert.Equal("WaitTime", signature.DataIns.Single().Name);
        Assert.Equal(DominoNodeCatalog.UnknownType, signature.DataIns.Single().Type);
    }

    [Fact]
    public void Treats_a_field_assigned_from_a_box_data_out_as_an_output_not_an_input()
    {
        const string subGraph = """
            export = { };
            function export:Init(cbox)
                self.Done = DummyFunction;
                self[0] = cbox:CreateBox("Domino/System/SpawnBuddy.lua");
            end;
            function export:Run()
                self.BuddyPawn = self[0].SpawnedBuddy;
                self[0]._type.Spawn(self[0]);
            end;
            function export:Done() end;
            """;
        var catalog = CatalogOf((@"domino\user\test.sub.lua", subGraph));

        NodeSignature? signature = catalog.Resolve("Domino/User/Test.SUB.lua");

        Assert.NotNull(signature);
        Assert.Equal("BuddyPawn", signature.DataOuts.Single().Name);
        Assert.Empty(signature.DataIns);
    }

    [Fact]
    public void Every_real_system_node_resolves_to_a_declared_signature()
    {
        if (DominoCorpus.SystemDirectory is not { } dir) return;

        var files = Directory.EnumerateFiles(dir, "*.lua", SearchOption.AllDirectories).ToList();
        Assert.True(files.Count > 0, "Fixture corpus is present but empty.");

        var catalog = new DominoNodeCatalog(path =>
        {
            string name = Path.GetFileName(path);
            string candidate = Path.Combine(dir, name);
            return File.Exists(candidate) ? File.ReadAllText(candidate) : null;
        });

        var failures = new List<string>();
        foreach (string file in files)
        {
            string typePath = $"Domino/System/{Path.GetFileName(file)}";
            NodeSignature? signature = catalog.Resolve(typePath);

            if (signature is null)
            {
                failures.Add($"{file}: did not resolve");
            }
            else if (signature.Origin != SignatureOrigin.Declared)
            {
                failures.Add($"{file}: resolved as {signature.Origin}, expected Declared");
            }
            else if (string.IsNullOrWhiteSpace(signature.Category))
            {
                failures.Add($"{file}: no display category");
            }
        }

        Assert.True(failures.Count == 0, $"{failures.Count}/{files.Count} nodes failed:\n" + string.Join('\n', failures.Take(10)));
    }
}
