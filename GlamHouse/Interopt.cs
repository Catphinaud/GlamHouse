using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin;
using ECommons.DalamudServices;
using Glamourer.Api.Enums;
using Glamourer.Api.IpcSubscribers;

namespace GlamHouse;

// Lazily borrowed from Glamhaholic
[Serializable]
internal class SavedPlate
{
    public string Name { get; set; }
    public Dictionary<PlateSlot, SavedGlamourItem> Items { get; init; } = new();
    public List<string> Tags { get; } = [];
    public bool FillWithNewEmperor { get; set; } = false;

    public SavedPlate(string name)
    {
        Name = name;
    }

    internal SavedPlate Clone()
    {
        return new SavedPlate(Name)
        {
            Items = Items.ToDictionary(entry => entry.Key, entry => entry.Value.Clone()),
        };
    }
}

[Serializable]
internal class SavedGlamourItem
{
    public uint ItemId { get; set; }
    public byte Stain1 { get; set; }
    public byte Stain2 { get; set; }

    internal SavedGlamourItem Clone()
    {
        return new SavedGlamourItem()
        {
            ItemId = ItemId,
            Stain1 = Stain1,
            Stain2 = Stain2
        };
    }
}

internal class GlamourerInteropt
{
    private static SetItem _SetItem { get; set; } = null!;
    private static RevertState _RevertState { get; set; } = null!;

    public static HashSet<int> ChangedPlayers { get; } = [];

    private static bool Initialized { get; set; } = false;
    private static bool Available { get; set; } = false;

    public static void TryOn(int playerIndex, SavedPlate plate)
    {
        if (!IsAvailable()) {
            Svc.Log.Warning("Glamourer IPC is not available.");
            return;
        }

        try {
            Svc.Framework.Run(() => {
                _RevertState.Invoke(playerIndex, flags: ApplyFlag.Equipment);

                var notAdded = true;

                foreach (var slot in Enum.GetValues<PlateSlot>()) {
                    if (!plate.Items.TryGetValue(slot, out var item)) {
                        if (!plate.FillWithNewEmperor) {
                            continue;
                        }

                        var empItem = GetEmperorItemForSlot(slot);
                        if (empItem != 0) {
                            _SetItem.Invoke(playerIndex, ConvertSlot(slot), empItem, [0, 0]);
                            ChangedPlayers.Add(playerIndex);
                            notAdded = false;
                        }

                        continue;
                    }

                    _SetItem.Invoke(playerIndex, ConvertSlot(slot), item.ItemId, [item.Stain1, item.Stain2]);
                    ChangedPlayers.Add(playerIndex);
                    notAdded = false;
                }

                if (notAdded) {
                    Plugin.Tracker.Remove(playerIndex);
                }
            });
        } catch (Exception ex) {
            Svc.Log.Error(ex, "Error while trying to communicate with Glamourer.");
        }
    }

    public static void Revert()
    {
        if (!IsAvailable()) {
            Svc.Log.Warning("Glamourer IPC is not available.");
            return;
        }

        try {
            Svc.Framework.Run(() => {
                foreach (var playerIndex in ChangedPlayers.ToArray()) {
                    _RevertState.Invoke(playerIndex, flags: ApplyFlag.Equipment);
                    ChangedPlayers.Remove(playerIndex);
                    Plugin.Tracker.Remove(playerIndex);
                }
            });
        } catch (Exception ex) {
            Svc.Log.Error(ex, "Error while trying to communicate with Glamourer.");
        }
    }

    public static void Revert(int objectIndex)
    {
        if (!IsAvailable()) {
            Svc.Log.Warning("Glamourer IPC is not available.");
            return;
        }

        try {
            Svc.Framework.Run(() => {
                if (!ChangedPlayers.Contains(objectIndex)) {
                    return;
                }

                _RevertState.Invoke(objectIndex, flags: ApplyFlag.Equipment);
                ChangedPlayers.Remove(objectIndex);
                Plugin.Tracker.Remove(objectIndex);
            });
        } catch (Exception ex) {
            Svc.Log.Error(ex, "Error while trying to communicate with Glamourer.");
        }
    }

    internal static uint GetEmperorItemForSlot(PlateSlot slot)
    {
        return slot switch
        {
            PlateSlot.Head => 10032,
            PlateSlot.Body => 10033,
            PlateSlot.Hands => 10034,
            PlateSlot.Legs => 10035,
            PlateSlot.Feet => 10036,
            PlateSlot.Ears => 9293,
            PlateSlot.Neck => 9292,
            PlateSlot.Wrists => 9294,
            PlateSlot.RightRing or PlateSlot.LeftRing => 9295,
            _ => 0
        };
    }

    private static ApiEquipSlot ConvertSlot(PlateSlot slot)
    {
        switch (slot) {
            case PlateSlot.LeftRing:
                return ApiEquipSlot.LFinger;

            case >= (PlateSlot) 5:
                return (ApiEquipSlot) ((int) slot + 2);

            default:
                return (ApiEquipSlot) ((int) slot + 1);
        }
    }


    public static void Initialize(IDalamudPluginInterface pluginInterface)
    {
        if (Initialized) {
            return;
        }

        _SetItem = new SetItem(pluginInterface);
        _RevertState = new RevertState(pluginInterface);

        Initialized = true;

        RefreshStatus(pluginInterface);
    }

    public static bool RefreshStatus(IDalamudPluginInterface pluginInterface)
    {
        var prev = Available;

        Available = false;

        foreach (var plugin in pluginInterface.InstalledPlugins) {
            if (plugin.Name == "Glamourer") {
                Available = plugin.IsLoaded;
                break;
            }
        }

        if (prev == Available) {
            return Available;
        }

        return Available;
    }

    public static bool IsAvailable()
    {
        return Available && IsIPCValid();
    }

    public static bool IsIPCValid()
    {
        return _SetItem.Valid && _RevertState.Valid;
    }
}

internal enum PlateSlot : uint
{
    MainHand = 0,
    OffHand = 1,
    Head = 2,
    Body = 3,
    Hands = 4,
    Legs = 5,
    Feet = 6,
    Ears = 7,
    Neck = 8,
    Wrists = 9,
    RightRing = 10,
    LeftRing = 11,
}
