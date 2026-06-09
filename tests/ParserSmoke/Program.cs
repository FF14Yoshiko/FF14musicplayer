using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using AllTimeSoundTrigger.Actions;
using AllTimeSoundTrigger.Community;
using AllTimeSoundTrigger.ConfigurationModels;
using AllTimeSoundTrigger.Core;
using AllTimeSoundTrigger.EventSources;
using AllTimeSoundTrigger.Services;
using AllTimeSoundTrigger.Rules;

var samples = new (string Text, string Actor, string Skill, bool IsCastStart)[]
{
    ("你使用了 强力射击 技能", "你", "强力射击", false),
    ("鳳凰發動了“刃舞”。", "鳳凰", "刃舞", false),
    ("伊弗利特正在發動“蓄力衝擊”。", "伊弗利特", "蓄力衝擊", true),
    ("伊弗利特詠唱了“大宇宙”。", "伊弗利特", "大宇宙", false),
    ("You use Heavy Swing.", "You", "Heavy Swing", false),
};

var failures = 0;
foreach (var sample in samples)
{
    if (!SkillUseChatParser.TryParse(sample.Text, out var parsed))
    {
        Console.Error.WriteLine($"MISS: {sample.Text}");
        failures++;
        continue;
    }

    if (parsed.ActorName != sample.Actor
        || parsed.SkillName != sample.Skill
        || parsed.IsCastStart != sample.IsCastStart)
    {
        Console.Error.WriteLine($"BAD: {sample.Text}");
        Console.Error.WriteLine($"  expected: {sample.Actor} / {sample.Skill} / {sample.IsCastStart}");
        Console.Error.WriteLine($"  actual:   {parsed.ActorName} / {parsed.SkillName} / {parsed.IsCastStart}");
        failures++;
    }
}

var itemSamples = new (string Text, string Sender, string Actor, string Item, int Quantity)[]
{
    ("你获得了 兽人金币。", "", "你", "兽人金币", 1),
    ("你获得了「高质暗物质」 x3。", "", "你", "高质暗物质", 3),
    ("You obtain an Allagan tomestone x2.", "", "You", "Allagan tomestone", 2),
};

foreach (var sample in itemSamples)
{
    if (!ItemAcquiredChatParser.TryParse(sample.Text, sample.Sender, out var parsed))
    {
        Console.Error.WriteLine($"ITEM MISS: {sample.Text}");
        failures++;
        continue;
    }

    if (parsed.ActorName != sample.Actor
        || parsed.ItemName != sample.Item
        || parsed.Quantity != sample.Quantity)
    {
        Console.Error.WriteLine($"ITEM BAD: {sample.Text}");
        Console.Error.WriteLine($"  expected: {sample.Actor} / {sample.Item} / {sample.Quantity}");
        Console.Error.WriteLine($"  actual:   {parsed.ActorName} / {parsed.ItemName} / {parsed.Quantity}");
        failures++;
    }
}

var killSamples = new (string Text, string Sender, string Actor, string Target)[]
{
    ("你击倒了 敌方贤者。", "", "你", "敌方贤者"),
    ("You defeated Enemy Sage.", "", "You", "Enemy Sage"),
    ("击倒了 敌方白魔。", "本机玩家", "本机玩家", "敌方白魔"),
    ("敌方武僧被本机玩家击倒了。", "", "本机玩家", "敌方武僧"),
};

foreach (var sample in killSamples)
{
    if (!KillChatParser.TryParse(sample.Text, sample.Sender, out var parsed))
    {
        Console.Error.WriteLine($"KILL MISS: {sample.Text}");
        failures++;
        continue;
    }

    if (parsed.ActorName != sample.Actor || parsed.TargetName != sample.Target)
    {
        Console.Error.WriteLine($"KILL BAD: {sample.Text}");
        Console.Error.WriteLine($"  expected: {sample.Actor} -> {sample.Target}");
        Console.Error.WriteLine($"  actual:   {parsed.ActorName} -> {parsed.TargetName}");
        failures++;
    }
}

if (!RunSfxPackSecuritySmoke())
    failures++;
if (!RunRuleIndexSmoke())
    failures++;
if (!RunRuleConflictSmoke())
    failures++;
if (!RunSubmissionPreflightSmoke())
    failures++;

if (failures > 0)
    return failures;

Console.WriteLine($"Parser smoke passed: {samples.Length + itemSamples.Length + killSamples.Length} samples.");
return 0;

static bool RunSfxPackSecuritySmoke()
{
    var directory = Path.Combine(Path.GetTempPath(), $"ats-sfxpack-security-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        var validPath = Path.Combine(directory, "valid.sfxpack");
        CreateSecuritySmokePack(validPath, "sounds/good.ogg", [(byte)'O', (byte)'g', (byte)'g', (byte)'S', 0, 0, 0, 0]);
        if (!ValidateArchiveByReflection(validPath, out var validMessage))
        {
            Console.Error.WriteLine($"SFX SECURITY VALID BAD: {validMessage}");
            return false;
        }

        var invalidPath = Path.Combine(directory, "invalid.sfxpack");
        CreateSecuritySmokePack(invalidPath, "sounds/fake.mp3", Encoding.ASCII.GetBytes("not really an mp3"));
        if (ValidateArchiveByReflection(invalidPath, out _))
        {
            Console.Error.WriteLine("SFX SECURITY MISS: fake mp3 was accepted.");
            return false;
        }

        return true;
    }
    finally
    {
        try
        {
            Directory.Delete(directory, true);
        }
        catch
        {
            // Best-effort cleanup for smoke artifacts.
        }
    }
}

static void CreateSecuritySmokePack(string path, string soundEntryName, byte[] soundBytes)
{
    using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
    AddTextEntry(archive, "profile.json", """{"Name":"Smoke","Groups":[]}""");
    var sound = archive.CreateEntry(soundEntryName, CompressionLevel.NoCompression);
    using var stream = sound.Open();
    stream.Write(soundBytes);
}

static void AddTextEntry(ZipArchive archive, string entryName, string text)
{
    var entry = archive.CreateEntry(entryName, CompressionLevel.NoCompression);
    using var stream = entry.Open();
    stream.Write(Encoding.UTF8.GetBytes(text));
}

static bool ValidateArchiveByReflection(string path, out string message)
{
    message = string.Empty;
    var type = typeof(SfxPackService).Assembly.GetType("AllTimeSoundTrigger.Services.SfxPackSecurity");
    var method = type?.GetMethod("ValidateArchive", BindingFlags.Public | BindingFlags.Static);
    if (method == null)
    {
        message = "SfxPackSecurity.ValidateArchive not found.";
        return false;
    }

    try
    {
        using var archive = ZipFile.OpenRead(path);
        method.Invoke(null, [archive]);
        return true;
    }
    catch (TargetInvocationException ex)
    {
        message = ex.InnerException?.Message ?? ex.Message;
        return false;
    }
    catch (Exception ex)
    {
        message = ex.Message;
        return false;
    }
}

static bool RunRuleIndexSmoke()
{
    var skillTrigger = new CountingTrigger(["SkillUsed"], true);
    var killTrigger = new CountingTrigger(["Kill"], true);
    var globalTrigger = new CountingTrigger([], false);
    var skillAction = new CountingAction();
    var killAction = new CountingAction();

    var engine = new RulesEngine();
    engine.ReplaceRules([
        new Rule("global", 0, globalTrigger, [], [new CountingAction()]),
        new Rule("skill", 3, skillTrigger, [], [skillAction]),
        new Rule("kill", 0, killTrigger, [], [killAction])
    ]);

    var now = DateTime.Now;
    engine.HandleEvent(new GameEvent { EventType = "SkillUsed", Timestamp = now });
    if (globalTrigger.Calls != 1 || skillTrigger.Calls != 1 || killTrigger.Calls != 0)
    {
        Console.Error.WriteLine($"RULE INDEX BAD: global={globalTrigger.Calls}, skill={skillTrigger.Calls}, kill={killTrigger.Calls}");
        return false;
    }

    var firstSnapshot = engine.SnapshotRuntime();
    if (firstSnapshot.RuleCount != 3
        || firstSnapshot.EventIndexBucketCount != 2
        || firstSnapshot.GlobalRuleCount != 1
        || firstSnapshot.LastEventType != "SkillUsed"
        || firstSnapshot.LastCandidateRuleCount != 2
        || firstSnapshot.LastMatchedRuleCount != 1
        || firstSnapshot.LastTriggeredRuleCount != 1
        || firstSnapshot.LastCooldownSkippedCount != 0)
    {
        Console.Error.WriteLine(
            "RULE RUNTIME SNAPSHOT BAD: "
            + $"rules={firstSnapshot.RuleCount}, buckets={firstSnapshot.EventIndexBucketCount}, global={firstSnapshot.GlobalRuleCount}, "
            + $"event={firstSnapshot.LastEventType}, candidates={firstSnapshot.LastCandidateRuleCount}, matched={firstSnapshot.LastMatchedRuleCount}, "
            + $"triggered={firstSnapshot.LastTriggeredRuleCount}, cooldown={firstSnapshot.LastCooldownSkippedCount}");
        return false;
    }

    engine.HandleEvent(new GameEvent { EventType = "SkillUsed", Timestamp = now.AddSeconds(1) });
    var cooldownSnapshot = engine.SnapshotRuntime();
    if (globalTrigger.Calls != 2
        || skillTrigger.Calls != 2
        || killTrigger.Calls != 0
        || cooldownSnapshot.LastCandidateRuleCount != 2
        || cooldownSnapshot.LastMatchedRuleCount != 1
        || cooldownSnapshot.LastTriggeredRuleCount != 0
        || cooldownSnapshot.LastCooldownSkippedCount != 1)
    {
        Console.Error.WriteLine(
            "RULE RUNTIME COOLDOWN BAD: "
            + $"global={globalTrigger.Calls}, skill={skillTrigger.Calls}, kill={killTrigger.Calls}, "
            + $"candidates={cooldownSnapshot.LastCandidateRuleCount}, matched={cooldownSnapshot.LastMatchedRuleCount}, "
            + $"triggered={cooldownSnapshot.LastTriggeredRuleCount}, cooldown={cooldownSnapshot.LastCooldownSkippedCount}");
        return false;
    }

    if (skillAction.Calls != 1 || killAction.Calls != 0)
    {
        Console.Error.WriteLine($"RULE ACTION BAD: skill={skillAction.Calls}, kill={killAction.Calls}");
        return false;
    }

    return true;
}

static bool RunRuleConflictSmoke()
{
    var existingGroupId = "existing-community-group";
    var existing = new ProfileDefinition
    {
        Name = "Existing",
        Groups =
        [
            new RuleGroupDefinition
            {
                Id = existingGroupId,
                Name = "已有社区包分组",
                Rules =
                [
                    new RuleDefinition
                    {
                        Name = "已有战斗之声",
                        Trigger = new TriggerDefinition
                        {
                            Type = "SkillUsed",
                            SkillNameContains = "战斗之声"
                        }
                    }
                ]
            }
        ]
    };
    existing.Normalize();

    var incoming = new ProfileDefinition
    {
        Name = "Incoming",
        Groups =
        [
            new RuleGroupDefinition
            {
                Name = "新社区包分组",
                Rules =
                [
                    new RuleDefinition
                    {
                        Name = "新战斗之声",
                        Trigger = new TriggerDefinition
                        {
                            Type = "SkillUsed",
                            SkillNameContains = "战斗之声"
                        }
                    }
                ]
            }
        ]
    };
    incoming.Normalize();

    var installed = new CommunityInstalledPack
    {
        Id = "battle-voice-pack",
        Name = "战斗语音包",
        GroupIds = [existingGroupId]
    };
    installed.Normalize();

    var report = RuleTriggerConflictDetector.Detect(incoming, existing, [installed], maxConflicts: 4);
    if (!report.HasConflicts
        || report.Conflicts.Count != 1
        || !report.Conflicts[0].ExistingSourceIsCommunityPack
        || report.Conflicts[0].ExistingSourceName != "战斗语音包"
        || report.Conflicts[0].Reason != "同一个技能")
    {
        Console.Error.WriteLine("RULE CONFLICT BAD: same skill conflict was not reported correctly.");
        return false;
    }

    var selfUpdateReport = RuleTriggerConflictDetector.Detect(incoming, existing, [installed], "battle-voice-pack", 4);
    if (selfUpdateReport.HasConflicts)
    {
        Console.Error.WriteLine("RULE CONFLICT BAD: self update groups were not excluded.");
        return false;
    }

    return true;
}

static bool RunSubmissionPreflightSmoke()
{
    var service = (SfxPackService)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(SfxPackService));
    var groupId = "submission-group";
    var profile = new ProfileDefinition
    {
        Name = "Submission",
        Groups =
        [
            new RuleGroupDefinition
            {
                Id = groupId,
                Name = "投稿分组",
                Rules =
                [
                    new RuleDefinition
                    {
                        Name = "宽泛循环规则",
                        Trigger = new TriggerDefinition
                        {
                            Type = "SkillUsed"
                        },
                        Actions =
                        [
                            new ActionDefinition
                            {
                                Type = "Sound",
                                Loop = true,
                                Volume = 1f
                            }
                        ]
                    }
                ]
            }
        ]
    };
    profile.Normalize();

    var result = service.ValidateSubmissionPreflight(
        profile,
        [groupId],
        [],
        new SoundLibraryConfiguration(),
        string.Empty);

    if (!result.HasErrors
        || !result.Issues.Any(issue => issue.Title == "音频缺失")
        || !result.Issues.Any(issue => issue.Title == "循环音效没有停止条件")
        || !result.Issues.Any(issue => issue.Title == "触发器太宽泛")
        || !result.Issues.Any(issue => issue.Title == "封面缺失"))
    {
        Console.Error.WriteLine("SUBMISSION PREFLIGHT BAD: expected issues were not reported.");
        foreach (var issue in result.Issues)
            Console.Error.WriteLine($"  {issue.Severity}: {issue.Title} / {issue.Message}");
        return false;
    }

    return true;
}

sealed class CountingTrigger(IReadOnlyList<string> eventTypes, bool matches) : IEventIndexedTrigger
{
    public int Calls { get; private set; }

    public IReadOnlyList<string> EventTypes { get; } = eventTypes;

    public bool IsMatch(GameEvent e)
    {
        Calls++;
        return matches;
    }
}

sealed class CountingAction : IAction
{
    public int Calls { get; private set; }

    public void Execute()
    {
        Calls++;
    }
}
