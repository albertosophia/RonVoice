namespace RonVoice.Core.Commands;

public sealed record OrderDefinition(
    string Id,
    string Context,
    IReadOnlyList<string> Path,
    bool CloseMenu,
    string Confidence,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Phrases);

public sealed record ElementDefinition(
    string Name,
    string Key,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Aliases);

public sealed record ModifierDefinition(
    IReadOnlyDictionary<string, IReadOnlyList<string>> Aliases);

public sealed record KeybindDefaults(
    string SwatCommandMenu,
    string DefaultCommand,
    string HoldCommand,
    string Back,
    string SelectGold,
    string SelectBlue,
    string SelectRed,
    IReadOnlyList<string> CommandKeys,
    string InteractYell);

public sealed record TimingSettings(
    int KeyHoldMs,
    int GapBetweenKeysMs,
    int MenuOpenSettleMs);
