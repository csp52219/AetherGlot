using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace AetherGlot.Windows;

/// <summary>
/// ③ Inspector: what was sent to the model and what came back, for one translation.
/// Four panels: input (placeholders, auto-translate map, glossary hints), context hints,
/// validation, and the raw attempts. Header values are never shown, only their names.
/// </summary>
public sealed class InspectorWindow : Window, IDisposable, IRelocalizable
{
    private readonly Plugin plugin;

    // Exactly one of these is set
    private LogEntry? entry;
    private OutboundOutcome? outbound;

    private volatile bool retranslating;
    private int selectedAttempt;

    // "Add to dictionary" popup for unresolved words
    private string addSurface = string.Empty;
    private string addMeaning = string.Empty;
    private bool openAddPopup;

    public InspectorWindow(Plugin plugin)
        : base(Title())
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(640, 420), MaximumSize = new Vector2(float.MaxValue, float.MaxValue) };
        Size = new Vector2(900, 640);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Inspect(LogEntry target)
    {
        entry = target;
        outbound = null;
        selectedAttempt = 0;
        IsOpen = true;
    }

    public void Inspect(OutboundOutcome target)
    {
        entry = null;
        outbound = target;
        selectedAttempt = 0;
        IsOpen = true;
    }

    /// <summary>The newest log entry that has a translation result.</summary>
    public bool InspectLatest()
    {
        var latest = plugin.TranslationLog.Snapshot().LastOrDefault(e => e.Outcome is not null);
        if (latest is null) return false;
        Inspect(latest);
        return true;
    }

    public override void Draw()
    {
        if (entry is null && outbound is null)
        {
            ImGui.TextColored(Theme.TextMuted, Loc.Localize("InspectorEmpty", "Nothing to inspect yet. Use \"Inspect\" on a log entry."));
            if (ImGui.Button(Loc.Localize("InspectorLatest", "Inspect the latest translation")))
                InspectLatest();
            return;
        }

        var settings = plugin.BuildConnectionSettings();
        var secrets  = settings.SecretValues().ToList();

        DrawHeader(settings);
        ImGui.Separator();

        var avail = ImGui.GetContentRegionAvail();
        var cell  = new Vector2(0, Math.Max(140, (avail.Y - 12) / 2));
        if (ImGui.BeginTable("##panels", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            Panel("##input", "① " + Loc.Localize("InspectorInput", "Input sent"), cell, DrawInput);
            ImGui.TableNextColumn();
            Panel("##hints", "② " + Loc.Localize("InspectorHints", "Context hints"), cell, DrawHints);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            Panel("##validation", "③ " + Loc.Localize("InspectorValidation", "Validation"), cell, DrawValidation);
            ImGui.TableNextColumn();
            Panel("##attempts", "④ " + Loc.Localize("InspectorAttempts", "Model attempts (raw)"), cell, () => DrawAttempts(settings, secrets));
            ImGui.EndTable();
        }

        DrawAddToDictionaryPopup();
    }

    private static void Panel(string id, string title, Vector2 size, System.Action body)
    {
        if (ImGui.BeginChild(id, size))
        {
            ImGui.TextColored(Theme.Accent, title);
            ImGui.Separator();
            body();
        }
        ImGui.EndChild();
    }

    // ── Header: what this is + re-translate ────────────────────────────

    private void DrawHeader(ConnectionSettings settings)
    {
        if (entry is not null)
        {
            var o = entry.Outcome;
            ImGui.TextColored(Theme.TextMuted, entry.Time.ToString("HH:mm:ss"));
            ImGui.SameLine();
            ImGui.TextColored(ChannelInfo.Color(entry.Group), $"[{ChannelInfo.Tag((XivChatType)entry.Channel)}]");
            ImGui.SameLine();
            ImGui.TextUnformatted(entry.Sender);
            ImGui.SameLine();
            ImGui.TextColored(Theme.TextDim, o is null ? Loc.Localize("LogTranslating", "translating…") : KindLabel(o.Kind) + (o.FromCache ? " · cache" : string.Empty));

            ImGui.SameLine();
            ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), ImGui.GetWindowContentRegionMax().X - 220));
            var canRetry = o is not null && !retranslating;
            var disabled1 = !canRetry;
            if (disabled1) ImGui.BeginDisabled();
            if (ImGui.Button(retranslating ? Loc.Localize("InspectorRetranslating", "Translating…") : Loc.Localize("InspectorRetranslate", "Translate again")))
                Retranslate(entry);
            if (disabled1) ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(Loc.Localize("InspectorRetranslateTip", "Same request, cache bypassed. The log entry is updated."));
            ImGui.SameLine();
            if (ImGui.Button(Loc.Localize("InspectorLatestShort", "Latest")))
                InspectLatest();

            if (o?.Kind == InboundOutcomeKind.Partial)
                DrawUnresolvedPrompt(o);
        }
        else if (outbound is not null)
        {
            ImGui.TextColored(Theme.TextDim, Loc.Localize("InspectorOutbound", "Outbound (send preview)"));
            ImGui.SameLine();
            ImGui.TextColored(outbound.Success ? Theme.Ok : Theme.Warn, outbound.Success ? "OK" : Loc.Localize("InspectorFailed", "failed"));
        }

        // Endpoint and header names; values are never displayed
        ImGui.TextColored(Theme.TextMuted, "POST " + SecretRedactor.Redact(RequestPreview.Endpoint(settings), settings.SecretValues()));
        var headers = RequestPreview.Headers(settings);
        var first = true;
        foreach (var h in headers)
        {
            var text = $"{h.Name}: {h.DisplayValue}";
            if (!first) RichText.SameLineIfFits(text);
            RichText.Chip(text, h.Sensitive ? Theme.TextDim : Theme.TextMuted, Theme.ChipBg);
            first = false;
        }
    }

    private void DrawUnresolvedPrompt(InboundOutcome o)
    {
        var spans = o.Segments.Where(s => s.Kind == SegmentKind.Unresolved).ToList();
        if (spans.Count == 0) return;
        ImGui.TextColored(Theme.Unresolved, Loc.Localize("InspectorUnresolvedPrompt", "Not in any dictionary — register it so next time it is translated:"));
        foreach (var u in spans)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton($"+ {u.Text}##reg{u.Text}"))
            {
                addSurface   = u.Text;
                addMeaning   = u.Guess ?? string.Empty;
                openAddPopup = true;
            }
        }
    }

    private void Retranslate(LogEntry target)
    {
        retranslating = true;
        selectedAttempt = 0;
        _ = Task.Run(async () =>
        {
            try { await plugin.RetranslateAsync(target).ConfigureAwait(false); }
            finally { retranslating = false; }
        });
    }

    // ── ① Input ────────────────────────────────────────────────────────

    private void DrawInput()
    {
        if (entry?.Outcome is { } o)
        {
            Field(Loc.Localize("InspectorPlaceholderized", "Text (auto-translate → {{atN}})"), o.Request.SourceText);
            ImGui.Spacing();
            ImGui.TextColored(Theme.TextDim, Loc.Localize("InspectorAtMap", "Auto-translate map"));
            if (o.Request.AutoTranslateTexts.Count == 0) ImGui.TextColored(Theme.TextMuted, "—");
            foreach (var (id, text) in o.Request.AutoTranslateTexts.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                ImGui.TextColored(Theme.TextMuted, $"{{{{{id}}}}}");
                ImGui.SameLine();
                ImGui.TextColored(Theme.AutoTranslate, text);
            }
            if (entry.Message.AutoTranslateParts.Count > 0)
            {
                var ids = string.Join(", ", entry.Message.AutoTranslateParts.Select(p => $"{p.Group}:{p.Key}"));
                ImGui.TextColored(Theme.TextMuted, "IDs: " + ids);
            }
            ImGui.Spacing();
            DrawGlossaryHints(o.Request.Glossary, Loc.Localize("InspectorGlossary", "Glossary pairs sent"));
            ImGui.Spacing();
            ImGui.TextColored(Theme.TextDim, Loc.Localize("InspectorTarget", "Target language"));
            ImGui.SameLine();
            ImGui.TextUnformatted($"{o.Request.TargetLanguage} ({o.Request.TargetLanguageCode})");
        }
        else if (outbound is { } ob)
        {
            Field(Loc.Localize("InspectorSource", "Text"), ob.Request.SourceText);
            ImGui.TextColored(Theme.TextDim, $"{ob.Request.SourceLanguage} → {ob.Request.TargetLanguage} · {ob.Request.Tone}");
            ImGui.Spacing();
            DrawGlossaryHints(ob.Request.FixedTerms, Loc.Localize("InspectorFixedTerms", "Fixed terms sent"));
        }
        else
        {
            ImGui.TextColored(Theme.TextMuted, Loc.Localize("LogTranslating", "translating…"));
        }
    }

    private static void DrawGlossaryHints(IReadOnlyList<GlossaryHint>? hints, string title)
    {
        ImGui.TextColored(Theme.TextDim, title);
        if (hints is null || hints.Count == 0)
        {
            ImGui.TextColored(Theme.TextMuted, Loc.Localize("InspectorNone", "none"));
            return;
        }
        foreach (var h in hints)
        {
            var sense = h.Targets.Count > 1 ? $"  ({Loc.Localize("InspectorModelChooses", "model chooses")})" : string.Empty;
            ImGui.TextColored(Theme.TermChip, h.Source);
            ImGui.SameLine();
            ImGui.TextUnformatted("→ " + string.Join(" / ", h.Targets) + sense);
            if (h.Note is { Length: > 0 } note)
            {
                ImGui.SameLine();
                ImGui.TextColored(Theme.TextMuted, note);
            }
        }
    }

    // ── ② Context hints ────────────────────────────────────────────────

    private void DrawHints()
    {
        var hints = entry?.Outcome?.Request.Hints ?? outbound?.Request.Hints;
        if (hints is null)
        {
            ImGui.TextColored(Theme.TextMuted, Loc.Localize("InspectorHintsOff", "No context hints were sent (off in AI / Translation settings, or not used for this request)."));
            return;
        }

        Row(Loc.Localize("InspectorInCombat", "In combat"), hints.InCombat switch { true => "yes", false => "no", null => "—" });
        Row(Loc.Localize("InspectorLocation", "Location"), hints.Location ?? "—");
        Row(Loc.Localize("InspectorPartyJobs", "Party jobs"), hints.PartyJobs is { Count: > 0 } jobs ? string.Join(", ", jobs) : "—");
        ImGui.TextColored(Theme.TextDim, Loc.Localize("InspectorRecent", "Recent lines"));
        if (hints.RecentLines is not { Count: > 0 } lines)
            ImGui.TextColored(Theme.TextMuted, "—");
        else
            foreach (var line in lines) ImGui.TextWrapped(line);
    }

    // ── ③ Validation ───────────────────────────────────────────────────

    private void DrawValidation()
    {
        var v = entry?.Outcome?.Validation ?? outbound?.Validation;
        var failure = entry?.Outcome?.FailureReason ?? outbound?.FailureReason;

        if (entry?.Outcome is { } o)
        {
            Row(Loc.Localize("InspectorResult", "Result"), KindLabel(o.Kind));
            if (!string.IsNullOrEmpty(o.DetectedLanguage)) Row(Loc.Localize("InspectorDetected", "Detected language"), o.DetectedLanguage);
        }

        if (v is null)
        {
            ImGui.TextColored(Theme.TextMuted, Loc.Localize("InspectorNoValidation", "No response was validated (the call failed or the result came from somewhere else)."));
        }
        else
        {
            Check(Loc.Localize("InspectorSchema", "JSON schema"), v.SchemaOk, v.SchemaError);
            Check(Loc.Localize("InspectorRefusal", "No refusal / explanation"), !v.RefusalDetected, null);
            Check(Loc.Localize("InspectorLength", "Length plausible"), !v.TooLong, null);
            Check(Loc.Localize("InspectorPlaceholders", "Placeholders kept"), v.PlaceholdersPreserved && v.UnknownPlaceholders.Count == 0,
                  string.Join(" ", v.MissingPlaceholders.Select(p => "-" + p).Concat(v.UnknownPlaceholders.Select(p => "+" + p))));
            Row(Loc.Localize("InspectorUnresolved", "Unresolved spans"), v.UnresolvedCount.ToString());
        }

        if (failure is not null)
        {
            ImGui.Spacing();
            ImGui.TextColored(Theme.Warn, Loc.Localize("InspectorWhyFallback", "Why the original is shown:"));
            ImGui.TextWrapped(failure);
        }

        var terms = entry?.Outcome?.Response?.Terms ?? outbound?.Response?.Terms;
        if (terms is { Count: > 0 })
        {
            ImGui.Spacing();
            ImGui.TextColored(Theme.TextDim, Loc.Localize("InspectorTerms", "Terms reported by the model"));
            foreach (var t in terms)
            {
                var conf = t.Conf is { } c ? $" {c * 100:0}%" : string.Empty;
                var from = t.From is { } f ? $" [{f}]" : string.Empty;
                var alts = t.Alts.Count > 0 ? $"  alt: {string.Join(", ", t.Alts)}" : string.Empty;
                ImGui.TextUnformatted($"{t.Src} → {t.Dst}{conf}{from}{alts}");
            }
        }
    }

    // ── ④ Attempts ─────────────────────────────────────────────────────

    private void DrawAttempts(ConnectionSettings settings, IReadOnlyList<string> secrets)
    {
        var attempts = entry?.Outcome?.Attempts ?? outbound?.Attempts ?? [];
        var systemPrompt = entry?.Outcome?.SystemPrompt ?? outbound?.SystemPrompt ?? string.Empty;
        var userPrompt   = entry?.Outcome?.UserPrompt ?? outbound?.UserPrompt ?? string.Empty;

        if (entry?.Outcome is { FromCache: true })
            ImGui.TextColored(Theme.TextMuted, Loc.Localize("InspectorFromCache", "Served from the cache; the attempts below are from the original call."));

        if (attempts.Count == 0)
        {
            ImGui.TextColored(Theme.TextMuted, Loc.Localize("InspectorNoAttempts", "No model call was made."));
        }
        else
        {
            selectedAttempt = Math.Clamp(selectedAttempt, 0, attempts.Count - 1);
            for (var i = 0; i < attempts.Count; i++)
            {
                if (i > 0) ImGui.SameLine();
                var selected = i == selectedAttempt;
                if (selected) ImGui.PushStyleColor(ImGuiCol.Button, Theme.Accent with { W = 0.45f });
                if (ImGui.SmallButton($"#{i + 1}##attempt{i}")) selectedAttempt = i;
                if (selected) ImGui.PopStyleColor();
            }

            var a = attempts[selectedAttempt];
            var tokens = a.PromptTokens is null && a.CompletionTokens is null ? "—" : $"{a.PromptTokens?.ToString() ?? "?"} → {a.CompletionTokens?.ToString() ?? "?"}";
            Row(Loc.Localize("InspectorModel", "Model"), a.Model);
            Row(Loc.Localize("InspectorTokens", "Tokens (in → out)"), tokens);
            Row(Loc.Localize("InspectorElapsed", "Elapsed"), $"{a.Elapsed.TotalSeconds:0.00}s");
            Row(Loc.Localize("InspectorJsonMode", "JSON mode"), a.JsonModeUsed ? "response_format" : Loc.Localize("InspectorPromptOnly", "prompt only"));
            if (a.Error is { } error)
                ImGui.TextColored(Theme.Warn, SecretRedactor.Redact(error, secrets));
            ReadOnlyBlock("##raw", SecretRedactor.Redact(PrettyJson(a.RawContent), secrets), 120);
        }

        if (ImGui.CollapsingHeader(Loc.Localize("InspectorPrompts", "Prompts") + "##prompts"))
        {
            ImGui.TextColored(Theme.TextDim, "system");
            ReadOnlyBlock("##system", SecretRedactor.Redact(systemPrompt, secrets), 100);
            ImGui.TextColored(Theme.TextDim, "user");
            ReadOnlyBlock("##user", SecretRedactor.Redact(userPrompt, secrets), 100);
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static string KindLabel(InboundOutcomeKind kind) => kind switch
    {
        InboundOutcomeKind.Translated => Loc.Localize("KindTranslated", "translated"),
        InboundOutcomeKind.Partial    => Loc.Localize("LogPartial", "partly unresolved"),
        InboundOutcomeKind.Skipped    => Loc.Localize("LogSkipped", "skipped"),
        _                             => Loc.Localize("LogOriginal", "original"),
    };

    private static void Row(string label, string value)
    {
        ImGui.TextColored(Theme.TextDim, label);
        ImGui.SameLine(170);
        ImGui.TextUnformatted(value);
    }

    private static void Check(string label, bool ok, string? detail)
    {
        ImGui.TextColored(ok ? Theme.Ok : Theme.Error, ok ? "✓" : "✗");
        ImGui.SameLine();
        ImGui.TextUnformatted(label);
        if (!ok && !string.IsNullOrWhiteSpace(detail))
        {
            ImGui.SameLine();
            ImGui.TextColored(Theme.TextMuted, detail);
        }
    }

    private static void Field(string label, string value)
    {
        ImGui.TextColored(Theme.TextDim, label);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Text);
        ImGui.TextWrapped(value);
        ImGui.PopStyleColor();
    }

    // Read-only, selectable text (copy with Ctrl+C)
    private static void ReadOnlyBlock(string id, string text, float height)
    {
        var copy = text;
        ImGui.InputTextMultiline(id, ref copy, Math.Max(256, Encoding.UTF8.GetByteCount(text) + 1), new Vector2(-1, height), ImGuiInputTextFlags.ReadOnly);
    }

    private static string PrettyJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
        }
        catch (JsonException)
        {
            return raw;
        }
    }

    private void DrawAddToDictionaryPopup()
    {
        var popupId = Loc.Localize("AddDictTitle", "Add to my dictionary") + "##inspectoradd";
        if (openAddPopup)
        {
            ImGui.OpenPopup(popupId);
            openAddPopup = false;
        }
        if (!ImGui.BeginPopupModal(popupId, ImGuiWindowFlags.AlwaysAutoResize)) return;

        ImGui.TextUnformatted(addSurface);
        ImGui.SetNextItemWidth(320);
        ImGui.InputTextWithHint("##meaning", Loc.Localize("AddDictMeaning", "Meaning"), ref addMeaning, 200);
        var canSave = !string.IsNullOrWhiteSpace(addMeaning);
        var disabled2 = !canSave;
        if (disabled2) ImGui.BeginDisabled();
        if (ImGui.Button(Loc.Localize("AddDictSave", "Save")))
        {
            plugin.AddToMyDictionary(addSurface, addMeaning.Trim());
            ImGui.CloseCurrentPopup();
        }
        if (disabled2) ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button(Loc.Localize("AddDictCancel", "Cancel")))
            ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    private static string Title() => Loc.Localize("InspectorTitle", "AetherGlot — Inspector") + "###AetherGlotInspector";

    /// <summary>Re-reads the localized title after the Dalamud language changes.</summary>
    public void Relocalize() => WindowName = Title();

    public void Dispose() { }
}
