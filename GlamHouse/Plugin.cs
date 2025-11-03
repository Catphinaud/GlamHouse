using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.DalamudServices;
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
            HelpMessage = "/glamhouse"
        });

        ECommonsMain.Init(pluginInterface, this, Module.ObjectLife);

        GlamourerInteropt.Initialize(pluginInterface);
    }

    private static void OnCommand(string command, string arguments)
    {
        // todo add gender, race switches with fuzzy search later
        // Races: "Hyur", "Elezen", "Lalafell", "Miqo'te", "Roegadyn", "Au Ra", "Hrothgar", "Viera"
        // Genders: "Male", "Female"

        var type = arguments.ToLowerInvariant().Trim() switch
        {
            "revert" => "revert",
            "p" or "party" => "party",
            "" or "all" or "nearby" => "all",
            _ => "help"
        };

        if (type == "help") {
            Svc.Chat.Print("Usage: /glamhouse [type|revert]");
            Svc.Chat.Print("type: party, all (default: all)");
            Svc.Chat.Print("revert: Revert all players changed by GlamHouse.");
            return;
        }

        Svc.Framework.RunOnTick(() => Toggle(type));
    }


    private static void Toggle(string type)
    {
        if (!GlamourerInteropt.IsAvailable() && (!GlamourerInteropt.RefreshStatus(PluginInterface) || !GlamourerInteropt.IsAvailable())) {
            Svc.Chat.PrintError("Glamourer IPC is not available.");
            return;
        }

        HashSet<uint>? changedPlayers = null;

        if (type == "revert") {
            GlamourerInteropt.Revert();
            Svc.Chat.Print("Reverted all players changed by GlamHouse.");
            return;
        }

        if (type == "party") {
            changedPlayers = UniversalParty.Members.Select(m => (uint) m.IGameObject.ObjectIndex).ToHashSet();
        }

        TryOnHelper.TryOnGlamourer(true, changedPlayers);
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

public enum TargetScope
{
    All = 0,
    Party = 1
}

public enum Gender
{
    Male = 0,
    Female = 1
}

// FFXIV
public enum Race
{
    All = 0,
    Hyur = 1,
    Elezen = 2,
    Lalafell = 3,
    Miqote = 4,
    Roegadyn = 5,
    AuRa = 6,
    Hrothgar = 7,
    Viera = 8
}
