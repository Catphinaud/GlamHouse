using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.Command;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using ECommons;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using ECommons.PartyFunctions;
using VT = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;

namespace GlamHouse;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/glamhouse";

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        PluginInterface = pluginInterface;

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = $"Use {CommandName} help for a list of commands." +
                          $" You can specify"
        });

        ECommonsMain.Init(pluginInterface, this, Module.ObjectLife);

        GlamourerInteropt.Initialize(pluginInterface);
    }

    private static void OnCommand(string command, string arguments)
    {
        // Races: "Hyur", "Elezen", "Lalafell", "Miqo'te", "Roegadyn", "Au Ra", "Hrothgar", "Viera"
        // Genders: "Male", "Female"

        if (!GlamourerInteropt.IsAvailable() && (!GlamourerInteropt.RefreshStatus(PluginInterface) || !GlamourerInteropt.IsAvailable())) {
            var builder =
                new SeStringBuilder()
                    .AddUiForeground("[GlamHouse]", 37)
                    .AddText(" Glamourer plugin is not installed or not loaded. Please install and load Glamourer to use GlamHouse.");
            Svc.Chat.Print(builder.Build());
            return;
        }

        var lowerTrimmedArgs = arguments.Trim().ToLowerInvariant().Replace("au ra", "au'ra").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        var firstArg = lowerTrimmedArgs.FirstOrDefault() ?? string.Empty;

        if (firstArg is "revert" or "reset" or "undo" or "r") {
            GlamourerInteropt.Revert();
            Svc.Chat.Print("Reverted all players changed by GlamHouse.");
            return;
        }

        var raceWords = new Dictionary<Race, List<string>>
        {
            // Short and long common words for ffxiv races
            { Race.Hyur, ["hyur", "bot"] },
            { Race.Elezen, ["elezen", "elf", "tall"] },
            { Race.Lalafell, ["lalafell", "lala", "short"] },
            { Race.Miqote, ["miqote", "miqo'te", "cat", "miqo", "kitty"] },
            { Race.Roegadyn, ["roegadyn", "roe", "big"] },
            { Race.AuRa, ["aura", "lizzy", "au'ra"] },
            { Race.Hrothgar, ["hrothgar", "doggie", "dog", "hrogh"] },
            { Race.Viera, ["viera", "bunny", "bunnygirl", "rabbit", "bun"] }
        };

        var genderWords = new Dictionary<Gender, List<string>>
        {
            { Gender.Male, ["male", "man"] },
            { Gender.Female, ["female", "fem"] }
        };

        if (firstArg == "help") {
            var builder =
                new SeStringBuilder()
                    .AddUiForeground($"GlamHouse v{Svc.PluginInterface.Manifest.AssemblyVersion} \n", 37)
                    .AddUiForeground($"{CommandName} {{ command | race | gender | [gender]race[gender] }}", 43);

            AddCommandHelp(builder, "party, players, all and npc", "Specify target scope. Default is players nearby.");
            AddCommandHelp(builder, "revert", "Revert all players changed by GlamHouse.");

            // or /glamhouse {gender} or /glamhouse {race} or /glamhouse {gender} {race} (you may mix order)

            AddCommandHelp(builder, "{ hyur | elezen | lalafell | miqote | roegadyn | aura | hroghgar | viera }");
            builder.AddText($"\n - Apply to specified race (Can use common prefixes found in {CommandName} help)");
            AddCommandHelp(builder, "{ female | fem, male | man }", "Apply to specified gender");

            builder.AddText("\nYou can combine Race and Gender in any order, with or without space: ");
            builder.AddUiForeground($"{CommandName} femroe", 41);


            builder.AddText("\nRaces And Genders:");

            foreach (var (race, raceAliases) in raceWords) {
                AddCommandHelp(builder, race.ToString("G"), raceAliases.Join(", "));
            }

            Svc.Chat.Print(builder.Build());

            return;
        }

        // Can we match race or gender or both?
        var input = new FilterInput();

        if (lowerTrimmedArgs.Contains("party")) {
            input.Scope = TargetScope.Party;
            lowerTrimmedArgs.Remove("party");
        } else if (lowerTrimmedArgs.Contains("npc")) {
            input.Scope = TargetScope.Npc;
            lowerTrimmedArgs.Remove("npc");
        } else if (lowerTrimmedArgs.Contains("players")) {
            input.Scope = TargetScope.Player;
            lowerTrimmedArgs.Remove("players");
        } else if (lowerTrimmedArgs.Contains("all")) {
            input.Scope = TargetScope.All;
            lowerTrimmedArgs.Remove("all");
        }

        bool matched = false;

        foreach (var (race, raceAliases) in raceWords) {
            if (raceAliases.Any(alias => lowerTrimmedArgs.Contains(alias))) {
                input.Race = race;
                matched = true;
            }

            // Try match [gender][race] or [race][gender] with or without space
            foreach (var (gender, genderAliases) in genderWords) {
                if (genderAliases.Any(alias => lowerTrimmedArgs.Contains(alias))) {
                    input.Gender = gender;
                    matched = true;
                }
            }
        }

        foreach (var gender in genderWords.Select(genderEntry => new { genderEntry, gender = genderEntry.Key })
                     .Select(t => new { t, genderAliases = t.genderEntry.Value })
                     .Where(t => t.genderAliases.Any(alias => lowerTrimmedArgs.Contains(alias)))
                     .Select(t => t.t.gender)) {
            input.Gender = gender;
            matched = true;
        }

        if (lowerTrimmedArgs.Count > 0 && !matched) {
            Svc.Chat.PrintError($"Unknown arguments: {string.Join(' ', lowerTrimmedArgs)}. Use {CommandName} help for a list of commands.");
            return;
        }

        Toggle(input);
    }

    private static void AddCommandHelp(SeStringBuilder builder, string command, string? description = null)
    {
        builder.AddText("\n");
        builder.AddUiForeground(command, 43);
        if (!string.IsNullOrEmpty(description)) {
            builder.AddText($" - {description}");
        }
    }

    private static void Toggle(FilterInput input)
    {
        if (input.Scope == TargetScope.Party) {
            TryOnParty();
            return;
        }

        if (input.Scope is TargetScope.All or TargetScope.Player) {
            TryOnAllNearby(input);
        }

        if (input.Scope == TargetScope.Npc) {
            TryOnAllNearbyNpcs();
        }
    }

    private static void TryOnParty()
    {
        if (Svc.ClientState.LocalPlayer == null) {
            return;
        }

        var plate = new SavedPlate("Glamourer")
        {
            Items = new Dictionary<PlateSlot, SavedGlamourItem>(),
            FillWithNewEmperor = true
        };

        var members = UniversalParty.Members;

        foreach (var member in members) {
            var objIndex = member.IGameObject.ObjectIndex;

            Svc.Log.Debug($"Trying to glamour party member: {member.Name} (ObjectIndex: {objIndex})");

            if (!member.IGameObject.IsValid()) {
                continue;
            }

            try {
                GlamourerInteropt.TryOn(objIndex, plate);
            } catch (Exception ex) {
                Svc.Log.Error(ex, $"Failed to glamour party member: {member.Name} (ObjectIndex: {objIndex})");
            }
        }
    }

    private static unsafe void TryOnAllNearby(FilterInput input)
    {
        if (Svc.ClientState.LocalPlayer == null) {
            return;
        }

        var plate = new SavedPlate("Glamourer")
        {
            Items = new Dictionary<PlateSlot, SavedGlamourItem>(),
            FillWithNewEmperor = true
        };

        Svc.Log.Debug($"Trying on Glamourer plate with {plate.Items.Count} items.");
        foreach (var item in plate.Items) {
            Svc.Log.Debug($"Slot: {item.Key}, ItemId: {item.Value.ItemId}, Stain1: {item.Value.Stain1}, Stain2: {item.Value.Stain2}");
        }

        foreach (var player in Svc.Objects.PlayerObjects) {
            var objIndex = player.ObjectIndex;

            Svc.Log.Debug($"Trying to glamour nearby player: {player.Name} (ObjectIndex: {objIndex})");

            try {
                if (!player.IsValid()) {
                    continue;
                }

                if (!player.IsCharacterVisible()) {
                    continue;
                }

                var character = player.Character();

                if (character == null) {
                    Svc.Log.Debug($"Character data is null for player: {player.Name} (ObjectIndex: {objIndex})");
                    continue;
                }

                var gender = character->Sex switch
                {
                    0 => Gender.Male,
                    1 => Gender.Female,
                    _ => Gender.Unknown
                };

                var race = character->DrawData.CustomizeData.Race switch
                {
                    1 => Race.Hyur,
                    2 => Race.Elezen,
                    3 => Race.Lalafell,
                    4 => Race.Miqote,
                    5 => Race.Roegadyn,
                    6 => Race.AuRa,
                    7 => Race.Hrothgar,
                    8 => Race.Viera,
                    _ => Race.Unknown
                };

                if (input.Gender != Gender.Unknown && input.Gender != gender) {
                    Svc.Log.Debug($"Skipping player {player.Name} due to gender: {input.Gender:G} != {gender:G}");
                    continue;
                }

                if (input.Race != Race.Unknown && input.Race != race) {
                    Svc.Log.Debug($"Skipping player {player.Name} due to race: {input.Race:G} != {race:G}");
                    continue;
                }

                GlamourerInteropt.TryOn(objIndex, plate);
            } catch (Exception ex) {
                Svc.Log.Error(ex, $"Failed to glamour nearby player: {player.Name} (ObjectIndex: {objIndex})");
            }
        }
    }

    private static void TryOnAllNearbyNpcs()
    {
        if (Svc.ClientState.LocalPlayer == null) {
            return;
        }

        var plate = new SavedPlate("Glamourer")
        {
            Items = new Dictionary<PlateSlot, SavedGlamourItem>(),
            FillWithNewEmperor = true
        };

        var doneObjectIndexes = new HashSet<int>();

        foreach (var o in Svc.Objects.StandObjects.Concat(Svc.Objects.EventObjects).Where(o => o.ObjectKind is ObjectKind.EventNpc or ObjectKind.BattleNpc)) {
            var objIndex = o.ObjectIndex;

            if (!doneObjectIndexes.Add(objIndex)) {
                continue;
            }

            if (!o.IsValid()) {
                continue;
            }

            try {
                GlamourerInteropt.TryOn(objIndex, plate);
            } catch (Exception ex) {
                Svc.Log.Error(ex, $"Failed to glamour nearby NPC: {o.Name} (ObjectIndex: {objIndex})");

                Svc.NotificationManager.AddNotification(new Notification
                {
                    Title = "GlamHouse",
                    Content = $"Failed to glamour nearby NPC: {o.Name} (ObjectIndex: {objIndex})\n{ex.Message.Truncate(200)}",
                    Type = NotificationType.Error
                });
                break;
            }
        }
    }

    [PluginService] private static IDalamudPluginInterface PluginInterface { get; set; } = null!;
    [PluginService] private static ICommandManager CommandManager { get; set; } = null!;
    [PluginService] private static IClientState ClientState { get; set; } = null!;
    [PluginService] private static IGameGui GameGui { get; set; } = null!;
    [PluginService] private static IFramework Framework { get; set; } = null!;
    [PluginService] private static IDataManager DataManager { get; set; } = null!;
    [PluginService] private static IPluginLog Log { get; set; } = null!;

    public void Dispose()
    {
        if (Svc.ClientState.IsLoggedIn) {
            GlamourerInteropt.Revert();
        }

        CommandManager.RemoveHandler(CommandName);
        ECommonsMain.Dispose();
    }
}

internal record FilterInput
{
    public TargetScope Scope { get; set; } = TargetScope.Player;
    public Race Race { get; set; } = Race.Unknown;
    public Gender Gender { get; set; } = Gender.Unknown;
}

public enum TargetScope
{
    All = 0,
    Party = 1,
    Npc = 2,
    Player = 3,
}

public enum Gender
{
    Male = 0,
    Female = 1,
    Unknown
}

// FFXIV
public enum Race
{
    Unknown = 0,
    Hyur = 1,
    Elezen = 2,
    Lalafell = 3,
    Miqote = 4,
    Roegadyn = 5,
    AuRa = 6,
    Hrothgar = 7,
    Viera = 8,
}
