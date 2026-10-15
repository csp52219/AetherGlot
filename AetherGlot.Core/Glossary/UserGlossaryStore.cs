namespace AetherGlot.Core.Glossary;

/// <summary>The user's own dictionary, stored as a glossary JSON file. Thread-safe.</summary>
public sealed class UserGlossaryStore
{
    private readonly string path;
    private readonly object gate = new();
    private List<TermEntry> entries = [];

    public UserGlossaryStore(string path) => this.path = path;

    public string FilePath => path;

    /// <summary>Errors from the last load (invalid entries are skipped, never deleted).</summary>
    public IReadOnlyList<string> LoadErrors { get; private set; } = [];

    public IReadOnlyList<TermEntry> Entries
    {
        get { lock (gate) return entries.ToList(); }
    }

    public void Load()
    {
        if (!File.Exists(path))
        {
            lock (gate) entries = [];
            LoadErrors = [];
            return;
        }
        var parsed = GlossaryFile.Parse(File.ReadAllText(path), TermSource.User);
        lock (gate) entries = parsed.Entries.ToList();
        LoadErrors = parsed.Errors;
    }

    /// <summary>Adds or replaces (by id) an entry and saves.</summary>
    public void Upsert(TermEntry entry)
    {
        entry.Source = TermSource.User;
        lock (gate)
        {
            entries.RemoveAll(e => e.Id == entry.Id);
            entries.Add(entry);
            Save();
        }
    }

    public bool Remove(string id)
    {
        lock (gate)
        {
            var removed = entries.RemoveAll(e => e.Id == id) > 0;
            if (removed) Save();
            return removed;
        }
    }

    // Caller holds the lock
    private void Save()
    {
        // Never overwrite a file we could not fully parse: keep a backup first
        if (LoadErrors.Count > 0 && File.Exists(path))
            File.Copy(path, path + ".bak", overwrite: true);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, GlossaryFile.Serialize(entries, "My dictionary"));
        File.Move(tmp, path, overwrite: true);
        LoadErrors = [];
    }
}
