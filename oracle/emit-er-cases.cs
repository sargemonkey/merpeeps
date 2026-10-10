#:project ../src/Merpeeps.Core/Merpeeps.Core.csproj

using System.Text.Json.Nodes;
using Merpeeps.Core;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Export;
using Merpeeps.Core.Model;

var draft = MermaidDiagramCatalog.CreateEmpty("er");
var history = new EditHistory(draft.Model);
var customer = EntityEditing.CreateEntity("CUSTOMER", "Customer **account**");
history.Apply(new AddEntityCommand(customer));
history.Apply(new AddEntityCommand(EntityEditing.CreateEntity("ORDER")));
history.Apply(EntityEditing.AddAttribute(customer, new("id", "int", "id", EntityKey.Primary)));
history.Apply(EntityEditing.AddAttribute(customer, new("email", "string?", "email", EntityKey.Unique, "Primary email")));
history.Apply(new AddEntityGroupCommand(new() { Id = "Domain", Title = "Customer domain", Direction = Direction.LR }));
history.Apply(new SetEntityGroupMembershipCommand(customer, "Domain"));
history.Apply(new AddEntityGroupCommand(new() { Id = "Empty" }));
history.Apply(new AddEntityRelationshipCommand(new()
{
    From = "CUSTOMER", To = "ORDER", Label = "places",
    EntityRelationship = new(EntityCardinality.ExactlyOne, EntityCardinality.ZeroOrMore),
}));
history.Apply(new AddEntityRelationshipCommand(new()
{
    From = "Domain", To = "ORDER", Label = "",
    EntityRelationship = new(EntityCardinality.OneOrMore, EntityCardinality.ZeroOrOne, false),
}));
history.Apply(new AddEntityRelationshipCommand(new()
{
    From = "CUSTOMER", To = "CUSTOMER", Label = "refers",
    EntityRelationship = new(EntityCardinality.ZeroOrOne, EntityCardinality.ZeroOrMore, false),
}));
var source = MerpeepsDocument.Save(draft.Source, draft.Model);
var reopened = MerpeepsDocument.Load(source);
JsonNode result = new JsonObject { ["name"] = "er-authored", ["source"] = source, ["generated"] = source, ["nativeSvg"] = SvgExporter.ToSvg(reopened), ["ok"] = true };
var array = new JsonArray();
array.Add(result);
File.WriteAllText(args[0], array.ToJsonString());
