using Avalonia.Controls;
using Avalonia.Layout;

using GuildWarsCompanion.Ava.Shell;

namespace GuildWarsCompanion.Ava.Views;

/// <summary>Stands in for a page the Avalonia front end does not have yet.</summary>
public sealed class NotPortedView : PageView
{
    public NotPortedView(string name)
    {
        Content = new TextBlock
        {
            Text = $"\"{name}\" is not in the Avalonia app yet. The Android app has it.",
            Classes = { "caption" },
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Avalonia.Thickness(24, 40)
        };
    }
}
