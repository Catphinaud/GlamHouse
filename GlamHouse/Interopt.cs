using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Plugin;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
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

                foreach (var slot in Enum.GetValues<PlateSlot>()) {
                    if (!plate.Items.TryGetValue(slot, out var item)) {
                        if (!plate.FillWithNewEmperor) {
                            continue;
                        }

                        uint empItem = GetEmperorItemForSlot(slot);
                        if (empItem != 0) {
                            _SetItem.Invoke(playerIndex, ConvertSlot(slot), empItem, [0, 0]);
                            ChangedPlayers.Add(playerIndex);
                        }

                        continue;
                    }

                    _SetItem.Invoke(playerIndex, ConvertSlot(slot), item.ItemId, [item.Stain1, item.Stain2]);
                    ChangedPlayers.Add(playerIndex);
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
                }
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

internal class TryOnHelper
{
    private static readonly Dictionary<ulong, bool> TriedOn = new();

    public static void TryOnGlamourer(bool withEmperor, HashSet<uint>? playersList = null)
    {
        if (Svc.ClientState.LocalPlayer == null) {
            return;
        }

        var plate = new SavedPlate("Glamourer")
        {
            Items = new Dictionary<PlateSlot, SavedGlamourItem>(),
            FillWithNewEmperor = withEmperor
        };

        TriedOn.Clear();

        if (playersList is { Count: > 0 }) {
            foreach (var player in Svc.Objects.PlayerObjects) {
                var objIndex = player.ObjectIndex;
                if (TriedOn.ContainsKey(objIndex) && TriedOn[objIndex]) {
                    continue;
                }

                TriedOn[objIndex] = true;

                if (!player.IsValid()) {
                    continue;
                }

                if (!player.IsCharacterVisible()) {
                    continue;
                }

                GlamourerInteropt.TryOn(objIndex, plate);
            }

            return;
        }

        Svc.Log.Debug($"Trying on Glamourer plate with {plate.Items.Count} items.");
        foreach (var item in plate.Items) {
            Svc.Log.Debug($"Slot: {item.Key}, ItemId: {item.Value.ItemId}, Stain1: {item.Value.Stain1}, Stain2: {item.Value.Stain2}");
        }

        foreach (var player in Svc.Objects.PlayerObjects) {
            var objIndex = player.ObjectIndex;

            TriedOn[objIndex] = true;

            if (!player.IsValid()) {
                continue;
            }

            if (!player.IsCharacterVisible()) {
                continue;
            }

            // var chara = player.Character();

            // if (chara != null && chara->Sex == 0) {
            //     continue;
            // }

            GlamourerInteropt.TryOn(objIndex, plate);
        }

        var obj = Svc.Objects.StandObjects;
        foreach (var o in obj) {
            var objIndex = o.ObjectIndex;

            if (TriedOn.ContainsKey(objIndex) && TriedOn[objIndex]) {
                continue;
            }

            TriedOn[objIndex] = true;

            if (!o.IsValid()) {
                continue;
            }

            GlamourerInteropt.TryOn(objIndex, plate);
        }


        foreach (var eventObject in Svc.Objects.EventObjects) {
            if (!eventObject.IsValid()) {
                continue;
            }

            var objIndex = eventObject.ObjectIndex;
            if (TriedOn.ContainsKey(objIndex) && TriedOn[objIndex]) {
                continue;
            }

            TriedOn[objIndex] = true;

            if (eventObject.ObjectKind is not (ObjectKind.EventNpc or ObjectKind.BattleNpc)) {
                continue;
            }

            GlamourerInteropt.TryOn(objIndex, plate);
        }
    }
}
