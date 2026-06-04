using System;
using AllTimeSoundTrigger.Community;
using AllTimeSoundTrigger.Audio;
using AllTimeSoundTrigger.Core;
using AllTimeSoundTrigger.EventSources;
using AllTimeSoundTrigger.EventSources.Payloads;
using AllTimeSoundTrigger.Rules;
using AllTimeSoundTrigger.Services;
using AllTimeSoundTrigger.UI;
using Dalamud.Game.Command;
using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ClientCondition = Dalamud.Plugin.Services.ICondition;

namespace AllTimeSoundTrigger;

public sealed class Plugin : IDalamudPlugin
{
    public const string DisplayName = "全时刻音效触发器";

    private const string CommandName = "/atsound";

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly IPluginLog log;
    private readonly EventBus eventBus = new();
    private readonly EventLogService eventLogService = new();
    private readonly RulesEngine rulesEngine = new();
    private readonly AudioPlaybackService audioPlaybackService;
    private readonly ProfileStorageService profileStorageService;
    private readonly SfxPackService sfxPackService;
    private readonly CommunityPackService communityPackService;
    private readonly GameDataLookupService gameDataLookupService;
    private readonly RuleFactory ruleFactory;
    private readonly WindowSystem windowSystem = new("AllTimeSoundTrigger");
    private readonly ChatLogListener chatLogListener;
    private readonly ClientStatePoller clientStatePoller;
    private readonly MainWindow mainWindow;
    private readonly IDisposable eventSubscription;
    private bool disposed;

    public string Name => DisplayName;

    public Configuration Configuration { get; }

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IObjectTable objectTable,
        IClientState clientState,
        IFramework framework,
        ClientCondition condition,
        IDataManager dataManager,
        IChatGui chatGui,
        ITextureProvider textureProvider,
        IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;
        this.log = log;

        Configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(pluginInterface);
        profileStorageService = new ProfileStorageService(log);
        profileStorageService.Initialize(Configuration);
        sfxPackService = new SfxPackService(log);
        communityPackService = new CommunityPackService(sfxPackService.RootDirectory, log);
        gameDataLookupService = new GameDataLookupService(dataManager, log);

        audioPlaybackService = new AudioPlaybackService(
            () => Configuration.Audio.MaxConcurrentSounds,
            () => Configuration.Audio.MasterVolume,
            eventLogService,
            log);
        ruleFactory = new RuleFactory(
            eventLogService,
            audioPlaybackService,
            log,
            soundId => Configuration.SoundLibrary.FindById(soundId));
        ReloadRulesFromConfiguration();

        mainWindow = new MainWindow(
            eventLogService,
            audioPlaybackService,
            profileStorageService,
            sfxPackService,
            communityPackService,
            gameDataLookupService,
            textureProvider,
            Configuration,
            ReloadRulesFromConfiguration,
            pluginInterface.AssemblyLocation.DirectoryName ?? string.Empty);
        windowSystem.AddWindow(mainWindow);

        eventSubscription = eventBus.Subscribe(OnGameEvent);
        chatLogListener = new ChatLogListener(chatGui, objectTable, log);
        clientStatePoller = new ClientStatePoller(framework, clientState, objectTable, condition, log);
        chatLogListener.Start(eventBus.Publish);
        clientStatePoller.Start(eventBus.Publish);

        pluginInterface.UiBuilder.Draw += DrawUi;
        pluginInterface.UiBuilder.OpenConfigUi += OpenMainWindow;
        pluginInterface.UiBuilder.OpenMainUi += OpenMainWindow;

        commandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "打开全时刻音效触发器窗口。"
        });

        eventLogService.AddSystemMessage("插件已加载，正在监听聊天日志中的技能使用事件。");
        log.Information("[AllTimeSoundTrigger] Plugin loaded.");
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        chatLogListener.Stop();
        clientStatePoller.Stop();
        eventSubscription.Dispose();
        commandManager.RemoveHandler(CommandName);
        pluginInterface.UiBuilder.Draw -= DrawUi;
        pluginInterface.UiBuilder.OpenConfigUi -= OpenMainWindow;
        pluginInterface.UiBuilder.OpenMainUi -= OpenMainWindow;
        windowSystem.RemoveAllWindows();
        mainWindow.Dispose();
        audioPlaybackService.Dispose();
        communityPackService.Dispose();
        log.Information("[AllTimeSoundTrigger] Plugin unloaded.");
    }

    private void DrawUi()
    {
        windowSystem.Draw();
    }

    private void OpenMainWindow()
    {
        mainWindow.IsOpen = true;
    }

    private void OnCommand(string command, string args)
    {
        OpenMainWindow();
    }

    private void OnGameEvent(GameEvent gameEvent)
    {
        eventLogService.RecordEvent(gameEvent);
        StopLoopingStatusSoundIfNeeded(gameEvent);
        AutoSwitchProfileIfNeeded(gameEvent);
        rulesEngine.HandleEvent(gameEvent);

        if (gameEvent.Payload is SkillUsedPayload payload)
            log.Information("[AllTimeSoundTrigger] SkillUsed: {ActorName} {Verb} {SkillName}", payload.ActorName, payload.Verb, payload.SkillName);
        else if (gameEvent.Payload is KillPayload killPayload)
            log.Information("[AllTimeSoundTrigger] Kill: {ActorName} defeated {TargetName}", killPayload.ActorName, killPayload.TargetName);
        else if (gameEvent.Payload is ItemAcquiredPayload itemPayload)
            log.Information("[AllTimeSoundTrigger] ItemAcquired: {ActorName} obtained {ItemName} x{Quantity}", itemPayload.ActorName, itemPayload.ItemName, itemPayload.Quantity);
        else if (gameEvent.Payload is MapChangedPayload mapPayload)
            log.Information("[AllTimeSoundTrigger] MapChanged: {OldTerritory}/{OldMap} -> {Territory}/{Map}", mapPayload.PreviousTerritoryType, mapPayload.PreviousMapId, mapPayload.TerritoryType, mapPayload.MapId);
        else if (gameEvent.Payload is JobChangedPayload jobPayload)
            log.Information("[AllTimeSoundTrigger] JobChanged: {OldJob} -> {Job}", jobPayload.PreviousClassJobId, jobPayload.ClassJobId);
        else if (gameEvent.Payload is HpChangedPayload hpPayload)
            log.Information("[AllTimeSoundTrigger] {EventType}: {OldHp}/{OldMaxHp} -> {Hp}/{MaxHp} ({Percent:0.0}%)", gameEvent.EventType, hpPayload.PreviousCurrentHp, hpPayload.PreviousMaxHp, hpPayload.CurrentHp, hpPayload.MaxHp, hpPayload.HpPercent);
        else if (gameEvent.Payload is CombatStateChangedPayload combatPayload)
            log.Information("[AllTimeSoundTrigger] CombatState: {OldState} -> {State}", combatPayload.WasInCombat, combatPayload.IsInCombat);
        else if (gameEvent.Payload is StatusChangedPayload statusPayload)
            log.Information("[AllTimeSoundTrigger] {EventType}: {StatusId} {StatusName}", gameEvent.EventType, statusPayload.StatusId, statusPayload.StatusName);
    }

    private void StopLoopingStatusSoundIfNeeded(GameEvent gameEvent)
    {
        if (!gameEvent.EventType.Equals("StatusLost", StringComparison.OrdinalIgnoreCase)
            || gameEvent.Payload is not StatusChangedPayload statusPayload)
        {
            return;
        }

        var stopped = audioPlaybackService.StopByStatusId(statusPayload.StatusId);
        if (stopped <= 0)
            return;

        eventLogService.AddRuleMessage($"Buff 消失，自动停止音效：{statusPayload.StatusName}({statusPayload.StatusId})");
        log.Information(
            "[AllTimeSoundTrigger] Stopped {Count} looping sounds for lost status {StatusId} {StatusName}.",
            stopped,
            statusPayload.StatusId,
            statusPayload.StatusName);
    }

    private void AutoSwitchProfileIfNeeded(GameEvent gameEvent)
    {
        var autoSwitch = Configuration.AutoSwitch;
        if (!autoSwitch.Enabled
            || gameEvent.EventType != "MapChanged"
            || gameEvent.Payload is not MapChangedPayload mapPayload
            || autoSwitch.TerritoryTypes.Count == 0)
        {
            return;
        }

        var inTargetMap = autoSwitch.TerritoryTypes.Contains((int)mapPayload.TerritoryType);
        var desiredProfileId = inTargetMap ? autoSwitch.TargetProfileId : autoSwitch.FallbackProfileId;
        if (string.IsNullOrWhiteSpace(desiredProfileId)
            || profileStorageService.ActiveProfile.Id.Equals(desiredProfileId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!profileStorageService.SwitchProfile(desiredProfileId, Configuration))
            return;

        ReloadRulesFromConfiguration();
        eventLogService.AddSystemMessage($"已自动切换方案：{profileStorageService.ActiveProfile.Name}");
        log.Information(
            "[AllTimeSoundTrigger] Auto switched profile to {ProfileName} ({ProfileId}) for territory {TerritoryType}.",
            profileStorageService.ActiveProfile.Name,
            profileStorageService.ActiveProfile.Id,
            mapPayload.TerritoryType);
    }

    private void ReloadRulesFromConfiguration()
    {
        var rules = ruleFactory.CreateRules(profileStorageService.GetActiveRules());
        rulesEngine.ReplaceRules(rules);
        eventLogService.AddSystemMessage($"已从方案“{profileStorageService.ActiveProfile.Name}”加载 {rules.Count} 条启用规则。");
        log.Information(
            "[AllTimeSoundTrigger] Loaded {RuleCount} enabled rules from profile {ProfileName} ({ProfileId}).",
            rules.Count,
            profileStorageService.ActiveProfile.Name,
            profileStorageService.ActiveProfile.Id);
    }
}
