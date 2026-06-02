using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using AllTimeSoundTrigger.ConfigurationModels;
using Dalamud.Plugin.Services;

namespace AllTimeSoundTrigger.Services;

public sealed class ProfileStorageService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IPluginLog log;
    private readonly List<ProfileDefinition> profiles = [];
    private string activeProfileId = "default";

    public ProfileStorageService(IPluginLog log)
    {
        this.log = log;
        RootDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "XIVLauncherCN",
            "pluginConfigs",
            "AllTimeSoundTrigger",
            "profiles");
    }

    public string RootDirectory { get; }

    public IReadOnlyList<ProfileDefinition> Profiles => profiles;

    public ProfileDefinition ActiveProfile { get; private set; } = ProfileDefinition.CreateDefault(new[] { RuleDefinition.CreateDefaultSkillLogRule() });

    public void Initialize(Configuration configuration)
    {
        Directory.CreateDirectory(RootDirectory);
        activeProfileId = string.IsNullOrWhiteSpace(configuration.ActiveProfileId)
            ? "default"
            : configuration.ActiveProfileId.Trim();

        profiles.Clear();
        foreach (var filePath in Directory.EnumerateFiles(RootDirectory, "*.json"))
        {
            var profile = LoadProfile(filePath);
            if (profile == null)
                continue;

            profiles.Add(profile);
        }

        if (profiles.Count == 0)
        {
            IEnumerable<RuleDefinition> legacyRules = configuration.Rules.Count > 0
                ? configuration.Rules
                : BuiltInExampleContent.CreateSampleRules();
            var defaultProfile = ProfileDefinition.CreateDefault(legacyRules);
            defaultProfile.GetOrCreateDefaultGroup().Name = "示例分组";
            defaultProfile.Normalize();
            profiles.Add(defaultProfile);
            SaveProfile(defaultProfile);

            configuration.Rules = [];
            configuration.ActiveProfileId = defaultProfile.Id;
            configuration.BuiltInExampleRulesInstalled = true;
            configuration.Save();
        }

        var builtInDefaultProfile = profiles.FirstOrDefault(profile => profile.Id.Equals("default", StringComparison.OrdinalIgnoreCase));
        if (!configuration.BuiltInExampleRulesInstalled
            && builtInDefaultProfile != null)
        {
            if (BuiltInExampleContent.EnsureSampleRules(builtInDefaultProfile))
                SaveProfile(builtInDefaultProfile);

            configuration.BuiltInExampleRulesInstalled = true;
            configuration.Save();
        }

        ActiveProfile = profiles.FirstOrDefault(profile => profile.Id.Equals(activeProfileId, StringComparison.OrdinalIgnoreCase))
            ?? profiles[0];
        activeProfileId = ActiveProfile.Id;

        if (!configuration.ActiveProfileId.Equals(activeProfileId, StringComparison.OrdinalIgnoreCase))
        {
            configuration.ActiveProfileId = activeProfileId;
            configuration.Save();
        }
    }

    public IReadOnlyList<RuleDefinition> GetActiveRules()
        => ActiveProfile.EnumerateRules().ToArray();

    public RuleGroupDefinition GetOrCreateDefaultGroup()
    {
        var group = ActiveProfile.GetOrCreateDefaultGroup();
        SaveActiveProfile();
        return group;
    }

    public ProfileDefinition CreateProfile(string requestedName)
    {
        var profile = new ProfileDefinition
        {
            Name = string.IsNullOrWhiteSpace(requestedName) ? "新方案" : requestedName.Trim(),
            Groups =
            [
                new RuleGroupDefinition
                {
                    Name = "默认分组",
                    Rules = []
                }
            ]
        };
        profile.Normalize();
        profiles.Add(profile);
        SaveProfile(profile);
        return profile;
    }

    public bool SwitchProfile(string profileId, Configuration configuration)
    {
        var profile = profiles.FirstOrDefault(item => item.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase));
        if (profile == null)
            return false;

        ActiveProfile = profile;
        activeProfileId = profile.Id;
        configuration.ActiveProfileId = profile.Id;
        configuration.Save();
        return true;
    }

    public bool DeleteProfile(string profileId, Configuration configuration)
    {
        if (profiles.Count <= 1)
            return false;

        var profile = profiles.FirstOrDefault(item => item.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase));
        if (profile == null)
            return false;

        profiles.Remove(profile);
        var filePath = GetProfileFilePath(profile.Id);
        if (File.Exists(filePath))
            File.Delete(filePath);

        if (ActiveProfile.Id.Equals(profile.Id, StringComparison.OrdinalIgnoreCase))
        {
            ActiveProfile = profiles[0];
            activeProfileId = ActiveProfile.Id;
            configuration.ActiveProfileId = ActiveProfile.Id;
            configuration.Save();
        }

        return true;
    }

    public void SaveActiveProfile()
    {
        ActiveProfile.Normalize();
        SaveProfile(ActiveProfile);
    }

    public void SaveProfile(ProfileDefinition profile)
    {
        Directory.CreateDirectory(RootDirectory);
        profile.Normalize();
        var json = JsonSerializer.Serialize(profile, SerializerOptions);
        File.WriteAllText(GetProfileFilePath(profile.Id), json);
    }

    private ProfileDefinition? LoadProfile(string filePath)
    {
        try
        {
            var profile = JsonSerializer.Deserialize<ProfileDefinition>(File.ReadAllText(filePath), SerializerOptions);
            if (profile == null)
                return null;

            profile.Normalize();
            return profile;
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[AllTimeSoundTrigger] 跳过无法读取的方案文件：{FilePath}", filePath);
            return null;
        }
    }

    private string GetProfileFilePath(string profileId)
    {
        var safeId = string.IsNullOrWhiteSpace(profileId) ? "default" : profileId.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
            safeId = safeId.Replace(invalid, '_');

        return Path.Combine(RootDirectory, $"{safeId}.json");
    }
}
