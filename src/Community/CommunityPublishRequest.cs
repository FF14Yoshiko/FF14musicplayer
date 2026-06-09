namespace AllTimeSoundTrigger.Community;

public sealed class CommunityPublishRequest
{
    public string RepositoryPath { get; set; } = string.Empty;

    public string PackagePath { get; set; } = string.Empty;

    public string CoverPath { get; set; } = string.Empty;

    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Author { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string PackageVersion { get; set; } = "1.0.0";

    public string TagsText { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string GameModesText { get; set; } = string.Empty;

    public string JobsText { get; set; } = string.Empty;

    public string TriggerTypesText { get; set; } = string.Empty;

    public string CompatiblePluginVersion { get; set; } = string.Empty;

    public string License { get; set; } = string.Empty;

    public string ContentWarning { get; set; } = string.Empty;

    public string Changelog { get; set; } = string.Empty;

    public string ChangelogUrl { get; set; } = string.Empty;

    public string Readme { get; set; } = string.Empty;

    public bool Deprecated { get; set; }

    public bool Hidden { get; set; }

    public bool AllowOverwrite { get; set; }

    public bool PushToRemote { get; set; } = true;
}
