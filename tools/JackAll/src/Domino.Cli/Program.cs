using Domino.Cli.Commands;
using Spectre.Console.Cli;

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("domino-cli");

    config.AddCommand<ParseAllCommand>("parse-all")
        .WithDescription("Parse every .lua file under a directory and report any that fail.")
        .WithExample("parse-all", @"tmp\fc2-archives\extracted\domino");

    config.AddCommand<ReflectAllCommand>("reflect-all")
        .WithDescription("Parse every system\\ node's DOMINO REFLECTION BOX pin metadata and report any that fail.")
        .WithExample("reflect-all", @"tmp\fc2-archives\extracted\domino\system");

    config.AddCommand<ClassifyGraphsCommand>("classify-graphs")
        .WithDescription("Classify every user\\ mission graph's statements and report the unclassified fraction.")
        .WithExample("classify-graphs", @"tmp\fc2-archives\extracted\domino\user");
});

return app.Run(args);
