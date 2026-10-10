using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Merpeeps.App;

public sealed class CommandMenuItem : MenuItem
{
    protected override Type StyleKeyOverride => typeof(MenuItem);
    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);
    private sealed class Peer(CommandMenuItem owner) : ControlAutomationPeer(owner), IInvokeProvider
    {
        protected override string GetNameCore() =>
            AutomationProperties.GetName(owner) ?? owner.Header?.ToString()?.Replace("_", "") ?? "";
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.MenuItem;
        public void Invoke()
        {
            if (!owner.IsEffectivelyEnabled)
                throw new InvalidOperationException("This menu command is unavailable.");
            Dispatcher.UIThread.Post(() =>
            {
                if (owner.IsEffectivelyEnabled) owner.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            }, DispatcherPriority.Input);
        }
    }
}
