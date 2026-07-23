using JackAll.Cli.Commands;
using Spectre.Console.Cli;

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("jackall");
    config.AddBranch("savegame", branch =>
    {
        branch.AddCommand<SavegameReverseCommand>("reverse")
            .WithDescription(
                "Measures how much of a save's PersistenceDB tree resolves against a reverse " +
                "hash->string dictionary harvested from String-typed values in a directory of .fcb " +
                "files (e.g. entitylibrary).");
    });
});

return app.Run(args);
