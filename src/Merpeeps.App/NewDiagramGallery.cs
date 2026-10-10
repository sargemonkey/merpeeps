using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Merpeeps.Core;

namespace Merpeeps.App;

internal sealed class NewDiagramGallery : UserControl
{
    private readonly Action<string> _create;
    private readonly Action _open;
    private readonly Action _back;
    private readonly Func<bool> _hasChanges;
    private readonly StackPanel _confirmation = new() { Spacing = 10, IsVisible = false };
    private readonly TextBlock _warning = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _backButton;
    private readonly Button _discard;
    private readonly Button _openButton;
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private string? _requestedSource;
    private bool _openingFile;

    public NewDiagramGallery(Action<string> create, Action open, Action back, Func<bool> hasChanges)
    {
        _create = create;
        _open = open;
        _back = back;
        _hasChanges = hasChanges;
        var root = new DockPanel { Margin = new Thickness(32) };
        var header = new StackPanel { Spacing = 14, Margin = new Thickness(0, 0, 0, 24) };
        header.Children.Add(new TextBlock { Text = ProductBrand.Name, FontSize = 18, FontWeight = FontWeight.SemiBold, Classes = { "display" } });
        header.Children.Add(new TextBlock { Text = "Create a diagram", FontSize = 30, FontWeight = FontWeight.SemiBold, Classes = { "display" } });
        header.Children.Add(new TextBlock
        {
            Text = "Choose the kind of diagram you want to make. Its tools will follow that choice.",
            TextWrapping = TextWrapping.Wrap,
        });
        var navigation = new WrapPanel();
        _openButton = Button("Open existing diagram…", () => RequestOpen());
        _backButton = Button("Back to diagram", () => _back());
        navigation.Children.Add(_openButton); navigation.Children.Add(_backButton);
        header.Children.Add(navigation);
        var brandedHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 24 };
        brandedHeader.Children.Add(header);
        var mascot = new MerpieMascot { Name = "GalleryMascot", Width = 124, Height = 128, Margin = new Thickness(0, 0, 16, 12) };
        Grid.SetColumn(mascot, 1);
        brandedHeader.Children.Add(mascot);
        DockPanel.SetDock(brandedHeader, Dock.Top);
        root.Children.Add(brandedHeader);

        var bottom = new StackPanel { Spacing = 12, Margin = new Thickness(0, 16, 0, 0) };
        _confirmation.Children.Add(_warning);
        var confirmButtons = new WrapPanel();
        _discard = Button("Discard changes and create", Commit);
        confirmButtons.Children.Add(_discard);
        confirmButtons.Children.Add(Button("Keep current diagram", () =>
        {
            ClearRequest();
            _back();
        }));
        _confirmation.Children.Add(confirmButtons);
        AutomationProperties.SetLiveSetting(_warning, AutomationLiveSetting.Assertive);
        AutomationProperties.SetLiveSetting(_error, AutomationLiveSetting.Assertive);
        bottom.Children.Add(_confirmation);
        bottom.Children.Add(_error);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);

        var body = new StackPanel { Spacing = 16, MaxWidth = 1120, HorizontalAlignment = HorizontalAlignment.Left };
        var tiles = new WrapPanel { Orientation = Orientation.Horizontal };
        var previews = new List<(StackPanel Tile, DiagramTypePreview Preview)>();
        var supported = MermaidDiagramCatalog.Families.Where(f => MerpeepsDocument.GetCapabilities(f.Directive).CanEdit).ToArray();
        foreach (var family in supported.OrderBy(f => f.Id switch { "flowchart" => 0, "state" => 1, _ => 2 }))
        {
            var tile = new StackPanel { Width = 292, Spacing = 7, Margin = new Thickness(0, 0, 16, 20) };
            var content = new StackPanel { Spacing = 12 };
            var preview = new DiagramTypePreview(family.Id) { Width = 268, Height = 164 };
            previews.Add((tile, preview));
            content.Children.Add(preview);
            content.Children.Add(new TextBlock { Text = family.DisplayName, FontSize = 19, FontWeight = FontWeight.SemiBold });
            content.Children.Add(new TextBlock { Text = Description(family.Id), TextWrapping = TextWrapping.Wrap });
            var button = new Button
            {
                Content = content,
                Padding = new Thickness(12),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Name = "CreateDiagram_" + family.Id,
            };
            AutomationProperties.SetName(button, "Create " + family.DisplayName);
            AutomationProperties.SetHelpText(button, "Starts an empty " + family.DisplayName + ". The thumbnail is only a preview.");
            button.Click += (_, _) => Request(family, family.Dialects[0]);
            tile.Children.Add(button);
            if (family.Dialects.Count > 1)
            {
                var variants = new Button { Content = "Other syntax…", HorizontalAlignment = HorizontalAlignment.Left };
                AutomationProperties.SetName(variants, family.DisplayName + " syntax variants");
                var menu = new ContextMenu
                {
                    ItemsSource = family.Dialects.Select(dialect =>
                    {
                        var item = new CommandMenuItem { Header = dialect.DisplayName };
                        item.Click += (_, _) => Request(family, dialect);
                        return item;
                    }).ToArray(),
                };
                variants.ContextMenu = menu;
                variants.Click += (_, _) => menu.Open(variants);
                tile.Children.Add(variants);
            }
            tiles.Children.Add(tile);
        }
        body.Children.Add(tiles);
        body.Children.Add(new TextBlock
        {
            Text = "Previews explain the diagram types. New diagrams start empty—no example content is inserted.",
            TextWrapping = TextWrapping.Wrap,
        });
        var unavailable = new WrapPanel();
        foreach (var family in MermaidDiagramCatalog.Families.Except(supported))
        {
            var label = new TextBlock
            {
                Text = family.DisplayName + " — not yet available",
                TextWrapping = TextWrapping.Wrap,
                Width = 290,
                Margin = new Thickness(0, 6, 16, 6),
            };
            AutomationProperties.SetName(label, family.DisplayName + ": native visual authoring is not implemented.");
            unavailable.Children.Add(label);
        }
        if (unavailable.Children.Count > 0) body.Children.Add(new Expander
        {
            Header = "Other diagram types — not yet available",
            Content = unavailable,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        });
        root.Children.Add(new ScrollViewer
        {
            Content = body,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });
        Content = root;
        SizeChanged += (_, _) =>
        {
            mascot.Width = Bounds.Width < 1100 ? 88 : 124;
            mascot.Height = Bounds.Width < 1100 ? 96 : 128;
            var available = Math.Min(1120, Math.Max(1, Bounds.Width - 64 - 32));
            var width = available >= 740 ? Math.Min(292, Math.Floor((available - 48) / 3)) : Math.Min(292, available - 16);
            foreach (var (tile, preview) in previews)
            {
                tile.Width = width;
                preview.Width = Math.Max(1, width - 24);
            }
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && _backButton.IsVisible)
            {
                _back();
                e.Handled = true;
            }
            else if (e.Key == Key.O && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                RequestOpen();
                e.Handled = true;
            }
        };
    }

    public void Show(bool hasDocument)
    {
        _backButton.IsVisible = hasDocument;
        ClearRequest();
        Dispatcher.UIThread.Post(() => _openButton.Focus(), DispatcherPriority.Input);
    }

    private void Request(MermaidDiagramFamily family, MermaidDiagramDialect dialect)
    {
        try
        {
            _requestedSource = MermaidDiagramCatalog.CreateEmpty(family.Id, dialect.Id).Source;
            _openingFile = false;
            if (!_hasChanges()) { Commit(); return; }
            _warning.Text = $"Starting a new {family.DisplayName} will discard the current diagram's unsaved changes and unapplied fields.";
            _discard.Content = "Discard changes and create";
            AutomationProperties.SetName(_discard, "Discard changes and create");
            _confirmation.IsVisible = true;
            _discard.Focus();
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or FormatException)
        {
            _error.Text = error.Message;
            _error.IsVisible = true;
        }
    }

    public void RequestOpen()
    {
        ClearRequest();
        _openingFile = true;
        if (!_hasChanges()) { Commit(); return; }
        _warning.Text = "Opening another file will discard the current diagram's unsaved changes and unapplied fields. Cancelling the file picker keeps your work.";
        _discard.Content = "Discard changes and open…";
        AutomationProperties.SetName(_discard, "Discard changes and open…");
        _confirmation.IsVisible = true;
    }

    private void Commit()
    {
        if (_openingFile) _open();
        else if (_requestedSource is { } source) _create(source);
        ClearRequest();
    }

    private void ClearRequest()
    {
        _requestedSource = null;
        _openingFile = false;
        _confirmation.IsVisible = false;
        _error.IsVisible = false;
    }

    private static Button Button(string label, Action action)
    {
        var button = new Button { Content = label, Margin = new Thickness(0, 0, 10, 0) };
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => action();
        return button;
    }

    private static string Description(string id) => id switch
    {
        "flowchart" => "Processes, decisions and connected steps.",
        "state" => "States and the events that move between them.",
        "class" => "Classes, attributes, methods and UML relationships.",
        "er" => "Entities, attributes, keys and cardinalities.",
        "sequence" => "Participants, messages and ordered interactions.",
        "pie" => "Proportions, donut charts and slice values.",
        "quadrant" => "Position and prioritize points on two axes.",
        "xy" => "Compare numeric bar and line series.",
        "radar" => "Compare curves across named dimensions.",
        "timeline" => "Arrange periods, events and sections.",
        "journey" => "Map tasks, actors and experience scores.",
        "mindmap" => "Build ideas into a visual hierarchy.",
        "treeview" => "Show directory-like trees and annotations.",
        "packet" => "Describe bit fields and packet structure.",
        "kanban" => "Organize cards into workflow columns.",
        "requirement" => "Trace typed requirements and elements.",
        "git" => "Model diagram commits, branches and merges.",
        "sankey" => "Visualize weighted flows between values.",
        "treemap" => "Compare values in a weighted hierarchy.",
        "block" => "Place shapes in scoped, nested grids.",
        "architecture" => "Connect services through explicit ports.",
        "c4" => "Model systems at five architectural levels.",
        "gantt" => "Schedule tasks with explicit temporal rules.",
        "wardley" => "Map visibility, evolution and value chains.",
        "ishikawa" => "Explore causes behind a problem.",
        "cynefin" => "Categorize work into complexity domains.",
        "venn" => "Compare sets and their common overlaps.",
        "railroad" => "Compose grammar rules in four notations.",
        "swimlane" => "Show process steps and their responsibility.",
        "usecase" => "Connect actors with system use cases.",
        "agentflow" => "Compose flows, tools and agent contracts.",
        "eventmodeling" => "Follow information through ordered timeframes.",
        "zenuml" => "Describe nested calls and interactions.",
        _ => "Visual authoring for " + id + ".",
    };
}
