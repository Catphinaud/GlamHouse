using System;
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

    private readonly IDalamudPluginInterface _pluginInterface;

    private DateTime _nextIpcCheck = DateTime.MinValue;
    private DateTime _lastStatusUpdate = DateTime.MinValue;
    private bool _isIpcAvailable;
    private bool _isIpcValid;

    private TargetScope _selectedScope = TargetScope.Player;
    private Race _selectedRace = Race.Unknown;
    private Gender _selectedGender = Gender.Unknown;

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

        DrawIpcStatus();
        ImGui.Spacing();

        DrawQuickActions();
        ImGui.Separator();

        DrawAdvancedMode();
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

    private void DrawQuickActions()
    {
        ImGui.Text("Quick Glamour Actions");

        using var _ = ImRaii.Disabled(!_isIpcAvailable);

        if (ImGui.Button("Players Nearby")) {
            Plugin.Apply(new FilterInput { Scope = TargetScope.Player });
        }

        ImGui.SameLine();
        if (ImGui.Button("Party")) {
            Plugin.Apply(new FilterInput { Scope = TargetScope.Party });
        }

        ImGui.SameLine();
        if (ImGui.Button("Nearby NPCs")) {
            Plugin.Apply(new FilterInput { Scope = TargetScope.Npc });
        }

        ImGui.SameLine();
        if (ImGui.Button("Everyone")) {
            Plugin.Apply(new FilterInput { Scope = TargetScope.All });
        }

        if (ImGui.IsItemHovered()) {
            ImGui.SetTooltip("Apply to both players and NPCs around you.");
        }

        ImGui.SameLine();

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
        _nextIpcCheck = now.AddSeconds(20);
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
