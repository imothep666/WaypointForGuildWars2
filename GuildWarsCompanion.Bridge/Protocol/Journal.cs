using System.Text;

namespace GuildWarsCompanion.Bridge.Protocol;

/// <summary>
/// A plain text log that survives the process, for the questions that only appear after hours
/// of playing.
///
/// Everything worth debugging about this pair of apps happens while somebody is in a raid and
/// not looking: a lease renews, a clipboard wedges, a character logs out and the roster keeps
/// insisting otherwise. None of that can be reproduced on demand, and this session found three
/// bugs that were invisible precisely because the evidence was gone by the time anyone looked
/// — a swallowed discovery exception, a paste answered before the clipboard was tried, and a
/// migration that deleted what it was migrating.
///
/// Deliberately not a logging framework. One file, appended, capped, readable in Notepad. The
/// alternative is a dependency and a configuration file for something whose entire job is to
/// still be there tomorrow.
///
/// <b>What must never go in.</b> No API key, no pairing secret, no clipboard contents. The
/// clipboard is the interesting one: what the phone sends is the whole point of the app, and it
/// is also whatever the user happened to copy. Lengths and verdicts, never text.
/// </summary>
public sealed class Journal
{
    /// <summary>
    /// Rolled at a quarter of a megabyte, one generation kept.
    ///
    /// Small on purpose. A log nobody can open in a text editor is a log nobody reads, and half
    /// a megabyte across two files is weeks of ordinary use — the traffic here is a handful of
    /// lines per emote, not a request per frame.
    /// </summary>
    private const long Cap = 256 * 1024;

    /// <summary>
    /// UTF-8 with no byte order mark.
    ///
    /// <c>Encoding.UTF8</c> emits one, and it lands at the start of the file where it is
    /// invisible in an editor and very visible to everything else: it becomes part of the first
    /// line, so grep misses a pattern anchored to the start of it and tail shows a stray glyph.
    /// </summary>
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly string _path;
    private readonly object _gate = new();

    public Journal(string path)
    {
        _path = path;

        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        }
        catch (Exception)
        {
            // Nowhere to write is not worth failing over; Write swallows the rest.
        }
    }

    /// <summary>Where it is, for a window that offers to open it.</summary>
    public string Location => _path;

    /// <summary>
    /// One line, timestamped, with a tag for grepping.
    ///
    /// Locked because requests arrive on socket threads and the game poll runs on a timer, so
    /// two writers is the normal case rather than the exotic one. Never throws: a logger that
    /// can take the app down with it is worse than no logger.
    /// </summary>
    public void Write(string tag, string message)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} {tag,-9} {message}";

        try
        {
            lock (_gate)
            {
                Roll();

                File.AppendAllText(_path, line + Environment.NewLine, Utf8);
            }
        }
        catch (Exception)
        {
            // A full disk, a locked file, a path that vanished. The app carries on.
        }
    }

    /// <summary>
    /// Starts a new file once the current one is big enough, keeping exactly one predecessor.
    ///
    /// The previous generation matters more than it looks: the interesting event is usually the
    /// one just before somebody noticed, and a log that rolled over at the wrong moment without
    /// keeping the old half would lose exactly that.
    /// </summary>
    private void Roll()
    {
        try
        {
            if (!File.Exists(_path) || new FileInfo(_path).Length < Cap) return;

            var previous = _path + ".1";

            if (File.Exists(previous)) File.Delete(previous);

            File.Move(_path, previous);
        }
        catch (Exception)
        {
            // Failing to roll is not a reason to stop logging; the file just grows a little.
        }
    }
}
