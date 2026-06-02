using System;
using System.Collections.Generic;
using AllTimeSoundTrigger.Audio;
using AllTimeSoundTrigger.ConfigurationModels;
using AllTimeSoundTrigger.Services;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace AllTimeSoundTrigger;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    private const int CurrentVersion = 10;

    public int Version { get; set; } = CurrentVersion;

    public AudioConfiguration Audio { get; set; } = new();

    public SoundLibraryConfiguration SoundLibrary { get; set; } = new();

    public string ActiveProfileId { get; set; } = "default";

    public AutoSwitchConfiguration AutoSwitch { get; set; } = new();

    public bool BuiltInExampleSoundsInstalled { get; set; }

    public bool BuiltInExampleRulesInstalled { get; set; }

    // Kept for one-time migration from older builds. New rule sets live in profiles/*.json.
    public List<RuleDefinition> Rules { get; set; } = [];

    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
        this.pluginInterface = pluginInterface;
        var changed = Normalize();
        if (!BuiltInExampleSoundsInstalled)
        {
            changed |= BuiltInExampleContent.EnsureSoundLibrary(pluginInterface, SoundLibrary);
            BuiltInExampleSoundsInstalled = true;
            changed = true;
        }

        if (changed)
            Save();
    }

    public void Save()
    {
        pluginInterface?.SavePluginConfig(this);
    }

    private bool Normalize()
    {
        var changed = false;
        Audio ??= new AudioConfiguration();
        Audio.Normalize();
        SoundLibrary ??= new SoundLibraryConfiguration();
        changed |= SoundLibrary.Normalize();
        AutoSwitch ??= new AutoSwitchConfiguration();
        AutoSwitch.Normalize();
        Rules ??= [];

        if (string.IsNullOrWhiteSpace(ActiveProfileId))
        {
            ActiveProfileId = "default";
            changed = true;
        }

        foreach (var rule in Rules)
        {
            if (rule.Id != "debug-local-skill-used" || rule.Trigger.Type != "SkillUsed")
                continue;

            rule.Id = "debug-visible-skill-used";
            rule.Name = "调试：可见角色使用技能";
            rule.CooldownSeconds = 0.5;
            rule.Trigger.LocalPlayerOnly = false;
            if (rule.Actions.Count > 0 && rule.Actions[0].Type == "Log")
                rule.Actions[0].Message = "规则命中：检测到可见技能使用事件。";
            changed = true;
        }

        if (Version != CurrentVersion)
        {
            Version = CurrentVersion;
            changed = true;
        }

        return changed;
    }
}
