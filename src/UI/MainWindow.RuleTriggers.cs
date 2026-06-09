using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Windows.Forms;
using AllTimeSoundTrigger.ConfigurationModels;
using AllTimeSoundTrigger.Services;
using AllTimeSoundTrigger.Utilities;
using Dalamud.Bindings.ImGui;

namespace AllTimeSoundTrigger.UI;

public sealed partial class MainWindow
{
    private void DrawTriggerEditor(RuleDefinition rule)
    {
        rule.Trigger ??= new TriggerDefinition();
        var trigger = rule.Trigger;

        ImGui.Text("触发器");
        if (DrawStringCombo("类型##TriggerType", trigger.Type, TriggerTypes, value => SetTriggerType(trigger, value)))
            rulesEditorMessage = "触发器类型已更新，保存后生效。";

        if (trigger.Type.Equals("SkillUsed", StringComparison.OrdinalIgnoreCase))
        {
            DrawInputText("角色名包含##TriggerActor", trigger.ActorName, 120, value => trigger.ActorName = value);
            DrawSkillNameSearch(trigger);

            var localOnly = trigger.LocalPlayerOnly ?? false;
            if (ImGui.Checkbox("仅本机角色##TriggerLocalOnly", ref localOnly))
                trigger.LocalPlayerOnly = localOnly;
        }
        else if (trigger.Type.Equals("Kill", StringComparison.OrdinalIgnoreCase))
        {
            DrawInputText("击杀者包含##TriggerKillActor", trigger.ActorName, 120, value => trigger.ActorName = value);
            DrawInputText("目标名包含##TriggerKillTarget", trigger.TargetName, 120, value => trigger.TargetName = value);

            var localOnly = trigger.LocalPlayerOnly ?? true;
            if (ImGui.Checkbox("仅本机击杀##TriggerLocalKillOnly", ref localOnly))
                trigger.LocalPlayerOnly = localOnly;
        }
        else
        {
            DrawInputText("事件类型##TriggerEventType", trigger.EventType, 120, value => trigger.EventType = value);
        }
    }

    private void DrawSkillNameSearch(TriggerDefinition trigger)
    {
        DrawInputText("技能名搜索##TriggerSkill", trigger.SkillNameContains, 120, value => trigger.SkillNameContains = value);

        var options = gameDataLookupService.SearchActions(trigger.SkillNameContains);
        if (options.Count == 0)
            return;

        ImGui.SetNextItemWidth(320f);
        if (!ImGui.BeginCombo("选择技能全称##TriggerSkillPick", "选择匹配技能"))
            return;

        foreach (var option in options)
        {
            if (ImGui.Selectable($"{option.Name}##Action{option.Id}", false))
                trigger.SkillNameContains = option.Name;
        }

        ImGui.EndCombo();
    }

    private void DrawMapSelector(TriggerDefinition trigger)
    {
        DrawInputText("地图名搜索##TriggerMapSearch", mapSearchText, 120, value => mapSearchText = value);

        var currentMap = gameDataLookupService.FindMap((uint)Math.Max(0, trigger.TerritoryType), (uint)Math.Max(0, trigger.MapId));
        var preview = currentMap == null
            ? "任意地图"
            : $"{currentMap.Name} ({currentMap.TerritoryType})";

        ImGui.SetNextItemWidth(320f);
        if (!ImGui.BeginCombo("指定地图##TriggerMapPick", preview))
            return;

        if (ImGui.Selectable("任意地图##AnyMap", trigger.TerritoryType == 0 && trigger.MapId == 0))
        {
            trigger.TerritoryType = 0;
            trigger.MapId = 0;
        }

        foreach (var option in gameDataLookupService.SearchMaps(mapSearchText))
        {
            var selected = trigger.TerritoryType == option.TerritoryType && trigger.MapId == option.MapId;
            if (ImGui.Selectable($"{option.Name} ({option.TerritoryType})##Map{option.TerritoryType}-{option.MapId}", selected))
            {
                trigger.TerritoryType = (int)option.TerritoryType;
                trigger.MapId = (int)option.MapId;
            }

            if (selected)
                ImGui.SetItemDefaultFocus();
        }

        ImGui.EndCombo();
    }

    private void DrawExtendedTriggerEditor(RuleDefinition rule)
    {
        rule.Trigger ??= new TriggerDefinition();
        var trigger = rule.Trigger;

        ImGui.Text("触发器");
        if (DrawStringCombo("类型##TriggerType", trigger.Type, TriggerTypes, value => SetTriggerType(trigger, value)))
            rulesEditorMessage = "触发器类型已更新，保存后生效。";

        if (trigger.Type.Equals("SkillUsed", StringComparison.OrdinalIgnoreCase))
        {
            DrawInputText("角色名包含##TriggerActor", trigger.ActorName, 120, value => trigger.ActorName = value);
            DrawSkillNameSearch(trigger);

            var localOnly = trigger.LocalPlayerOnly ?? false;
            if (ImGui.Checkbox("仅本机角色##TriggerLocalOnly", ref localOnly))
                trigger.LocalPlayerOnly = localOnly;
        }
        else if (trigger.Type.Equals("ItemAcquired", StringComparison.OrdinalIgnoreCase))
        {
            DrawInputText("物品名包含##TriggerItemName", trigger.ItemNameContains, 160, value => trigger.ItemNameContains = value);
            DrawInputText("获得者包含##TriggerItemActor", trigger.ActorName, 120, value => trigger.ActorName = value);

            var localOnly = trigger.LocalPlayerOnly ?? true;
            if (ImGui.Checkbox("仅本机玩家获得##TriggerItemLocalOnly", ref localOnly))
                trigger.LocalPlayerOnly = localOnly;
        }
        else if (trigger.Type.Equals("MapChanged", StringComparison.OrdinalIgnoreCase))
        {
            DrawMapSelector(trigger);
        }
        else if (trigger.Type.Equals("CombatEntered", StringComparison.OrdinalIgnoreCase)
                 || trigger.Type.Equals("CombatExited", StringComparison.OrdinalIgnoreCase))
        {
            ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), "此触发器无需额外参数。");
        }
        else if (trigger.Type.Equals("HpChanged", StringComparison.OrdinalIgnoreCase))
        {
            ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), "HP 数值或最大 HP 变化时触发。");
        }
        else if (trigger.Type.Equals("HpLow", StringComparison.OrdinalIgnoreCase))
        {
            DrawPercentInputInt("低于等于 HP%##TriggerHpLowPercent", trigger.HpPercentBelow, value => trigger.HpPercentBelow = value);
        }
        else if (trigger.Type.Equals("LocalPlayerDefeated", StringComparison.OrdinalIgnoreCase))
        {
            ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), "本机玩家 HP 从大于 0 变为 0 时触发。");
        }
        else if (trigger.Type.Equals("JobChanged", StringComparison.OrdinalIgnoreCase))
        {
            DrawNonNegativeInputInt("指定职业 ID，0=任意##TriggerClassJobId", trigger.ClassJobId, value => trigger.ClassJobId = value);
            DrawInputText("职业名包含##TriggerJobName", trigger.JobNameContains, 120, value => trigger.JobNameContains = value);
        }
        else if (trigger.Type.Equals("StatusGained", StringComparison.OrdinalIgnoreCase)
                 || trigger.Type.Equals("StatusLost", StringComparison.OrdinalIgnoreCase))
        {
            DrawNonNegativeInputInt("状态/Buff ID，0=任意##TriggerStatusId", trigger.StatusId, value => trigger.StatusId = value);
            DrawInputText("状态/Buff 名包含##TriggerStatusName", trigger.StatusNameContains, 120, value => trigger.StatusNameContains = value);
        }
        else if (trigger.Type.Equals("Kill", StringComparison.OrdinalIgnoreCase))
        {
            DrawInputText("击杀者包含##TriggerKillActor", trigger.ActorName, 120, value => trigger.ActorName = value);
            DrawInputText("目标名包含##TriggerKillTarget", trigger.TargetName, 120, value => trigger.TargetName = value);

            var localOnly = trigger.LocalPlayerOnly ?? true;
            if (ImGui.Checkbox("仅本机击杀##TriggerLocalKillOnly", ref localOnly))
                trigger.LocalPlayerOnly = localOnly;
        }
        else
        {
            DrawInputText("事件类型##TriggerEventType", trigger.EventType, 120, value => trigger.EventType = value);
        }
    }
}
