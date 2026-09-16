using Godot;
using System;
using System.Collections.Generic;
using Worklings.Core.Pet;
using Worklings.Core.Progression;

namespace Worklings.Core.Host;

/// Click a slot, get the things that fit in it.
///
/// **The whole equip surface, in one place.** What is worn is checked,
/// everything else is a swap, and taking it off is the last entry — which is
/// what turned the Inventory tab from the only door to gear into a browser.
///
/// Shared between the character screen and the loadout because it is the same
/// decision in both rooms, and because the loadout's design asks for exactly
/// this: the bench's best idea was seeing every alternative rather than cycling
/// through them, and the way to get that on the rig is to open the alternatives
/// at the plate rather than in a menu somewhere else.
///
/// Every price is quoted for the wearer, attunement included, from `ItemRates` —
/// the same number the Inventory tab shows, from the same place. And every
/// change routes through `PetState`, which validates ownership and slot; no
/// surface builds a loadout itself.
public static class SlotPicker
{
    private const int TakeOff = -2;

    /// Opens the picker under `anchor` and hands back the state that results.
    ///
    /// `host` owns the menu rather than the anchor, because equipping usually
    /// rebuilds whatever was clicked and a menu owned by the plate would be
    /// freed mid-signal.
    public static void Open(
        Node host, Control anchor, ItemSlot slot, PetState state, float scale,
        Action<PetState> changed)
    {
        var menu = new PopupMenu { Theme = WorklingsTheme.For(scale) };
        var options = state.AvailableItems(slot);
        var equipped = state.Loadout[slot];
        var byId = new Dictionary<int, Item>();

        for (int i = 0; i < options.Count; i++)
        {
            var item = options[i];
            int bonus = ItemRates.Default.Modifier(item, state.Family);
            bool attuned = ItemRates.Default.IsAttuned(item, state.Family);
            menu.AddRadioCheckItem(
                $"{item.DisplayName()}   {item.Tier().DisplayName()} · +{bonus} "
              + $"{item.Stat().DisplayName()}{(attuned ? "  ✦" : "")}", i);
            menu.SetItemChecked(i, item == equipped);
            byId[i] = item;
        }

        if (options.Count == 0)
        {
            menu.AddItem("Nothing for this slot yet", -1);
            menu.SetItemDisabled(menu.ItemCount - 1, true);
        }

        if (equipped is not null)
        {
            menu.AddSeparator();
            menu.AddItem("Take it off", TakeOff);
        }

        menu.IdPressed += id =>
        {
            if (id == TakeOff)
            {
                changed(state.ClearingSlot(slot));
                return;
            }
            if (byId.TryGetValue((int)id, out var item) && item != equipped)
            {
                changed(state.Equipping(item, slot));
            }
        };

        host.AddChild(menu);
        menu.PopupHide += menu.QueueFree;
        menu.ResetSize();
        menu.Popup(new Rect2I(
            (Vector2I)(anchor.GetScreenPosition() + new Vector2(0, anchor.Size.Y + Px(2, scale))),
            new Vector2I((int)Mathf.Max(menu.Size.X, anchor.Size.X), 0)));
    }

    private static int Px(float units, float scale) =>
        Math.Max(1, (int)Math.Round(units * scale));
}
