using System.Collections.Generic;
using System.IO;
using System.Linq;
using AllTimeSoundTrigger.ConfigurationModels;
using Dalamud.Plugin;

namespace AllTimeSoundTrigger.Services;

public static class BuiltInExampleContent
{
    private const string ExampleSoundDirectoryName = "example-sounds";
    private const string ManagedSoundDirectoryName = "sounds";

    private static readonly ExampleSound[] ExampleSounds =
    [
        new("00000", "示例音频---MAN", "sample-man.mp3"),
        new("00001", "示例音频---Manbaout", "sample-manbaout.mp3"),
        new("00002", "示例音频---What can I say", "sample-what-can-i-say.mp3"),
        new("00003", "示例音频---念张师", "sample-nian-zhang-shi.mp3")
    ];

    public static bool EnsureSoundLibrary(IDalamudPluginInterface pluginInterface, SoundLibraryConfiguration soundLibrary)
    {
        var changed = false;
        soundLibrary.Entries ??= [];

        var pluginDirectory = pluginInterface.AssemblyLocation.DirectoryName ?? string.Empty;
        var sourceDirectory = Path.Combine(pluginDirectory, ExampleSoundDirectoryName);
        var destinationDirectory = Path.Combine(pluginInterface.ConfigDirectory.FullName, ManagedSoundDirectoryName);
        Directory.CreateDirectory(destinationDirectory);

        foreach (var sound in ExampleSounds)
        {
            var destinationPath = Path.Combine(destinationDirectory, sound.FileName);
            var sourcePath = Path.Combine(sourceDirectory, sound.FileName);
            if (!File.Exists(destinationPath) && File.Exists(sourcePath))
            {
                File.Copy(sourcePath, destinationPath);
                changed = true;
            }

            if (soundLibrary.FindById(sound.Id) != null)
                continue;

            soundLibrary.Entries.Add(new SoundLibraryEntry
            {
                Id = sound.Id,
                Name = sound.Name,
                FilePath = destinationPath,
                DefaultVolume = 1f,
                Priority = 0,
                InterruptLowerPriority = true
            });
            changed = true;
        }

        return changed | soundLibrary.Normalize();
    }

    public static IReadOnlyList<RuleDefinition> CreateSampleRules()
        =>
        [
            CreateSoundRule(
                "sample-local-kill-man",
                "击杀自动播放man",
                new TriggerDefinition
                {
                    Type = "Kill",
                    LocalPlayerOnly = true
                },
                "00000"),
            CreateSoundRule(
                "sample-skill-mediation-manbaout",
                "调停自动manbaout",
                new TriggerDefinition
                {
                    Type = "SkillUsed",
                    SkillNameContains = "调停",
                    LocalPlayerOnly = true
                },
                "00001"),
            CreateSoundRule(
                "sample-skill-sprint-what-can-i-say",
                "冲刺自动什么罐头我说",
                new TriggerDefinition
                {
                    Type = "SkillUsed",
                    SkillNameContains = "冲刺",
                    LocalPlayerOnly = true
                },
                "00002"),
            CreateSoundRule(
                "sample-status-speed-nian-zhang-shi",
                "你跑不过我",
                new TriggerDefinition
                {
                    Type = "StatusGained",
                    StatusNameContains = "敏捷"
                },
                "00003",
                loop: true,
                stopOnStatusLost: true)
        ];

    public static bool EnsureSampleRules(ProfileDefinition profile)
    {
        var changed = false;
        profile.Groups ??= [];
        var group = profile.Groups.FirstOrDefault(item => item.Name.Contains("示例", System.StringComparison.OrdinalIgnoreCase));
        if (group == null)
        {
            group = profile.Groups.Count == 1 && profile.Groups[0].Rules.Count == 0
                ? profile.Groups[0]
                : new RuleGroupDefinition();

            group.Name = "示例分组";
            if (!profile.Groups.Contains(group))
                profile.Groups.Add(group);
            changed = true;
        }

        foreach (var sampleRule in CreateSampleRules())
        {
            if (profile.EnumerateRules().Any(rule =>
                    rule.Id.Equals(sampleRule.Id, System.StringComparison.OrdinalIgnoreCase)
                    || rule.Name.Equals(sampleRule.Name, System.StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            group.Rules.Add(sampleRule);
            changed = true;
        }

        return changed;
    }

    private static RuleDefinition CreateSoundRule(
        string id,
        string name,
        TriggerDefinition trigger,
        string soundId,
        bool loop = false,
        bool stopOnStatusLost = false)
        => new()
        {
            Id = id,
            Name = name,
            Enabled = true,
            CooldownSeconds = 0.5,
            Trigger = trigger,
            Conditions = [],
            Actions =
            [
                new ActionDefinition
                {
                    Type = "Sound",
                    SoundId = soundId,
                    Volume = 1f,
                    Priority = 0,
                    InterruptLowerPriority = true,
                    Loop = loop || stopOnStatusLost,
                    StopOnStatusLost = stopOnStatusLost
                }
            ]
        };

    private readonly record struct ExampleSound(string Id, string Name, string FileName);
}
