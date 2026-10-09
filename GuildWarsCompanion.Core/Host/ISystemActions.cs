namespace GuildWarsCompanion.Host;

/// <summary>
/// Handing something to the rest of the device: a file to the share sheet, text to the
/// clipboard, a link to the browser.
/// </summary>
/// <summary>Asks the platform to redraw its home screen widgets, where it has any.</summary>
public interface IWidgetRedraw
{
    void Redraw();
}

public interface ISystemActions
{
    Task ShareFileAsync(string title, string path);

    Task CopyAsync(string text);

    Task OpenLinkAsync(string url);
}
