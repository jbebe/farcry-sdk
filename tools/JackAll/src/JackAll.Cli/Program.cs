using JackAll.Cli.Commands;
using Spectre.Console.Cli;

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("jackall");
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
});

return app.Run(args);
