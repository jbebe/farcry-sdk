using JackAll.Cli.Commands;
using JackAll.Cli.Commands.Archive;
using JackAll.Cli.Commands.Fcb;
using JackAll.Cli.Commands.Rml;
using JackAll.Cli.Commands.Sbao;
using JackAll.Cli.Commands.Xbg;
using JackAll.Cli.Commands.Xbt;
using Spectre.Console.Cli;

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("jackall");

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
