#:project ../src/Merpeeps.Core/Merpeeps.Core.csproj

using System.Text.Json.Nodes;
using Merpeeps.Core;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Export;
using Merpeeps.Core.Model;

var draft = MermaidDiagramCatalog.CreateEmpty("sequence");
var history = new EditHistory(draft.Model);
history.Apply(SequenceEditing.AddParticipant(new("Client", SequenceParticipantKind.Actor)));
history.Apply(SequenceEditing.AddParticipant(new("API", Shape: SequenceParticipantShape.Boundary, Alias: "Public API")));
history.Apply(SequenceEditing.AddParticipant(new("DB", Shape: SequenceParticipantShape.Database)));
history.Apply(SequenceEditing.Insert(new SequenceNumbering("numbers", true, 1.25m, .25m)));
history.Apply(SequenceEditing.Insert(new SequenceMessage("request", "Client", "API", "Request", ActivateTarget: true)));
var fragment = SequenceEditing.NewBlock(SequenceBlockKind.Alt, "Allowed");
history.Apply(SequenceEditing.Insert(fragment));
history.Apply(SequenceEditing.Insert(new SequenceMessage("query", "API", "DB", "Lookup"), fragment.Branches[0].Id));
var branch = SequenceEditing.AddBranch(fragment.Id, "Denied");
history.Apply(branch);
history.Apply(SequenceEditing.Insert(new SequenceNote("denied", SequenceNotePlacement.Over, "Client", "API", "No access\nTry again"), branch.ResultId));
history.Apply(SequenceEditing.Insert(new SequenceMessage("response", "API", "Client", "Response", "dotted-arrow", DeactivateSource: true)));
history.Apply(SequenceEditing.Insert(new SequenceMessage("self", "Client", "Client", "Finished", CentralSource: true, CentralTarget: true)));
var source = MerpeepsDocument.Save(draft.Source, draft.Model);
var model = MerpeepsDocument.Load(source);
var output = new JsonArray();
JsonNode record = new JsonObject { ["source"] = source, ["nativeSvg"] = SvgExporter.ToSvg(model) };
output.Add(record);
File.WriteAllText(args[0], output.ToJsonString());
