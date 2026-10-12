using Avalonia.Markup.Xaml;

using GuildWarsCompanion.Localization;

namespace GuildWarsCompanion.Ava.Ui;

/// <summary>
/// <c>Text="{ui:T 'PRICE WATCH'}"</c>: a view's own words, in the phone's language.
///
/// The English stays in the view, so the markup still reads as the screen does; see
/// <see cref="L"/> for where the German comes from. Resolved once, when the view is built, which
/// is all a language that only changes with the phone's settings needs.
/// </summary>
public sealed class TExtension(string text) : MarkupExtension
{
    public string Text { get; } = text;

    public override object ProvideValue(IServiceProvider serviceProvider) => L.T(Text);
}
