using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Merpeeps.Core;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.App.Tests;

public class BoardPacketWorkspaceTests
{
    [AvaloniaFact]
    public void PacketFieldsAreAuthoredAsBitsAndReflowUndoKeepsIdentity()
    {
        using var f = new Fixture("packet\n");
        WorkspaceControls.OpenCreationForm(f.Window);
        f.Field<TextBox>("Field label").Text = "Header";
        f.Field<TextBox>("Bit count").Text = "16";
        f.Click("Apply field");
        f.Window.FindControl<MenuItem>("MenuClassAdd")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); f.Settle();
        f.Field<TextBox>("Field label").Text = "Payload";
        f.Field<TextBox>("Bit count").Text = "40";
        f.Click("Apply field");
        var model = f.Window.CanvasForTest.Model!;
        Assert.Empty(model.Nodes);
        Assert.Equal(55, model.Packet!.Resolve()[1].End);
        var id = model.Packet.Fields[1].Id;
        f.Click("Move earlier and reflow");
        Assert.Equal(id, model.Packet!.Fields[0].Id);
        Assert.Equal(0, model.Packet.Resolve()[0].Start);
        f.Window.CanvasForTest.Undo(); f.Settle();
        Assert.Equal(id, MerpeepsDocument.Load(f.Window.CurrentSource).Packet!.Fields[1].Id);
    }

    [AvaloniaFact]
    public void BoardColumnsCardsMetadataAndTransfersUseContextualTasks()
    {
        using var f = new Fixture("kanban\n");
        WorkspaceControls.OpenCreationForm(f.Window);
        f.Field<TextBox>("Column label").Text = "Todo";
        f.Click("Apply column");
        f.Click("Add card");
        f.Field<TextBox>("Card label").Text = "Implement";
        f.Field<TextBox>("Assigned to").Text = "Me";
        f.Field<TextBox>("Ticket").Text = "X-42";
        f.Click("Apply card");
        var model = f.Window.CanvasForTest.Model!;
        var from = model.Kanban!.Columns[0];
        var card = from.Cards[0];
        f.Window.FindControl<MenuItem>("MenuClassAdd")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); f.Settle();
        f.Field<TextBox>("Column label").Text = "Done";
        f.Click("Apply column");
        var to = model.Kanban!.Columns[1];
        f.Window.CanvasForTest.SelectData(new(DataDiagramRole.Node, card.Id, from.Id)); f.Settle();
        f.Click("Move card…");
        var combo = f.Field<ComboBox>("Destination column");
        combo.SelectedIndex = 1;
        f.Click("Move card");
        var reopened = MerpeepsDocument.Load(f.Window.CurrentSource).Kanban!;
        Assert.Empty(reopened.Columns[0].Cards);
        Assert.Equal(card.Id, reopened.Columns[1].Cards[0].Id);
        Assert.Equal("Me", reopened.Columns[1].Cards[0].Metadata["assigned"]!.GetValue<string>());
        Assert.Equal("X-42", reopened.Columns[1].Cards[0].Metadata["ticket"]!.GetValue<string>());
    }

    private sealed class Fixture : IDisposable
    {
        public MainWindow Window { get; } = new();
        private Border Panel => Window.FindControl<Border>("ClassInspectorHost")!;
        public Fixture(string source) { Window.Show(); Window.LoadSourceForTest(source); Settle(); }
        public T Field<T>(string name) where T : Control => WorkspaceControls.Find<T>(Window, name);
        public void Click(string name) => WorkspaceControls.Click(Window, name);
        public void Settle() => Dispatcher.UIThread.RunJobs();
        public void Dispose() { foreach (var child in Window.OwnedWindows.ToArray()) child.Close(); Window.Close(); }
    }
}
