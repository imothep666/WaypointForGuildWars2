using GuildWarsCompanion.Data;
using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Host;
using GuildWarsCompanion.Services;
using GuildWarsCompanion.Services.Alerts;
using GuildWarsCompanion.ViewModels;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GuildWarsCompanion;

/// <summary>
/// Everything a front end needs registered that is not a screen, in one list the Android and
/// desktop heads share. Two copies of this were the obvious way for two front ends to drift
/// apart: a service added to one and forgotten in the other only shows when its screen opens.
///
/// What only a platform can provide comes first, from the front end: <see cref="AppEnvironment"/>
/// and the interfaces in Host\. The few platform choices made here — how the bridge is reached,
/// how a reminder is posted — are <c>TryAdd</c> defaults, so a front end that has a better answer
/// registers it before calling this and wins.
/// </summary>
public static class CompanionServices
{
    public static IServiceCollection AddCompanionCore(this IServiceCollection services)
    {
        // One HttpClient and one session for the app's lifetime — the client carries the
        // API key, and the cache is a single SQLite file in app-private storage.
        services.AddSingleton(Gw2Client.CreateHttpClient());
        services.AddSingleton<Gw2Client>();
        // In the client's language, so what the cache keeps and what the client fetches agree.
        services.AddSingleton(provider =>
            new Gw2Cache(
                Path.Combine(Environment(provider).DataDirectory, "gw2cache.db3"),
                provider.GetRequiredService<Gw2Client>().Language));
        services.AddSingleton(provider =>
            new AlertStore(Path.Combine(Environment(provider).DataDirectory, "alerts.db3")));
        services.AddSingleton(provider =>
            new ActivityStore(Path.Combine(Environment(provider).DataDirectory, "activity.db3")));
        services.AddSingleton(provider =>
            new LedgerStore(Path.Combine(Environment(provider).DataDirectory, "ledger.db3")));
        services.AddSingleton<LedgerService>();
        services.AddSingleton(provider =>
            new SnapshotStore(Path.Combine(Environment(provider).DataDirectory, "snapshots.db3")));
        services.AddSingleton<ItemLookup>();
        services.AddSingleton<ItemCatalogue>();
        services.AddSingleton<ItemInspector>();
        services.AddSingleton<OfflineReader>();
        services.AddSingleton<ApiKeySession>();
        services.AddSingleton<AccountDataService>();
        services.AddSingleton<StorageService>();
        services.AddSingleton<VaultService>();
        services.AddSingleton<VaultSignal>();
        services.AddSingleton<BuildChecker>();
        services.AddSingleton<GearAdvisor>();
        services.AddSingleton<CharacterService>();
        services.AddSingleton<StoryService>();
        services.AddSingleton<TradingPostService>();
        services.AddSingleton<WorthService>();
        services.AddSingleton<HomeService>();
        services.AddSingleton<MasteryService>();
        services.AddSingleton<EventService>();
        services.AddSingleton<TodayService>();
        services.AddSingleton<HereService>();
        services.AddSingleton<MetaSource>();
        services.AddSingleton<MetaReminderStore>();
        services.AddSingleton<AchievementService>();
        services.AddSingleton<CraftingService>();
        services.AddSingleton<StandingService>();
        services.AddSingleton<EmoteService>();
        services.AddSingleton<WidgetService>();
        // Beside the databases in the app's private storage. The bridge keeps the same kind of
        // file next to its config; between them they are the only account of what happened
        // during a play session, which is when nobody is watching either screen.
        // The first line of each run says which build wrote it, so a shared log can be read
        // against the right source.
        services.AddSingleton(provider =>
        {
            var environment = Environment(provider);
            var journal = new Bridge.Protocol.Journal(
                Path.Combine(environment.DataDirectory, "companion.log"));
            journal.Write("start", $"app {environment.Version} up");
            return journal;
        });
        // Storage behind an interface so that everything which decides *which* PC to talk to
        // can be driven from a test host — see IBridgeStore for why that mattered.
        services.AddSingleton<IBridgeStore, SettingsBridgeStore>();
        services.AddSingleton<KnownBridges>();
        services.AddSingleton<BridgeFinder>();
        services.AddSingleton<BridgeClient>();

        // The compass stream's own way onto the right network. Separate from IBridgeTransport
        // because SignalR cannot be handed a Java.Net.HttpURLConnection; see IStreamBinding.
        services.TryAddSingleton<IStreamBinding, DefaultStreamBinding>();
        services.AddSingleton<LocalMapNames>();
        services.AddSingleton<GameStream>();
        services.AddSingleton<UnlockService>();
        services.AddSingleton<CompassTargets>();
        services.AddSingleton<MapCatalogue>();
        services.AddSingleton<IRouteChoiceStore, SettingsRouteChoiceStore>();

        // Behind an interface for the reason IBridgeStore is: the deciding in
        // CompassVisibility is worth a test, and the store only stores.
        services.AddSingleton<ICompassVisibilityStore, SettingsCompassVisibilityStore>();
        services.AddSingleton<MapIcons>();

        // The game's own map tiles, and the objective both map screens argue over. Singletons
        // because the tile cache is the app's memory ceiling for that screen and there must be
        // exactly one of it; see Gw2Tiles.
        services.AddSingleton<Gw2Tiles>();
        services.AddSingleton<Objective>();
        services.AddSingleton<MarkerStore>();
        services.AddSingleton<PackRepository>();
        services.AddSingleton<TrailShare>();
        services.TryAddSingleton<IBridgeDiscovery, SocketBridgeDiscovery>();
        services.TryAddSingleton<IBridgeTransport, DefaultBridgeTransport>();

        // Watchers are resolved as a set: adding one here is all it takes for the background
        // poll to start reporting it.
        services.AddSingleton(provider =>
            new WatchStore(Path.Combine(Environment(provider).DataDirectory, "watches.db3")));
        services.AddSingleton<WatchService>();

        services.AddSingleton(provider =>
            new GoalStore(Path.Combine(Environment(provider).DataDirectory, "goals.db3")));
        services.AddSingleton<GoalService>();

        services.AddSingleton<IAccountWatcher, PriceWatcher>();
        services.AddSingleton<IAccountWatcher, WizardsVaultWatcher>();
        services.AddSingleton<IAccountWatcher, TradingPostWatcher>();
        services.AddSingleton<IAccountWatcher, WorldBossWatcher>();
        services.AddSingleton<IAccountWatcher, ResetWatcher>();
        services.AddSingleton<IAccountWatcher, MetaWatcher>();
        services.AddSingleton<IAccountWatcher, SessionWatcher>();
        services.AddSingleton<IAccountWatcher, GoalWatcher>();
        // A no-op outside the debug build; the background poll needs it too, so it is here.
        services.AddSingleton<TestEvidence>();
        services.AddSingleton<NotifySchedule>();
        services.AddSingleton<AlertService>();

        services.TryAddSingleton<INotifier, NullNotifier>();
        services.TryAddSingleton<IWatchScheduler, NullWatchScheduler>();
        services.TryAddSingleton<IWindowMode, NoWindowMode>();
        services.TryAddSingleton<IAppUpdates, NoAppUpdates>();

        services.AddSingleton<WalletViewModel>();
        services.AddSingleton<MasteriesViewModel>();
        services.AddSingleton<EmotesViewModel>();
        services.AddSingleton<MarkerPacksViewModel>();
#if DEBUG
        services.AddSingleton<TestPlanViewModel>();
#endif
        services.AddSingleton<VaultViewModel>();
        services.AddSingleton<CharactersViewModel>();
        services.AddSingleton<TodayViewModel>();
        services.AddSingleton<AchievementsViewModel>();
        services.AddSingleton<CraftingViewModel>();
        services.AddSingleton<StandingViewModel>();
        services.AddSingleton<EventsViewModel>();
        services.AddSingleton<OrdersViewModel>();
        services.AddSingleton<WatchViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<DeliveryViewModel>();
        // Transient: each visit to a character rebinds to a different one.
        services.AddTransient<CharacterViewModel>();
        services.AddTransient<ItemViewModel>();
        services.AddTransient<CollectionViewModel>();
        services.AddSingleton<MaterialsViewModel>();
        services.AddSingleton<FindViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<CompassViewModel>();
        services.AddSingleton<MapViewModel>();
        services.AddSingleton<MapPickerViewModel>();
        services.AddSingleton<UnlocksViewModel>();
        services.AddTransient<UnlockCategoryViewModel>();

        return services;
    }

    private static AppEnvironment Environment(IServiceProvider provider) =>
        provider.GetRequiredService<AppEnvironment>();
}
