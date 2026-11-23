using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;

// =========================================================================================
// SECTION 1: SYSTEM INITIALIZATION & BOOTSTRAPPER
// =========================================================================================

// Lock working directory to the executable folder so relative paths stay consistent
// regardless of whether the app is launched via dotnet run, an IDE, or a shortcut.
try { Directory.SetCurrentDirectory(AppContext.BaseDirectory); } catch { }

// Ensure box-drawing characters, stat bars, and status icons render cleanly on Windows terminals.
try { Console.OutputEncoding = Encoding.UTF8; } catch { }
try { Console.Title = "Reality Engine v2.0 - Final Posterity Edition"; } catch { }
SetCursorVisible(false);

// Always restore cursor visibility and terminal colors even on Ctrl+C or unexpected exit.
Console.CancelKeyPress += (_, _) =>
{
    try { Console.ResetColor(); } catch { }
    SetCursorVisible(true);
};
AppDomain.CurrentDomain.ProcessExit += (_, _) =>
{
    try { Console.ResetColor(); } catch { }
    SetCursorVisible(true);
};

// Forgiving JSON read options so hand-edited universe files tolerate comments,
// trailing commas, and minor casing differences.
var jsonReadOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true
};

// Clean JSON write options used by Architect Mode and character JSON export.
var jsonWriteOptions = new JsonSerializerOptions
{
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};

// File storing the last successfully loaded module path for automatic startup loading.
string lastModuleConfigPath = Path.Combine("UniverseFiles", ".last_module");

// Create working folders and starter documentation if missing.
InitializeEnvironment();

// Global runtime state.
UniverseData? activeModule = null;
string? activeModulePath = null;
bool keepRunning = true;
bool isDebugMode = false;
Random masterRand = new Random();

// Attempt to silently auto-load the last used module (or the sole module if only one exists).
TryAutoLoadModule();

try
{
    // Display startup splash screen.
    ClearScreen();
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine(@"
  _____  ______          _      _____ _______ __     __ ______ _   _  _____ _____ _   _ ______ 
 |  __ \|  ____|   /\   | |    |_   _|__   __|\ \   / /|  ____| \ | |/ ____|_   _| \ | |  ____|
 | |__) | |__     /  \  | |      | |    | |    \ \_/ / | |__  |  \| | |  __  | | |  \| | |__   
 |  _  /|  __|   / /\ \ | |      | |    | |     \   /  |  __| | . ` | | |_ | | | | . ` |  __|  
 | | \ \| |____ / ____ \| |____ _| |_   | |      | |   | |____| |\  | |__| |_| |_| |\  | |____ 
 |_|  \_\______/_/    \_\______|_____|  |_|      |_|   |______|_| \_|\_____|_____|_| \_|______|
");
    Console.ForegroundColor = ConsoleColor.Gray;
    Console.WriteLine(" --------------------------------------------------------------------------");
    Console.WriteLine("                       [ UNIVERSAL CHARACTER ENGINE ]                      ");
    Console.WriteLine("                        Version 2.0 - Posterity Build                      ");
    Console.WriteLine(" --------------------------------------------------------------------------");

    if (activeModule != null)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\n              [ AUTO-LOADED MODULE: {activeModule.UniverseName} ]");
        Console.ForegroundColor = ConsoleColor.Gray;
    }

    Console.WriteLine("\n        >> SYSTEM INITIALIZED. PRESS ANY KEY TO ENTER THE GRID <<        ");
    Console.ResetColor();
    FlushInputBuffer();
    Console.ReadKey(true);


    // =========================================================================================
    // SECTION 2: MAIN LOOP
    // =========================================================================================

    while (keepRunning)
    {
        string status = activeModule == null ? "None (Please load a module)" : activeModule.UniverseName;
        string debugStatus = isDebugMode ? "[ON]" : "[OFF]";
        string reloadLabel = activeModule == null
            ? "Reload Active Module (None Loaded)"
            : $"Reload Active Module ({activeModule.UniverseName})";

        string header = $"--- REALITY ENGINE v2.0 ---\nACTIVE MODULE: {status}";

        string[] menuOptions =
        {
            "Generate Entity",
            "Generate Entity from Seed",
            "Batch Generate Roster",
            "Load Universe Module",
            reloadLabel,
            "Architect Mode (Build New Universe)",
            "Save Archive",
            $"Toggle Debug Mode {debugStatus}",
            "Help Library",
            "Exit"
        };

        // Main menu disables Escape-exit so accidental presses don't abruptly close the app.
        int choice = DrawInteractiveMenu(header, menuOptions, allowEscape: false);

        switch (choice)
        {
            case 0:
                if (EnsureModuleLoaded() && activeModule != null) GenerateEntityFlow(activeModule);
                break;
            case 1:
                if (EnsureModuleLoaded() && activeModule != null) GenerateFromSeedFlow(activeModule);
                break;
            case 2:
                if (EnsureModuleLoaded() && activeModule != null) BatchGenerateFlow(activeModule);
                break;
            case 3:
                LoadModuleFlow();
                break;
            case 4:
                ReloadActiveModule();
                break;
            case 5:
                ArchitectModeFlow();
                break;
            case 6:
                BrowseSaveArchive();
                break;
            case 7:
                isDebugMode = !isDebugMode;
                break;
            case 8:
                ShowInstructions();
                break;
            case 9:
                keepRunning = false;
                break;
        }
    }
}
finally
{
    // Restore terminal defaults cleanly before exiting.
    try { Console.ResetColor(); } catch { }
    SetCursorVisible(true);
}


// =========================================================================================
// SECTION 3: THE UI FRAMEWORK & CORE HELPERS
// =========================================================================================

/// <summary>
/// Safely toggles console cursor visibility without throwing on redirected/headless terminals.
/// </summary>
static void SetCursorVisible(bool visible)
{
    try { Console.CursorVisible = visible; }
    catch { }
}

/// <summary>
/// Safely reads the current console height without throwing on redirected/headless hosts.
/// </summary>
static int GetSafeWindowHeight()
{
    try { return Math.Max(12, Console.WindowHeight); }
    catch { return 28; }
}

/// <summary>
/// Clears any queued keypresses so an accidental double-tap never skips a menu or character sheet.
/// </summary>
static void FlushInputBuffer()
{
    try
    {
        while (Console.KeyAvailable) Console.ReadKey(true);
    }
    catch
    {
        // Ignore if input is redirected.
    }
}

/// <summary>
/// Clears the screen and eliminates Windows scrollback "ghosting".
/// When scrollable is true, immediately restores a tall buffer so long documents,
/// debug logs, and validator reports can be scrolled freely.
/// </summary>
static void ClearScreen(bool scrollable = false)
{
    try { Console.Clear(); } catch { }
    try { Console.SetCursorPosition(0, 0); } catch { }

    try
    {
        if (OperatingSystem.IsWindows())
        {
            int winHeight = Console.WindowHeight;
            // Briefly match window height to wipe stale scrollback history.
            Console.BufferHeight = winHeight;

            if (scrollable)
            {
                // Re-enable clean scrollback for long upcoming output.
                Console.BufferHeight = Math.Max(winHeight, 1000);
            }
        }
    }
    catch
    {
        // Safely ignore on terminals/hosts that disallow programmatic buffer resizing.
    }

    try { Console.SetCursorPosition(0, 0); } catch { }
}

/// <summary>
/// Guarantees that the expected working directories and a starter help file exist on disk.
/// </summary>
static void InitializeEnvironment()
{
    string[] requiredFolders = { "UniverseFiles", "Docs", "Saves" };
    foreach (var folder in requiredFolders)
    {
        if (!Directory.Exists(folder))
            Directory.CreateDirectory(folder);
    }

    string defaultHelpPath = Path.Combine("Docs", "00_Start_Here.txt");
    if (Directory.GetFiles("Docs", "*.txt").Length == 0)
    {
        File.WriteAllText(
            defaultHelpPath,
            "Welcome to Reality Engine v2.0.\n\n" +
            "CONTROLS:\n" +
            "- Up/Down Arrows or W/S to navigate menus (wraps top-to-bottom)\n" +
            "- Enter to select, Escape to go back\n" +
            "- On a Character Sheet: [R] Reroll, [S] Save .txt, [J] Export .json, [M/Esc] Main Menu\n\n" +
            "MODULES SUPPORTED IN JSON:\n" +
            "ShowRarity, UseNarrative, UsePointPools, UseGender, ShowTags, ShowStatBars, UseRanges, UseMultiTraits.\n\n" +
            "GETTING STARTED:\n" +
            "Place .json universe modules in 'UniverseFiles' or use Architect Mode to generate a starter file.\n" +
            "Supported features include comma-separated Requires/Excludes/GrantsTag rules, trait StatMods,\n" +
            "independent numeric Ranges, multi-pick MultiTraits, MinStat/MaxStat gates, and {SubjectCap} pronouns."
        );
    }
}

/// <summary>
/// Renders a keyboard-navigable menu with wrap-around Up/Down & W/S movement,
/// automatic viewport scrolling for long lists, and optional Escape cancellation.
/// Returns the selected index, or -1 if cancelled with Escape.
/// </summary>
static int DrawInteractiveMenu(string title, string[] options, bool allowEscape = true)
{
    if (options == null || options.Length == 0) return -1;

    int selectedIndex = 0;
    int scrollOffset = 0;

    FlushInputBuffer();

    while (true)
    {
        ClearScreen();

        string[] titleLines = title.Split('\n');
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(title);
        Console.WriteLine(new string('=', Math.Max(30, titleLines.Max(s => s.Length))));
        Console.WriteLine();
        Console.ResetColor();

        // Reserve lines for header, scroll hints, and footer controls.
        int reservedLines = titleLines.Length + 6;
        int maxVisible = Math.Max(5, GetSafeWindowHeight() - reservedLines);

        if (selectedIndex < scrollOffset)
            scrollOffset = selectedIndex;
        else if (selectedIndex >= scrollOffset + maxVisible)
            scrollOffset = selectedIndex - maxVisible + 1;

        int endExclusive = Math.Min(options.Length, scrollOffset + maxVisible);

        if (scrollOffset > 0)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"   ▲ ({scrollOffset} more above)");
            Console.ResetColor();
        }

        for (int i = scrollOffset; i < endExclusive; i++)
        {
            if (i == selectedIndex)
            {
                Console.BackgroundColor = ConsoleColor.Cyan;
                Console.ForegroundColor = ConsoleColor.Black;
                Console.WriteLine($" > {options[i]} ");
                Console.ResetColor();
            }
            else
            {
                Console.WriteLine($"   {options[i]} ");
            }
        }

        if (endExclusive < options.Length)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"   ▼ ({options.Length - endExclusive} more below)");
            Console.ResetColor();
        }

        Console.ForegroundColor = ConsoleColor.DarkGray;
        string navHint = allowEscape
            ? "\n[↑/↓ or W/S] Navigate  |  [Enter] Select  |  [Esc] Back"
            : "\n[↑/↓ or W/S] Navigate  |  [Enter] Select";
        Console.WriteLine(navHint);
        Console.ResetColor();

        ConsoleKey key = Console.ReadKey(true).Key;

        if (key == ConsoleKey.UpArrow || key == ConsoleKey.W)
        {
            selectedIndex = (selectedIndex - 1 + options.Length) % options.Length;
        }
        else if (key == ConsoleKey.DownArrow || key == ConsoleKey.S)
        {
            selectedIndex = (selectedIndex + 1) % options.Length;
        }
        else if (key == ConsoleKey.Enter)
        {
            return selectedIndex;
        }
        else if (key == ConsoleKey.Escape && allowEscape)
        {
            return -1;
        }
    }
}

/// <summary>
/// Temporarily restores the cursor to read a trimmed line of input from the user.
/// Always returns a non-null string.
/// </summary>
static string PromptUser(string message)
{
    FlushInputBuffer();
    SetCursorVisible(true);
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.Write($"\n{message} ");
    Console.ResetColor();
    string input = Console.ReadLine() ?? string.Empty;
    SetCursorVisible(false);
    return input.Trim();
}

/// <summary>
/// Strips OS-forbidden characters, trailing periods, overly long names, and Windows
/// reserved device names (CON, NUL, AUX, COM1, etc.) so file I/O never crashes.
/// </summary>
static string SanitizeFileName(string? rawName, string fallback = "Unnamed", int maxLength = 60)
{
    if (string.IsNullOrWhiteSpace(rawName)) return fallback;

    var invalidChars = new HashSet<char>(Path.GetInvalidFileNameChars());
    foreach (char c in new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' })
        invalidChars.Add(c);

    string cleaned = new string(rawName.Where(c => !invalidChars.Contains(c) && !char.IsControl(c)).ToArray())
        .Trim()
        .TrimEnd('.')
        .Replace(" ", "_");

    if (string.IsNullOrWhiteSpace(cleaned)) return fallback;

    if (cleaned.Length > maxLength)
        cleaned = cleaned[..maxLength].TrimEnd('_', '.');

    string[] reserved =
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    if (reserved.Contains(cleaned.ToUpperInvariant()))
        cleaned = "_" + cleaned;

    return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
}

/// <summary>
/// Returns a non-colliding file path by appending _2, _3, etc. if the initial path exists.
/// </summary>
static string GetUniqueFilePath(string folder, string baseName, string extension)
{
    string ext = extension.StartsWith('.') ? extension : "." + extension;
    string candidate = Path.Combine(folder, baseName + ext);
    int counter = 2;

    while (File.Exists(candidate))
    {
        candidate = Path.Combine(folder, $"{baseName}_{counter}{ext}");
        counter++;
    }

    return candidate;
}

/// <summary>
/// Splits a comma-separated tag field (e.g., "Elite, Cyborg") into a clean list of individual tags.
/// Fully backward-compatible with single-tag strings.
/// </summary>
static List<string> ParseTags(string? rawTags)
{
    if (string.IsNullOrWhiteSpace(rawTags)) return new List<string>();
    return rawTags.Split(',', StringSplitOptions.RemoveEmptyEntries)
                  .Select(t => t.Trim())
                  .Where(t => t.Length > 0)
                  .ToList();
}

/// <summary>
/// Joins a list of strings in natural English ("A", "A and B", "A, B, and C") for narrative bios.
/// </summary>
static string JoinNatural(List<string>? items)
{
    if (items == null || items.Count == 0) return "";
    if (items.Count == 1) return items[0];
    if (items.Count == 2) return $"{items[0]} and {items[1]}";
    return string.Join(", ", items.Take(items.Count - 1)) + $", and {items[^1]}";
}

/// <summary>
/// Formats a rolled Range number with its optional unit suffix (e.g., "34 yrs", "2500 Cr").
/// </summary>
static string FormatRangeValue(int value, string? unit)
{
    return string.IsNullOrEmpty(unit) ? value.ToString() : $"{value}{unit}";
}

/// <summary>
/// Produces a cross-session deterministic positive integer seed from any text input (FNV-1a 32-bit).
/// </summary>
static int DeterministicHash(string input)
{
    unchecked
    {
        uint hash = 2166136261;
        foreach (char c in input)
        {
            hash ^= c;
            hash *= 16777619;
        }
        return (int)(hash & 0x7FFFFFFF);
    }
}

/// <summary>
/// Maps rarity labels to distinct console highlight colors.
/// </summary>
static ConsoleColor GetRarityColor(string? rarity)
{
    if (string.IsNullOrWhiteSpace(rarity)) return ConsoleColor.Gray;

    return rarity.Trim().ToLowerInvariant() switch
    {
        "common" => ConsoleColor.Gray,
        "uncommon" => ConsoleColor.Green,
        "rare" => ConsoleColor.Cyan,
        "epic" => ConsoleColor.Magenta,
        "legendary" or "mythic" or "unique" => ConsoleColor.Yellow,
        _ => ConsoleColor.White
    };
}

/// <summary>
/// Builds a fixed-width visual progress bar scaled against a Point Pool's max attribute cap.
/// </summary>
static string BuildStatBar(int value, int maxValue, int barWidth = 10)
{
    if (maxValue <= 0) return "[" + new string('░', barWidth) + "]";

    double ratio = Math.Clamp((double)value / maxValue, 0.0, 1.0);
    int filled = (int)Math.Round(ratio * barWidth);
    string suffix = value > maxValue ? "+" : "";
    return "[" + new string('█', filled) + new string('░', barWidth - filled) + "]" + suffix;
}

/// <summary>
/// Ensures an active module is loaded before entering generation menus.
/// Diverts to the module loader if none is currently active.
/// </summary>
bool EnsureModuleLoaded()
{
    if (activeModule != null) return true;

    ClearScreen();
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("\n[!] Error: No module loaded! Diverting to Load Menu...");
    Console.ResetColor();
    Thread.Sleep(1000);
    LoadModuleFlow();
    return activeModule != null;
}


// =========================================================================================
// SECTION 4: THE ARCHITECT WIZARD
// =========================================================================================

/// <summary>
/// Guided wizard that collects core world details and serializes a valid starter
/// UniverseData JSON file demonstrating all v2.0 modules (Ranges, MultiTraits, StatMods, etc.).
/// </summary>
void ArchitectModeFlow()
{
    ClearScreen(scrollable: true);
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("==================================================");
    Console.WriteLine("          ARCHITECT MODE: UNIVERSE BUILDER        ");
    Console.WriteLine("==================================================");
    Console.ResetColor();
    Console.WriteLine("This wizard will generate a validly formatted v2.0 JSON foundation.");

    string uniName = PromptUser("Enter Universe Name (e.g., Cyberpunk 2099):");
    if (string.IsNullOrWhiteSpace(uniName)) return;

    string fileName = SanitizeFileName(uniName, "New_Universe") + ".json";
    string filePath = Path.Combine("UniverseFiles", fileName);

    // Protect existing hand-edited modules from accidental overwrites.
    if (File.Exists(filePath))
    {
        string confirm = PromptUser($"'{fileName}' already exists. Overwrite it? (Y/N):");
        if (!confirm.Equals("Y", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("\nCancelled. Existing file was not modified.");
            Thread.Sleep(1000);
            return;
        }
    }

    string archTitle = PromptUser("What are the character classes called? (e.g., Job, Species):");
    if (string.IsNullOrWhiteSpace(archTitle)) archTitle = "Class";

    string archName = PromptUser($"Enter the name of your first {archTitle} (e.g., Hacker, Elf):");
    if (string.IsNullOrWhiteSpace(archName)) archName = "Wanderer";

    string firstNamesRaw = PromptUser("Enter 3-5 First Names (separated by commas):");
    var firstNames = firstNamesRaw.Split(',').Select(n => n.Trim()).Where(n => !string.IsNullOrEmpty(n)).ToList();
    if (firstNames.Count == 0) firstNames.AddRange(new[] { "Alex", "Jordan", "Morgan" });

    string lastNamesRaw = PromptUser("Enter 3-5 Last Names (separated by commas):");
    var lastNames = lastNamesRaw.Split(',').Select(n => n.Trim()).Where(n => !string.IsNullOrEmpty(n)).ToList();
    if (lastNames.Count == 0) lastNames.AddRange(new[] { "Vance", "Mercer", "Hale" });

    var starterUniverse = new UniverseData
    {
        UniverseName = uniName,
        ArchetypeTitle = archTitle,
        Modules = new ModuleSettings
        {
            ShowRarity = true,
            UseNarrative = true,
            UsePointPools = true,
            UseGender = true,
            ShowTags = true,
            ShowStatBars = true,
            UseRanges = true,
            UseMultiTraits = true
        },
        Archetypes = new Dictionary<string, EntityArchetype?>
        {
            [archName] = new EntityArchetype
            {
                NicknameChance = 0.5,
                GenderOdds = new Dictionary<string, int>
                {
                    ["Male"] = 45,
                    ["Female"] = 45,
                    ["Non-Binary"] = 10
                },
                FirstNames = firstNames.Select(n => JsonSerializer.SerializeToElement(n)).ToList(),
                LastNames = lastNames.Select(n => JsonSerializer.SerializeToElement(n)).ToList(),
                Nicknames = new[] { "Ghost", "Ace", "Spark" }.Select(n => JsonSerializer.SerializeToElement(n)).ToList(),
                StartingTags = new List<string> { "Base-Tag" },
                MandatoryTraits = new Dictionary<string, List<JsonElement>>
                {
                    ["Primary Weapon"] = new List<JsonElement>
                    {
                        JsonSerializer.SerializeToElement(new SmartOption
                        {
                            Value = "Basic Tool",
                            Weight = 50,
                            Rarity = "Common"
                        }, jsonWriteOptions),
                        JsonSerializer.SerializeToElement(new SmartOption
                        {
                            Value = "Masterwork Blade",
                            Weight = 15,
                            GrantsTag = "Veteran",
                            Rarity = "Rare",
                            StatMods = new Dictionary<string, int> { ["Agility"] = 1 }
                        }, jsonWriteOptions),
                        JsonSerializer.SerializeToElement(new SmartOption
                        {
                            Value = "Relic Prototype",
                            Weight = 5,
                            GrantsTag = "Elite, Veteran",
                            Rarity = "Legendary",
                            StatMods = new Dictionary<string, int> { ["Strength"] = 2, ["Intelligence"] = 1, ["Bounty"] = 1500 }
                        }, jsonWriteOptions)
                    }
                },
                OptionalTraits = new Dictionary<string, ProbabilityTrait?>
                {
                    ["Augmentation"] = new ProbabilityTrait
                    {
                        Chance = 0.4,
                        Options = new List<JsonElement>
                        {
                            JsonSerializer.SerializeToElement(new SmartOption
                            {
                                Value = "Neural Link",
                                Weight = 20,
                                GrantsTag = "Augmented",
                                Rarity = "Uncommon",
                                StatMods = new Dictionary<string, int> { ["Intelligence"] = 2 }
                            }, jsonWriteOptions),
                            JsonSerializer.SerializeToElement(new SmartOption
                            {
                                Value = "Battle Scars",
                                Weight = 20,
                                Excludes = "Elite",
                                Rarity = "Common",
                                StatMods = new Dictionary<string, int> { ["Strength"] = 1 }
                            }, jsonWriteOptions)
                        }
                    }
                },
                MultiTraits = new Dictionary<string, MultiTrait?>
                {
                    ["Field Gear"] = new MultiTrait
                    {
                        Chance = 1.0,
                        MinPicks = 2,
                        MaxPicks = 3,
                        AllowDuplicates = false,
                        Options = new List<JsonElement>
                        {
                            JsonSerializer.SerializeToElement(new SmartOption { Value = "Trauma Kit", Weight = 30, Rarity = "Common" }, jsonWriteOptions),
                            JsonSerializer.SerializeToElement(new SmartOption { Value = "Signal Flare", Weight = 30, Rarity = "Common" }, jsonWriteOptions),
                            JsonSerializer.SerializeToElement(new SmartOption { Value = "Lockpick Set", Weight = 20, Rarity = "Uncommon", GrantsTag = "Infiltrator" }, jsonWriteOptions),
                            JsonSerializer.SerializeToElement(new SmartOption { Value = "Encrypted Comms", Weight = 15, Rarity = "Rare", StatMods = new Dictionary<string, int> { ["Intelligence"] = 1 } }, jsonWriteOptions),
                            JsonSerializer.SerializeToElement(new SmartOption { Value = "Stealth Cloak", Weight = 5, Rarity = "Epic", Requires = "Veteran" }, jsonWriteOptions)
                        }
                    }
                },
                Ranges = new Dictionary<string, NumericRange?>
                {
                    ["Age"] = new NumericRange
                    {
                        Min = 19,
                        Max = 55,
                        Step = 1,
                        Unit = " yrs"
                    },
                    ["Bounty"] = new NumericRange
                    {
                        Min = 500,
                        Max = 5000,
                        Step = 250,
                        Unit = " Cr",
                        Requires = "Veteran"
                    }
                },
                PointPools = new Dictionary<string, PointPool?>
                {
                    ["Core Stats"] = new PointPool
                    {
                        TotalPoints = 30,
                        MinPerAttribute = 5,
                        MaxPerAttribute = 15,
                        Attributes = new List<string> { "Strength", "Agility", "Intelligence" }
                    }
                }
            }
        },
        BioTemplates = new Dictionary<string, List<StoryTemplate?>>
        {
            ["1_Intro"] = new List<StoryTemplate?>
            {
                new StoryTemplate
                {
                    Text = "At {Age}, {Full} is best known for carrying a {Primary Weapon}.",
                    Weight = 10
                },
                new StoryTemplate
                {
                    Text = "As a recognized elite with a {Bounty} price tag, {Nick} handles {Possessive} {Primary Weapon} with unmatched skill.",
                    Requires = "Elite, Veteran",
                    Weight = 50
                }
            },
            ["2_Loadout"] = new List<StoryTemplate?>
            {
                new StoryTemplate
                {
                    Text = "{SubjectCap} never heads into the field without {Field Gear}.",
                    Weight = 20
                }
            },
            ["3_Flavour"] = new List<StoryTemplate?>
            {
                new StoryTemplate
                {
                    Text = "{SubjectCap} is easily recognized by {Possessive} {Augmentation}.",
                    Weight = 25
                },
                new StoryTemplate
                {
                    Text = "{SubjectCap} prefers brute force over careful planning.",
                    MaxStatName = "Intelligence",
                    MaxStatValue = 7,
                    Weight = 30
                },
                new StoryTemplate
                {
                    Text = "{SubjectCap} keeps a quiet profile and rarely lingers in one place for long.",
                    Weight = 10
                }
            }
        }
    };

    try
    {
        string newJson = JsonSerializer.Serialize(starterUniverse, jsonWriteOptions);
        File.WriteAllText(filePath, newJson);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\n[SUCCESS] Foundation built and saved to: {filePath}");
        Console.ResetColor();
        Console.WriteLine("You can now load this module, or edit the JSON file to add more complexity.");
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"\n[ERROR] Failed to write universe file: {ex.Message}");
        Console.ResetColor();
    }

    Console.WriteLine("Press any key to return to the Main Menu...");
    FlushInputBuffer();
    Console.ReadKey(true);
}


// =========================================================================================
// SECTION 5: MODULE LOADING, AUTO-LOAD, HOT-RELOAD & VALIDATOR
// =========================================================================================

/// <summary>
/// Silently loads the previously used module on startup, or auto-loads the only
/// .json file in UniverseFiles if just one exists.
/// </summary>
void TryAutoLoadModule()
{
    try
    {
        if (File.Exists(lastModuleConfigPath))
        {
            string savedPath = File.ReadAllText(lastModuleConfigPath).Trim();
            if (!string.IsNullOrWhiteSpace(savedPath) && File.Exists(savedPath))
            {
                if (LoadModuleFromPath(savedPath, interactive: false)) return;
            }
        }

        string[] files = Directory.GetFiles("UniverseFiles", "*.json");
        if (files.Length == 1)
        {
            LoadModuleFromPath(files[0], interactive: false);
        }
    }
    catch
    {
        // Auto-load is a convenience feature; never block startup if a saved path is stale.
    }
}

/// <summary>
/// Opens the universe selector menu and loads the chosen JSON module.
/// </summary>
void LoadModuleFlow()
{
    string? filePath = SelectUniverse();
    if (filePath == null) return;
    LoadModuleFromPath(filePath, interactive: true);
}

/// <summary>
/// Re-reads the currently loaded universe JSON from disk and re-runs diagnostics.
/// Ideal when tweaking JSON files in an external editor alongside the engine.
/// </summary>
void ReloadActiveModule()
{
    if (string.IsNullOrWhiteSpace(activeModulePath) || !File.Exists(activeModulePath))
    {
        ClearScreen();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\n[!] No active module file available to reload.");
        Console.ResetColor();
        Console.WriteLine("Load a universe module first.");
        Thread.Sleep(1200);
        return;
    }

    LoadModuleFromPath(activeModulePath, interactive: true);
}

/// <summary>
/// Deserializes a universe JSON file into a temporary object, validates it, and commits
/// it to activeModule only if valid (preserving any existing active module on failure).
/// </summary>
bool LoadModuleFromPath(string filePath, bool interactive)
{
    try
    {
        string jsonString = File.ReadAllText(filePath);
        var loadedModule = JsonSerializer.Deserialize<UniverseData>(jsonString, jsonReadOptions);

        if (loadedModule == null || loadedModule.Archetypes == null
            || !loadedModule.Archetypes.Values.Any(v => v != null))
        {
            throw new InvalidDataException("Universe JSON is empty or contains no valid 'Archetypes' entries.");
        }

        loadedModule.Modules ??= new ModuleSettings();
        if (string.IsNullOrWhiteSpace(loadedModule.UniverseName))
            loadedModule.UniverseName = Path.GetFileNameWithoutExtension(filePath);
        if (string.IsNullOrWhiteSpace(loadedModule.ArchetypeTitle))
            loadedModule.ArchetypeTitle = "Archetype";

        activeModule = loadedModule;
        activeModulePath = filePath;

        // Remember this module for auto-loading on the next launch.
        try { File.WriteAllText(lastModuleConfigPath, filePath); } catch { }

        if (!interactive) return true;

        ClearScreen(scrollable: true);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"SUCCESSFULLY LOADED: {activeModule.UniverseName}");
        Console.WriteLine("--------------------------------------------------");
        Console.ResetColor();

        RunValidator(activeModule);

        // Use BigInteger so large trait tables never overflow into negative values.
        BigInteger totalUniverseCombinations = 0;
        var validArchetypes = activeModule.Archetypes.Where(a => a.Value != null).ToList();
        Console.WriteLine($"Archetypes Found: {validArchetypes.Count}\n");

        foreach (var arch in validArchetypes)
        {
            var archVal = arch.Value!;
            int genders = activeModule.Modules.UseGender
                ? Math.Max(1, archVal.GenderOdds?.Count(g => g.Value > 0) ?? 0)
                : 1;
            int firsts = Math.Max(1, archVal.FirstNames?.Count ?? 0);
            int lasts = Math.Max(1, archVal.LastNames?.Count ?? 0);

            int nickChoices = 1;
            int nickCount = archVal.Nicknames?.Count ?? 0;
            if (nickCount > 0 && archVal.NicknameChance > 0)
            {
                nickChoices = archVal.NicknameChance >= 1.0 ? nickCount : nickCount + 1;
            }

            BigInteger archCombos = (BigInteger)genders * firsts * lasts * nickChoices;

            if (archVal.MandatoryTraits != null)
            {
                foreach (var trait in archVal.MandatoryTraits)
                {
                    if (trait.Value != null && trait.Value.Count > 0)
                        archCombos *= trait.Value.Count;
                }
            }

            if (archVal.OptionalTraits != null)
            {
                foreach (var opt in archVal.OptionalTraits)
                {
                    if (opt.Value is { Options.Count: > 0, Chance: > 0 } optVal)
                    {
                        int optCount = optVal.Options.Count;
                        archCombos *= optVal.Chance >= 1.0 ? optCount : optCount + 1;
                    }
                }
            }

            if (activeModule.Modules.UseMultiTraits && archVal.MultiTraits != null)
            {
                foreach (var mt in archVal.MultiTraits)
                {
                    if (mt.Value is { Options.Count: > 0, Chance: > 0, MaxPicks: > 0 } mtVal)
                        archCombos *= Math.Max(1, mtVal.Options.Count);
                }
            }

            if (activeModule.Modules.UseRanges && archVal.Ranges != null)
            {
                foreach (var range in archVal.Ranges)
                {
                    if (range.Value == null) continue;
                    int minV = Math.Min(range.Value.Min, range.Value.Max);
                    int maxV = Math.Max(range.Value.Min, range.Value.Max);
                    int step = Math.Max(1, range.Value.Step);
                    long distinctValues = ((long)maxV - minV) / step + 1;
                    archCombos *= Math.Max(1, distinctValues);
                }
            }

            totalUniverseCombinations += archCombos;

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"> {arch.Key}:");
            Console.ResetColor();
            Console.WriteLine($"  - Total Archetype Combinations: {archCombos:N0}");
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n[ TOTAL UNIVERSE COMBINATIONS: {totalUniverseCombinations:N0}+ ]");
        Console.ResetColor();

        Console.WriteLine("\nSystem Ready. Press any key to return to menu...");
        FlushInputBuffer();
        Console.ReadKey(true);
        return true;
    }
    catch (Exception ex)
    {
        if (!interactive) return false;

        ClearScreen(scrollable: true);
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"\nCRITICAL ERROR LOADING JSON: {ex.Message}");
        Console.ResetColor();
        Console.WriteLine("\nPress any key to return to menu...");
        FlushInputBuffer();
        Console.ReadKey(true);
        return false;
    }
}

/// <summary>
/// Inspects a loaded universe module for dead tag dependencies, forward-referenced tag ordering
/// errors, unreachable exclusions, disabled zero-weight options, invalid probability/range/
/// multi-trait/PointPool bounds, unknown StatMod targets, and broken BioTemplate gates or {Tokens}.
/// </summary>
void RunValidator(UniverseData data)
{
    Console.WriteLine("Running Pre-Flight Diagnostics...");
    List<string> warnings = new List<string>();

    if (data.Archetypes == null) return;

    var allGrantedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var allKnownStats = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var allKnownTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "First", "Last", "Nick", "Nickname", "Full", "Seed",
        "Subject", "Object", "Possessive", "SubjectCap", "ObjectCap", "PossessiveCap"
    };

    if (data.Modules.UseGender)
        allKnownTokens.Add("Gender");

    if (!string.IsNullOrWhiteSpace(data.ArchetypeTitle))
        allKnownTokens.Add(data.ArchetypeTitle);

    foreach (var archEntry in data.Archetypes)
    {
        string aName = archEntry.Key;
        var arch = archEntry.Value;

        if (arch == null)
        {
            warnings.Add($"[{aName}] Empty/null archetype definition (will be hidden from menus).");
            continue;
        }

        if (arch.NicknameChance < 0 || arch.NicknameChance > 1)
            warnings.Add($"[{aName}] NicknameChance ({arch.NicknameChance}) should be between 0.0 and 1.0.");

        var startingTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var grantedSoFar = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allArchGrantedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reqChecks = new List<(string Section, string ItemLabel, string ReqTag, bool AvailableAtStage)>();
        var archNumericStats = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var statModTargets = new List<string>();

        if (arch.StartingTags != null)
        {
            foreach (var raw in arch.StartingTags)
            {
                foreach (var tag in ParseTags(raw))
                {
                    startingTags.Add(tag);
                    grantedSoFar.Add(tag);
                    allArchGrantedTags.Add(tag);
                }
            }
        }

        if (data.Modules.UseGender)
        {
            if (arch.GenderOdds != null && arch.GenderOdds.Count > 0)
            {
                if (arch.GenderOdds.Values.Where(v => v > 0).Sum(v => (long)v) <= 0)
                    warnings.Add($"[{aName}] GenderOdds has no positive weights; will fall back to 'Neutral'.");

                foreach (var gender in arch.GenderOdds.Where(g => g.Value > 0 && !string.IsNullOrWhiteSpace(g.Key)).Select(g => g.Key))
                {
                    grantedSoFar.Add(gender);
                    allArchGrantedTags.Add(gender);
                }

                if (arch.GenderOdds.Values.All(v => v <= 0))
                {
                    grantedSoFar.Add("Neutral");
                    allArchGrantedTags.Add("Neutral");
                }
            }
            else
            {
                grantedSoFar.Add("Neutral");
                allArchGrantedTags.Add("Neutral");
            }
        }

        // Helper to scan any option list in generation order for tag dependencies,
        // impossible exclusions, and disabled weights.
        void ScanOptions(string sectionLabel, List<JsonElement>? elements, bool allowSelfPoolGrants = false)
        {
            if (elements == null) return;

            var resolved = elements.Select(ResolveElement).ToList();
            var sectionGrants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in resolved)
            {
                if (item.Weight <= 0) continue;
                foreach (var g in ParseTags(item.GrantsTag))
                    sectionGrants.Add(g);
            }

            foreach (var item in resolved)
            {
                string itemLabel = string.IsNullOrEmpty(item.Value) ? "(null/empty)" : item.Value;

                if (item.Weight <= 0)
                    warnings.Add($"[{aName}/{sectionLabel}] Disabled Option: '{itemLabel}' has Weight <= 0 and will never roll.");

                foreach (var r in ParseTags(item.Requires))
                {
                    bool available = grantedSoFar.Contains(r) || (allowSelfPoolGrants && sectionGrants.Contains(r));
                    reqChecks.Add((sectionLabel, itemLabel, r, available));
                }

                foreach (var exc in ParseTags(item.Excludes))
                {
                    if (startingTags.Contains(exc))
                        warnings.Add($"[{aName}/{sectionLabel}] Unreachable Option: '{itemLabel}' excludes StartingTag '{exc}'.");
                }

                if (item.StatMods != null) statModTargets.AddRange(item.StatMods.Keys);
            }

            foreach (var g in sectionGrants)
            {
                grantedSoFar.Add(g);
                allArchGrantedTags.Add(g);
            }
        }

        ScanOptions("FirstNames", arch.FirstNames);
        ScanOptions("LastNames", arch.LastNames);
        ScanOptions("Nicknames", arch.Nicknames);

        if (arch.MandatoryTraits != null)
        {
            foreach (var trait in arch.MandatoryTraits)
            {
                allKnownStats.Add(trait.Key);
                allKnownTokens.Add(trait.Key);
                ScanOptions(trait.Key, trait.Value);
            }
        }

        if (arch.OptionalTraits != null)
        {
            foreach (var optTrait in arch.OptionalTraits)
            {
                allKnownStats.Add(optTrait.Key);
                allKnownTokens.Add(optTrait.Key);
                if (optTrait.Value == null) continue;

                if (optTrait.Value.Chance < 0 || optTrait.Value.Chance > 1)
                    warnings.Add($"[{aName}/{optTrait.Key}] Chance ({optTrait.Value.Chance}) should be between 0.0 and 1.0.");

                ScanOptions(optTrait.Key, optTrait.Value.Options);
            }
        }

        // Validate MultiTraits when enabled.
        if (data.Modules.UseMultiTraits && arch.MultiTraits != null)
        {
            foreach (var mtEntry in arch.MultiTraits)
            {
                allKnownTokens.Add(mtEntry.Key);
                var mt = mtEntry.Value;
                if (mt == null || mt.Options == null || mt.Options.Count == 0)
                {
                    warnings.Add($"[{aName}] MultiTrait '{mtEntry.Key}' has no options defined.");
                    continue;
                }

                if (mt.Chance < 0 || mt.Chance > 1)
                    warnings.Add($"[{aName}/{mtEntry.Key}] Chance ({mt.Chance}) should be between 0.0 and 1.0.");

                if (mt.MinPicks < 0 || mt.MaxPicks < 0)
                    warnings.Add($"[{aName}/{mtEntry.Key}] MinPicks/MaxPicks cannot be negative.");
                else if (mt.MinPicks > mt.MaxPicks)
                    warnings.Add($"[{aName}/{mtEntry.Key}] MinPicks ({mt.MinPicks}) exceeds MaxPicks ({mt.MaxPicks}).");

                foreach (var r in ParseTags(mt.Requires))
                    reqChecks.Add((mtEntry.Key, $"Pool '{mtEntry.Key}'", r, grantedSoFar.Contains(r)));

                foreach (var exc in ParseTags(mt.Excludes))
                {
                    if (startingTags.Contains(exc))
                        warnings.Add($"[{aName}/{mtEntry.Key}] Unreachable MultiTrait: Excludes StartingTag '{exc}'.");
                }

                int positiveOptions = mt.Options.Select(ResolveElement).Count(o => o.Weight > 0);
                if (!mt.AllowDuplicates && mt.MinPicks > positiveOptions)
                {
                    warnings.Add($"[{aName}/{mtEntry.Key}] MinPicks ({mt.MinPicks}) exceeds unique valid options ({positiveOptions}) while AllowDuplicates is false.");
                }

                bool canChainWithinPool = Math.Max(mt.MinPicks, mt.MaxPicks) > 1;
                ScanOptions(mtEntry.Key, mt.Options, allowSelfPoolGrants: canChainWithinPool);
            }
        }

        // Validate independent Numeric Ranges when enabled.
        if (data.Modules.UseRanges && arch.Ranges != null)
        {
            foreach (var rangeEntry in arch.Ranges)
            {
                var r = rangeEntry.Value;
                if (r == null)
                {
                    warnings.Add($"[{aName}] Range '{rangeEntry.Key}' is empty/null.");
                    continue;
                }

                allKnownStats.Add(rangeEntry.Key);
                allKnownTokens.Add(rangeEntry.Key);
                allKnownTokens.Add($"{rangeEntry.Key}_Raw");
                archNumericStats.Add(rangeEntry.Key);

                if (r.Min > r.Max)
                    warnings.Add($"[{aName}] Range Error: '{rangeEntry.Key}' Min ({r.Min}) exceeds Max ({r.Max}).");
                if (r.Step <= 0)
                    warnings.Add($"[{aName}] Range Error: '{rangeEntry.Key}' Step ({r.Step}) must be greater than 0.");

                foreach (var req in ParseTags(r.Requires))
                    reqChecks.Add(("Ranges", rangeEntry.Key, req, grantedSoFar.Contains(req)));

                foreach (var exc in ParseTags(r.Excludes))
                {
                    if (startingTags.Contains(exc))
                        warnings.Add($"[{aName}] Unreachable Range: '{rangeEntry.Key}' excludes StartingTag '{exc}'.");
                }
            }
        }

        foreach (var check in reqChecks.Distinct())
        {
            if (!allArchGrantedTags.Contains(check.ReqTag))
            {
                warnings.Add($"[{aName}/{check.Section}] Dead Item: '{check.ItemLabel}' requires '{check.ReqTag}', but nothing in the archetype grants this tag.");
            }
            else if (!check.AvailableAtStage)
            {
                warnings.Add($"[{aName}/{check.Section}] Order Error: '{check.ItemLabel}' requires '{check.ReqTag}', which is only granted later in the generation order.");
            }
        }

        foreach (var tag in allArchGrantedTags) allGrantedTags.Add(tag);

        // Validate Point Pool math and register known stat keys.
        if (data.Modules.UsePointPools && arch.PointPools != null)
        {
            foreach (var pool in arch.PointPools)
            {
                var validAttrs = pool.Value?.Attributes?
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (pool.Value == null || validAttrs == null || validAttrs.Count == 0)
                {
                    warnings.Add($"[{aName}] Point Pool '{pool.Key}' has no attributes defined.");
                    continue;
                }

                foreach (var attr in validAttrs)
                {
                    allKnownStats.Add(attr);
                    allKnownStats.Add($"{pool.Key}: {attr}");
                    allKnownTokens.Add(attr);
                    allKnownTokens.Add($"{pool.Key}: {attr}");
                    archNumericStats.Add(attr);
                    archNumericStats.Add($"{pool.Key}: {attr}");
                }

                int attrCount = validAttrs.Count;
                if (pool.Value.MinPerAttribute > pool.Value.MaxPerAttribute)
                {
                    warnings.Add($"[{aName}] Math Error: '{pool.Key}' MinPerAttribute ({pool.Value.MinPerAttribute}) exceeds MaxPerAttribute ({pool.Value.MaxPerAttribute}).");
                    continue;
                }

                long minPossible = (long)pool.Value.MinPerAttribute * attrCount;
                long maxPossible = (long)pool.Value.MaxPerAttribute * attrCount;

                if (pool.Value.TotalPoints > maxPossible)
                {
                    warnings.Add($"[{aName}] Math Error: '{pool.Key}' Budget ({pool.Value.TotalPoints}) exceeds Max Capacity ({maxPossible}).");
                }
                else if (pool.Value.TotalPoints < minPossible)
                {
                    warnings.Add($"[{aName}] Math Error: '{pool.Key}' Budget ({pool.Value.TotalPoints}) is below Min Floor ({minPossible}).");
                }
            }
        }

        // Warn if a trait defines a StatMod targeting a stat that doesn't exist in PointPools or Ranges.
        if (data.Modules.UsePointPools || data.Modules.UseRanges)
        {
            foreach (var modTarget in statModTargets.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!archNumericStats.Contains(modTarget))
                {
                    warnings.Add($"[{aName}] Unused StatMod: Targets '{modTarget}', which matches no PointPool attribute or Range.");
                }
            }
        }
    }

    // Validate narrative BioTemplates against all tags, stats, and tokens in the universe.
    if (data.Modules.UseNarrative && data.BioTemplates != null)
    {
        foreach (var category in data.BioTemplates)
        {
            if (category.Value == null) continue;
            foreach (var template in category.Value)
            {
                if (template == null) continue;

                if (template.Weight <= 0)
                    warnings.Add($"[BioTemplates/{category.Key}] Disabled Template: Has Weight <= 0 and will never roll.");

                foreach (var req in ParseTags(template.Requires))
                {
                    if (!allGrantedTags.Contains(req))
                        warnings.Add($"[BioTemplates/{category.Key}] Dead Template: Requires tag '{req}', which no archetype grants.");
                }

                if (!string.IsNullOrEmpty(template.MinStatName) && !allKnownStats.Contains(template.MinStatName))
                    warnings.Add($"[BioTemplates/{category.Key}] Dead MinStat Gate: Checks '{template.MinStatName}', which matches no trait, range, or stat.");

                if (!string.IsNullOrEmpty(template.MaxStatName))
                {
                    if (!allKnownStats.Contains(template.MaxStatName))
                        warnings.Add($"[BioTemplates/{category.Key}] Dead MaxStat Gate: Checks '{template.MaxStatName}', which matches no trait, range, or stat.");
                    else if (!template.MaxStatValue.HasValue)
                        warnings.Add($"[BioTemplates/{category.Key}] Incomplete MaxStat Gate: '{template.MaxStatName}' has no MaxStatValue set and will be ignored.");
                }

                if (!string.IsNullOrEmpty(template.MinStatName) && !string.IsNullOrEmpty(template.MaxStatName)
                    && string.Equals(template.MinStatName, template.MaxStatName, StringComparison.OrdinalIgnoreCase)
                    && template.MaxStatValue.HasValue && template.MinStatValue > template.MaxStatValue.Value)
                {
                    warnings.Add($"[BioTemplates/{category.Key}] Impossible Stat Gate: '{template.MinStatName}' requires >= {template.MinStatValue} and <= {template.MaxStatValue}.");
                }

                if (!string.IsNullOrEmpty(template.Text))
                {
                    foreach (Match m in Regex.Matches(template.Text, @"\{([^{}]+)\}"))
                    {
                        string token = m.Groups[1].Value.Trim();
                        if (!allKnownTokens.Contains(token))
                            warnings.Add($"[BioTemplates/{category.Key}] Unknown Token: '{{{token}}}' matches no built-in token, trait, range, or stat.");
                    }
                }
            }
        }
    }

    if (warnings.Count > 0)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"\n[!] {warnings.Count} WARNING(S) DETECTED:");
        foreach (var w in warnings) Console.WriteLine($" - {w}");
        Console.ResetColor();
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\n[OK] Diagnostics Passed: No logic loops or dead references detected.");
        Console.ResetColor();
    }
    Console.WriteLine("--------------------------------------------------");
}

/// <summary>
/// Lists available .json universe files and lets the user choose one to load.
/// </summary>
string? SelectUniverse()
{
    string folder = "UniverseFiles";
    if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

    string[] files = Directory.GetFiles(folder, "*.json").OrderBy(f => f).ToArray();
    if (files.Length == 0)
    {
        ClearScreen();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\n[!] No universe .json files found in 'UniverseFiles'.");
        Console.ResetColor();
        Console.WriteLine("Use Architect Mode to generate a starter module, or drop a .json file into the folder.");
        Console.WriteLine("\nPress any key to return to the Main Menu...");
        FlushInputBuffer();
        Console.ReadKey(true);
        return null;
    }

    List<string> options = files.Select(f => Path.GetFileNameWithoutExtension(f)!).ToList();
    options.Add("[ Back to Main Menu ]");

    int choice = DrawInteractiveMenu("--- SELECT A UNIVERSE MODULE ---", options.ToArray());
    if (choice < 0 || choice == options.Count - 1) return null;
    return files[choice];
}

/// <summary>
/// Displays all .txt manuals inside the Docs folder as an interactive scrollable reader.
/// </summary>
void ShowInstructions()
{
    string folder = "Docs";
    if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

    while (true)
    {
        string[] files = Directory.GetFiles(folder, "*.txt").OrderBy(f => f).ToArray();
        List<string> options = files.Select(f => Path.GetFileNameWithoutExtension(f).Replace("_", " ")).ToList();
        options.Add("[ Back to Main Menu ]");

        int choice = DrawInteractiveMenu("--- REALITY ENGINE HELP LIBRARY ---", options.ToArray());
        if (choice < 0 || choice == options.Count - 1) break;

        ClearScreen(scrollable: true);
        try
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine(File.ReadAllText(files[choice]));
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[ERROR] Could not read file: {ex.Message}");
            Console.ResetColor();
        }

        Console.WriteLine("\n--------------------------------------------------");
        Console.WriteLine(">> Press any key to return to the Help Library...");
        FlushInputBuffer();
        Console.ReadKey(true);
    }
}

/// <summary>
/// In-app browser for reading previously saved .txt records, rosters, and .json exports under Saves/.
/// </summary>
void BrowseSaveArchive()
{
    string baseFolder = "Saves";
    if (!Directory.Exists(baseFolder)) Directory.CreateDirectory(baseFolder);

    while (true)
    {
        string[] universeDirs = Directory.GetDirectories(baseFolder).OrderBy(d => d).ToArray();
        if (universeDirs.Length == 0)
        {
            ClearScreen();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n[!] Your Save Archive is currently empty.");
            Console.ResetColor();
            Console.WriteLine("Generate a character and press [S] or [J] to archive it here.");
            Console.WriteLine("\nPress any key to return to the Main Menu...");
            FlushInputBuffer();
            Console.ReadKey(true);
            return;
        }

        List<string> folderOptions = universeDirs.Select(Path.GetFileName).Where(n => n != null).Select(n => n!).ToList();
        folderOptions.Add("[ Back to Main Menu ]");

        int folderChoice = DrawInteractiveMenu("--- SAVE ARCHIVE: SELECT UNIVERSE ---", folderOptions.ToArray());
        if (folderChoice < 0 || folderChoice == folderOptions.Count - 1) return;

        string chosenDir = universeDirs[folderChoice];
        string chosenUniverseName = Path.GetFileName(chosenDir);

        // Browse individual files inside the chosen universe folder (including Rosters/).
        while (true)
        {
            var savedFiles = Directory.GetFiles(chosenDir, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTime)
                .ToList();

            if (savedFiles.Count == 0)
            {
                ClearScreen();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"\n[!] No saved files found in '{chosenUniverseName}'.");
                Console.ResetColor();
                Console.WriteLine("Press any key to go back...");
                FlushInputBuffer();
                Console.ReadKey(true);
                break;
            }

            List<string> fileOptions = savedFiles.Select(f =>
            {
                string rel = Path.GetRelativePath(chosenDir, f);
                string ext = Path.GetExtension(f).ToUpperInvariant();
                return $"[{ext.TrimStart('.')}] {rel}";
            }).ToList();
            fileOptions.Add("[ Back to Universes ]");

            int fileChoice = DrawInteractiveMenu($"--- ARCHIVE: {chosenUniverseName} ---", fileOptions.ToArray());
            if (fileChoice < 0 || fileChoice == fileOptions.Count - 1) break;

            ClearScreen(scrollable: true);
            try
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"FILE: {Path.GetFileName(savedFiles[fileChoice])}");
                Console.WriteLine("------------------------------------------------------------------");
                Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine(File.ReadAllText(savedFiles[fileChoice]));
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ERROR] Could not read file: {ex.Message}");
                Console.ResetColor();
            }

            Console.WriteLine("\n------------------------------------------------------------------");
            Console.WriteLine(">> Press any key to return to the Archive...");
            FlushInputBuffer();
            Console.ReadKey(true);
        }
    }
}


// =========================================================================================
// SECTION 6: THE GENERATOR ENGINE, SEEDED ROLLS & BATCH ROSTERS
// =========================================================================================

/// <summary>
/// Prints step-by-step roll logs when Debug Mode is active and output is not suppressed.
/// </summary>
void LogDebug(string message, bool quietMode = false)
{
    if (!isDebugMode || quietMode) return;
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.Write("[DEBUG] ");
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine(message);
    Console.ResetColor();
    Thread.Sleep(60);
}

/// <summary>
/// Lets the user select a specific valid archetype or [ Random Archetype ] from the active module.
/// </summary>
(bool Cancelled, bool IsRandom, string? ArchetypeName) SelectArchetype(UniverseData data, string subtitle)
{
    var validKeys = data.Archetypes?
        .Where(kvp => kvp.Value != null)
        .Select(kvp => kvp.Key)
        .ToList() ?? new List<string>();

    if (validKeys.Count == 0) return (true, false, null);

    List<string> typeOptions = new List<string> { "[ Random Archetype ]" };
    typeOptions.AddRange(validKeys);
    typeOptions.Add("[ Back to Main Menu ]");

    int choice = DrawInteractiveMenu($"--- {data.UniverseName} ---\n{subtitle}", typeOptions.ToArray());
    if (choice < 0 || choice == typeOptions.Count - 1)
        return (true, false, null);

    if (choice == 0)
        return (false, true, null);

    return (false, false, typeOptions[choice]);
}

/// <summary>
/// Prompts for a custom numeric or text seed, then launches the entity generation loop with it.
/// </summary>
void GenerateFromSeedFlow(UniverseData data)
{
    ClearScreen();
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("==================================================");
    Console.WriteLine("            REPRODUCIBLE SEED GENERATOR           ");
    Console.WriteLine("==================================================");
    Console.ResetColor();
    Console.WriteLine("Enter a numeric seed from a previous sheet (or any word/phrase).");

    string rawSeed = PromptUser("Enter Seed (leave blank to cancel):");
    if (string.IsNullOrWhiteSpace(rawSeed)) return;

    int parsedSeed = int.TryParse(rawSeed, out int numericSeed)
        ? numericSeed
        : DeterministicHash(rawSeed);

    GenerateEntityFlow(data, forcedSeed: parsedSeed);
}

/// <summary>
/// Interactive single-entity flow supporting instant [R] Reroll of the same archetype (or random pool).
/// </summary>
void GenerateEntityFlow(UniverseData data, int? forcedSeed = null)
{
    var selection = SelectArchetype(data, $"Select an {data.ArchetypeTitle}:");
    if (selection.Cancelled) return;

    // Sort valid keys so seeded random-archetype picks are deterministic across runs.
    List<string> sortedArchetypeKeys = data.Archetypes!
        .Where(kvp => kvp.Value != null)
        .Select(kvp => kvp.Key)
        .OrderBy(k => k, StringComparer.Ordinal)
        .ToList();

    while (true)
    {
        int currentSeed = forcedSeed ?? masterRand.Next(100000, 9999999);

        // Derive archetype choice from an independent salted RNG so BuildEntity always starts
        // with a clean Random(currentSeed) regardless of whether Random Archetype was selected.
        string targetArchetype = selection.IsRandom
            ? sortedArchetypeKeys[new Random(currentSeed ^ 0x5F3759DF).Next(sortedArchetypeKeys.Count)]
            : selection.ArchetypeName!;

        Random entityRand = new Random(currentSeed);
        GeneratedEntity entity = BuildEntity(data, targetArchetype, currentSeed, entityRand, quietMode: false);

        if (isDebugMode)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n[ GENERATION COMPLETE ]");
            Console.ResetColor();
            Console.WriteLine("Press any key to view the formatted Character Sheet...");
            FlushInputBuffer();
            Console.ReadKey(true);
        }

        // DisplayEntity returns true if the user pressed [R] to reroll.
        bool reroll = DisplayEntity(entity, data);
        if (!reroll) break;

        // Clear any one-time forced seed so subsequent rerolls produce fresh characters.
        forcedSeed = null;
    }
}

/// <summary>
/// Generates a roster of N entities and exports all of them into a single timestamped .txt file.
/// </summary>
void BatchGenerateFlow(UniverseData data)
{
    var selection = SelectArchetype(data, $"Batch Roster - Select an {data.ArchetypeTitle}:");
    if (selection.Cancelled) return;

    ClearScreen();
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"--- BATCH ROSTER GENERATOR ({data.UniverseName}) ---");
    Console.ResetColor();

    string rawCount = PromptUser("How many entities would you like to generate? (1-500):");
    if (!int.TryParse(rawCount, out int count) || count < 1 || count > 500)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\n[!] Invalid amount. Please enter a number between 1 and 500.");
        Console.ResetColor();
        Thread.Sleep(1200);
        return;
    }

    List<string> sortedArchetypeKeys = data.Archetypes!
        .Where(kvp => kvp.Value != null)
        .Select(kvp => kvp.Key)
        .OrderBy(k => k, StringComparer.Ordinal)
        .ToList();

    List<GeneratedEntity> roster = new List<GeneratedEntity>(count);
    HashSet<int> usedSeeds = new HashSet<int>();

    for (int i = 0; i < count; i++)
    {
        int seed;
        do
        {
            seed = masterRand.Next(100000, 9999999);
        } while (!usedSeeds.Add(seed));

        string targetArchetype = selection.IsRandom
            ? sortedArchetypeKeys[new Random(seed ^ 0x5F3759DF).Next(sortedArchetypeKeys.Count)]
            : selection.ArchetypeName!;

        roster.Add(BuildEntity(data, targetArchetype, seed, new Random(seed), quietMode: true));
    }

    try
    {
        string universeDir = Path.Combine("Saves", SanitizeFileName(data.UniverseName, "Unknown_Universe"));
        string rosterDir = Path.Combine(universeDir, "Rosters");
        if (!Directory.Exists(rosterDir)) Directory.CreateDirectory(rosterDir);

        string label = selection.IsRandom ? "Mixed" : SanitizeFileName(selection.ArchetypeName, "Roster");
        string baseName = $"Roster_{label}_{count}x_{DateTime.Now:yyyyMMdd_HHmmss}";
        string fullPath = GetUniqueFilePath(rosterDir, baseName, ".txt");

        using (StreamWriter writer = new StreamWriter(fullPath))
        {
            writer.WriteLine("##################################################################");
            writer.WriteLine($"  REALITY ENGINE ROSTER EXPORT: {data.UniverseName.ToUpper()}");
            writer.WriteLine($"  GENERATED: {DateTime.Now:yyyy-MM-dd HH:mm:ss}  |  COUNT: {count}");
            writer.WriteLine("##################################################################\n");

            for (int i = 0; i < roster.Count; i++)
            {
                writer.WriteLine($"[ ENTRY #{i + 1} ]");
                writer.WriteLine(FormatEntityText(roster[i], data.Modules));
                writer.WriteLine();
            }
        }

        ClearScreen(scrollable: true);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[SUCCESS] Generated {count} entities and exported roster to:");
        Console.WriteLine($"  {fullPath}\n");
        Console.ResetColor();

        // Preview up to the first 15 entries directly in the console.
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"{"#",-4} {"NAME",-28} {data.ArchetypeTitle.ToUpper(),-16} {"SEED",-10}");
        Console.WriteLine(new string('-', 62));
        Console.ResetColor();

        int previewLimit = Math.Min(15, roster.Count);
        for (int i = 0; i < previewLimit; i++)
        {
            var e = roster[i];
            string shortName = e.FullName.Length > 26 ? e.FullName[..25] + "…" : e.FullName;
            string shortArch = e.Archetype.Length > 15 ? e.Archetype[..14] + "…" : e.Archetype;
            Console.WriteLine($"{i + 1,-4} {shortName,-28} {shortArch,-16} {e.Seed,-10}");
        }
        if (roster.Count > previewLimit)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"... and {roster.Count - previewLimit} more entries saved in the file.");
            Console.ResetColor();
        }
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"\n[ERROR] Failed to export batch roster: {ex.Message}");
        Console.ResetColor();
    }

    Console.WriteLine("\nPress any key to return to the Main Menu...");
    FlushInputBuffer();
    Console.ReadKey(true);
}

/// <summary>
/// Core deterministic entity builder. Given a module, archetype, and seeded Random,
/// rolls identity, traits, multi-traits, ranges, point pools, stat modifiers, and narrative text.
/// </summary>
GeneratedEntity BuildEntity(UniverseData data, string selectedTypeName, int seed, Random rand, bool quietMode = false)
{
    GeneratedEntity entity = new GeneratedEntity
    {
        Seed = seed,
        Archetype = selectedTypeName
    };

    // Case-insensitive tag tracking prevents missed gates due to capitalization typos.
    var activeTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var pendingStatMods = new List<(string Source, Dictionary<string, int> Mods)>();

    if (!quietMode)
    {
        ClearScreen(scrollable: isDebugMode);
        if (isDebugMode)
            Console.WriteLine($"=== INITIATING GENERATION SEQUENCE: {selectedTypeName.ToUpper()} (SEED: {seed}) ===\n");
    }

    EntityArchetype archetype = (data.Archetypes != null && data.Archetypes.TryGetValue(selectedTypeName, out var foundArch) && foundArch != null)
        ? foundArch
        : new EntityArchetype();

    // 1. Seed initial archetype tags (supporting comma-separated entries).
    if (archetype.StartingTags != null && archetype.StartingTags.Count > 0)
    {
        foreach (var rawTag in archetype.StartingTags)
        {
            foreach (var tag in ParseTags(rawTag))
                activeTags.Add(tag);
        }
        if (activeTags.Count > 0)
            LogDebug($"Applied Starting Tags: {string.Join(", ", activeTags)}", quietMode);
    }

    entity.Attributes[data.ArchetypeTitle] = selectedTypeName;

    // 2. Roll gender if UseGender is enabled; otherwise omit gender and infer neutral/inanimate pronouns.
    if (data.Modules.UseGender)
    {
        if (archetype.GenderOdds != null)
        {
            var validGenders = archetype.GenderOdds.Where(g => g.Value > 0 && !string.IsNullOrWhiteSpace(g.Key)).ToList();
            long totalGenderWeight = validGenders.Sum(g => (long)g.Value);

            if (totalGenderWeight > 0)
            {
                long genderRoll = rand.NextInt64(0, totalGenderWeight);
                long cursor = 0;
                foreach (var entry in validGenders)
                {
                    cursor += entry.Value;
                    if (genderRoll < cursor)
                    {
                        entity.Gender = entry.Key;
                        break;
                    }
                }
            }
        }
        if (string.IsNullOrWhiteSpace(entity.Gender)) entity.Gender = "Neutral";

        activeTags.Add(entity.Gender);
        AssignPronouns(entity, activeTags, selectedTypeName);
        LogDebug($"Rolled Gender: {entity.Gender} (Added to Tags)", quietMode);
    }
    else
    {
        entity.Gender = null;
        AssignPronouns(entity, activeTags, selectedTypeName);
        LogDebug("Gender: Disabled by module settings (UseGender = false)", quietMode);
    }

    // 3. Roll names (supports single-name archetypes cleanly without inserting "N/A").
    SmartOption pickedFirst = PickSmart("First Name", archetype.FirstNames, activeTags, rand, quietMode, emptyFallback: "Unnamed", blockedFallback: "Unnamed");
    entity.FirstName = pickedFirst.Value;
    if (pickedFirst.StatMods != null) pendingStatMods.Add(("First Name", pickedFirst.StatMods));

    SmartOption pickedLast = PickSmart("Last Name", archetype.LastNames, activeTags, rand, quietMode, emptyFallback: "", blockedFallback: "");
    entity.LastName = pickedLast.Value;
    if (pickedLast.StatMods != null) pendingStatMods.Add(("Last Name", pickedLast.StatMods));

    if (archetype.Nicknames != null && archetype.Nicknames.Count > 0 && rand.NextDouble() < archetype.NicknameChance)
    {
        SmartOption pickedNick = PickSmart("Nickname", archetype.Nicknames, activeTags, rand, quietMode, emptyFallback: "", blockedFallback: "");
        entity.Nickname = pickedNick.Value;
        if (pickedNick.StatMods != null) pendingStatMods.Add(("Nickname", pickedNick.StatMods));
    }
    else
    {
        entity.Nickname = "";
        LogDebug("Nickname: Skipped (None defined or failed probability roll)", quietMode);
    }

    // 4. Roll Mandatory Traits (always rolled).
    if (archetype.MandatoryTraits != null)
    {
        foreach (var trait in archetype.MandatoryTraits)
        {
            SmartOption picked = PickSmart(trait.Key, trait.Value, activeTags, rand, quietMode, emptyFallback: "N/A", blockedFallback: "None");
            RecordTrait(entity, trait.Key, picked, data.Modules.ShowRarity, pendingStatMods);
        }
    }

    // 5. Roll Optional Traits (rolled only when passing their probability check and having eligible options).
    if (archetype.OptionalTraits != null)
    {
        foreach (var trait in archetype.OptionalTraits)
        {
            if (trait.Value != null && rand.NextDouble() < trait.Value.Chance)
            {
                SmartOption picked = PickSmart(trait.Key, trait.Value.Options, activeTags, rand, quietMode, emptyFallback: "", blockedFallback: "");
                if (!string.IsNullOrEmpty(picked.Value))
                    RecordTrait(entity, trait.Key, picked, data.Modules.ShowRarity, pendingStatMods);
                else
                    LogDebug($"Optional Trait '{trait.Key}': Skipped (No eligible options)", quietMode);
            }
            else
            {
                LogDebug($"Optional Trait '{trait.Key}': Skipped (Failed probability roll)", quietMode);
            }
        }
    }

    // 6. Roll MultiTraits (inventories, loadouts, quirks - multiple picks from one pool).
    if (data.Modules.UseMultiTraits && archetype.MultiTraits != null)
    {
        foreach (var mtEntry in archetype.MultiTraits)
        {
            var mt = mtEntry.Value;
            if (mt == null || mt.Options == null || mt.Options.Count == 0) continue;

            // Check pool-level Requires/Excludes and Chance before rolling picks.
            if (!ParseTags(mt.Requires).All(activeTags.Contains) || ParseTags(mt.Excludes).Any(activeTags.Contains))
            {
                LogDebug($"MultiTrait '{mtEntry.Key}': Skipped (Tag gate not met)", quietMode);
                continue;
            }

            if (rand.NextDouble() >= mt.Chance)
            {
                LogDebug($"MultiTrait '{mtEntry.Key}': Skipped (Failed probability roll)", quietMode);
                continue;
            }

            int minPicks = Math.Max(0, Math.Min(mt.MinPicks, mt.MaxPicks));
            int maxPicks = Math.Max(0, Math.Max(mt.MinPicks, mt.MaxPicks));
            int targetPicks = (int)rand.NextInt64(minPicks, (long)maxPicks + 1);

            var workingPool = mt.Options.Select(ResolveElement).ToList();
            var pickedValues = new List<string>();
            var pickedRarities = new List<string>();

            for (int p = 0; p < targetPicks; p++)
            {
                var eligible = workingPool.Where(o =>
                    o.Weight > 0
                    && ParseTags(o.Requires).All(activeTags.Contains)
                    && !ParseTags(o.Excludes).Any(activeTags.Contains)).ToList();

                if (eligible.Count == 0) break;

                long totalWeight = eligible.Sum(o => (long)o.Weight);
                long roll = rand.NextInt64(0, totalWeight);
                long cursor = 0;
                SmartOption chosen = eligible[0];

                foreach (var opt in eligible)
                {
                    cursor += opt.Weight;
                    if (roll < cursor)
                    {
                        chosen = opt;
                        break;
                    }
                }

                pickedValues.Add(chosen.Value);
                pickedRarities.Add(data.Modules.ShowRarity ? (chosen.Rarity ?? "") : "");

                var newlyGranted = new List<string>();
                foreach (var tag in ParseTags(chosen.GrantsTag))
                {
                    if (activeTags.Add(tag)) newlyGranted.Add(tag);
                }

                if (chosen.StatMods != null && chosen.StatMods.Count > 0)
                    pendingStatMods.Add(($"{mtEntry.Key}: {chosen.Value}", chosen.StatMods));

                if (newlyGranted.Count > 0)
                    LogDebug($"Rolled {mtEntry.Key} [{p + 1}/{targetPicks}]: {chosen.Value} (Granted: {string.Join(", ", newlyGranted)})", quietMode);
                else
                    LogDebug($"Rolled {mtEntry.Key} [{p + 1}/{targetPicks}]: {chosen.Value}", quietMode);

                if (!mt.AllowDuplicates)
                    workingPool.Remove(chosen);
            }

            if (pickedValues.Count > 0)
            {
                entity.MultiTraits[mtEntry.Key] = pickedValues;
                entity.MultiTraitRarities[mtEntry.Key] = pickedRarities;
            }
        }
    }

    // 7. Roll independent Numeric Ranges (Age, Bounty, Crew Size, etc.).
    var rangeMinFloors = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    if (data.Modules.UseRanges && archetype.Ranges != null)
    {
        foreach (var rangeEntry in archetype.Ranges)
        {
            var r = rangeEntry.Value;
            if (r == null) continue;

            if (!ParseTags(r.Requires).All(activeTags.Contains) || ParseTags(r.Excludes).Any(activeTags.Contains))
            {
                LogDebug($"Range '{rangeEntry.Key}': Skipped (Tag gate not met)", quietMode);
                continue;
            }

            int minVal = Math.Min(r.Min, r.Max);
            int maxVal = Math.Max(r.Min, r.Max);
            int step = Math.Max(1, r.Step);

            long stepsCount = ((long)maxVal - minVal) / step + 1;
            int rolled = (int)(minVal + rand.NextInt64(0, stepsCount) * step);

            entity.Ranges[rangeEntry.Key] = rolled;
            rangeMinFloors[rangeEntry.Key] = minVal;

            if (!string.IsNullOrEmpty(r.Unit))
                entity.RangeUnits[rangeEntry.Key] = r.Unit;

            LogDebug($"Rolled Range '{rangeEntry.Key}': {FormatRangeValue(rolled, r.Unit)}", quietMode);
        }
    }

    // 8. Distribute RPG Point Pools within min/max constraints.
    var poolMinFloors = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    if (data.Modules.UsePointPools && archetype.PointPools != null)
    {
        LogDebug("Calculating RPG Point Pools...", quietMode);
        foreach (var poolEntry in archetype.PointPools)
        {
            PointPool? pool = poolEntry.Value;
            if (pool?.Attributes == null || pool.Attributes.Count == 0) continue;

            var uniqueAttrs = pool.Attributes
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (uniqueAttrs.Count == 0) continue;

            int minVal = Math.Min(pool.MinPerAttribute, pool.MaxPerAttribute);
            int maxVal = Math.Max(pool.MinPerAttribute, pool.MaxPerAttribute);
            poolMinFloors[poolEntry.Key] = minVal;

            Dictionary<string, int> scores = uniqueAttrs.ToDictionary(a => a, _ => minVal, StringComparer.OrdinalIgnoreCase);
            long remaining = (long)pool.TotalPoints - ((long)uniqueAttrs.Count * minVal);

            while (remaining > 0)
            {
                if (scores.Values.All(v => v >= maxVal)) break;

                string target = uniqueAttrs[rand.Next(uniqueAttrs.Count)];
                if (scores[target] < maxVal)
                {
                    scores[target]++;
                    remaining--;
                }
            }

            entity.PointPools[poolEntry.Key] = scores;
            entity.PointPoolCaps[poolEntry.Key] = maxVal;
            LogDebug($"Pool [{poolEntry.Key}] Distributed points across {uniqueAttrs.Count} attributes.", quietMode);
        }
    }

    // 9. Apply any queued trait StatMods on top of Point Pools and/or Ranges.
    if (pendingStatMods.Count > 0 && (data.Modules.UsePointPools || data.Modules.UseRanges))
    {
        foreach (var (source, mods) in pendingStatMods)
        {
            foreach (var mod in mods)
            {
                // Apply to matching PointPool attributes.
                foreach (var pool in entity.PointPools)
                {
                    string? matchedStat = pool.Value.Keys.FirstOrDefault(
                        k => string.Equals(k, mod.Key, StringComparison.OrdinalIgnoreCase)
                          || string.Equals($"{pool.Key}: {k}", mod.Key, StringComparison.OrdinalIgnoreCase));

                    if (matchedStat != null)
                    {
                        long floor = poolMinFloors.GetValueOrDefault(pool.Key, 0) < 0 ? int.MinValue : 0L;
                        int updated = (int)Math.Clamp((long)pool.Value[matchedStat] + mod.Value, floor, int.MaxValue);
                        int actualDelta = updated - pool.Value[matchedStat];
                        pool.Value[matchedStat] = updated;

                        string modKey = $"{pool.Key}: {matchedStat}";
                        entity.StatModTotals[modKey] = entity.StatModTotals.GetValueOrDefault(modKey) + actualDelta;

                        string sign = actualDelta >= 0 ? "+" : "";
                        LogDebug($"StatMod ({source}): {matchedStat} {sign}{actualDelta} -> {updated}", quietMode);
                    }
                }

                // Apply to matching Numeric Ranges.
                string? matchedRange = entity.Ranges.Keys.FirstOrDefault(
                    k => string.Equals(k, mod.Key, StringComparison.OrdinalIgnoreCase));

                if (matchedRange != null)
                {
                    long floor = rangeMinFloors.GetValueOrDefault(matchedRange, 0) < 0 ? int.MinValue : 0L;
                    int updated = (int)Math.Clamp((long)entity.Ranges[matchedRange] + mod.Value, floor, int.MaxValue);
                    int actualDelta = updated - entity.Ranges[matchedRange];
                    entity.Ranges[matchedRange] = updated;

                    entity.StatModTotals[matchedRange] = entity.StatModTotals.GetValueOrDefault(matchedRange) + actualDelta;

                    string sign = actualDelta >= 0 ? "+" : "";
                    LogDebug($"StatMod ({source}): {matchedRange} {sign}{actualDelta} -> {updated}", quietMode);
                }
            }
        }
    }

    entity.Tags = activeTags.ToList();

    // 10. Assemble narrative from BioTemplates using tags, stat gates, and token filtering.
    if (data.Modules.UseNarrative)
    {
        LogDebug("Stitching Narrative using Tag, Stat & Token Gating...", quietMode);
        entity.Story = ConstructStory(entity, data, rand, quietMode);
        LogDebug("Narrative constructed successfully.", quietMode);
    }

    return entity;
}

/// <summary>
/// Stores a rolled trait's clean value, optional rarity metadata, and pending stat modifiers.
/// </summary>
static void RecordTrait(
    GeneratedEntity entity,
    string traitName,
    SmartOption picked,
    bool showRarity,
    List<(string Source, Dictionary<string, int> Mods)> pendingStatMods)
{
    entity.Attributes[traitName] = picked.Value;

    if (showRarity && !string.IsNullOrEmpty(picked.Rarity))
        entity.AttributeRarities[traitName] = picked.Rarity;
    else
        entity.AttributeRarities.Remove(traitName);

    if (picked.StatMods != null && picked.StatMods.Count > 0)
        pendingStatMods.Add(($"{traitName}: {picked.Value}", picked.StatMods));
}


// =========================================================================================
// SECTION 7: LOGIC GATES & RESOLVERS
// =========================================================================================

/// <summary>
/// Evaluates each BioTemplate section in order, filtering by multi-tag Requires/Excludes,
/// MinStat/MaxStat thresholds, and single-pass {Token} resolution before weighted selection.
/// </summary>
string? ConstructStory(GeneratedEntity entity, UniverseData data, Random rand, bool quietMode = false)
{
    if (data.BioTemplates == null) return null;

    var tagSet = new HashSet<string>(entity.Tags, StringComparer.OrdinalIgnoreCase);

    // Case-insensitive token map evaluated in a single pass to avoid chained replacements.
    var tokenMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["First"] = entity.FirstName,
        ["Nick"] = entity.NicknameOrFirst,
        ["Nickname"] = entity.NicknameOrFirst,
        ["Full"] = entity.FullName,
        ["Seed"] = entity.Seed.ToString()
    };

    if (!string.IsNullOrWhiteSpace(entity.LastName))
        tokenMap["Last"] = entity.LastName;

    if (!string.IsNullOrWhiteSpace(entity.Gender))
        tokenMap["Gender"] = entity.Gender;

    foreach (var p in entity.Pronouns) tokenMap[p.Key] = p.Value;
    foreach (var a in entity.Attributes) tokenMap[a.Key] = a.Value;

    foreach (var mt in entity.MultiTraits)
    {
        if (mt.Value != null && mt.Value.Count > 0)
            tokenMap[mt.Key] = JoinNatural(mt.Value);
    }

    foreach (var r in entity.Ranges)
    {
        string unit = entity.RangeUnits.GetValueOrDefault(r.Key, "");
        tokenMap[r.Key] = FormatRangeValue(r.Value, unit);
        tokenMap[$"{r.Key}_Raw"] = r.Value.ToString();
    }

    foreach (var pool in entity.PointPools)
    {
        foreach (var stat in pool.Value)
        {
            tokenMap[$"{pool.Key}: {stat.Key}"] = stat.Value.ToString();
            tokenMap[stat.Key] = stat.Value.ToString();
        }
    }

    List<string> storyBits = new List<string>();

    foreach (var category in data.BioTemplates)
    {
        if (category.Value == null) continue;

        var validCandidates = new List<(StoryTemplate Template, string ResolvedText)>();

        foreach (var t in category.Value)
        {
            if (t == null || t.Weight <= 0 || string.IsNullOrEmpty(t.Text)) continue;

            // Multi-tag Requires (AND) & Excludes (NOT ANY).
            var reqTags = ParseTags(t.Requires);
            var excTags = ParseTags(t.Excludes);
            if (!reqTags.All(tagSet.Contains)) continue;
            if (excTags.Any(tagSet.Contains)) continue;

            // High-stat gate (>=).
            if (!string.IsNullOrEmpty(t.MinStatName))
            {
                if (!TryGetStatScore(entity, t.MinStatName, out int minScore) || minScore < t.MinStatValue)
                    continue;
            }

            // Low-stat gate (<=).
            if (!string.IsNullOrEmpty(t.MaxStatName) && t.MaxStatValue.HasValue)
            {
                if (!TryGetStatScore(entity, t.MaxStatName, out int maxScore) || maxScore > t.MaxStatValue.Value)
                    continue;
            }

            // Resolve placeholders in a single pass; skip if any {Token} wasn't rolled or doesn't exist.
            var missingTokens = new List<string>();
            string resolved = Regex.Replace(t.Text, @"\{([^{}]+)\}", match =>
            {
                string key = match.Groups[1].Value.Trim();
                if (tokenMap.TryGetValue(key, out string? val))
                    return val ?? string.Empty;

                missingTokens.Add(match.Value);
                return match.Value;
            });

            if (missingTokens.Count > 0)
            {
                LogDebug($"Skipped template in '{category.Key}' due to unresolved token(s): {string.Join(", ", missingTokens)}", quietMode);
                continue;
            }

            validCandidates.Add((t, resolved));
        }

        if (validCandidates.Count == 0) continue;

        long totalWeight = validCandidates.Sum(c => (long)c.Template.Weight);
        long roll = rand.NextInt64(0, totalWeight);
        long cursor = 0;
        string pickedText = validCandidates[0].ResolvedText;

        foreach (var candidate in validCandidates)
        {
            cursor += candidate.Template.Weight;
            if (roll < cursor)
            {
                pickedText = candidate.ResolvedText;
                break;
            }
        }

        storyBits.Add(pickedText);
    }

    return string.Join(" ", storyBits);
}

/// <summary>
/// Looks up a numeric stat across PointPools first (supporting "Stat" and "Pool: Stat"),
/// then rolled Ranges, then numeric Attributes.
/// </summary>
static bool TryGetStatScore(GeneratedEntity entity, string statName, out int score)
{
    foreach (var pool in entity.PointPools)
    {
        foreach (var stat in pool.Value)
        {
            if (string.Equals(stat.Key, statName, StringComparison.OrdinalIgnoreCase)
                || string.Equals($"{pool.Key}: {stat.Key}", statName, StringComparison.OrdinalIgnoreCase))
            {
                score = stat.Value;
                return true;
            }
        }
    }

    var matchedRange = entity.Ranges.FirstOrDefault(r => string.Equals(r.Key, statName, StringComparison.OrdinalIgnoreCase));
    if (matchedRange.Key != null)
    {
        score = matchedRange.Value;
        return true;
    }

    var matchedAttr = entity.Attributes.FirstOrDefault(a => string.Equals(a.Key, statName, StringComparison.OrdinalIgnoreCase));
    if (matchedAttr.Key != null && int.TryParse(matchedAttr.Value, out score))
        return true;

    score = 0;
    return false;
}

/// <summary>
/// Maps the entity's rolled gender (or archetype/tags when UseGender is false) to lowercase
/// and capitalized Subject/Object/Possessive pronouns using whole-word matching.
/// </summary>
static void AssignPronouns(GeneratedEntity entity, HashSet<string> activeTags, string archetypeName)
{
    string[] inanimateKeywords =
    {
        "machine", "ai", "inert", "ship", "starship", "vessel", "vehicle",
        "robot", "weapon", "artifact", "android", "mech", "station", "planet", "location", "item"
    };

    static bool HasInanimateToken(string text, string[] keywords)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        string[] tokens = text.ToLowerInvariant().Split(new[] { ' ', '-', '_', '/' }, StringSplitOptions.RemoveEmptyEntries);
        return tokens.Any(t => keywords.Contains(t));
    }

    string gen = (entity.Gender ?? "").Trim().ToLowerInvariant();
    string subj, obj, poss;

    if (gen == "male" || gen == "man" || gen == "boy")
    {
        (subj, obj, poss) = ("he", "him", "his");
    }
    else if (gen == "female" || gen == "woman" || gen == "girl")
    {
        (subj, obj, poss) = ("she", "her", "her");
    }
    else if (HasInanimateToken(gen, inanimateKeywords)
             || (string.IsNullOrEmpty(gen) && (HasInanimateToken(archetypeName, inanimateKeywords)
                                            || activeTags.Any(t => HasInanimateToken(t, inanimateKeywords)))))
    {
        (subj, obj, poss) = ("it", "it", "its");
    }
    else
    {
        (subj, obj, poss) = ("they", "them", "their");
    }

    static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];

    entity.Pronouns = new Dictionary<string, string>
    {
        ["Subject"] = subj,
        ["Object"] = obj,
        ["Possessive"] = poss,
        ["SubjectCap"] = Cap(subj),
        ["ObjectCap"] = Cap(obj),
        ["PossessiveCap"] = Cap(poss)
    };
}

/// <summary>
/// Filters a list of mixed JSON options by multi-tag Requires/Excludes rules, performs a
/// weighted roll (safe against int overflow), and appends any newly granted tags to activeTags.
/// </summary>
SmartOption PickSmart(
    string categoryName,
    List<JsonElement>? elements,
    HashSet<string> activeTags,
    Random rand,
    bool quietMode = false,
    string emptyFallback = "N/A",
    string? blockedFallback = null)
{
    if (elements == null || elements.Count == 0)
        return new SmartOption { Value = emptyFallback };

    var options = elements.Select(ResolveElement).ToList();

    bool IsEligible(SmartOption opt)
    {
        if (opt.Weight <= 0) return false;
        var reqTags = ParseTags(opt.Requires);
        var excTags = ParseTags(opt.Excludes);
        return reqTags.All(activeTags.Contains)
            && !excTags.Any(activeTags.Contains);
    }

    if (isDebugMode && !quietMode)
    {
        var blocked = options.Where(o => o.Weight > 0 && !IsEligible(o)).Select(o => o.Value).ToList();
        if (blocked.Count > 0)
            LogDebug($"Blocked {categoryName} options via Requires/Excludes: {string.Join(", ", blocked)}", quietMode);
    }

    var validOptions = options.Where(IsEligible).ToList();
    if (validOptions.Count == 0)
        return new SmartOption { Value = blockedFallback ?? (string.IsNullOrEmpty(emptyFallback) ? "" : "None") };

    long totalWeight = validOptions.Sum(o => (long)o.Weight);
    long roll = rand.NextInt64(0, totalWeight);
    long cursor = 0;

    foreach (var opt in validOptions)
    {
        cursor += opt.Weight;
        if (roll < cursor)
        {
            var newlyGranted = new List<string>();
            foreach (var tag in ParseTags(opt.GrantsTag))
            {
                if (activeTags.Add(tag))
                    newlyGranted.Add(tag);
            }

            if (newlyGranted.Count > 0)
                LogDebug($"Rolled {categoryName}: {opt.Value} (Granted: {string.Join(", ", newlyGranted)})", quietMode);
            else
                LogDebug($"Rolled {categoryName}: {opt.Value}", quietMode);

            return opt;
        }
    }

    return validOptions[0];
}

/// <summary>
/// Normalizes either a shorthand JSON string ("Laser Rifle") or a full SmartOption JSON object
/// into a unified SmartOption instance using case-insensitive property lookup.
/// </summary>
static SmartOption ResolveElement(JsonElement element)
{
    if (element.ValueKind == JsonValueKind.Null || element.ValueKind == JsonValueKind.Undefined)
        return new SmartOption { Value = "", Weight = 0 };

    if (element.ValueKind == JsonValueKind.String)
        return new SmartOption { Value = element.GetString() ?? "Unknown", Weight = 1 };

    if (element.ValueKind != JsonValueKind.Object)
        return new SmartOption { Value = element.ToString(), Weight = 1 };

    bool TryGetPropCI(string propName, out JsonElement found)
    {
        if (element.TryGetProperty(propName, out found)) return true;
        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, propName, StringComparison.OrdinalIgnoreCase))
            {
                found = prop.Value;
                return true;
            }
        }
        found = default;
        return false;
    }

    string? ReadString(string propName, string? fallback = null) =>
        TryGetPropCI(propName, out var prop) && prop.ValueKind != JsonValueKind.Null
            ? (prop.ValueKind == JsonValueKind.String ? prop.GetString() ?? fallback : prop.ToString())
            : fallback;

    int weight = 1;
    if (TryGetPropCI("Weight", out var w))
    {
        if (w.ValueKind == JsonValueKind.Number && w.TryGetInt32(out int parsedWeight))
            weight = parsedWeight;
        else if (w.ValueKind == JsonValueKind.String && int.TryParse(w.GetString(), out int parsedStringWeight))
            weight = parsedStringWeight;
    }

    Dictionary<string, int>? statMods = null;
    if (TryGetPropCI("StatMods", out var modsProp) && modsProp.ValueKind == JsonValueKind.Object)
    {
        statMods = new Dictionary<string, int>();
        foreach (var prop in modsProp.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt32(out int delta))
                statMods[prop.Name] = delta;
            else if (prop.Value.ValueKind == JsonValueKind.String && int.TryParse(prop.Value.GetString(), out int parsedDelta))
                statMods[prop.Name] = parsedDelta;
        }
    }

    return new SmartOption
    {
        Value = ReadString("Value", "Unknown") ?? "Unknown",
        Weight = weight,
        Requires = ReadString("Requires"),
        Excludes = ReadString("Excludes"),
        GrantsTag = ReadString("GrantsTag"),
        Rarity = ReadString("Rarity"),
        StatMods = statMods
    };
}


// =========================================================================================
// SECTION 8: FINAL OUTPUT, FORMATTING & SAVING (.TXT & .JSON)
// =========================================================================================

/// <summary>
/// Computes a uniform label column width across attributes, ranges, multi-traits, and point pools.
/// </summary>
static int ComputeLabelWidth(GeneratedEntity entity)
{
    var allLabels = entity.Attributes.Keys
        .Concat(entity.Ranges.Keys)
        .Concat(entity.MultiTraits.Keys)
        .Concat(entity.PointPools.Values.SelectMany(p => p.Keys))
        .ToList();

    return Math.Clamp(allLabels.Count > 0 ? allLabels.Max(k => k.Length) + 1 : 20, 20, 32);
}

/// <summary>
/// Renders the formatted character sheet respecting all ModuleSettings toggles
/// (UseGender, ShowTags, ShowRarity, ShowStatBars) and stays on screen for saves or rerolls.
/// Returns true if the user requests a reroll; false to return to the Main Menu.
/// </summary>
bool DisplayEntity(GeneratedEntity entity, UniverseData data)
{
    var modules = data.Modules;
    string? statusMessage = null;
    ConsoleColor statusColor = ConsoleColor.Green;
    int labelWidth = ComputeLabelWidth(entity);

    while (true)
    {
        ClearScreen(scrollable: true);

        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                    [ IDENTIFICATION RECORD ]                     ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
        Console.ResetColor();

        Console.WriteLine($"   NAME   : {entity.FullName}");
        if (modules.UseGender && !string.IsNullOrWhiteSpace(entity.Gender))
            Console.WriteLine($"   GENDER : {entity.Gender}");

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"   SEED   : {entity.Seed}");
        if (modules.ShowTags && entity.Tags.Count > 0)
            Console.WriteLine($"   TAGS   : [{string.Join("] [", entity.Tags)}]");
        Console.ResetColor();
        Console.WriteLine();

        if (!string.IsNullOrEmpty(entity.Story))
        {
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("┌─── [ NARRATIVE LOG ] ────────────────────────────────────────────┐");
            Console.ResetColor();

            const int maxLineWidth = 64;
            string[] words = entity.Story.Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            string currentLine = "   ";

            foreach (var word in words)
            {
                if (currentLine.Length > 3 && currentLine.Length + word.Length > maxLineWidth)
                {
                    Console.WriteLine(currentLine.TrimEnd());
                    currentLine = "   " + word + " ";
                }
                else
                {
                    currentLine += word + " ";
                }
            }
            if (currentLine.Length > 3)
                Console.WriteLine(currentLine.TrimEnd());

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("└──────────────────────────────────────────────────────────────────┘\n");
            Console.ResetColor();
        }

        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine("┌─── [ ASSETS & ATTRIBUTES ] ──────────────────────────────────────┐");
        Console.ResetColor();

        // 1. Standard traits with color-coded rarity badges.
        foreach (var attr in entity.Attributes)
        {
            Console.Write($"   {attr.Key.PadRight(labelWidth)}: {attr.Value}");
            if (modules.ShowRarity
                && entity.AttributeRarities.TryGetValue(attr.Key, out string? rarity)
                && !string.IsNullOrEmpty(rarity))
            {
                Console.Write(" ");
                Console.ForegroundColor = GetRarityColor(rarity);
                Console.Write($"[{rarity}]");
                Console.ResetColor();
            }
            Console.WriteLine();
        }

        // 2. Rolled Numeric Ranges (Age, Bounty, etc.) with optional units and StatMod deltas.
        foreach (var range in entity.Ranges)
        {
            string unit = entity.RangeUnits.GetValueOrDefault(range.Key, "");
            int modDelta = entity.StatModTotals.GetValueOrDefault(range.Key, 0);
            string modSuffix = modDelta != 0 ? $" ({(modDelta > 0 ? "+" : "")}{modDelta})" : "";
            Console.WriteLine($"   {range.Key.PadRight(labelWidth)}: {FormatRangeValue(range.Value, unit)}{modSuffix}");
        }

        // 3. MultiTraits (inventories, loadouts, quirks) with per-item rarity colors.
        foreach (var mt in entity.MultiTraits)
        {
            if (mt.Value == null || mt.Value.Count == 0) continue;
            entity.MultiTraitRarities.TryGetValue(mt.Key, out var rarities);

            if (mt.Value.Count == 1)
            {
                Console.Write($"   {mt.Key.PadRight(labelWidth)}: {mt.Value[0]}");
                if (modules.ShowRarity && rarities != null && rarities.Count > 0 && !string.IsNullOrEmpty(rarities[0]))
                {
                    Console.Write(" ");
                    Console.ForegroundColor = GetRarityColor(rarities[0]);
                    Console.Write($"[{rarities[0]}]");
                    Console.ResetColor();
                }
                Console.WriteLine();
            }
            else
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"   --- {mt.Key.ToUpper()} ---");
                Console.ResetColor();

                for (int i = 0; i < mt.Value.Count; i++)
                {
                    Console.Write($"   • {mt.Value[i]}");
                    if (modules.ShowRarity && rarities != null && i < rarities.Count && !string.IsNullOrEmpty(rarities[i]))
                    {
                        Console.Write(" ");
                        Console.ForegroundColor = GetRarityColor(rarities[i]);
                        Console.Write($"[{rarities[i]}]");
                        Console.ResetColor();
                    }
                    Console.WriteLine();
                }
            }
        }

        // 4. Point Pools with optional visual stat bars and trait StatMod readouts.
        foreach (var pool in entity.PointPools)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"   --- {pool.Key.ToUpper()} ---");
            Console.ResetColor();

            int poolCap = entity.PointPoolCaps.GetValueOrDefault(pool.Key, 10);

            foreach (var stat in pool.Value)
            {
                string modKey = $"{pool.Key}: {stat.Key}";
                int modDelta = entity.StatModTotals.GetValueOrDefault(modKey, 0);
                string scoreLabel = modDelta != 0
                    ? $"{stat.Value} ({(modDelta > 0 ? "+" : "")}{modDelta})"
                    : stat.Value.ToString();

                if (modules.ShowStatBars)
                {
                    Console.Write($"   {stat.Key.PadRight(labelWidth)}: {scoreLabel.PadRight(10)} ");

                    double ratio = poolCap > 0 ? (double)stat.Value / poolCap : 0;
                    Console.ForegroundColor = stat.Value > poolCap ? ConsoleColor.Cyan
                        : ratio >= 0.75 ? ConsoleColor.Green
                        : ratio >= 0.40 ? ConsoleColor.Yellow
                        : ConsoleColor.DarkGray;

                    Console.WriteLine(BuildStatBar(stat.Value, poolCap));
                    Console.ResetColor();
                }
                else
                {
                    Console.WriteLine($"   {stat.Key.PadRight(labelWidth)}: {scoreLabel}");
                }
            }
        }

        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine("└──────────────────────────────────────────────────────────────────┘\n");
        Console.ResetColor();

        if (!string.IsNullOrEmpty(statusMessage))
        {
            Console.ForegroundColor = statusColor;
            Console.WriteLine($"{statusMessage}\n");
            Console.ResetColor();
        }

        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine("[R] Reroll Same Archetype  |  [S] Save .txt  |  [J] Export .json  |  [M / Esc] Main Menu");
        Console.ResetColor();

        FlushInputBuffer();
        ConsoleKey key = Console.ReadKey(true).Key;

        if (key == ConsoleKey.R)
        {
            return true;
        }
        else if (key == ConsoleKey.S)
        {
            (statusMessage, statusColor) = SaveEntityToTxt(entity, data);
        }
        else if (key == ConsoleKey.J)
        {
            (statusMessage, statusColor) = SaveEntityToJson(entity, data.UniverseName);
        }
        else if (key == ConsoleKey.M || key == ConsoleKey.Escape)
        {
            return false;
        }
    }
}

/// <summary>
/// Formats a GeneratedEntity into clean plain text shared by single saves and batch rosters,
/// honoring all display toggles in ModuleSettings.
/// </summary>
static string FormatEntityText(GeneratedEntity entity, ModuleSettings modules)
{
    int labelWidth = ComputeLabelWidth(entity);
    var sb = new StringBuilder();

    sb.AppendLine("==================================================================");
    sb.AppendLine($"   RECORD : {entity.FullName.ToUpper()}");
    if (modules.UseGender && !string.IsNullOrWhiteSpace(entity.Gender))
        sb.AppendLine($"   GENDER : {entity.Gender}");
    sb.AppendLine($"   SEED   : {entity.Seed}");
    if (modules.ShowTags && entity.Tags.Count > 0)
        sb.AppendLine($"   TAGS   : [{string.Join("] [", entity.Tags)}]");
    sb.AppendLine("==================================================================\n");

    if (!string.IsNullOrEmpty(entity.Story))
    {
        sb.AppendLine("[ NARRATIVE ]");
        sb.AppendLine(entity.Story);
        sb.AppendLine("\n------------------------------------------------------------------\n");
    }

    sb.AppendLine("[ ATTRIBUTES ]");
    foreach (var a in entity.Attributes)
    {
        string raritySuffix = modules.ShowRarity
            && entity.AttributeRarities.TryGetValue(a.Key, out string? rarity)
            && !string.IsNullOrEmpty(rarity)
                ? $" [{rarity}]"
                : "";
        sb.AppendLine($"{a.Key.PadRight(labelWidth)}: {a.Value}{raritySuffix}");
    }

    foreach (var range in entity.Ranges)
    {
        string unit = entity.RangeUnits.GetValueOrDefault(range.Key, "");
        int modDelta = entity.StatModTotals.GetValueOrDefault(range.Key, 0);
        string modSuffix = modDelta != 0 ? $" ({(modDelta > 0 ? "+" : "")}{modDelta})" : "";
        sb.AppendLine($"{range.Key.PadRight(labelWidth)}: {FormatRangeValue(range.Value, unit)}{modSuffix}");
    }

    foreach (var mt in entity.MultiTraits)
    {
        if (mt.Value == null || mt.Value.Count == 0) continue;
        entity.MultiTraitRarities.TryGetValue(mt.Key, out var rarities);

        if (mt.Value.Count == 1)
        {
            string raritySuffix = modules.ShowRarity && rarities != null && rarities.Count > 0 && !string.IsNullOrEmpty(rarities[0])
                ? $" [{rarities[0]}]"
                : "";
            sb.AppendLine($"{mt.Key.PadRight(labelWidth)}: {mt.Value[0]}{raritySuffix}");
        }
        else
        {
            sb.AppendLine($"\n--- {mt.Key.ToUpper()} ---");
            for (int i = 0; i < mt.Value.Count; i++)
            {
                string raritySuffix = modules.ShowRarity && rarities != null && i < rarities.Count && !string.IsNullOrEmpty(rarities[i])
                    ? $" [{rarities[i]}]"
                    : "";
                sb.AppendLine($"- {mt.Value[i]}{raritySuffix}");
            }
        }
    }

    foreach (var pool in entity.PointPools)
    {
        int poolCap = entity.PointPoolCaps.GetValueOrDefault(pool.Key, 10);
        sb.AppendLine($"\n--- {pool.Key.ToUpper()} ---");
        foreach (var stat in pool.Value)
        {
            string modKey = $"{pool.Key}: {stat.Key}";
            int modDelta = entity.StatModTotals.GetValueOrDefault(modKey, 0);
            string scoreLabel = modDelta != 0
                ? $"{stat.Value} ({(modDelta > 0 ? "+" : "")}{modDelta})"
                : stat.Value.ToString();

            if (modules.ShowStatBars)
                sb.AppendLine($"{stat.Key.PadRight(labelWidth)}: {scoreLabel.PadRight(10)} {BuildStatBar(stat.Value, poolCap)}");
            else
                sb.AppendLine($"{stat.Key.PadRight(labelWidth)}: {scoreLabel}");
        }
    }

    return sb.ToString();
}

/// <summary>
/// Saves a generated entity as a unique timestamped .txt record under Saves/[UniverseName]/.
/// </summary>
(string Message, ConsoleColor Color) SaveEntityToTxt(GeneratedEntity entity, UniverseData data)
{
    try
    {
        string universeFolder = Path.Combine("Saves", SanitizeFileName(data.UniverseName, "Unknown_Universe"));
        if (!Directory.Exists(universeFolder)) Directory.CreateDirectory(universeFolder);

        string baseName = $"{SanitizeFileName(entity.FullName, "Entity")}_{DateTime.Now:yyyyMMdd_HHmmss}";
        string fullPath = GetUniqueFilePath(universeFolder, baseName, ".txt");

        File.WriteAllText(fullPath, FormatEntityText(entity, data.Modules));
        return ($"[SUCCESS] Archived text record to: {fullPath}", ConsoleColor.Green);
    }
    catch (Exception ex)
    {
        return ($"[ERROR] Could not save text file: {ex.Message}", ConsoleColor.Red);
    }
}

/// <summary>
/// Exports a generated entity as clean, structured JSON under Saves/[UniverseName]/
/// for easy import into external tools, spreadsheets, or games.
/// </summary>
(string Message, ConsoleColor Color) SaveEntityToJson(GeneratedEntity entity, string universeName)
{
    try
    {
        string universeFolder = Path.Combine("Saves", SanitizeFileName(universeName, "Unknown_Universe"));
        if (!Directory.Exists(universeFolder)) Directory.CreateDirectory(universeFolder);

        string baseName = $"{SanitizeFileName(entity.FullName, "Entity")}_{DateTime.Now:yyyyMMdd_HHmmss}";
        string fullPath = GetUniqueFilePath(universeFolder, baseName, ".json");

        string json = JsonSerializer.Serialize(entity, jsonWriteOptions);
        File.WriteAllText(fullPath, json);
        return ($"[SUCCESS] Exported JSON record to: {fullPath}", ConsoleColor.Cyan);
    }
    catch (Exception ex)
    {
        return ($"[ERROR] Could not export JSON file: {ex.Message}", ConsoleColor.Red);
    }
}


// =========================================================================================
// SECTION 9: DATA MODELS
// =========================================================================================

/// <summary>
/// Runtime and exportable representation of a fully generated character/entity.
/// </summary>
public class GeneratedEntity
{
    public int Seed { get; set; }
    public string Archetype { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string? Gender { get; set; }
    public string? Story { get; set; }
    public Dictionary<string, string> Pronouns { get; set; } = new Dictionary<string, string>();

    // Clean attribute values (no "[Rarity]" text baked in; rarities live in AttributeRarities).
    public Dictionary<string, string> Attributes { get; set; } = new Dictionary<string, string>();

    // Optional rarity labels keyed by attribute name.
    public Dictionary<string, string> AttributeRarities { get; set; } = new Dictionary<string, string>();

    // Multi-pick traits (e.g., inventories, loadouts, quirks).
    public Dictionary<string, List<string>> MultiTraits { get; set; } = new Dictionary<string, List<string>>();

    // Independent rolled numeric ranges (e.g., Age, Bounty, Crew Size).
    public Dictionary<string, int> Ranges { get; set; } = new Dictionary<string, int>();

    // Optional unit strings for Ranges (e.g., " yrs", " Cr").
    public Dictionary<string, string> RangeUnits { get; set; } = new Dictionary<string, string>();

    // Structured RPG stat pools (Pool Name -> { Stat Name -> Final Score }).
    public Dictionary<string, Dictionary<string, int>> PointPools { get; set; } = new Dictionary<string, Dictionary<string, int>>();

    // Cumulative trait StatMods applied to PointPool stats or Ranges.
    public Dictionary<string, int> StatModTotals { get; set; } = new Dictionary<string, int>();

    public List<string> Tags { get; set; } = new List<string>();

    // Internal rendering metadata omitted from JSON exports.
    [JsonIgnore]
    public Dictionary<string, int> PointPoolCaps { get; set; } = new Dictionary<string, int>();

    [JsonIgnore]
    public Dictionary<string, List<string>> MultiTraitRarities { get; set; } = new Dictionary<string, List<string>>();

    public string FullName
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(FirstName)) parts.Add(FirstName);
            if (!string.IsNullOrWhiteSpace(Nickname)) parts.Add($"'{Nickname}'");
            if (!string.IsNullOrWhiteSpace(LastName)) parts.Add(LastName);
            return parts.Count > 0 ? string.Join(" ", parts) : "Unnamed Entity";
        }
    }

    [JsonIgnore]
    public string NicknameOrFirst => !string.IsNullOrWhiteSpace(Nickname)
        ? Nickname
        : (!string.IsNullOrWhiteSpace(FirstName) ? FirstName : "Unnamed");
}

/// <summary>
/// Root schema for a Universe JSON file stored in UniverseFiles/.
/// </summary>
public class UniverseData
{
    public string UniverseName { get; set; } = "";
    public string ArchetypeTitle { get; set; } = "Archetype";
    public ModuleSettings Modules { get; set; } = new ModuleSettings();
    public Dictionary<string, EntityArchetype?>? Archetypes { get; set; }
    public Dictionary<string, List<StoryTemplate?>>? BioTemplates { get; set; }
}

/// <summary>
/// Feature and display toggles configured per universe module.
/// All flags default to true for full backward compatibility with v1.x JSON files.
/// </summary>
public class ModuleSettings
{
    public bool ShowRarity { get; set; } = true;
    public bool UseNarrative { get; set; } = true;
    public bool UsePointPools { get; set; } = true;
    public bool UseGender { get; set; } = true;
    public bool ShowTags { get; set; } = true;
    public bool ShowStatBars { get; set; } = true;
    public bool UseRanges { get; set; } = true;
    public bool UseMultiTraits { get; set; } = true;
}

/// <summary>
/// Blueprint for a specific class/species/role/object type within a universe.
/// </summary>
public class EntityArchetype
{
    public List<JsonElement>? FirstNames { get; set; }
    public List<JsonElement>? LastNames { get; set; }
    public List<JsonElement>? Nicknames { get; set; }
    public double NicknameChance { get; set; } = 0.5;
    public Dictionary<string, int>? GenderOdds { get; set; }
    public List<string>? StartingTags { get; set; } = new List<string>();
    public Dictionary<string, List<JsonElement>>? MandatoryTraits { get; set; }
    public Dictionary<string, ProbabilityTrait?>? OptionalTraits { get; set; }
    public Dictionary<string, MultiTrait?>? MultiTraits { get; set; }
    public Dictionary<string, NumericRange?>? Ranges { get; set; }
    public Dictionary<string, PointPool?>? PointPools { get; set; }
}

/// <summary>
/// A single weighted narrative fragment gated by optional tag and stat checks.
/// </summary>
public class StoryTemplate
{
    public string? Text { get; set; }
    public int Weight { get; set; } = 1;
    public string? Requires { get; set; }
    public string? Excludes { get; set; }
    public string? MinStatName { get; set; }
    public int MinStatValue { get; set; } = 0;
    public string? MaxStatName { get; set; }
    public int? MaxStatValue { get; set; }
}

/// <summary>
/// Wrapper for an optional trait category with a 0.0-1.0 roll chance.
/// </summary>
public class ProbabilityTrait
{
    public double Chance { get; set; }
    public List<JsonElement>? Options { get; set; }
}

/// <summary>
/// Multi-pick trait pool for inventories, loadouts, quirks, or ship subsystems.
/// </summary>
public class MultiTrait
{
    public double Chance { get; set; } = 1.0;
    public int MinPicks { get; set; } = 1;
    public int MaxPicks { get; set; } = 1;
    public bool AllowDuplicates { get; set; } = false;
    public string? Requires { get; set; }
    public string? Excludes { get; set; }
    public List<JsonElement>? Options { get; set; }
}

/// <summary>
/// Independent rolled numeric value with optional step granularity, display unit, and tag gates.
/// </summary>
public class NumericRange
{
    public int Min { get; set; }
    public int Max { get; set; }
    public int Step { get; set; } = 1;
    public string? Unit { get; set; }
    public string? Requires { get; set; }
    public string? Excludes { get; set; }
}

/// <summary>
/// Defines a pool of points distributed randomly across named attributes.
/// </summary>
public class PointPool
{
    public int TotalPoints { get; set; }
    public int MinPerAttribute { get; set; }
    public int MaxPerAttribute { get; set; }
    public List<string>? Attributes { get; set; }
}

/// <summary>
/// Normalized representation of an individual selectable option.
/// </summary>
public class SmartOption
{
    public string Value { get; set; } = "";
    public int Weight { get; set; } = 1;
    public string? Requires { get; set; }
    public string? Excludes { get; set; }
    public string? GrantsTag { get; set; }
    public string? Rarity { get; set; }
    public Dictionary<string, int>? StatMods { get; set; }
}