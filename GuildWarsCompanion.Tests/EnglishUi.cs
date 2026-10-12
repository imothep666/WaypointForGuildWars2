using System.Globalization;
using System.Runtime.CompilerServices;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Every test sees the app in English, whatever the machine running it is set to.
///
/// The assertions are written against the English text, and the app's words follow the UI
/// culture, so on a German Windows they would come out German and fail for nothing. Number and
/// date formatting stay the machine's: the tests that care about those set CurrentCulture
/// themselves.
/// </summary>
internal static class EnglishUi
{
    [ModuleInitializer]
    internal static void Pin()
    {
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
    }
}
