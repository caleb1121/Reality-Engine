# 🌌 Reality Engine v2.0

**A Universal, Logic-Driven Generation Suite for Worldbuilders, GMs, and Authors**

Reality Engine is a lightweight, JSON-driven terminal application built in C# (.NET) for generating deep, internally consistent characters, starships, planets, factions, artifacts, and novel-ready story casts. Feed it any custom `.json` Universe Module—or build one from scratch with the in-app Architect Wizard—and the engine turns raw tables into living, rule-checked entities.

---

## ✨ Core Features

- **Interactive Terminal UI:** Arrow-key and `W/S` wrap-around navigation with automatic viewport scrolling for long lists. No command-line arguments required.
- **Deterministic Seeds:** Every generated entity displays a reproducible `Seed`. Enter any number—or any word/phrase—to recreate the exact same entity on demand.
- **Instant Reroll, `.txt` & `.json` Export:** Press `[R]` to reroll the same archetype, `[S]` to archive a formatted `.txt` sheet, or `[J]` to export clean structured `.json` for external tools, spreadsheets, or games.
- **Batch Roster Generator:** Roll 1 to 500 unique entities at once into a single timestamped roster file under `Saves/`.
- **Smart Narrative Engine:** Stitches biographies from ordered `BioTemplates` using single-pass token injection, natural-English list joining (`A, B, and C`), `MinStat`/`MaxStat` stat-gating, and dynamic pronouns (`he/she/they/it` with capitalized variants).
- **Deep Logic & RPG Systems:** Supports comma-separated `Requires`, `Excludes`, and `GrantsTag` rules, multi-pick `MultiTraits`, independent numeric `Ranges` with units, balanced `PointPools` with visual stat bars, and dynamic trait `StatMods`.
- **8 Per-Universe Module Toggles:** Switch `ShowRarity`, `UseNarrative`, `UsePointPools`, `ShowStatBars`, `UseGender`, `ShowTags`, `UseRanges`, and `UseMultiTraits` on or off to tailor the engine for RPGs, sci-fi worldbuilding, or literary character dossiers.
- **Pre-Flight Validator & Live Hot-Reload:** Automatically audits modules on load for dead tags, stage-order errors, broken `{Tokens}`, unused `StatMods`, and impossible `PointPool`/`Range` math—then calculates exact total combinations using `BigInteger`. Edit JSON externally and hit **Reload Active Module** to test changes live.
- **Architect Wizard & Debug Mode:** Generate a valid v2.0 starter `.json` inside the app, or toggle **Debug Mode** to watch every roll, tag grant, blocked option, and `StatMod` execute step-by-step.

---

## 📦 Bundled Universe Modules

Reality Engine v2.0 ships with three flagship modules showcasing different ways to use the engine:

| Module | Focus | Archetypes | Highlights |
| :--- | :--- | :---: | :--- |
| **The Andromeda Expanse** | Sci-Fi Sandbox | 12 | Marines, Corsairs, Bounty Hunters, Xeno-Biologists, Strike Fighters, Capital Starships, Mechs, Planets, Stations, Factions, and Precursor Artifacts |
| **Chronicles of Aetheria** | High & Dark Fantasy | 8 | Highland Knights, Aether-Weavers, Shadowblades, Sylvan Wardens, Runeforged Artificers, Oathsworn Clerics, Abyssal Horrors, and Ancient Wyrms |
| **The Great Library: Character Portraits** | Author's Dossier Tool | 5 | Protagonist, Antagonist, Confidant & Foil, Reluctant Mentor, and Wildcard—with `ShowRarity: false`, dual psychological stat pools, physical tells, keepsakes, and dramatic arcs |

---

## 📁 Installation & Folder Structure

No installation required—just run `RealityEngine.exe` (or `dotnet run` on .NET 6.0+).

On first launch, the engine automatically creates its working folders next to the executable:

- **`/UniverseFiles/`** — Drop your custom `.json` universe modules here. The engine remembers and auto-loads your last used module on startup.
- **`/Saves/`** — Stores all exported `.txt` character sheets, `.json` exports, and batch `Rosters/`, organized by universe and readable inside the in-app **Save Archive**.
- **`/Docs/`** — Contains the built-in multi-level **Help Library**.

---

## 🎮 Quick Start

1. **Load a Universe:** Select **Load Universe Module** from the Main Menu. The Pre-Flight Validator will audit the file and display total possible combinations.
2. **Generate:** Pick **Generate Entity**, **Generate Entity from Seed**, or **Batch Generate Roster**, then choose a specific archetype or `[ Random Archetype ]`.
3. **Inspect & Export:** On any character sheet:
   - `[R]` Reroll the same archetype
   - `[S]` Save a formatted `.txt` sheet
   - `[J]` Export structured `.json`
   - `[M]` or `[Esc]` Return to Main Menu
4. **Live Editing:** Keep a `.json` file open in your editor, save your tweaks, and select **Reload Active Module** to immediately re-validate and test.

---

## 🛠️ Modding Guide (For Worldbuilders)

Reality Engine v2.0 uses a forgiving JSON parser that ignores `//` comments, allows trailing commas, and reads property names case-insensitively.

### 1. Generation Order & Tag Logic
The engine evaluates every archetype in a strict order:

> `StartingTags` → `Gender & Pronouns` → `Names` → `MandatoryTraits` → `OptionalTraits` → `MultiTraits` → `Ranges` → `PointPools` → `StatMods` → `BioTemplates`

Options can chain together using comma-separated `GrantsTag`, `Requires` (ALL required), and `Excludes` (ANY blocks):

```json
{
  "Value": "Powered Exosuit",
  "Weight": 15,
  "GrantsTag": "Heavy_Armor, Veteran",
  "Rarity": "Rare",
  "StatMods": { "Strength": 2, "Endurance": 1 }
}
2. Stat-Gated & Tag-Gated NarrativesLock BioTemplates behind Requires, Excludes, MinStatName/MinStatValue (>=), or MaxStatName/MaxStatValue (<=). Both stat gates work on PointPools (after StatMods), Ranges, and numeric traits:JSON{
  "Text": "{PossessiveCap} reflexes bordered on preternatural; {Subject} reacted before the muzzle flash even registered.",
  "Requires": "Military",
  "MinStatName": "Reflexes",
  "MinStatValue": 16,
  "Weight": 80
}
3. Smart PlaceholdersPlaceholders are case-insensitive and resolved in a single pass. If a template references an unrolled optional trait or range, the engine skips that template cleanly instead of printing raw brackets.PlaceholderOutput{First} / {Last} / {Full}First name, Last name, or First 'Nickname' Last{Nick} / {Nickname}Nickname if rolled; falls back to First Name{Seed} / {Gender}Entity seed or rolled gender (when UseGender is true){Subject} / {Object} / {Possessive}he/him/his, she/her/her, they/them/their, or it/it/its{SubjectCap} / {ObjectCap} / {PossessiveCap}Capitalized pronouns for starting sentences{Trait Name}Rolled value of any Mandatory or Optional trait{MultiTrait Name}Natural English list ("A", "A and B", "A, B, and C"){Range Name} / {Range Name_Raw}Rolled range with unit ("34 yrs") or raw number ("34"){Stat Name} / {Pool: Stat}Final PointPool score (including StatMods)⚠️ TroubleshootingMath Error on Load: Your PointPool budget (TotalPoints) is either higher than Attributes.Count * MaxPerAttribute or lower than Attributes.Count * MinPerAttribute. Adjust the budget or min/max bounds.Dead Item on Load: An option, range, or template Requires a tag that nothing in the archetype grants (usually a spelling typo).Order Error on Load: An option Requires a tag that is only granted later in the generation chain. Move the granting trait earlier (e.g., from OptionalTraits to MandatoryTraits, or earlier in the same section).Unknown Token on Load: A {Token} inside BioTemplates doesn't match any built-in token, trait, range, or stat name.Note on Casing: Tags and {Tokens} are case-insensitive in v2.0 (Heavy_armor matches Heavy_Armor), so missing rolls are almost always caused by a typo, a Weight of 0, or a tag-order mismatch—toggle Debug Mode [ON] to see the exact reason any option was skipped.Built with C# (.NET) | Designed for infinite worlds.
