using System;
using AllTimeSoundTrigger.EventSources;

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

if (failures > 0)
    return failures;

Console.WriteLine($"Parser smoke passed: {samples.Length + itemSamples.Length + killSamples.Length} samples.");
return 0;
