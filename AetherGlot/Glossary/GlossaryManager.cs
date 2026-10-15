namespace AetherGlot.Glossary;

/// <summary>
/// Loads and merges the three dictionaries (user, game data, community) into the
/// <see cref="GlossaryService"/>, and records unresolved words. All loading runs in the background.
/// </summary>
internal sealed class GlossaryManager : IDisposable
{
    public const string UserFileName    = "mydict.json";
    public const string PendingFileName = "pending.json";

    private readonly Configuration config;
    private readonly IDataManager dataManager;
    private readonly IPluginLog log;
    private readonly CommunityGlossaryClient communityClient;
    private readonly Func<ConnectionSettings> connection;
    private readonly string configDirectory;
    private readonly string bundledPath;
    private readonly SemaphoreSlim reloadGate = new(1, 1);
    private readonly CancellationTokenSource disposeCts = new();

    private GameDataResult? gameData;
    private CommunityGlossaryResult? community;

    public GlossaryService Service { get; } = new();
    public UserGlossaryStore User { get; }
    public UnresolvedCandidateStore Pending { get; }
    public AutoTranslateCatalog Catalog => gameData?.Catalog ?? AutoTranslateCatalog.Empty;
    public CommunityGlossaryResult? Community => community;
    public bool IsLoading { get; private set; }

    public GlossaryManager(
        Configuration config, IDataManager dataManager, IPluginLog log, HttpClient httpClient,
        Func<ConnectionSettings> connection, string configDirectory, string assemblyDirectory)
    {
        this.config          = config;
        this.dataManager     = dataManager;
        this.log             = log;
        this.connection      = connection;
        this.configDirectory = configDirectory;
        communityClient      = new CommunityGlossaryClient(httpClient);
        bundledPath          = Path.Combine(assemblyDirectory, CommunityGlossaryClient.CacheFileName);
        User                 = new UserGlossaryStore(Path.Combine(configDirectory, UserFileName));
        Pending              = new UnresolvedCandidateStore(Path.Combine(configDirectory, PendingFileName));
    }

    /// <summary>
    /// Reloads everything. Game data is read once per session; the community dictionary is
    /// downloaded when <paramref name="forceDownload"/> or auto-update is on.
    /// </summary>
    public async Task ReloadAsync(bool forceDownload = false)
    {
        var ct = disposeCts.Token;
        await reloadGate.WaitAsync(ct).ConfigureAwait(false);
        IsLoading = true;
        try
        {
            await Task.Run(() =>
            {
                try { User.Load(); }
                catch (Exception ex) { log.Warning("Could not read {File} ({Type}).", UserFileName, ex.GetType().Name); }
                Pending.Load();

                if (config.Translation.UseGameData && gameData is null)
                {
                    gameData = GameDataGlossaryLoader.Load(dataManager, log);
                    log.Information("Game data glossary: {Terms} terms, {Phrases} auto-translate phrases.",
                                    gameData.Terms.Count, gameData.Catalog.Entries.Count);
                }
            }, ct).ConfigureAwait(false);

            var download = forceDownload || config.Translation.CommunityAutoUpdate;
            community = await communityClient.LoadAsync(
                config.Translation.CommunityGlossaryUrl, configDirectory, bundledPath, connection(), download, ct).ConfigureAwait(false);
            if (community.Error is not null)
                log.Warning("Community dictionary: {Error} (using {Origin}).", community.Error, community.Origin);
            log.Information("Community dictionary: {Count} entries, version {Version}, from {Origin}.",
                            community.Entries.Count, community.Parsed?.Version ?? "-", community.Origin);

            Rebuild();
        }
        catch (OperationCanceledException)
        {
            // Plugin unloading
        }
        catch (Exception ex)
        {
            log.Error("Glossary reload failed ({Type}).", ex.GetType().Name);
        }
        finally
        {
            IsLoading = false;
            reloadGate.Release();
        }
    }

    /// <summary>Rebuilds the merged index from what is loaded (e.g. after editing My dictionary).</summary>
    public void Rebuild()
    {
        var game = config.Translation.UseGameData ? gameData : null;
        Service.Rebuild(
            User.Entries,
            game?.Terms ?? [],
            community?.Entries ?? [],
            new GlossaryStats(
                User.Entries.Count,
                game?.Terms.Count ?? 0,
                community?.Entries.Count ?? 0,
                community?.Parsed?.Version,
                community?.FetchedAt,
                game?.LoadedAt));
    }

    /// <summary>Records unresolved spans of an outcome as "add to dictionary" candidates (background).</summary>
    public void RecordUnresolved(InboundOutcome outcome)
    {
        var spans = outcome.Segments.Where(s => s.Kind == SegmentKind.Unresolved).Select(s => (s.Text, s.Guess)).ToList();
        if (spans.Count == 0) return;
        _ = Task.Run(() =>
        {
            var example = Rehydrator.ToPlainText(Rehydrator.Original(outcome.Request.SourceText, outcome.Request.AutoTranslateTexts));
            if (Pending.Record(spans, example, DateTimeOffset.UtcNow))
                Pending.Save();
        });
    }

    public void Dispose()
    {
        disposeCts.Cancel();
        disposeCts.Dispose();
    }
}
