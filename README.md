# GlamHouse

## Feature Highlights
- Apply the Emperor's New set to nearby players, your party, or NPCs with a single command.
- Filter targets by race and/or gender (e.g. `femroe`, `male miqo`) before applying the glamour.

## Requirements
- The [Glamourer](https://github.com/Ottermandias/Glamourer) plugin installed and loaded.

## Installation
- `https://raw.githubusercontent.com/Catphinaud/DalamudPlugins/refs/heads/main/pluginmaster.json`

## Usage
- `/glamhouse [ui]` - open the Glamourer user interface.
- `/glamhouse help` - show the in-game usage summary.
- `/glamhouse players` - apply to nearby players (default scope).
- `/glamhouse party` - apply to everyone in your party.
- `/glamhouse npc` - affect nearby event and battle NPCs.
- `/glamhouse all` - include every visible player, as well as NPCs.
- `/glamhouse revert` (aliases: `reset`, `undo`, `r`) - restore every character modified by GlamHouse.

- `/glamhouse {race}` - apply to nearby players of the specified race.
- `/glamhouse {gender}` - apply to nearby players of the specified gender.

- Add race and gender filters in any order, with or without spaces e.g: `/glamhouse femroe`, `/glamhouse male miqo`, `/glamhouse party viera`.


- Supported races: 
  - Hyur (aliases: `hyu`, `bot`)
  - Elezen (aliases: `ele`, `elf`)
  - Lalafell (aliases: `lala`, `short`)
  - Miqo'te (aliases: `miqo`, `cat`, `kitty`)
  - Roegadyn (aliases: `roe`, `big`)
  - Au Ra (aliases: `aura`, `lizzy`)
  - Hrothgar (aliases: `dog`, `hrogh`)
  - Viera (aliases: `rabbit`, `bun`)
- Supported genders:
  - Male (alias: `man`)
  - Female (alias: `fem`)
