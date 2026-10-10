#:project ../src/Merpeeps.Render/Merpeeps.Render.csproj

using Merpeeps.Render;

if (args.Length != 1)
    throw new ArgumentException("usage: dotnet run --file emit-validation-shell.cs -- <output.html>");

File.WriteAllText(args[0], MermaidValidationPage.BuildShell());
Console.WriteLine($"Wrote offline Mermaid {RenderAssets.MermaidVersion} validation shell.");
