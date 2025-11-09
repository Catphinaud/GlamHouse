using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;

namespace GlamHouse;

internal sealed class GlamHouseWindow : Window
{
    private static readonly (TargetScope Scope, string Label)[] ScopeOptions =
    {
        (TargetScope.Player, "Nearby Players"),
        (TargetScope.Party, "Party"),
        (TargetScope.Npc, "Nearby NPCs"),
        (TargetScope.All, "Everyone Nearby"),
    };

    private static readonly (Race Race, string Label)[] RaceOptions =
    {
        (Race.Unknown, "Any"),
        (Race.Hyur, "Hyur"),
        (Race.Elezen, "Elezen"),
        (Race.Lalafell, "Lalafell"),
        (Race.Miqote, "Miqo'te"),
        (Race.Roegadyn, "Roegadyn"),
        (Race.AuRa, "Au Ra"),
        (Race.Hrothgar, "Hrothgar"),
        (Race.Viera, "Viera"),
    };

    private static readonly (Gender Gender, string Label)[] GenderOptions =
    {
        (Gender.Unknown, "Any"),
        (Gender.Male, "Male"),
        (Gender.Female, "Female"),
    };

    private static readonly (DefaultNoArgBehavior Behavior, string Label)[] DefaultBehaviorOptions =
    {
        (DefaultNoArgBehavior.OpenUi, "Open UI (Default)"),
        (DefaultNoArgBehavior.Party, "Party"),
        (DefaultNoArgBehavior.Everyone, "Everyone"),
    };

    private readonly IDalamudPluginInterface _pluginInterface;

    private DateTime _nextIpcCheck = DateTime.MinValue;
    private DateTime _lastStatusUpdate = DateTime.MinValue;
    private bool _isIpcAvailable;
    private bool _isIpcValid;

    private TargetScope _selectedScope = TargetScope.Player;
    private Race _selectedRace = Race.Unknown;
    private Gender _selectedGender = Gender.Unknown;

    // Editing/commit-mode state for presets
    private string _editingPresetId = string.Empty; // Guid string in "N" format
    private string _editingName = string.Empty;
    private string _editingCommand = string.Empty;
    private TargetScope _editingScope = TargetScope.All;
    private Dictionary<Race, GenderSelection> _editingSelections = new();
    private bool _editingDirty = false;
    private string _presetValidationError = string.Empty;

    private static readonly HashSet<string> ReservedTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        // core commands
        "help","ui","window","config","revert","reset","undo","r",
        // scopes
        "party","npc","players","all",
        // gender words
        "male","man","female","fem",
        // common race tokens / aliases (from Plugin.OnCommand)
        "hyur","bot","elezen","elf","tall","lalafell","lala","short",
        "miqote","miqo'te","miqo","kitty","roegadyn","roe","big","aura","au'ra","lizzy","hrothgar","hrogh","dog","viera","rabbit","bun"
    };

    private void StartEditing(RaceGenderPreset preset)
    {
        _editingPresetId = preset.Id.ToString("N");
        _editingName = preset.Name;
        _editingCommand = preset.Command;
        _editingScope = preset.Scope;
        _editingSelections = preset.Selections.ToDictionary(kv => kv.Key, kv => new GenderSelection { Male = kv.Value.Male, Female = kv.Value.Female });
        _editingDirty = false;
        _presetValidationError = string.Empty;
    }

    private void StopEditing(bool save)
    {
        if (string.IsNullOrEmpty(_editingPresetId)) return;

        var preset = Plugin.Config.Presets.FirstOrDefault(p => p.Id.ToString("N") == _editingPresetId);
        if (preset != null && save) {
            // apply buffer to actual preset
            preset.Name = _editingName.Trim();
            preset.Command = _editingCommand.Trim();
            preset.Scope = _editingScope;
            preset.Selections = new Dictionary<Race, GenderSelection>(_editingSelections);
            Plugin.Config.Save();
        }

        _editingPresetId = string.Empty;
        _editingDirty = false;
        _presetValidationError = string.Empty;
    }

    private bool ValidateEditing(RaceGenderPreset currentPreset)
    {
        _presetValidationError = string.Empty;

        var name = _editingName.Trim();
        var cmd = _editingCommand.Trim();

        if (string.IsNullOrWhiteSpace(name)) {
            _presetValidationError = "Name cannot be empty.";
            return false;
        }

        // duplicate name
        if (Plugin.Config.Presets.Any(p => p.Id.ToString("N") != currentPreset.Id.ToString("N") && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))) {
            _presetValidationError = "A preset with this name already exists.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(cmd)) {
            _presetValidationError = "Command cannot be empty.";
            return false;
        }

        // reserved tokens
        if (ReservedTokens.Contains(cmd.ToLowerInvariant())) {
            _presetValidationError = $"'{cmd}' is a reserved command or alias and cannot be used.";
            return false;
        }

        // duplicate command
        if (Plugin.Config.Presets.Any(p => p.Id.ToString("N") != currentPreset.Id.ToString("N") && string.Equals(p.Command, cmd, StringComparison.OrdinalIgnoreCase))) {
            _presetValidationError = "A preset with this command already exists.";
            return false;
        }

        return true;
    }

    public GlamHouseWindow(IDalamudPluginInterface pluginInterface) : base("GlamHouse")
    {
        _pluginInterface = pluginInterface;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(675f, 450f),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        RespectCloseHotkey = true;

        CheckIpcStatus(force: true);
    }

    public override void Draw()
    {
        CheckIpcStatus();

        if (!_isIpcAvailable) {
            ImGui.TextColored(ImGuiColors.DalamudYellow,
                "No Glamourer IPC connection. Last checked " +
                $"{(int) (DateTime.UtcNow - _lastStatusUpdate).TotalSeconds}s ago."
            );

            ImGui.Spacing();

            return;
        }

        using var tabmenu = ImRaii.TabBar("GlamHouseTabMenu");

        using (var tab = ImRaii.TabItem("Actions")) {
            if (tab.Success) {
                DrawQuickActions();
            }
        }

        using (var tab = ImRaii.TabItem("Advanced")) {
            if (tab.Success) {
                DrawAdvancedMode();
            }
        }

        using (var tab = ImRaii.TabItem("Config")) {
            if (tab.Success) {
                DrawConfigSection();
                ImGui.Separator();
                DrawPresetsSection();
            }
        }

        using (var tab = ImRaii.TabItem("Status")) {
            if (tab.Success) {
                DrawIpcStatus();
            }
        }
    }

    private void DrawIpcStatus()
    {
        var statusColor = _isIpcAvailable ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed;
        var statusText = _isIpcAvailable ? "Available" : "Unavailable";

        ImGui.TextColored(statusColor, $"Glamourer IPC: {statusText}");

        if (!_isIpcValid) {
            ImGui.TextColored(ImGuiColors.DalamudYellow, "IPC signatures are not ready.");
        }
    }

    private void DrawConfigSection()
    {
        ImGui.SetNextItemWidth(260f);

        using var combo = ImRaii.Combo("/glamhouse with no sub-commands", GetDefaultBehaviorLabel(Plugin.Config.DefaultNoArgBehavior));
        if (!combo.Success) return;
        foreach (var (behavior, label) in DefaultBehaviorOptions) {
            var selected = behavior == Plugin.Config.DefaultNoArgBehavior;
            if (ImGui.Selectable(label, selected)) {
                Plugin.Config.DefaultNoArgBehavior = behavior;
                Plugin.Config.Save();
            }

            if (selected) ImGui.SetItemDefaultFocus();
        }
    }

    private string _selectedPresetCommand = string.Empty;

    private void DrawPresetsSection()
    {
        var anyDirty = !string.IsNullOrEmpty(_editingPresetId) && _editingDirty;
        if (anyDirty) {
            ImGui.TextColored(ImGuiColors.DalamudYellow, "Presets (Unsaved Changes)");
        } else {
            ImGui.Text("Presets");
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("+ Add Preset##AddPreset")) {
            var preset = new RaceGenderPreset
            {
                Name = "New Preset",
                Command = $"preset-{Guid.NewGuid().ToString("N")[..8]}",
            };
            Plugin.Config.Presets.Add(preset);
            Plugin.Config.Save();
        }

        if (Plugin.Config.Presets.Count == 0) {
            ImGui.TextDisabled("No presets yet. Click '+ Add Preset' to create one.");
            return;
        }

        ImGui.Spacing();

        foreach (var preset in Plugin.Config.Presets.ToList()) {
            var selected = _selectedPresetCommand == preset.Id.ToString("N");

            // Use the preset's "N" format ID to avoid duplicate header issues
            bool open = ImGui.CollapsingHeader($"{preset.Name}##{preset.Id.ToString("N")}", selected ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None);

            ImGui.PushID(preset.Id.ToString("N"));

            if (open) {
                _selectedPresetCommand = preset.Id.ToString("N");

                // If starting to edit a new preset, initialize buffer
                if (_editingPresetId != preset.Id.ToString("N")) {
                    StartEditing(preset);
                }

                // Draw buffered inputs
                if (_editingPresetId == preset.Id.ToString("N")) {
                    string name = _editingName;
                    if (ImGui.InputText("Name##PresetName", ref name, 128)) {
                        _editingName = name;
                        _editingDirty = true;
                    }

                    string command = _editingCommand;
                    if (ImGui.InputText("Command (used in /glamhouse)##PresetCommand", ref command, 64)) {
                        _editingCommand = command.Trim();
                        _editingDirty = true;
                    }

                    ImGui.TextDisabled($"Id: {preset.Id}");

                    ImGui.Spacing();

                    // Scope selector (buffered)
                    ImGui.SetNextItemWidth(200f);
                    using (var scopeCombo = ImRaii.Combo($"Scope##PresetScope-{preset.Id}", GetScopeLabel(_editingScope))) {
                        if (scopeCombo.Success) {
                            foreach (var (scope, label) in ScopeOptions) {
                                var selScope = scope == _editingScope;
                                if (ImGui.Selectable(label, selScope)) {
                                    _editingScope = scope;
                                    _editingDirty = true;
                                }

                                if (selScope) ImGui.SetItemDefaultFocus();
                            }
                        }
                    }

                    ImGui.Spacing();

                    // If dirty, show Save/Cancel and hide apply buttons
                    if (_editingDirty) {
                        if (!string.IsNullOrEmpty(_presetValidationError)) {
                            ImGui.TextColored(ImGuiColors.DalamudRed, _presetValidationError);
                        }

                        if (ImGui.Button($"Save##SavePreset-{preset.Id}")) {
                            if (ValidateEditing(preset)) {
                                StopEditing(save: true);
                            }
                        }

                        ImGui.SameLine();
                        if (ImGui.Button($"Cancel##CancelPreset-{preset.Id}")) {
                            StopEditing(save: false);
                        }
                    } else {
                        // Apply buttons (disabled globally if IPC unavailable)
                        using (ImRaii.Disabled(!_isIpcAvailable)) {
                            var applyLabel = $"Apply Preset ({GetScopeLabel(preset.Scope)})##ApplyPreset-{preset.Id}";
                            if (ImGui.Button(applyLabel)) {
                                Plugin.Apply(new FilterInput { Scope = preset.Scope, Preset = preset });
                            }

                            ImGui.SameLine();
                            if (ImGui.Button($"Apply to Nearby Players##ApplyPlayers-{preset.Id}")) {
                                Plugin.Apply(new FilterInput { Scope = TargetScope.Player, Preset = preset });
                            }

                            ImGui.SameLine();
                            if (ImGui.Button($"Apply to Party##ApplyParty-{preset.Id}")) {
                                Plugin.Apply(new FilterInput { Scope = TargetScope.Party, Preset = preset });
                            }

                            ImGui.SameLine();
                            if (ImGui.Button($"Apply to Everyone Nearby##ApplyAll-{preset.Id}")) {
                                Plugin.Apply(new FilterInput { Scope = TargetScope.All, Preset = preset });
                            }
                        }
                    }

                    ImGui.Spacing();
                    ImGui.Separator();
                    ImGui.Text("Per-Race Gender Selection");

                    using (var table = ImRaii.Table($"PresetRaceTable-{preset.Id}", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingFixedFit)) {
                        if (table.Success) {
                            ImGui.TableSetupColumn("Race", ImGuiTableColumnFlags.WidthFixed, 140f);
                            ImGui.TableSetupColumn("Both", ImGuiTableColumnFlags.WidthFixed, 60f);
                            ImGui.TableSetupColumn("Male", ImGuiTableColumnFlags.WidthFixed, 60f);
                            ImGui.TableSetupColumn("Female", ImGuiTableColumnFlags.WidthFixed, 60f);
                            ImGui.TableHeadersRow();

                            foreach (var (race, label) in RaceOptions) {
                                using var id = ImRaii.PushId($"{race}-{preset.Id}");
                                if (race == Race.Unknown) continue; // skip 'Any'
                                ImGui.TableNextRow();

                                ImGui.TableNextColumn();
                                ImGui.TextUnformatted(label);

                                var sel = _editingSelections.TryGetValue(race, out var current)
                                    ? current
                                    : new GenderSelection { Male = false, Female = false };

                                // Both
                                ImGui.TableNextColumn();
                                // ReSharper disable once MergeIntoPattern
                                bool both = sel.Male && sel.Female;
                                if (ImGui.Checkbox("##Both", ref both)) {
                                    sel.Male = both;
                                    sel.Female = both;
                                    _editingSelections[race] = sel;
                                    _editingDirty = true;
                                }

                                // Male
                                ImGui.TableNextColumn();
                                bool male = sel.Male;
                                if (ImGui.Checkbox("##Male", ref male)) {
                                    sel.Male = male;
                                    _editingSelections[race] = sel;
                                    _editingDirty = true;
                                }

                                // Female
                                ImGui.TableNextColumn();
                                bool female = sel.Female;
                                if (ImGui.Checkbox("##Female", ref female)) {
                                    sel.Female = female;
                                    _editingSelections[race] = sel;
                                    _editingDirty = true;
                                }
                            }
                        }
                    }

                    ImGui.Spacing();

                    ImGui.PushStyleColor(ImGuiCol.Button, ImGuiColors.DalamudRed);
                    if (ImGui.Button($"Delete Preset##DeletePreset-{preset.Id}")) {
                        Plugin.Config.Presets.Remove(preset);
                        Plugin.Config.Save();
                        ImGui.PopStyleColor();
                        ImGui.PopID();
                        break;
                    }

                    ImGui.PopStyleColor();
                } else {
                    // Not expected but show fallback: show live preset values if buffer mismatch
                    ImGui.TextUnformatted(preset.Name);
                }
            }

            ImGui.PopID();
            ImGui.Spacing();
        }
    }

    private void DrawQuickActions()
    {
        using var _ = ImRaii.Disabled(!_isIpcAvailable);

        if (ImGui.Button("Players Nearby###PlayersNearby")) {
            Plugin.Apply(new FilterInput { Scope = TargetScope.Player });
        }

        if (ImGui.Button("Party Members###PartyMembers")) {
            Plugin.Apply(new FilterInput { Scope = TargetScope.Party });
        }

        if (ImGui.Button("Nearby NPCs###NearbyNpcs")) {
            Plugin.Apply(new FilterInput { Scope = TargetScope.Npc });
        }

        if (ImGui.Button("Everyone###EveryoneNearby")) {
            Plugin.Apply(new FilterInput { Scope = TargetScope.All });
        }

        if (ImGui.IsItemHovered()) {
            ImGui.SetTooltip("Apply to both players and NPCs around you.");
        }


        using (ImRaii.Disabled(Plugin.Tracker.Count == 0)) {
            if (ImGui.Button("Reset Glamours")) {
                Plugin.ResetAll();
            }
        }
    }

    private void DrawAdvancedMode()
    {
        ImGui.Spacing();

        DrawScopeCombo();
        DrawGenderCombo();
        DrawRaceCombo();

        ImGui.Spacing();

        using (ImRaii.Disabled(!_isIpcAvailable)) {
            if (ImGui.Button("Apply Advanced Selection")) {
                var input = new FilterInput
                {
                    Scope = _selectedScope,
                    Race = _selectedRace,
                    Gender = _selectedGender
                };

                Plugin.Apply(input);
            }
        }

        ImGui.Spacing();
        DrawTrackedEntries();
    }

    private void DrawScopeCombo()
    {
        ImGui.SetNextItemWidth(200f);

        using var combo = ImRaii.Combo("Target Scope", GetScopeLabel(_selectedScope));

        if (!combo.Success) return;

        foreach (var (scope, label) in ScopeOptions) {
            var selected = scope == _selectedScope;
            if (ImGui.Selectable(label, selected)) {
                _selectedScope = scope;
            }

            if (selected) {
                ImGui.SetItemDefaultFocus();
            }
        }
    }

    private void DrawGenderCombo()
    {
        ImGui.SetNextItemWidth(200f);

        using var combo = ImRaii.Combo("Gender", GetGenderLabel(_selectedGender));

        if (!combo.Success) return;

        foreach (var (gender, label) in GenderOptions) {
            var selected = gender == _selectedGender;
            if (ImGui.Selectable(label, selected)) {
                _selectedGender = gender;
            }

            if (selected) {
                ImGui.SetItemDefaultFocus();
            }
        }
    }

    private void DrawRaceCombo()
    {
        ImGui.SetNextItemWidth(200f);

        using var combo = ImRaii.Combo("Race", GetRaceLabel(_selectedRace));

        if (!combo.Success) return;

        foreach (var (race, label) in RaceOptions) {
            var selected = race == _selectedRace;

            if (ImGui.Selectable(label, selected)) {
                _selectedRace = race;
            }

            if (selected) {
                ImGui.SetItemDefaultFocus();
            }
        }
    }

    private void DrawTrackedEntries()
    {
        if (Plugin.Tracker.Count == 0) {
            ImGui.TextDisabled("No tracked entries yet. Use a quick action to apply glamour.");
            return;
        }

        var entries = Plugin.Tracker.All().OrderBy(entry => entry.AppliedAt).ThenBy(entry => entry.Name).ToList();

        ImGui.Text($"Tracked Glamours: {entries.Count}");

        using var table = ImRaii.Table(
            "##TrackedEntries",
            6,
            ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingFixedFit,
            new Vector2(-1, 160f)
        );

        if (!table.Success) return;

        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 140f);
        ImGui.TableSetupColumn("Race", ImGuiTableColumnFlags.WidthFixed, 80f);
        ImGui.TableSetupColumn("Gender", ImGuiTableColumnFlags.WidthFixed, 60f);
        ImGui.TableSetupColumn("Kind", ImGuiTableColumnFlags.WidthFixed, 80f);
        ImGui.TableSetupColumn("Applied", ImGuiTableColumnFlags.WidthFixed, 110f);
        ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed, 70f);
        ImGui.TableHeadersRow();

        foreach (var entry in entries) {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(entry.Name);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(GetRaceLabel(entry.Race));

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(GetGenderLabel(entry.Gender));

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(entry.Kind.ToString());

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(FormatAppliedAt(entry.AppliedAt));

            ImGui.TableNextColumn();
            ImGui.BeginDisabled(!_isIpcAvailable);
            ImGui.PushID(entry.ObjectIndex);
            if (ImGui.SmallButton("Revert")) {
                Plugin.RevertTracked(entry.ObjectIndex);
            }

            ImGui.PopID();
            ImGui.EndDisabled();
        }
    }

    private void CheckIpcStatus(bool force = false)
    {
        var now = DateTime.UtcNow;
        if (!force && now < _nextIpcCheck) {
            return;
        }

        GlamourerInteropt.RefreshStatus(_pluginInterface);

        _isIpcAvailable = GlamourerInteropt.IsAvailable();
        _isIpcValid = GlamourerInteropt.IsIPCValid();

        _lastStatusUpdate = now;
        _nextIpcCheck = now.AddSeconds(5); // check every 5s
    }

    private static string GetScopeLabel(TargetScope scope)
    {
        foreach (var option in ScopeOptions) {
            if (option.Scope == scope) {
                return option.Label;
            }
        }

        return scope.ToString();
    }

    private static string GetRaceLabel(Race race)
    {
        foreach (var option in RaceOptions) {
            if (option.Race == race) {
                return option.Label;
            }
        }

        return race.ToString();
    }

    private static string GetGenderLabel(Gender gender)
    {
        foreach (var option in GenderOptions) {
            if (option.Gender == gender) {
                return option.Label;
            }
        }

        return gender.ToString();
    }

    private static string GetDefaultBehaviorLabel(DefaultNoArgBehavior behavior)
    {
        foreach (var (b, label) in DefaultBehaviorOptions) {
            if (b == behavior) return label;
        }

        return behavior.ToString();
    }

    private static string FormatAppliedAt(DateTime appliedAtUtc)
    {
        var span = DateTime.UtcNow - appliedAtUtc;
        if (span.TotalSeconds < 60) {
            return "moments ago";
        }

        if (span.TotalMinutes < 60) {
            return $"{(int) span.TotalMinutes}m ago";
        }

        if (span.TotalHours < 24) {
            return $"{(int) span.TotalHours}h ago";
        }

        return appliedAtUtc.ToLocalTime().ToString("g");
    }
}
