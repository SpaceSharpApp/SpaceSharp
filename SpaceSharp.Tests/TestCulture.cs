using System.Globalization;
using System.Runtime.CompilerServices;

namespace SpaceSharp.Tests;

/// <summary>Pins the test run to the invariant culture so expected strings do not depend on the developer's Windows settings.</summary>
/// <remarks>
/// The app formats sizes, percentages and dates for the user's culture on purpose, and the resources fall back to English
/// under the invariant culture. Without this, a French Windows turns "1.5 KB" into "1,5 KB" and "Language" into "Langue"
/// and seven tests fail. A test that needs a specific culture sets it on its own thread and restores it in a finally block,
/// as <see cref="LocalizationTests"/> does; the thread setting wins over the default set here.
/// </remarks>
public static class TestCulture
{
    /// <summary>Runs once when the test assembly loads, before xUnit creates its worker threads.</summary>
    [ModuleInitializer]
    public static void Pin()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
    }
}
