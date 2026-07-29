using JackAll.Cli.Commands;
using JackAll.Cli.Commands.Archive;
using JackAll.Cli.Commands.Fcb;
using JackAll.Cli.Commands.Mod;
using JackAll.Cli.Commands.Rml;
using JackAll.Cli.Commands.Sbao;
using JackAll.Cli.Commands.Spk;
using JackAll.Cli.Commands.Xbg;
using JackAll.Cli.Commands.Xbt;
using Spectre.Console.Cli;

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("jackall-cli");

    // --- Maintenance -----------------------------------------------------
    config.AddBranch("system", system =>
    {
        system.AddBranch("hash", hash =>
        {
            hash.AddCommand<HashArchiveItemsCommand>("archiveitems")
                .WithDescription(
                    "Rehashes every line of assets/fc2.hashlist in place to HHHHHHHH<TAB>name. Append " +
                    "new entries as a bare name on their own line, then run this to fill in the hash.");
        });
    });

    // --- Mods -------------------------------------------------------------
    // The headless half of JackAll.App's Mods tab, and the whole surface a mod manager driving
    // JackAll needs. Every one of these takes --json: exactly one object on stdout, progress on
    // stderr, so a caller never has to scrape human-readable text.
    config.AddBranch("mod", mod =>
    {
        mod.AddCommand<ModStatusCommand>("status")
            .WithDescription("Report whether a folder is a Far Cry 2 install and what state its patch archive is in.")
            .WithExample("mod", "status", "--game", @"C:\Games\Far Cry 2", "--json");
        mod.AddCommand<ModInspectCommand>("inspect")
            .WithDescription("Say whether a folder/zip is a mod layer or a legacy full-patch mod, and where its tree starts.")
            .WithExample("mod", "inspect", "coolmod.zip", "--game", @"C:\Games\Far Cry 2");
        mod.AddCommand<ModImportLegacyCommand>("import-legacy")
            .WithDescription("Convert a legacy replacement patch.dat/patch.fat mod into an ordinary layer folder.")
            .WithExample("mod", "import-legacy", "--game", @"C:\Games\Far Cry 2", "--from", "oldmod.zip", "--out", "oldmod");
        mod.AddCommand<ModBuildCommand>("build")
            .WithDescription("Compile the vanilla patch plus the given layers into the game's patch.dat/patch.fat.")
            .WithExample("mod", "build", "--game", @"C:\Games\Far Cry 2", "--layer", "mods\\a", "--layer", "mods\\b");
        mod.AddCommand<ModRestoreCommand>("restore")
            .WithDescription("Put the pristine patch.dat/patch.fat back, undoing every build.")
            .WithExample("mod", "restore", "--game", @"C:\Games\Far Cry 2");
    });

    // --- Archives (.fat/.dat) -------------------------------------------
    config.AddBranch("archive", archive =>
    {
        archive.AddCommand<ArchiveExtractCommand>("extract")
            .WithDescription("Extract and decompress a .fat/.dat archive's entries to a folder.")
            .WithExample("archive", "extract", "worlds.fat", "--names");
    });

    // --- .xbt textures ---------------------------------------------------
    config.AddBranch("xbt", xbt =>
    {
        xbt.AddCommand<XbtExtractCommand>("extract")
            .WithDescription("Split an .xbt into its .dds payload and .xml header.")
            .WithExample("xbt", "extract", "texture.xbt");
        xbt.AddCommand<XbtBuildCommand>("build")
            .WithDescription("Reassemble an .xbt from a .dds and its .xml header.")
            .WithExample("xbt", "build", "texture.dds", "texture.xml");
    });

    // --- .xbg meshes -----------------------------------------------------
    config.AddBranch("xbg", xbg =>
    {
        xbg.AddCommand<XbgExportCommand>("export")
            .WithDescription("Convert an .xbg's geometry to a Wavefront .obj.")
            .WithExample("xbg", "export", "mesh.xbg");
    });

    // --- .sbao audio -----------------------------------------------------
    config.AddBranch("sbao", sbao =>
    {
        sbao.AddCommand<SbaoExtractCommand>("extract")
            .WithDescription("Split an .sbao into its .ogg stream and .sbaoheader.")
            .WithExample("sbao", "extract", "music.sbao");
        sbao.AddCommand<SbaoBuildCommand>("build")
            .WithDescription("Reassemble an .sbao from a .ogg and its .sbaoheader.")
            .WithExample("sbao", "build", "music.ogg", "music.sbaoheader");
    });

    // --- .spk sound banks --------------------------------------------------
    config.AddBranch("spk", spk =>
    {
        spk.AddCommand<SpkListCommand>("list")
            .WithDescription("List an .spk bank's records - id, type, and a decoded summary.")
            .WithExample("spk", "list", "004e1c52.spk");
        spk.AddCommand<SpkExtractCommand>("extract")
            .WithDescription("Extract one record's audio (Ogg Vorbis or IMA-ADPCM, detected automatically) as .ogg/.wav.")
            .WithExample("spk", "extract", "004e1c52.spk", "0x004e1c50");
        spk.AddCommand<SpkImportCommand>("import")
            .WithDescription("Replace one record's audio with an already-encoded .ogg/.wav file.")
            .WithExample("spk", "import", "004e1c52.spk", "0x004e1c50", "replacement.wav");
    });

    // --- .fcb object trees ----------------------------------------------
    config.AddBranch("fcb", fcb =>
    {
        fcb.AddCommand<FcbDecodeCommand>("decode")
            .WithDescription("Decode an .fcb to XML (index + external sub-files).")
            .WithExample("fcb", "decode", "entitylibrary.fcb");
        fcb.AddCommand<FcbEncodeCommand>("encode")
            .WithDescription("Re-encode an XML index folder back into an .fcb.")
            .WithExample("fcb", "encode", "entitylibrary/entitylibrary.xml");
    });

    // --- .rml resource manifests ----------------------------------------
    config.AddBranch("rml", rml =>
    {
        rml.AddCommand<RmlDecodeCommand>("decode")
            .WithDescription("Decode a binary .rml to plain XML.")
            .WithExample("rml", "decode", "toc.rml");
        rml.AddCommand<RmlEncodeCommand>("encode")
            .WithDescription("Re-encode an XML document back into a binary .rml.")
            .WithExample("rml", "encode", "toc.xml");
    });
});

return app.Run(args);
