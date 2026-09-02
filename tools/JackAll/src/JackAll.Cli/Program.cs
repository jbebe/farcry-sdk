using JackAll.Cli.Commands;
using JackAll.Cli.Commands.Archive;
using JackAll.Cli.Commands.Fc2Model;
using JackAll.Cli.Commands.Fcb;
using JackAll.Cli.Commands.Mgb;
using JackAll.Cli.Commands.Mod;
using JackAll.Cli.Commands.Move;
using JackAll.Cli.Commands.Rml;
using JackAll.Cli.Commands.Sav;
using JackAll.Cli.Commands.Sbao;
using JackAll.Cli.Commands.Spk;
using JackAll.Cli.Commands.Xbg;
using JackAll.Cli.Commands.Xbt;
using JackAll.Cli.Commands.Xref;
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
        mod.AddCommand<ModLintCommand>("lint")
            .WithDescription("Report archetype edits a later entity library overrides, so they change nothing in game.")
            .WithExample("mod", "lint", "--game", @"C:\Games\Far Cry 2", "--layer", "mods\\a");
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

    // --- .fc2model packs -------------------------------------------------
    config.AddBranch("fc2model", pack =>
    {
        pack.AddCommand<Fc2ModelExportCommand>("export")
            .WithDescription("Collect a model and everything it references into a decoded pack.")
            .WithExample("fc2model", "export", "-g", @"C:\Games\Far Cry 2",
                "graphics/weapons/primary/ak47/ak47.xbg");
        pack.AddCommand<Fc2ModelExtractCommand>("extract")
            .WithDescription("Write a pack's edits out as game files, laid out as a mod layer.")
            .WithExample("fc2model", "extract", "ak47.fc2model");
        pack.AddCommand<Fc2ModelInspectCommand>("inspect")
            .WithDescription("List what a pack holds and which of it has been changed.")
            .WithExample("fc2model", "inspect", "ak47.fc2model");
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
            .WithDescription("Decode an .fcb to XML.")
            .WithExample("fcb", "decode", "entitylibrary.fcb");
        fcb.AddCommand<FcbEncodeCommand>("encode")
            .WithDescription("Re-encode XML back into an .fcb.")
            .WithExample("fcb", "encode", "entitylibrary.xml");
    });

    // --- .sav savegames ---------------------------------------------------
    config.AddBranch("sav", sav =>
    {
        sav.AddCommand<SavListCommand>("list")
            .WithDescription("List the player's saves.")
            .WithExample("sav", "list");
        sav.AddCommand<SavCleanCommand>("clean")
            .WithDescription(
                "Copy a save with its persisted entities dropped, so entities respawn from the current " +
                "entity library and a mod installed after the save was made takes effect.")
            .WithExample("sav", "clean", "759340844606.sav");
    });

    // --- .mgb Magma UI packages -----------------------------------------
    config.AddBranch("mgb", mgb =>
    {
        mgb.AddCommand<MgbDecodeCommand>("decode")
            .WithDescription("Decode a binary .mgb (Magma UI package) to editable XML.")
            .WithExample("mgb", "decode", "options.mgb");
        mgb.AddCommand<MgbEncodeCommand>("encode")
            .WithDescription("Build an XML document back into a binary .mgb.")
            .WithExample("mgb", "encode", "options.xml");
        mgb.AddCommand<MgbVerifyCommand>("verify")
            .WithDescription("Check that a .mgb, or the XML it is built from, references only names it declares.")
            .WithExample("mgb", "verify", "fcse.mgb.xml", "--page", "FCSE_PAGE");
    });

    // --- MOVE animation graphs ------------------------------------------
    config.AddBranch("move", move =>
    {
        move.AddCommand<MoveDecodeCommand>("decode")
            .WithDescription("Decode a MOVE animation graph (movemgr.bin) to editable XML.")
            .WithExample("move", "decode", "movemgr.bin", "--names", "movemgrnamed.bin");
        move.AddCommand<MoveEncodeCommand>("encode")
            .WithDescription("Build an XML document back into a binary MOVE graph.")
            .WithExample("move", "encode", "movemgr.xml");
        move.AddCommand<MoveVerifyCommand>("verify")
            .WithDescription("Check that a MOVE graph, or the XML it is built from, reads back to itself.")
            .WithExample("move", "verify", "dlc1.bin");
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

    // --- Hash reference graph --------------------------------------------
    // The headless half of the app's Xrefs panel. `build` is also the only way to see what the
    // index actually costs - counts, timing, and how many file references resolve to a real entry.
    config.AddBranch("xref", xref =>
    {
        xref.AddCommand<XrefBuildCommand>("build")
            .WithDescription("Index every hash reference in the game's archives, beside the install.")
            .WithExample("xref", "build", "--game", @"C:\Games\Far Cry 2");
        xref.AddCommand<XrefToCommand>("to")
            .WithDescription("List everything that references a path or hash.")
            .WithExample("xref", "to", @"graphics\_common\weapons\ak47\ak47_d.xbt", "--game", @"C:\Games\Far Cry 2");
        xref.AddCommand<XrefFromCommand>("from")
            .WithDescription("List everything a file references.")
            .WithExample("xref", "from", @"graphics\_common\weapons\ak47\ak47.xbm", "--game", @"C:\Games\Far Cry 2");
    });
});

return app.Run(args);
