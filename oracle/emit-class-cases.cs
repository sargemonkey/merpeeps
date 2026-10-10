#:project ../src/Merpeeps.Core/Merpeeps.Core.csproj

using System.Text.Json.Nodes;
using Merpeeps.Core;
using Merpeeps.Core.Model;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Export;

var output = new JsonArray();
foreach (var dialect in new[] { "classDiagram", "classDiagram-v2" })
{
    var draft = MermaidDiagramCatalog.CreateEmpty("class", dialect);
    var history = new EditHistory(draft.Model);
    var account = ClassEditing.CreateClass("Account", "Customer account");
    var definition = account.Class!;
    definition.GenericType = "T";
    definition.Annotations.Add("service");
    history.Apply(new AddClassElementCommand(account));
    history.Apply(ClassEditing.AddMember(account, new("name", ClassMemberKind.Attribute, "String owner", ClassVisibility.Public)));
    history.Apply(ClassEditing.AddMember(account, new("save", ClassMemberKind.Operation, "save() bool", ClassVisibility.Public, isAbstract: true)));
    history.Apply(new AddClassElementCommand(ClassEditing.CreateClass("Order")));
    history.Apply(new AddClassRelationshipCommand(new()
    {
        From = "Account", To = "Order", Label = "contains",
        ClassRelationship = new(ClassRelationEnd.Composition, ClassRelationEnd.None, false, "1", "*"),
    }));
    history.Apply(new AddClassInterfaceCommand("Account", "port", "Storage"));
    history.Apply(new AddClassNamespaceCommand(new() { Id = "Domain", Title = "Domain objects" }));
    history.Apply(new SetClassNamespaceCommand(account, "Domain"));
    history.Apply(new AddClassElementCommand(ClassEditing.CreateNote("note", "First line\nSecond line", "Account")));
    var saved = MerpeepsDocument.Save(draft.Source, draft.Model);
    var reopened = MerpeepsDocument.Load(saved);
    if (reopened.Node("Account")!.Class!.Members.Count != 2 || reopened.Node("port") is null)
        throw new InvalidOperationException("Authoring lost typed class content.");
    JsonNode record = new JsonObject
    {
        ["name"] = dialect, ["source"] = saved, ["nativeSvg"] = SvgExporter.ToSvg(reopened),
    };
    output.Add(record);
}
File.WriteAllText(args[0], output.ToJsonString());
