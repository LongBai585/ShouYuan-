using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Timers;
using Newtonsoft.Json;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;
using TShockAPI.DB;
using Timer = System.Timers.Timer;
using Microsoft.Xna.Framework;

namespace XiuXianShouYuan
{
    [ApiVersion(2, 1)]
    public class XiuXianShouYuan : TerrariaPlugin
    {
        #region 基础配置
        private static readonly string ConfigPath = Path.Combine(TShock.SavePath, "XiuXianConfig.json");
        private static readonly string DataPath = Path.Combine(TShock.SavePath, "XiuXianData.json");
        private readonly Timer _cultivationTimer = new Timer(30000);
        private readonly Timer _lifeCheckTimer = new Timer(5000);
        private readonly Timer _topUiRefreshTimer = new Timer(10000); // 顶部UI刷新定时器（默认10秒）
        private StatusManager statusManager; // 状态管理器

        public override string Name => "修仙星宿系统";
        public override string Author => "泷白";
        public override Version Version => new Version(1, 2, 2);
        public override string Description => "完美世界修炼体系与星宿流派系统";

        public XiuXianShouYuan(Main game) : base(game)
        {
            Order = 1;
            _cultivationTimer.AutoReset = true;
            _lifeCheckTimer.AutoReset = true;
            _topUiRefreshTimer.AutoReset = true; // 顶部UI定时器自动重置
            statusManager = new StatusManager(); // 初始化状态管理器
        }
        #endregion

        #region 初始化与钩子注册
        public override void Initialize()
        {
            PlayerHooks.PlayerChat += OnPlayerChat;
            PlayerHooks.PlayerPostLogin += OnPlayerPostLogin;
            PlayerHooks.PlayerPreLogin += OnPlayerPreLogin;
            PlayerHooks.PlayerLogout += OnPlayerLogout;

            GetDataHandlers.PlayerDamage += OnPlayerDamage;
            GetDataHandlers.KillMe += OnDead;

            GeneralHooks.ReloadEvent += OnReload;

            ServerApi.Hooks.GameInitialize.Register(this, OnInitialize);
            ServerApi.Hooks.ServerJoin.Register(this, OnServerJoin);
            ServerApi.Hooks.ServerLeave.Register(this, OnServerLeave);
            ServerApi.Hooks.NpcKilled.Register(this, OnBossKilled);
            AccountHooks.AccountCreate += OnAccountCreate;
            RegionHooks.RegionEntered += OnRegionEntered;
            RegionHooks.RegionLeft += OnRegionLeft;

            _cultivationTimer.Elapsed += Cultivate;
            _lifeCheckTimer.Elapsed += CheckPlayerLife;
            _topUiRefreshTimer.Elapsed += RefreshTopUIForAllPlayers; // 顶部UI刷新事件

            Commands.ChatCommands.Add(new Command("shouyuan.player", ShowStatus, "状态"));
            Commands.ChatCommands.Add(new Command("shouyuan.player", CultivationCommand, "修仙"));
            Commands.ChatCommands.Add(new Command("shouyuan.admin", AdminForceRebirth, "寿元转生"));
            Commands.ChatCommands.Add(new Command("shouyuan.admin", AdminAdjustLife, "调整寿元"));
            Commands.ChatCommands.Add(new Command("shouyuan.player", SetDharmaName, "法号"));
            Commands.ChatCommands.Add(new Command("shouyuan.player", ChooseStarSign, "选择星宿"));
            Commands.ChatCommands.Add(new Command("shouyuan.player", ViewConstellation, "命座"));
            Commands.ChatCommands.Add(new Command("shouyuan.admin", SetServerName, "设置服务器名称"));
            Commands.ChatCommands.Add(new Command("shouyuan.admin", SetChatUIOffset, "设置聊天ui偏移", "chatuioffset"));
            Commands.ChatCommands.Add(new Command("shouyuan.admin", SetTopUIOffset, "设置顶部ui偏移", "topuioffset"));
            Commands.ChatCommands.Add(new Command("shouyuan.admin", AddRealmCondition, "添加境界条件"));
            Commands.ChatCommands.Add(new Command("shouyuan.admin", AddRealmReward, "添加境界奖励"));
            Commands.ChatCommands.Add(new Command("shouyuan.admin", SetTopUIRefreshInterval, "设置顶部ui刷新间隔", "topuirefresh"));
            Commands.ChatCommands.Add(new Command("shouyuan.admin", SetStarSignIcon, "设置星宿图标", "setsignicon"));
            Commands.ChatCommands.Add(new Command("shouyuan.admin", AddRealmBuff, "添加境界buff"));
            Commands.ChatCommands.Add(new Command("shouyuan.admin", ReloadXiuXianConfig, "重读修仙"));
            
            // 新增命令
            Commands.ChatCommands.Add(new Command("shouyuan.player", ResetCultivation, "散尽修为"));
            Commands.ChatCommands.Add(new Command("shouyuan.admin", ResetAllPlayersCultivation, "仙道重开"));

            CreateDefaultPermissions();
        }

        private void CreateDefaultPermissions()
        {
            try
            {
                if (!TShock.Groups.GroupExists("修仙弟子"))
                {
                    TShock.Groups.AddGroup(
                        "修仙弟子",
                        "default",
                        "0,255,0",
                        "修仙系统玩家权限"
                    );
                    TShock.Groups.AddPermissions("修仙弟子", new List<string> { "shouyuan.player" });
                }

                if (!TShock.Groups.GroupExists("修仙仙尊"))
                {
                    TShock.Groups.AddGroup(
                        "修仙仙尊",
                        "修仙弟子",
                        "255,0,0",
                        "修仙系统管理员权限"
                    );
                    TShock.Groups.AddPermissions("修仙仙尊", new List<string> {
                        "shouyuan.player",
                        "shouyuan.admin"
                    });
                }
            }
            catch (Exception ex)
            {
                TShock.Log.Error($"创建权限组失败: {ex.Message}");
            }
        }

        private void OnInitialize(EventArgs args)
        {
            try
            {
                XiuXianConfig.Load(ConfigPath);
                XiuXianData.Load(DataPath);

                _cultivationTimer.Start();
                _lifeCheckTimer.Start();

                // 配置并启动顶部UI刷新定时器
                _topUiRefreshTimer.Interval = XiuXianConfig.Instance.TopUIRefreshInterval;
                _topUiRefreshTimer.Start();

                TSPlayer.All.SendSuccessMessage($"[{XiuXianConfig.Instance.ServerName}] 天地灵气已复苏！输入 /修仙 开启修炼之路");
            }
            catch (Exception ex)
            {
                TShock.Log.Error($"修仙系统初始化失败: {ex.Message}");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                PlayerHooks.PlayerChat -= OnPlayerChat;
                PlayerHooks.PlayerPostLogin -= OnPlayerPostLogin;
                PlayerHooks.PlayerPreLogin -= OnPlayerPreLogin;
                PlayerHooks.PlayerLogout -= OnPlayerLogout;

                GetDataHandlers.PlayerDamage -= OnPlayerDamage;
                GetDataHandlers.KillMe -= OnDead;

                GeneralHooks.ReloadEvent -= OnReload;

                ServerApi.Hooks.GameInitialize.Deregister(this, OnInitialize);
                ServerApi.Hooks.ServerJoin.Deregister(this, OnServerJoin);
                ServerApi.Hooks.ServerLeave.Deregister(this, OnServerLeave);
                ServerApi.Hooks.NpcKilled.Deregister(this, OnBossKilled);
                AccountHooks.AccountCreate -= OnAccountCreate;
                RegionHooks.RegionEntered -= OnRegionEntered;
                RegionHooks.RegionLeft -= OnRegionLeft;

                _cultivationTimer.Stop();
                _cultivationTimer.Dispose();
                _lifeCheckTimer.Stop();
                _lifeCheckTimer.Dispose();
                _topUiRefreshTimer.Stop();
                _topUiRefreshTimer.Dispose();

                XiuXianData.Save(DataPath);
            }
            base.Dispose(disposing);
        }
        #endregion

        #region 寿元耗尽处理
        private void CheckPlayerLife(object sender, ElapsedEventArgs e)
        {
            foreach (var player in TShock.Players.Where(p => p != null && p.Active && p.IsLoggedIn))
            {
                try
                {
                    var data = XiuXianData.GetPlayer(player.Name);

                    // 管理员豁免
                    if (player.Group.HasPermission("shouyuan.admin"))
                        continue;

                    // 寿元耗尽处理
                    if (data.LifeYears <= 1)
                    {
                        player.Kick("寿元已耗尽！请使用/修仙转生或联系管理员", true);
                        TShock.Log.Info($"玩家 {player.Name} 因寿元耗尽被踢出");

                        // 重置状态
                        data.LifeDepletionWarned = false;
                        data.LifeDepletionTime = DateTime.MinValue;
                    }
                }
                catch (Exception ex)
                {
                    TShock.Log.Error($"{player.Name}寿元检查失败: {ex.Message}");
                }
            }
        }

        private void OnPlayerPreLogin(PlayerPreLoginEventArgs args)
        {
            try
            {
                var playerName = args.Player.Name;
                var data = XiuXianData.GetPlayer(playerName);

                // 检查玩家账号组权限
                bool isAdmin = false;
                var account = TShock.UserAccounts.GetUserAccountByName(playerName);
                if (account != null)
                {
                    var group = TShock.Groups.GetGroupByName(account.Group);
                    isAdmin = group != null && group.HasPermission("shouyuan.admin");
                }

                // 寿元耗尽（=1年）且非管理员则阻止登录
                if (data.LifeYears <= 1 && !isAdmin)
                {
                    args.Handled = true;
                    args.Player.Kick("寿元已耗尽！请联系管理员使用/寿元转生", true);
                    TShock.Log.Info($"玩家 {playerName} 登录时因寿元耗尽被阻止登录");
                }
            }
            catch (Exception ex)
            {
                TShock.Log.Error($"玩家登录前检查失败: {ex.Message}");
            }
        }
        #endregion

        #region 事件处理
        private void OnAccountCreate(AccountCreateEventArgs args)
        {
            try
            {
                var data = XiuXianData.GetPlayer(args.Account.Name);
                data.Realm = "凡人";
                data.LifeYears = 80;
                data.StarSign = "未选择";
                data.DharmaName = "";
                data.LifeDepletionWarned = false;
                data.LifeDepletionTime = DateTime.MinValue;
                data.KilledBosses = new HashSet<string>();

                var player = TShock.Players.FirstOrDefault(p => p?.Account?.Name == args.Account.Name);
                if (player != null)
                {
                    player.Group = TShock.Groups.GetGroupByName("修仙弟子");
                    player.SendSuccessMessage("你已被分配到修仙弟子权限组");
                }
                TShock.Log.Info($"新修士诞生: {args.Account.Name}");
            }
            catch (Exception ex)
            {
                TShock.Log.Error($"创建账号事件处理失败: {ex.Message}");
            }
        }

        private void OnBossKilled(NpcKilledEventArgs args)
        {
            try
            {
                NPC npc = args.npc;
                if (npc.boss && npc.type != Terraria.ID.NPCID.TargetDummy)
                {
                    string bossName = npc.GivenOrTypeName;
                    TShock.Log.Info($"检测到Boss击杀: {bossName}");

                    // 获取造成最后伤害的玩家
                    int lastDamagePlayer = npc.lastInteraction;
                    if (lastDamagePlayer >= 0 && lastDamagePlayer < 255)
                    {
                        var player = TShock.Players[lastDamagePlayer];
                        if (player != null && player.Active && player.IsLoggedIn)
                        {
                            var data = XiuXianData.GetPlayer(player.Name);
                            
                            // 修复：使用标准化的Boss名称进行比较
                            string normalizedBossName = NormalizeBossName(bossName);
                            if (!data.KilledBosses.Contains(normalizedBossName))
                            {
                                data.KilledBosses.Add(normalizedBossName);
                                player.SendSuccessMessage($"★★★ 你已击败 {bossName}，修为大增！ ★★★");
                                TShock.Log.Info($"玩家 {player.Name} 击败Boss {bossName}，标准化名称: {normalizedBossName}");
                                
                                // 更新UI
                                UpdateChatUI(player, data);
                                UpdateTopUI(player, data);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TShock.Log.Error($"处理Boss击杀失败: {ex.Message}");
            }
        }
        
        // 新增：标准化Boss名称
        private string NormalizeBossName(string bossName)
        {
            // 将Boss名称转换为小写并移除空格，以便比较
            return bossName.ToLowerInvariant().Replace(" ", "").Replace("'", "").Replace("-", "");
        }

        private void OnPlayerChat(PlayerChatEventArgs args)
        {
            try
            {
                var data = XiuXianData.GetPlayer(args.Player.Name);

                // 添加法号前缀
                if (!string.IsNullOrEmpty(data.DharmaName))
                {
                    string dharmaPrefix = $"[c/{data.NameColor}:{data.DharmaName}] ";
                    args.RawText = dharmaPrefix + args.RawText;
                }

                // 寿元查询
                if (args.RawText.Contains("寿元") || args.RawText.Contains("寿命"))
                {
                    args.Player.SendInfoMessage($"当前剩余寿元: {data.LifeYears:F1}年");
                    args.Handled = true;
                }
            }
            catch (Exception ex)
            {
                TShock.Log.Error($"玩家聊天事件处理失败: {ex.Message}");
            }
        }

        private void OnPlayerDamage(object sender, GetDataHandlers.PlayerDamageEventArgs args)
        {
            try
            {
                var player = args.Player;
                if (player == null || player.Dead || args.Damage < 30)
                    return;

                var data = XiuXianData.GetPlayer(player.Name);
                int penalty = new Random().Next(5, 16);
                data.LifeYears = Math.Max(1, data.LifeYears - penalty);

                // 显示状态更新
                UpdateChatUI(player, data);
                UpdateTopUI(player, data);

                player.SendWarningMessage($"根基受损！寿元减少{penalty}年 (剩余: {data.LifeYears:F1}年)");

                // 当减少到1年时立即踢出
                if (data.LifeYears <= 1 && !player.Group.HasPermission("shouyuan.admin"))
                {
                    player.Kick("根基严重受损！寿元耗尽", true);
                    TShock.Log.Info($"玩家 {player.Name} 因战斗损伤寿元耗尽被踢出");
                }
            }
            catch (Exception ex)
            {
                TShock.Log.Error($"玩家受伤事件处理失败: {ex.Message}");
            }
        }

        private void OnDead(object sender, GetDataHandlers.KillMeEventArgs args)
        {
            try
            {
                var player = args.Player;
                if (player == null) return;

                var data = XiuXianData.GetPlayer(player.Name);
                int penalty = new Random().Next(20, 51);
                data.LifeYears = Math.Max(1, data.LifeYears - penalty);

                // 显示状态更新
                UpdateChatUI(player, data);
                UpdateTopUI(player, data);

                player.SendWarningMessage($"道陨之际！寿元减少{penalty}年 (剩余: {data.LifeYears:F1}年)");

                // 当减少到1年时立即踢出
                if (data.LifeYears <= 1 && !player.Group.HasPermission("shouyuan.admin"))
                {
                    player.Kick("道陨之际！寿元耗尽", true);
                    TShock.Log.Info($"玩家 {player.Name} 因死亡导致寿元耗尽被踢出");
                }
            }
            catch (Exception ex)
            {
                TShock.Log.Error($"玩家死亡事件处理失败: {ex.Message}");
            }
        }

        private void OnPlayerPostLogin(PlayerPostLoginEventArgs args)
        {
            try
            {
                var player = args.Player;
                var data = XiuXianData.GetPlayer(player.Name);

                player.SendMessage("====================================", Microsoft.Xna.Framework.Color.Gold);
                player.SendMessage($"欢迎来到 [c/FF69B4:{XiuXianConfig.Instance.ServerName}]", Microsoft.Xna.Framework.Color.White);
                player.SendMessage("====================================", Microsoft.Xna.Framework.Color.Gold);

                ShowFullStatus(player, data);
                UpdateChatUI(player, data);
                UpdateTopUI(player, data);

                if (data.StarSign == "未选择")
                {
                    player.SendMessage("★★★ 请使用 /选择星宿 开启修仙之旅 ★★★", Microsoft.Xna.Framework.Color.Yellow);
                }

                if (data.LifeYears <= 1)
                {
                    player.SendErrorMessage("★★★ 警告：寿元已耗尽！将被立即踢出 ★★★");
                    if (!player.Group.HasPermission("shouyuan.admin"))
                    {
                        player.Kick("寿元已耗尽！请使用/修仙转生", true);
                        TShock.Log.Info($"玩家 {player.Name} 登录时因寿元耗尽被踢出");
                    }
                }

                if (player.Group.HasPermission("shouyuan.admin"))
                    player.SendSuccessMessage("★★★ 你拥有修仙仙尊管理权限 ★★★");
            }
            catch (Exception ex)
            {
                TShock.Log.Error($"玩家登录后处理失败: {ex.Message}");
            }
        }

        private void OnPlayerLogout(PlayerLogoutEventArgs args)
        {
            try
            {
                XiuXianData.SavePlayer(args.Player.Name);
                // 清理状态文本
                statusManager.RemoveText(args.Player);
            }
            catch (Exception ex) { TShock.Log.Error($"玩家登出处理失败: {ex.Message}"); }
        }

        private void OnReload(ReloadEventArgs args)
        {
            try
            {
                XiuXianConfig.Load(ConfigPath);
                args.Player.SendSuccessMessage("修仙法则已重新感悟！");

                // 更新顶部UI刷新间隔
                _topUiRefreshTimer.Interval = XiuXianConfig.Instance.TopUIRefreshInterval;
            }
            catch (Exception ex)
            {
                TShock.Log.Error($"重载配置失败: {ex.Message}");
            }
        }

        private void OnRegionEntered(RegionHooks.RegionEnteredEventArgs args)
        {
            try
            {
                if (args.Region.Name.Contains("灵脉") || args.Region.Name.Contains("洞天"))
                {
                    var player = args.Player;
                    player.SendSuccessMessage("进入灵脉之地，修炼速度提升！");
                    var data = XiuXianData.GetPlayer(player.Name);
                    data.InHolyLand = true;
                    UpdateChatUI(player, data);
                    UpdateTopUI(player, data);
                }
            }
            catch (Exception ex)
            {
                TShock.Log.Error($"进入区域事件处理失败: {ex.Message}");
            }
        }

        private void OnRegionLeft(RegionHooks.RegionLeftEventArgs args)
        {
            try
            {
                if (args.Region.Name.Contains("灵脉") || args.Region.Name.Contains("洞天"))
                {
                    var player = args.Player;
                    player.SendInfoMessage("离开灵脉之地，修炼恢复正常");
                    var data = XiuXianData.GetPlayer(player.Name);
                    data.InHolyLand = false;
                    UpdateChatUI(player, data);
                    UpdateTopUI(player, data);
                }
            }
            catch (Exception ex)
            {
                TShock.Log.Error($"离开区域事件处理失败: {ex.Message}");
            }
        }

        private void OnServerJoin(JoinEventArgs args)
        {
            try
            {
                var player = TShock.Players[args.Who];
                if (player != null && !XiuXianData.Players.ContainsKey(player.Name))
                {
                    var data = XiuXianData.GetPlayer(player.Name);

                    player.SendMessage("====================================", Microsoft.Xna.Framework.Color.Gold);
                    player.SendMessage($"欢迎来到 [c/FF69B4:{XiuXianConfig.Instance.ServerName}]", Microsoft.Xna.Framework.Color.White);
                    player.SendMessage("====================================", Microsoft.Xna.Framework.Color.Gold);
                    player.SendMessage($"新修士，初始寿元: {data.LifeYears}年", Microsoft.Xna.Framework.Color.LightGreen);
                    player.SendMessage("使用 /选择星宿 开启你的修仙之路", Microsoft.Xna.Framework.Color.Yellow);

                    UpdateChatUI(player, data);
                    UpdateTopUI(player, data);

                    // 检查寿元是否耗尽
                    if (data.LifeYears <= 1 && !player.Group.HasPermission("shouyuan.admin"))
                    {
                        player.Kick("寿元已耗尽！请使用/修仙转生", true);
                        TShock.Log.Info($"玩家 {player.Name} 加入时因寿元耗尽被踢出");
                    }
                }
            }
            catch (Exception ex)
            {
                TShock.Log.Error($"玩家加入事件处理失败: {ex.Message}");
            }
        }

        private void OnServerLeave(LeaveEventArgs args)
        {
            try
            {
                var player = TShock.Players[args.Who];
                if (player != null)
                {
                    XiuXianData.SavePlayer(player.Name);
                    // 清理状态文本
                    statusManager.RemoveText(player);
                }
            }
            catch (Exception ex)
            {
                TShock.Log.Error($"玩家离开事件处理失败: {ex.Message}");
            }
        }
        #endregion

        #region 可视化功能
        private void ShowFullStatus(TSPlayer player, XiuXianData data)
        {
            var realmInfo = XiuXianConfig.Instance.GetRealmInfo(data.Realm);
            var starSign = XiuXianConfig.Instance.StarSigns.FirstOrDefault(s => s.Name == data.StarSign);

            string progressBar = GenerateProgressBar(data.CultivationProgress,
                XiuXianConfig.Instance.GetNextRealm(realmInfo)?.BreakthroughReq ?? 100);

            player.SendMessage("═══════════ 修仙状态 ═══════════", Microsoft.Xna.Framework.Color.Cyan);
            player.SendMessage($"服务器: [c/FF69B4:{XiuXianConfig.Instance.ServerName}]", Microsoft.Xna.Framework.Color.White);

            if (starSign != null)
            {
                player.SendMessage($"星宿: [c/{starSign.Color.Hex3()}:{starSign.Name}] ([c/FFD700:{starSign.Type}])", Microsoft.Xna.Framework.Color.White);
            }
            else
            {
                player.SendMessage($"星宿: [c/FF0000:未选择]", Microsoft.Xna.Framework.Color.White);
            }

            player.SendMessage($"境界: [c/00FF00:{data.Realm}境]", Microsoft.Xna.Framework.Color.White);

            // 修改寿元显示逻辑
            var lifeColor = data.LifeYears > 100 ? "00FF00" :
                            data.LifeYears > 1 ? "FFFF00" :
                            "FF0000";
            string lifeText = data.LifeYears > 1 ?
                $"{data.LifeYears:F1}年" :
                "寿元耗尽";
            player.SendMessage($"剩余寿元: [c/{lifeColor}:{lifeText}]", Microsoft.Xna.Framework.Color.White);

            if (!string.IsNullOrEmpty(data.DharmaName))
            {
                player.SendMessage($"法号: [c/FF69B4:{data.DharmaName}]", Microsoft.Xna.Framework.Color.White);
            }

            player.SendMessage($"修炼进度: [c/00BFFF:{data.CultivationProgress}%]", Microsoft.Xna.Framework.Color.White);
            player.SendMessage(progressBar, Microsoft.Xna.Framework.Color.LightBlue);

            player.SendMessage($"命座: [c/9370DB:{data.ConstellationLevel}/7]", Microsoft.Xna.Framework.Color.White);
            player.SendMessage($"转生次数: [c/FFD700:{data.RebirthCount}]", Microsoft.Xna.Framework.Color.White);

            // 显示已击杀Boss
            if (data.KilledBosses.Count > 0)
            {
                player.SendMessage($"已击杀Boss: {string.Join(", ", data.KilledBosses)}", Microsoft.Xna.Framework.Color.Orange);
            }

            if (data.InHolyLand)
            {
                player.SendMessage("修炼环境: [c/00FF00:灵脉之地]", Microsoft.Xna.Framework.Color.White);
            }

            if (data.LifeYears <= 1)
            {
                player.SendMessage("═══════════ 警告 ═══════════", Microsoft.Xna.Framework.Color.Red);
                player.SendMessage("★★★ 寿元已耗尽！将被立即踢出 ★★★", Microsoft.Xna.Framework.Color.Red);
                player.SendMessage("★★★ 请立即使用 /修仙转生 ★★★", Microsoft.Xna.Framework.Color.Red);
            }

            player.SendMessage("══════════════════════════════", Microsoft.Xna.Framework.Color.Cyan);
        }

        // 聊天框UI（原始UI）
        private void UpdateChatUI(TSPlayer player, XiuXianData data)
        {
            var sb = new StringBuilder();
            var config = XiuXianConfig.Instance;

            // 使用配置文件中的偏移量
            int offsetX = config.ChatUIOffsetX;
            int offsetY = config.ChatUIOffsetY;

            // Y轴偏移 - 使用换行符
            if (offsetY > 0)
            {
                sb.Append(new string('\n', offsetY));
            }

            // X轴偏移 - 使用空格
            string xOffset = (offsetX > 0) ? new string(' ', offsetX) : "";

            // 境界信息
            var realmInfo = config.GetRealmInfo(data.Realm);
            sb.AppendLine($"{xOffset}{$"境界: {data.Realm}境".Color(Color.LightGreen)}");

            // 星宿信息
            if (data.StarSign != "未选择")
            {
                var starSign = config.StarSigns.FirstOrDefault(s => s.Name == data.StarSign);
                if (starSign != null)
                {
                    sb.AppendLine($"{xOffset}{$"星宿: {starSign.Name}".Color(starSign.Color)}");
                }
            }

            // 修改寿元显示逻辑
            var lifeColor = data.LifeYears > 100 ? Color.LightGreen :
                           data.LifeYears > 1 ? Color.Yellow :
                           Color.Red;
            string lifeText = data.LifeYears > 1 ?
                $"{data.LifeYears:F1}年" :
                "寿元耗尽";
            sb.AppendLine($"{xOffset}{$"寿元: {lifeText}".Color(lifeColor)}");

            var nextRealm = config.GetNextRealm(realmInfo);
            int nextReq = nextRealm?.BreakthroughReq ?? 100;
            sb.AppendLine($"{xOffset}{CreateExpBar(data.CultivationProgress, nextReq, 20, Color.Cyan, Color.Gray, Color.LightBlue)}");

            sb.AppendLine($"{xOffset}{$"命座: {data.ConstellationLevel}/7".Color(Color.Purple)}");

            if (!string.IsNullOrEmpty(data.DharmaName))
            {
                sb.AppendLine($"{xOffset}{$"法号: {data.DharmaName}".Color(Color.HotPink)}");
            }

            if (data.LifeYears <= 1)
            {
                sb.AppendLine($"{xOffset}{"★★★ 寿元耗尽！将被踢出 ★★★".Color(Color.Red)}");
            }

            // 直接发送构建的字符串
            player.SendMessage(sb.ToString(), Color.White);
        }

        //顶部状态栏ui
        private void UpdateTopUI(TSPlayer player, XiuXianData data)
        {
            var sb = new StringBuilder();
            var config = XiuXianConfig.Instance;

            // 处理Y轴偏移
            if (config.TopUIOffsetY > 0)
            {
                sb.Append(new string('\n', config.TopUIOffsetY));
            }

            // 处理X轴偏移 - 负数向左偏移，正数向右偏移
            int absXOffset = Math.Abs(config.TopUIOffsetX);
            string xOffset = new string(' ', absXOffset);

            // 如果X偏移为负数，则在每行末尾添加空格（向左偏移）
            // 如果X偏移为正数，则在每行开头添加空格（向右偏移）
            Func<string, string> applyXOffset = line =>
            {
                if (config.TopUIOffsetX < 0)
                    return line + xOffset; // 向左偏移：末尾添加空格
                else
                    return xOffset + line; // 向右偏移：开头添加空格
            };

            sb.AppendLine(applyXOffset($"玩家名称: {player.Name}".Color(Color.LightSkyBlue)));
            sb.AppendLine(applyXOffset($"玩家境界: {data.Realm}境".Color(Color.LightGreen)));
            sb.AppendLine(applyXOffset($"转生次数: {data.RebirthCount}".Color(Color.Yellow)));

            var realmInfo = config.GetRealmInfo(data.Realm);
            var nextRealm = config.GetNextRealm(realmInfo);
            int expMax = nextRealm?.BreakthroughReq ?? 100;
            sb.AppendLine(applyXOffset(CreateExpBar(data.CultivationProgress, expMax, 20, Color.Cyan, Color.DarkSlateGray, Color.LightBlue, "修炼进度: ")));

            // 修改星宿图标显示 - 使用配置中的图标
            string starIcon = "[i:65]";
            if (data.StarSign != "未选择")
            {
                var starSign = config.StarSigns.FirstOrDefault(s => s.Name == data.StarSign);
                if (starSign != null && starSign.Icon != 0)
                {
                    starIcon = $"[i:{starSign.Icon}]";
                }
            }

            var lifeColor = data.LifeYears > 100 ? Color.LightGreen :
                            data.LifeYears > 1 ? Color.Yellow :
                            Color.Red;
            string lifeText = data.LifeYears > 1 ?
                $"{data.LifeYears:F1}年" :
                "寿元耗尽";
            sb.AppendLine(applyXOffset($"{starIcon} {$"寿元: {lifeText}".Color(lifeColor)}"));

            sb.AppendLine(applyXOffset($"命座: {data.ConstellationLevel}/7".Color(Color.Purple)));

            if (!string.IsNullOrEmpty(data.DharmaName))
                sb.AppendLine(applyXOffset($"法号: {data.DharmaName}".Color(Color.HotPink)));

            if (data.LifeYears <= 1)
                sb.AppendLine(applyXOffset("⚠ 寿元耗尽！".Color(Color.Red)));

            // 使用StatusManager显示顶部UI
            statusManager.AddOrUpdateText(player, "top_xiuxian_info", sb.ToString().TrimEnd(), Color.Transparent);
        }

        // 统一使用更简洁的经验条生成方法
        private string CreateExpBar(int current, int max, int width,
            Color filledColor, Color emptyColor, Color textColor, string prefix = "")
        {
            if (max <= 0) max = 1;
            double percent = Math.Min(1.0, (double)current / max);

            int filledBlocks = (int)Math.Ceiling(percent * width);
            if (percent > 0 && filledBlocks == 0) filledBlocks = 1;
            if (current == 0) filledBlocks = 0;

            int emptyBlocks = width - filledBlocks;

            return $"{prefix}{$"【".Color(filledColor)}" +
                   $"{new string('=', filledBlocks).Color(filledColor)}" +
                   $"{new string(' ', emptyBlocks).Color(emptyColor)}" +
                   $"{$"】".Color(filledColor)} " +
                   $"{$"{(int)(percent * 100)}%".Color(textColor)}";
        }

        private string GenerateProgressBar(int progress, int max)
        {
            int barLength = 20;
            int filled = (int)Math.Round((double)progress / max * barLength);
            filled = Math.Min(barLength, Math.Max(0, filled));

            var sb = new StringBuilder("[");
            sb.Append(new string('|', filled));
            sb.Append(new string('.', barLength - filled));
            sb.Append($"] {progress}/{max} ({progress * 100 / max}%)");

            return sb.ToString();
        }


        #endregion

        #region 顶部UI刷新功能
        private void RefreshTopUIForAllPlayers(object sender, ElapsedEventArgs e)
        {
            try
            {
                foreach (var player in TShock.Players.Where(p => p != null && p.Active && p.IsLoggedIn))
                {
                    try
                    {
                        var data = XiuXianData.GetPlayer(player.Name);
                        UpdateTopUI(player, data);
                    }
                    catch (Exception ex)
                    {
                        TShock.Log.Error($"刷新 {player.Name} 的顶部UI失败: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                TShock.Log.Error($"刷新顶部UI失败: {ex.Message}");
            }
        }
        #endregion

        #region 指令实现
        private void SetStarSignIcon(CommandArgs args)
        {
            if (args.Parameters.Count < 2)
            {
                args.Player.SendInfoMessage("用法: /设置星宿图标 <星宿名称> <物品ID>");
                args.Player.SendInfoMessage("示例: /设置星宿图标 紫微帝星 5005");
                args.Player.SendInfoMessage("提示: 物品ID可以在Terraria Wiki查询");
                return;
            }

            string signName = args.Parameters[0];
            if (!int.TryParse(args.Parameters[1], out int itemId))
            {
                args.Player.SendErrorMessage("无效的物品ID，必须为整数");
                return;
            }

            var sign = XiuXianConfig.Instance.StarSigns.FirstOrDefault(s => s.Name.Equals(signName, StringComparison.OrdinalIgnoreCase));
            if (sign == null)
            {
                args.Player.SendErrorMessage($"找不到星宿: {signName}");
                return;
            }

            sign.Icon = itemId;
            XiuXianConfig.Save(ConfigPath);

            args.Player.SendSuccessMessage($"已将星宿 {sign.Name} 的图标设置为物品ID: {itemId}");

            // 更新所有在线玩家的UI
            foreach (var player in TShock.Players.Where(p => p != null && p.Active))
            {
                var data = XiuXianData.GetPlayer(player.Name);
                UpdateTopUI(player, data);
            }
        }

        private void ShowStatus(CommandArgs args)
        {
            try
            {
                var data = XiuXianData.GetPlayer(args.Player.Name);
                ShowFullStatus(args.Player, data);
            }
            catch (Exception ex)
            {
                TShock.Log.Error($"显示状态失败: {ex.Message}");
            }
        }

        private void SetServerName(CommandArgs args)
        {
            if (args.Parameters.Count == 0)
            {
                args.Player.SendInfoMessage($"当前服务器名称: {XiuXianConfig.Instance.ServerName}");
                args.Player.SendInfoMessage("用法: /设置服务器名称 <新名称>");
                return;
            }

            string newName = string.Join(" ", args.Parameters);
            if (newName.Length > 30)
            {
                args.Player.SendErrorMessage("服务器名称过长，最多30个字符");
                return;
            }

            XiuXianConfig.Instance.ServerName = newName;
            XiuXianConfig.Save(ConfigPath);

            args.Player.SendSuccessMessage($"服务器名称已更新为: {newName}");
            TSPlayer.All.SendMessage($"★★★ 服务器更名为: {newName} ★★★", Microsoft.Xna.Framework.Color.Gold);
        }

        // 设置聊天UI偏移
        private void SetChatUIOffset(CommandArgs args)
        {
            if (args.Parameters.Count < 2)
            {
                args.Player.SendInfoMessage("用法: /设置聊天ui偏移 <X偏移> <Y偏移>");
                args.Player.SendInfoMessage($"当前偏移: X={XiuXianConfig.Instance.ChatUIOffsetX}, Y={XiuXianConfig.Instance.ChatUIOffsetY}");
                args.Player.SendInfoMessage("提示：X偏移控制水平位置，Y偏移控制垂直位置");
                return;
            }

            if (!int.TryParse(args.Parameters[0], out int x) || !int.TryParse(args.Parameters[1], out int y))
            {
                args.Player.SendErrorMessage("无效的偏移值，必须为整数");
                return;
            }

            if (x < -100 || y < -100 || x > 100 || y > 100)
            {
                args.Player.SendErrorMessage("偏移值范围: X(-100-100), Y(-100-100)");
                return;
            }

            XiuXianConfig.Instance.ChatUIOffsetX = x;
            XiuXianConfig.Instance.ChatUIOffsetY = y;
            XiuXianConfig.Save(ConfigPath);

            args.Player.SendSuccessMessage($"聊天UI偏移已更新: X={x}, Y={y}");

            // 更新所有在线玩家的UI
            foreach (var player in TShock.Players.Where(p => p != null && p.Active))
            {
                var data = XiuXianData.GetPlayer(player.Name);
                UpdateChatUI(player, data);
            }
        }

        // 设置顶部UI偏移
        private void SetTopUIOffset(CommandArgs args)
        {
            if (args.Parameters.Count < 2)
            {
                args.Player.SendInfoMessage("用法: /设置顶部ui偏移 <X偏移> <Y偏移>");
                args.Player.SendInfoMessage($"当前偏移: X={XiuXianConfig.Instance.TopUIOffsetX}, Y={XiuXianConfig.Instance.TopUIOffsetY}");
                args.Player.SendInfoMessage("提示：X偏移控制水平位置（负数向左，正数向右）");
                args.Player.SendInfoMessage("      Y偏移控制垂直位置（负数向上，正数向下）");
                return;
            }

            if (!int.TryParse(args.Parameters[0], out int x) || !int.TryParse(args.Parameters[1], out int y))
            {
                args.Player.SendErrorMessage("无效的偏移值，必须为整数");
                return;
            }

            if (x < -1000 || y < -1000 || x > 100 || y > 100)
            {
                args.Player.SendErrorMessage("偏移值范围: X(-1000-100), Y(-1000-100)");
                return;
            }

            XiuXianConfig.Instance.TopUIOffsetX = x;
            XiuXianConfig.Instance.TopUIOffsetY = y;
            XiuXianConfig.Save(ConfigPath);

            args.Player.SendSuccessMessage($"顶部UI偏移已更新: X={x}, Y={y}");

            // 更新所有在线玩家的UI
            foreach (var player in TShock.Players.Where(p => p != null && p.Active))
            {
                var data = XiuXianData.GetPlayer(player.Name);
                UpdateTopUI(player, data);
            }
        }

        // 设置顶部UI刷新间隔
        private void SetTopUIRefreshInterval(CommandArgs args)
        {
            if (args.Parameters.Count == 0)
            {
                args.Player.SendInfoMessage($"当前顶部UI刷新间隔: {XiuXianConfig.Instance.TopUIRefreshInterval}毫秒");
                args.Player.SendInfoMessage("用法: /设置顶部ui刷新间隔 <毫秒数>");
                args.Player.SendInfoMessage("注意: 1000毫秒 = 1秒");
                return;
            }

            if (!int.TryParse(args.Parameters[0], out int interval) || interval < 1000 || interval > 60000)
            {
                args.Player.SendErrorMessage("无效的间隔值，必须为1000-60000之间的整数");
                return;
            }

            XiuXianConfig.Instance.TopUIRefreshInterval = interval;
            XiuXianConfig.Save(ConfigPath);

            // 更新定时器间隔
            _topUiRefreshTimer.Interval = interval;

            args.Player.SendSuccessMessage($"顶部UI刷新间隔已更新为: {interval}毫秒");
        }

        private void AdminForceRebirth(CommandArgs args)
        {
            if (args.Parameters.Count == 0)
            {
                args.Player.SendInfoMessage("用法: /寿元转生 <玩家名>");
                return;
            }

            string playerName = string.Join(" ", args.Parameters);
            var targetData = XiuXianData.GetPlayer(playerName);
            if (targetData == null)
            {
                args.Player.SendErrorMessage($"找不到玩家: {playerName}");
                return;
            }

            PerformRebirth(targetData, args.Player);
            targetData.LifeDepletionWarned = false;
            targetData.LifeDepletionTime = DateTime.MinValue;

            args.Player.SendSuccessMessage($"已为 {playerName} 转生");

            var onlinePlayer = TSPlayer.FindByNameOrID(playerName);
            if (onlinePlayer.Count > 0)
            {
                onlinePlayer[0].SendSuccessMessage($"管理员已为你转生！当前寿元: {targetData.LifeYears:F1}年");
                UpdateChatUI(onlinePlayer[0], targetData);
                UpdateTopUI(onlinePlayer[0], targetData);
            }
        }

        private void AdminAdjustLife(CommandArgs args)
        {
            if (args.Parameters.Count < 2)
            {
                args.Player.SendErrorMessage("格式: /调整寿元 <玩家名> <数值>");
                return;
            }

            if (!int.TryParse(args.Parameters[1], out int amount))
            {
                args.Player.SendErrorMessage("无效的数值");
                return;
            }

            string playerName = args.Parameters[0];
            var targetData = XiuXianData.GetPlayer(playerName);
            if (targetData == null)
            {
                args.Player.SendErrorMessage($"找不到玩家: {playerName}");
                return;
            }

            targetData.LifeYears += amount;

            if (targetData.LifeYears > 1)
            {
                targetData.LifeDepletionWarned = false;
                targetData.LifeDepletionTime = DateTime.MinValue;
            }

            args.Player.SendSuccessMessage($"已将 {playerName} 寿元调整为: {targetData.LifeYears:F1}年");

            var onlinePlayer = TSPlayer.FindByNameOrID(playerName);
            if (onlinePlayer.Count > 0)
            {
                onlinePlayer[0].SendInfoMessage($"管理员已将你的寿元调整为: {targetData.LifeYears:F1}年");
                UpdateChatUI(onlinePlayer[0], targetData);
                UpdateTopUI(onlinePlayer[0], targetData);
            }
        }

        private void SetDharmaName(CommandArgs args)
        {
            if (args.Parameters.Count == 0)
            {
                args.Player.SendInfoMessage("用法: /法号 <自定义法号>");
                args.Player.SendInfoMessage($"当前法号: {(string.IsNullOrEmpty(args.Player.Name) ? "未设置" : args.Player.Name)}");
                return;
            }

            string newName = string.Join(" ", args.Parameters);
            if (newName.Length > 15)
            {
                args.Player.SendErrorMessage("法号过长，最多15个字符");
                return;
            }

            var data = XiuXianData.GetPlayer(args.Player.Name);
            data.DharmaName = newName;
            data.NameColor = GetRandomColorHex();

            args.Player.SendSuccessMessage($"法号已更新为: {newName}");
            UpdateChatUI(args.Player, data);
            UpdateTopUI(args.Player, data);
        }

        private string GetRandomColorHex()
        {
            var colors = new[] { "FF69B4", "00BFFF", "7CFC00", "FFD700", "9370DB", "FF6347", "20B2AA" };
            return colors[new Random().Next(colors.Length)];
        }

        private void ChooseStarSign(CommandArgs args)
        {
            var data = XiuXianData.GetPlayer(args.Player.Name);

            if (data.StarSign != "未选择")
            {
                args.Player.SendErrorMessage("星宿已选定，无法更改！");
                return;
            }

            if (args.Parameters.Count == 0)
            {
                args.Player.SendMessage("════════════ 星宿流派 ════════════", Microsoft.Xna.Framework.Color.Yellow);

                foreach (var sign in XiuXianConfig.Instance.StarSigns)
                {
                    args.Player.SendMessage($"{sign.Name} ({sign.Type}): {sign.ShortDesc}", sign.Color);
                }

                args.Player.SendMessage("══════════════════════════════════", Microsoft.Xna.Framework.Color.Yellow);
                args.Player.SendInfoMessage("用法: /选择星宿 <星宿名称>");
                return;
            }

            string signName = string.Join(" ", args.Parameters);
            var selectedSign = XiuXianConfig.Instance.StarSigns.FirstOrDefault(s => s.Name.Equals(signName, StringComparison.OrdinalIgnoreCase));

            if (selectedSign == null)
            {
                args.Player.SendErrorMessage("无效的星宿名称，请重新选择");
                return;
            }

            data.StarSign = selectedSign.Name;
            args.Player.SendMessage($"★★★ 星宿觉醒: {selectedSign.Name} ★★★", selectedSign.Color);
            args.Player.SendMessage($"流派: {selectedSign.Type}", Microsoft.Xna.Framework.Color.LightGreen);
            args.Player.SendMessage($"特性: {selectedSign.Description}", Microsoft.Xna.Framework.Color.Yellow);
            args.Player.SendSuccessMessage("你的修仙之路正式开始！输入 /状态 查看完整信息");

            UpdateChatUI(args.Player, data);
            UpdateTopUI(args.Player, data);
        }

        private void ViewConstellation(CommandArgs args)
        {
            var data = XiuXianData.GetPlayer(args.Player.Name);
            args.Player.SendInfoMessage($"命座等级: {data.ConstellationLevel}/7");
            args.Player.SendMessage("命座效果:", Microsoft.Xna.Framework.Color.Cyan);

            for (int i = 0; i < data.ConstellationLevel; i++)
            {
                string effect = XiuXianConfig.Instance.Constellations[i];
                args.Player.SendMessage($"★ {effect}", Microsoft.Xna.Framework.Color.LightBlue);
            }
        }

        private void PerformRebirth(XiuXianData data, TSPlayer executor)
        {
            data.RebirthCount++;
            data.LifeYears = 60 + (data.RebirthCount * 20);
            data.CultivationProgress = 0;
            data.Realm = "凡人";
            data.ConstellationLevel = 0;
            data.KilledBosses.Clear();
            XiuXianData.SavePlayer(data.Name);
            TShock.Log.Info($"管理员 {executor.Name} 为 {data.Name} 执行转生");
        }

        private void Cultivate(object sender, ElapsedEventArgs e)
        {
            foreach (var player in TShock.Players.Where(p => p != null && p.Active && p.IsLoggedIn))
            {
                try
                {
                    var data = XiuXianData.GetPlayer(player.Name);
                    var realmInfo = XiuXianConfig.Instance.GetRealmInfo(data.Realm);

                    if (data.StarSign == "未选择") continue;

                    var starSign = XiuXianConfig.Instance.StarSigns.FirstOrDefault(s => s.Name == data.StarSign);
                    if (starSign == null) continue;

                    float multiplier = data.InHolyLand ? 2.0f : 1.0f;
                    float signBonus = starSign.CultivationBonus;
                    float constellationBonus = 1.0f + (data.ConstellationLevel * 0.05f);

                    data.CultivationProgress += (int)(0.5 * multiplier * constellationBonus * (1 + signBonus));

                    // 修炼消耗寿元（修改后逻辑）
                    float lifeReduction = 0.001f;
                    data.LifeYears = Math.Max(1, data.LifeYears - lifeReduction);

                    // 当减少到1年时立即踢出
                    if (data.LifeYears <= 1 && !player.Group.HasPermission("shouyuan.admin"))
                    {
                        player.Kick("修炼过度导致寿元耗尽", true);
                        TShock.Log.Info($"玩家 {player.Name} 因修炼导致寿元耗尽被踢出");
                        continue;
                    }

                    if (data.CultivationProgress > 1000 && data.ConstellationLevel < 7)
                    {
                        data.ConstellationLevel++;
                        data.CultivationProgress = 0;
                        player.SendMessage($"★★★ 命座突破: {data.ConstellationLevel}/7 ★★★", Microsoft.Xna.Framework.Color.Cyan);
                        player.SendInfoMessage($"获得命座效果: {XiuXianConfig.Instance.Constellations[data.ConstellationLevel - 1]}");
                    }

                    if (DateTime.Now.Minute == 0 && DateTime.Now.Second < 30)
                    {
                        player.SendInfoMessage("灵潮涌动！修炼速度大幅提升");
                        data.CultivationProgress += 30;
                    }

                    UpdateChatUI(player, data);
                    UpdateTopUI(player, data);
                }
                catch (Exception ex)
                {
                    TShock.Log.Error($"{player.Name}修炼失败: {ex.Message}");
                }
            }
        }

        private void CultivationCommand(CommandArgs args)
        {
            if (args.Parameters.Count == 0)
            {
                args.Player.SendInfoMessage("修仙指令: /修仙 [状态|修炼|突破|转生|星宿]");
                return;
            }

            string subcmd = args.Parameters[0].ToLower();
            var data = XiuXianData.GetPlayer(args.Player.Name);

            if (data.StarSign == "未选择" && subcmd != "选择星宿")
            {
                args.Player.SendErrorMessage("请先使用 /选择星宿 确定你的修仙流派");
                return;
            }

            var realmInfo = XiuXianConfig.Instance.GetRealmInfo(data.Realm);
            var starSign = XiuXianConfig.Instance.StarSigns.FirstOrDefault(s => s.Name == data.StarSign);

            switch (subcmd)
            {
                case "状态":
                    ShowFullStatus(args.Player, data);
                    break;
                case "修炼":
                    ProcessMeditation(args.Player, data, starSign);
                    break;
                case "突破":
                    ProcessBreakthrough(args.Player, data, realmInfo, starSign);
                    break;
                case "转生":
                    ProcessRebirth(args.Player, data);
                    break;
                case "星宿":
                    ChooseStarSign(args);
                    break;
                default:
                    args.Player.SendErrorMessage("未知指令，可用: 状态, 修炼, 突破, 转生, 星宿");
                    break;
            }
        }

        private void ProcessMeditation(TSPlayer player, XiuXianData data, StarSignInfo starSign)
        {
            if (data.LifeYears < 0.1f)
            {
                player.SendErrorMessage("寿元不足无法修炼！");
                return;
            }

            data.LifeYears -= 0.1f;
            int baseGain = new Random().Next(5, 15);
            int gain = (int)(baseGain * (1 + starSign.CultivationBonus));
            gain = (int)(gain * (1.0f + data.ConstellationLevel * 0.05f));
            float lifeGain = gain * 0.1f;
            data.CultivationProgress += gain;
            data.LifeYears += lifeGain;

            player.SendSuccessMessage($"修炼成功！修为+{gain}%，寿元+{lifeGain:F1}年");
            player.SendMessage($"星宿「{starSign.Name}」加成: +{(starSign.CultivationBonus * 100):F0}%", starSign.Color);

            // 当减少到1年时立即踢出
            if (data.LifeYears <= 1 && !player.Group.HasPermission("shouyuan.admin"))
            {
                player.Kick("修炼过度导致寿元耗尽", true);
                TShock.Log.Info($"玩家 {player.Name} 因修炼导致寿元耗尽被踢出");
                return;
            }

            UpdateChatUI(player, data);
            UpdateTopUI(player, data);
        }

        private void ProcessBreakthrough(TSPlayer player, XiuXianData data, RealmInfo realm, StarSignInfo starSign)
        {
            var next = XiuXianConfig.Instance.GetNextRealm(realm);
            if (next == null)
            {
                player.SendErrorMessage("已至大道巅峰，无法突破");
                return;
            }

            // 检查突破条件是否满足
            var unmetConditions = GetUnmetBreakthroughConditions(next, data);
            if (unmetConditions.Count > 0)
            {
                player.SendErrorMessage($"突破条件未满足！请完成以下要求:");
                foreach (var condition in unmetConditions)
                {
                    player.SendErrorMessage($"- 击败 {condition}");
                }
                return;
            }

            if (data.CultivationProgress < next.BreakthroughReq)
            {
                player.SendErrorMessage($"突破{next.Name}需要{next.BreakthroughReq}%修为");
                return;
            }

            double adjustedRate = next.SuccessRate;
            if (starSign.Type == "防御" || starSign.Type == "辅助")
                adjustedRate *= 1.1;

            if (new Random().NextDouble() < adjustedRate)
            {
                data.Realm = next.Name;
                data.CultivationProgress = 0;
                data.LifeYears += next.LifeBonus;

                player.SendMessage($"★★★★ {realm.Name}→{next.Name} ★★★★", Microsoft.Xna.Framework.Color.Gold);
                player.SendMessage($"星宿「{starSign.Name}」护佑突破！", starSign.Color);
                player.SendSuccessMessage($"寿元增加{next.LifeBonus}年! 当前: {data.LifeYears:F1}年");

                // 发放突破奖励
                if (next.RewardGoods != null && next.RewardGoods.Length > 0)
                {
                    player.SendSuccessMessage($"获得{next.RewardGoods.Length}种突破奖励!");
                    foreach (var item in next.RewardGoods)
                    {
                        player.GiveItem(item.NetID, item.Stack, item.Prefix);
                        player.SendSuccessMessage($"获得突破奖励: {TShock.Utils.GetItemById(item.NetID).Name} x{item.Stack}");
                    }
                }
                else
                {
                    player.SendInfoMessage("本次突破未获得物品奖励");
                }

                // 给予Buff奖励
                if (next.RewardBuffs != null && next.RewardBuffs.Length > 0)
                {
                    foreach (var buff in next.RewardBuffs)
                    {
                        player.SetBuff(buff.BuffID, buff.Duration * 60); // 转换为游戏刻（1秒=60刻）
                        player.SendSuccessMessage($"获得境界Buff: {TShock.Utils.GetBuffName(buff.BuffID)} ({buff.Duration}秒)");
                    }
                }

                if (XiuXianConfig.Instance.BroadcastBreakthrough)
                    TSPlayer.All.SendMessage($"{player.Name}突破{next.Name}境，天地异象！", Microsoft.Xna.Framework.Color.Yellow);
            }
            else
            {
                int damage = new Random().Next(10, 30);
                data.LifeYears = Math.Max(1, data.LifeYears - damage);
                player.SendErrorMessage($"★★ 天劫降临！损失{damage}年寿元 ★★");
                player.SendMessage($"星宿「{starSign.Name}」为你抵挡部分天劫", starSign.Color);

                // 当减少到1年时立即踢出
                if (data.LifeYears <= 1 && !player.Group.HasPermission("shouyuan.admin"))
                {
                    player.Kick("天劫导致寿元耗尽", true);
                    TShock.Log.Info($"玩家 {player.Name} 因突破失败导致寿元耗尽被踢出");
                    return;
                }
            }

            UpdateChatUI(player, data);
            UpdateTopUI(player, data);
        }

        // 检查突破条件是否满足，返回未满足条件的Boss列表
        private List<string> GetUnmetBreakthroughConditions(RealmInfo nextRealm, XiuXianData data)
        {
            var unmet = new List<string>();
            if (nextRealm.ProgressLimits == null || nextRealm.ProgressLimits.Count == 0)
                return unmet;

            foreach (var bossName in nextRealm.ProgressLimits)
            {
                // 修复：使用标准化的Boss名称进行比较
                string normalizedBossName = NormalizeBossName(bossName);
                bool hasKilled = false;
                
                foreach (var killedBoss in data.KilledBosses)
                {
                    if (NormalizeBossName(killedBoss) == normalizedBossName)
                    {
                        hasKilled = true;
                        break;
                    }
                }
                
                if (!hasKilled)
                {
                    unmet.Add(bossName);
                }
            }

            return unmet;
        }

        private void ProcessRebirth(TSPlayer player, XiuXianData data)
        {
            if (data.Realm != "凡人")
            {
                player.SendErrorMessage("需散尽修为才可转世重修！");
                return;
            }
            if (data.LifeYears < XiuXianConfig.Instance.RebirthCost)
            {
                player.SendErrorMessage($"转生需要{XiuXianConfig.Instance.RebirthCost}年寿元！");
                return;
            }
            data.RebirthCount++;
            data.LifeYears = 60 + (data.RebirthCount * 20);
            data.ConstellationLevel = 0;
            data.KilledBosses.Clear();
            player.SendSuccessMessage($"★ 转世成功！获得新生寿元: {data.LifeYears:F1}年 ★");

            data.LifeDepletionWarned = false;
            data.LifeDepletionTime = DateTime.MinValue;

            UpdateChatUI(player, data);
            UpdateTopUI(player, data);
        }

        // 添加境界条件命令
        private void AddRealmCondition(CommandArgs args)
        {
            if (args.Parameters.Count < 2)
            {
                args.Player.SendInfoMessage("用法: /添加境界条件 <境界名称> <Boss名称>");
                args.Player.SendInfoMessage("示例: /添加境界条件 筑基 \"克苏鲁之眼\"");
                return;
            }

            string realmName = args.Parameters[0];
            string bossName = string.Join(" ", args.Parameters.Skip(1));

            var realm = XiuXianConfig.Instance.Realms.FirstOrDefault(r => r.Name.Equals(realmName, StringComparison.OrdinalIgnoreCase));
            if (realm == null)
            {
                args.Player.SendErrorMessage($"找不到境界: {realmName}");
                return;
            }

            // 初始化ProgressLimits
            if (realm.ProgressLimits == null)
                realm.ProgressLimits = new List<string>();

            realm.ProgressLimits.Add(bossName);
            XiuXianConfig.Save(ConfigPath);

            args.Player.SendSuccessMessage($"已为境界 {realm.Name} 添加进度条件: 击败 {bossName}");
        }

        // 添加境界奖励命令
        private void AddRealmReward(CommandArgs args)
        {
            if (args.Parameters.Count < 4)
            {
                args.Player.SendInfoMessage("用法: /添加境界奖励 <境界名称> <物品ID> <数量> [前缀]");
                args.Player.SendInfoMessage("示例: /添加境界奖励 筑基 123 5 0");
                args.Player.SendInfoMessage("示例: /添加境界奖励 金丹 456 1 10");
                return;
            }

            string realmName = args.Parameters[0];
            if (!int.TryParse(args.Parameters[1], out int itemId) ||
                !int.TryParse(args.Parameters[2], out int stack) ||
                !byte.TryParse(args.Parameters[3], out byte prefix))
            {
                args.Player.SendErrorMessage("无效的参数格式");
                return;
            }

            var realm = XiuXianConfig.Instance.Realms.FirstOrDefault(r => r.Name.Equals(realmName, StringComparison.OrdinalIgnoreCase));
            if (realm == null)
            {
                args.Player.SendErrorMessage($"找不到境界: {realmName}");
                return;
            }

            var newItem = new Item
            {
                NetID = itemId,
                Stack = stack,
                Prefix = prefix
            };

            // 创建新数组并添加奖励
            var newRewards = realm.RewardGoods.ToList();
            newRewards.Add(newItem);
            realm.RewardGoods = newRewards.ToArray();

            XiuXianConfig.Save(ConfigPath);

            var itemName = TShock.Utils.GetItemById(itemId)?.Name ?? $"物品ID:{itemId}";
            args.Player.SendSuccessMessage($"已为境界 {realm.Name} 添加奖励: {itemName} x{stack} (前缀:{prefix})");
        }

        // 添加境界Buff奖励命令
        private void AddRealmBuff(CommandArgs args)
        {
            if (args.Parameters.Count < 3)
            {
                args.Player.SendInfoMessage("用法: /添加境界buff <境界名称> <BuffID> <持续时间(秒)>");
                args.Player.SendInfoMessage("示例: /添加境界buff 筑基 1 300  (给予300秒的再生Buff)");
                args.Player.SendInfoMessage("提示: BuffID可以在Terraria Wiki查询");
                return;
            }

            string realmName = args.Parameters[0];
            if (!int.TryParse(args.Parameters[1], out int buffId) ||
                !int.TryParse(args.Parameters[2], out int duration))
            {
                args.Player.SendErrorMessage("无效的参数格式");
                return;
            }

            var realm = XiuXianConfig.Instance.Realms.FirstOrDefault(r => r.Name.Equals(realmName, StringComparison.OrdinalIgnoreCase));
            if (realm == null)
            {
                args.Player.SendErrorMessage($"找不到境界: {realmName}");
                return;
            }

            // 验证BuffID是否有效
            if (buffId < 1 || buffId > Terraria.ID.BuffID.Count)
            {
                args.Player.SendErrorMessage($"无效的BuffID，范围应为1-{Terraria.ID.BuffID.Count}");
                return;
            }

            var newBuff = new RealmBuff
            {
                BuffID = buffId,
                Duration = duration
            };

            // 创建新数组并添加Buff奖励
            var newBuffs = realm.RewardBuffs.ToList();
            newBuffs.Add(newBuff);
            realm.RewardBuffs = newBuffs.ToArray();

            XiuXianConfig.Save(ConfigPath);

            var buffName = TShock.Utils.GetBuffName(buffId);
            args.Player.SendSuccessMessage($"已为境界 {realm.Name} 添加Buff奖励: {buffName} ({duration}秒)");
        }

        // 新增：重读修仙配置文件指令
        private void ReloadXiuXianConfig(CommandArgs args)
        {
            try
            {
                XiuXianConfig.Load(ConfigPath);
                args.Player.SendSuccessMessage("修仙配置文件已重新加载！");

                // 更新顶部UI刷新间隔
                _topUiRefreshTimer.Interval = XiuXianConfig.Instance.TopUIRefreshInterval;

                // 通知所有在线玩家
                TSPlayer.All.SendInfoMessage($"管理员 {args.Player.Name} 已重读修仙配置文件");
                TSPlayer.All.SendInfoMessage($"当前服务器名称: {XiuXianConfig.Instance.ServerName}");

                // 更新所有在线玩家的UI
                foreach (var player in TShock.Players.Where(p => p != null && p.Active))
                {
                    var data = XiuXianData.GetPlayer(player.Name);
                    UpdateChatUI(player, data);
                    UpdateTopUI(player, data);
                }
            }
            catch (Exception ex)
            {
                args.Player.SendErrorMessage($"重读修仙配置文件失败: {ex.Message}");
                TShock.Log.Error($"重读修仙配置文件失败: {ex.Message}");
            }
        }
        
        // 新增：散尽修为命令
        private void ResetCultivation(CommandArgs args)
        {
            var data = XiuXianData.GetPlayer(args.Player.Name);
            
            if (data.Realm == "凡人")
            {
                args.Player.SendErrorMessage("你已经是凡人境界，无需散尽修为！");
                return;
            }
            
            // 确认提示
            if (args.Parameters.Count == 0 || args.Parameters[0].ToLower() != "确认")
            {
                args.Player.SendWarningMessage($"警告：散尽修为将重置你的境界到凡人，所有修炼进度清零！");
                args.Player.SendWarningMessage($"当前境界: {data.Realm}境，修炼进度: {data.CultivationProgress}%");
                args.Player.SendInfoMessage($"如果你确定要散尽修为，请输入: /散尽修为 确认");
                return;
            }
            
            // 执行散尽修为
            data.Realm = "凡人";
            data.CultivationProgress = 0;
            data.ConstellationLevel = 0;
            
            // 保留寿元和转生次数
            args.Player.SendSuccessMessage("★★★ 已散尽修为，重返凡人！★★★");
            args.Player.SendInfoMessage($"当前境界: {data.Realm}境，寿元: {data.LifeYears:F1}年");
            
            UpdateChatUI(args.Player, data);
            UpdateTopUI(args.Player, data);
        }
        
        // 新增：仙道重开命令（管理员）
        private void ResetAllPlayersCultivation(CommandArgs args)
        {
            // 确认提示
            if (args.Parameters.Count == 0 || args.Parameters[0].ToLower() != "确认")
            {
                args.Player.SendWarningMessage($"警告：仙道重开将重置所有玩家的境界到凡人！");
                args.Player.SendWarningMessage($"这将影响 {XiuXianData.Players.Count} 名玩家");
                args.Player.SendInfoMessage($"如果你确定要执行仙道重开，请输入: /仙道重开 确认");
                return;
            }
            
            int resetCount = 0;
            foreach (var playerData in XiuXianData.Players.Values)
            {
                if (playerData.Realm != "凡人")
                {
                    playerData.Realm = "凡人";
                    playerData.CultivationProgress = 0;
                    playerData.ConstellationLevel = 0;
                    resetCount++;
                }
            }
            
            XiuXianData.Save(DataPath);
            
            args.Player.SendSuccessMessage($"★★★ 仙道重开完成！共重置 {resetCount} 名玩家的境界 ★★★");
            
            // 通知所有在线玩家
            TSPlayer.All.SendInfoMessage($"管理员 {args.Player.Name} 已执行仙道重开，所有玩家境界重置为凡人");
            
            // 更新所有在线玩家的UI
            foreach (var player in TShock.Players.Where(p => p != null && p.Active))
            {
                var data = XiuXianData.GetPlayer(player.Name);
                UpdateChatUI(player, data);
                UpdateTopUI(player, data);
            }
        }
        #endregion

        #region 配置与数据
        public class Item
        {
            [JsonProperty("物品ID")]
            public int NetID { get; set; }

            [JsonProperty("数量")]
            public int Stack { get; set; }

            [JsonProperty("前缀")]
            public byte Prefix { get; set; } = 0;
        }

        // 新增：境界Buff类
        public class RealmBuff
        {
            [JsonProperty("BuffID")]
            public int BuffID { get; set; }

            [JsonProperty("持续时间")]
            public int Duration { get; set; } // 单位：秒
        }

        public class RealmInfo
        {
            public string Name { get; set; } = "凡人";
            public int Level { get; set; } = 0;
            public int LifeBonus { get; set; } = 0;
            public int BreakthroughReq { get; set; } = 0;
            public double SuccessRate { get; set; } = 1.0;

            // 进度限制（需要击败的Boss列表）
            [JsonProperty("进度限制")]
            public List<string> ProgressLimits { get; set; } = new List<string>();

            // 突破奖励
            [JsonProperty("突破奖励")]
            public Item[] RewardGoods { get; set; } = Array.Empty<Item>();

            // 新增：突破Buff奖励
            [JsonProperty("突破Buff奖励")]
            public RealmBuff[] RewardBuffs { get; set; } = Array.Empty<RealmBuff>();
        }

        public class StarSignInfo
        {
            public string Name { get; set; }
            public string Type { get; set; }
            public string Description { get; set; }
            public string ShortDesc { get; set; }
            public float CultivationBonus { get; set; } = 0f;
            public float LifeBonus { get; set; } = 0f;
            public float DamageBonus { get; set; } = 0f;
            public float DefenseBonus { get; set; } = 0f;
            public Microsoft.Xna.Framework.Color Color { get; set; }

            // 新增图标字段
            [JsonProperty("图标物品ID", DefaultValueHandling = DefaultValueHandling.Populate)]
            public int Icon { get; set; } = 0;

            public string Hex3()
            {
                return $"{Color.R:X2}{Color.G:X2}{Color.B:X2}";
            }
        }

        public class XiuXianConfig
        {
            public static XiuXianConfig Instance;

            public string ServerName { get; set; } = "泰拉修仙传";
            public int RebirthCost { get; set; } = 200;
            public bool BroadcastBreakthrough { get; set; } = true;
            public List<RealmInfo> Realms { get; set; } = new List<RealmInfo>();
            public List<StarSignInfo> StarSigns { get; set; } = new List<StarSignInfo>();
            public List<string> Constellations { get; set; } = new List<string>();

            // 聊天UI偏移配置
            public int ChatUIOffsetX { get; set; } = 2;
            public int ChatUIOffsetY { get; set; } = 1;

            // 顶部UI偏移配置
            public int TopUIOffsetX { get; set; } = -13;
            public int TopUIOffsetY { get; set; } = 10;

            // 顶部UI刷新间隔（毫秒）
            [JsonProperty("顶部UI刷新间隔")]
            public int TopUIRefreshInterval { get; set; } = 10000; // 默认10秒

            public XiuXianConfig()
            {
                // 完美世界境界体系
                Realms.Add(new RealmInfo
                {
                    Name = "凡人",
                    Level = 0,
                    LifeBonus = 10,
                    BreakthroughReq = 50,
                    SuccessRate = 0.5,
                    RewardBuffs = new RealmBuff[] { new RealmBuff { BuffID = 1, Duration = 30 } } // 再生Buff
                });
                Realms.Add(new RealmInfo
                {
                    Name = "搬血",
                    Level = 1,
                    LifeBonus = 50,
                    BreakthroughReq = 100,
                    SuccessRate = 0.95,
                    RewardBuffs = new RealmBuff[] { new RealmBuff { BuffID = 1, Duration = 300 } } // 再生Buff
                });
                Realms.Add(new RealmInfo
                {
                    Name = "洞天",
                    Level = 2,
                    LifeBonus = 100,
                    BreakthroughReq = 150,
                    SuccessRate = 0.9,
                    RewardBuffs = new RealmBuff[] { new RealmBuff { BuffID = 2, Duration = 300 } } // 迅捷Buff
                });
                Realms.Add(new RealmInfo
                {
                    Name = "化灵",
                    Level = 3,
                    LifeBonus = 200,
                    BreakthroughReq = 200,
                    SuccessRate = 0.85,
                    RewardBuffs = new RealmBuff[] { new RealmBuff { BuffID = 3, Duration = 300 } } // 铁皮Buff
                });
                Realms.Add(new RealmInfo
                {
                    Name = "铭纹",
                    Level = 4,
                    LifeBonus = 400,
                    BreakthroughReq = 300,
                    SuccessRate = 0.8,
                    RewardBuffs = new RealmBuff[] { new RealmBuff { BuffID = 4, Duration = 300 } } // 伤害Buff
                });
                Realms.Add(new RealmInfo
                {
                    Name = "列阵",
                    Level = 5,
                    LifeBonus = 800,
                    BreakthroughReq = 400,
                    SuccessRate = 0.75,
                    ProgressLimits = new List<string> { "克苏鲁之眼" },
                    RewardBuffs = new RealmBuff[] { new RealmBuff { BuffID = 5, Duration = 300 } } // 恢复Buff
                });
                Realms.Add(new RealmInfo
                {
                    Name = "尊者",
                    Level = 6,
                    LifeBonus = 1600,
                    BreakthroughReq = 500,
                    SuccessRate = 0.7,
                    ProgressLimits = new List<string> { "世界吞噬者", "克苏鲁之脑" },
                    RewardBuffs = new RealmBuff[] { new RealmBuff { BuffID = 6, Duration = 300 } } // 羽落Buff
                });
                Realms.Add(new RealmInfo
                {
                    Name = "神火",
                    Level = 7,
                    LifeBonus = 3200,
                    BreakthroughReq = 600,
                    SuccessRate = 0.65,
                    RewardBuffs = new RealmBuff[] { new RealmBuff { BuffID = 7, Duration = 300 } } // 挖矿Buff
                });
                Realms.Add(new RealmInfo
                {
                    Name = "真一",
                    Level = 8,
                    LifeBonus = 6400,
                    BreakthroughReq = 700,
                    SuccessRate = 0.6,
                    RewardBuffs = new RealmBuff[] { new RealmBuff { BuffID = 8, Duration = 300 } } // 光芒Buff
                });
                Realms.Add(new RealmInfo
                {
                    Name = "圣祭",
                    Level = 9,
                    LifeBonus = 12800,
                    BreakthroughReq = 800,
                    SuccessRate = 0.55,
                    RewardBuffs = new RealmBuff[] { new RealmBuff { BuffID = 9, Duration = 300 } } // 水下呼吸Buff
                });
                Realms.Add(new RealmInfo
                {
                    Name = "天神",
                    Level = 10,
                    LifeBonus = 25600,
                    BreakthroughReq = 900,
                    SuccessRate = 0.5,
                    RewardBuffs = new RealmBuff[] { new RealmBuff { BuffID = 10, Duration = 300 } } // 荆棘Buff
                });
                Realms.Add(new RealmInfo
                {
                    Name = "虚道",
                    Level = 11,
                    LifeBonus = 51200,
                    BreakthroughReq = 1000,
                    SuccessRate = 0.45,
                    RewardBuffs = new RealmBuff[] { new RealmBuff { BuffID = 11, Duration = 300 } } // 隐身Buff
                });
                Realms.Add(new RealmInfo
                {
                    Name = "斩我",
                    Level = 12,
                    LifeBonus = 102400,
                    BreakthroughReq = 1100,
                    SuccessRate = 0.4,
                    RewardBuffs = new RealmBuff[] { new RealmBuff { BuffID = 12, Duration = 300 } } // 黑曜石皮肤Buff
                });
                Realms.Add(new RealmInfo
                {
                    Name = "遁一",
                    Level = 13,
                    LifeBonus = 204800,
                    BreakthroughReq = 1200,
                    SuccessRate = 0.35,
                    RewardBuffs = new RealmBuff[] { new RealmBuff { BuffID = 13, Duration = 300 } } // 再生法杖Buff
                });
                Realms.Add(new RealmInfo
                {
                    Name = "至尊",
                    Level = 14,
                    LifeBonus = 409600,
                    BreakthroughReq = 1300,
                    SuccessRate = 0.3,
                    RewardBuffs = new RealmBuff[] { new RealmBuff { BuffID = 14, Duration = 300 } } // 敏捷Buff
                });
                Realms.Add(new RealmInfo
                {
                    Name = "真仙",
                    Level = 15,
                    LifeBonus = 819200,
                    BreakthroughReq = 1400,
                    SuccessRate = 0.25,
                    RewardBuffs = new RealmBuff[] { new RealmBuff { BuffID = 15, Duration = 300 } } // 荆棘Buff
                });
                Realms.Add(new RealmInfo
                {
                    Name = "仙王",
                    Level = 16,
                    LifeBonus = 1638400,
                    BreakthroughReq = 1500,
                    SuccessRate = 0.2,
                    RewardBuffs = new RealmBuff[] { new RealmBuff { BuffID = 16, Duration = 300 } } // 光芒Buff
                });
                Realms.Add(new RealmInfo
                {
                    Name = "准仙帝",
                    Level = 17,
                    LifeBonus = 3276800,
                    BreakthroughReq = 1600,
                    SuccessRate = 0.15,
                    RewardBuffs = new RealmBuff[] { new RealmBuff { BuffID = 17, Duration = 300 } } // 镇静Buff
                });
                Realms.Add(new RealmInfo
                {
                    Name = "仙帝",
                    Level = 18,
                    LifeBonus = 6553600,
                    BreakthroughReq = 1700,
                    SuccessRate = 0.1,
                    RewardBuffs = new RealmBuff[] {
                        new RealmBuff { BuffID = 18, Duration = 300 }, // 建筑工Buff
                        new RealmBuff { BuffID = 19, Duration = 300 }, // 挖矿Buff
                        new RealmBuff { BuffID = 20, Duration = 300 }  // 心箭Buff
                    }
                });

                // 星宿流派系统 - 新增图标配置
                StarSigns.Add(new StarSignInfo
                {
                    Name = "紫微帝星",
                    Type = "全能",
                    Description = "帝王之相，统御四方，全面提升修炼效率",
                    ShortDesc = "全面均衡发展",
                    CultivationBonus = 0.15f,
                    LifeBonus = 0.1f,
                    DamageBonus = 0.1f,
                    DefenseBonus = 0.1f,
                    Color = new Microsoft.Xna.Framework.Color(255, 215, 0),
                    Icon = 5005  // 新增图标ID
                });

                StarSigns.Add(new StarSignInfo
                {
                    Name = "破军杀星",
                    Type = "攻击",
                    Description = "主杀伐征战，攻击力冠绝天下",
                    ShortDesc = "极致攻击路线",
                    CultivationBonus = 0.1f,
                    LifeBonus = 0.05f,
                    DamageBonus = 0.3f,
                    DefenseBonus = -0.1f,
                    Color = new Microsoft.Xna.Framework.Color(220, 20, 60),
                    Icon = 4956  // 新增图标ID
                });

                StarSigns.Add(new StarSignInfo
                {
                    Name = "天机玄星",
                    Type = "辅助",
                    Description = "洞悉天机，擅长辅助与阵法",
                    ShortDesc = "辅助与阵法专精",
                    CultivationBonus = 0.2f,
                    LifeBonus = 0.15f,
                    DamageBonus = -0.05f,
                    DefenseBonus = 0.1f,
                    Color = new Microsoft.Xna.Framework.Color(138, 43, 226),
                    Icon = 3459  // 新增图标ID
                });

                StarSigns.Add(new StarSignInfo
                {
                    Name = "武曲战星",
                    Type = "防御",
                    Description = "战神护体，铜墙铁壁般的防御",
                    ShortDesc = "极致防御路线",
                    CultivationBonus = 0.1f,
                    LifeBonus = 0.25f,
                    DamageBonus = 0.05f,
                    DefenseBonus = 0.3f,
                    Color = new Microsoft.Xna.Framework.Color(0, 191, 255),
                    Icon = 3456  // 新增图标ID
                });

                StarSigns.Add(new StarSignInfo
                {
                    Name = "七杀凶星",
                    Type = "攻击",
                    Description = "以杀证道，越战越勇",
                    ShortDesc = "狂暴攻击路线",
                    CultivationBonus = 0.05f,
                    LifeBonus = -0.1f,
                    DamageBonus = 0.4f,
                    DefenseBonus = -0.15f,
                    Color = new Microsoft.Xna.Framework.Color(178, 34, 34),
                    Icon = 3458  // 新增图标ID
                });

                StarSigns.Add(new StarSignInfo
                {
                    Name = "太阴玄星",
                    Type = "辅助",
                    Description = "月华之力，治疗与恢复",
                    ShortDesc = "治疗与恢复专精",
                    CultivationBonus = 0.15f,
                    LifeBonus = 0.3f,
                    DamageBonus = -0.1f,
                    DefenseBonus = 0.15f,
                    Color = new Microsoft.Xna.Framework.Color(173, 216, 230),
                    Icon = 3457  // 新增图标ID
                });

                // 命座系统
                Constellations = new List<string> {
                    "天枢：生命上限+20%，基础防御+15%",
                    "天璇：攻击力+25%，暴击伤害+30%",
                    "天玑：灵气吸收速度+40%，修炼效率+10%",
                    "天权：寿元消耗减少20%，突破成功率+10%",
                    "玉衡：全元素抗性+30%，异常状态抵抗+50%",
                    "开阳：移动速度+35%，闪避率+25%",
                    "摇光：全属性+30%，大道感悟速度翻倍"
                };
            }

            public RealmInfo GetRealmInfo(string name) => Realms.FirstOrDefault(r => r.Name == name) ?? Realms[0];
            public RealmInfo GetNextRealm(RealmInfo current) => Realms.FirstOrDefault(r => r.Level == current.Level + 1);

            public static void Load(string path)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        Instance = JsonConvert.DeserializeObject<XiuXianConfig>(File.ReadAllText(path));

                        // 兼容旧配置：初始化新增字段
                        if (Instance.ChatUIOffsetX == 0 && Instance.ChatUIOffsetY == 0)
                        {
                            Instance.ChatUIOffsetX = 2;
                            Instance.ChatUIOffsetY = 1;
                        }

                        if (Instance.TopUIOffsetX == 0 && Instance.TopUIOffsetY == 0)
                        {
                            Instance.TopUIOffsetX = -13;
                            Instance.TopUIOffsetY = 10;
                        }

                        // 确保刷新间隔在合理范围内
                        if (Instance.TopUIRefreshInterval < 1000 || Instance.TopUIRefreshInterval > 60000)
                        {
                            TShock.Log.Warn($"配置的顶部UI刷新间隔 {Instance.TopUIRefreshInterval} 不在合理范围(1000-60000)，重置为默认值10000");
                            Instance.TopUIRefreshInterval = 10000;
                        }

                        // 确保ProgressLimits字段不为null
                        foreach (var realm in Instance.Realms)
                        {
                            if (realm.ProgressLimits == null)
                                realm.ProgressLimits = new List<string>();

                            if (realm.RewardGoods == null)
                                realm.RewardGoods = Array.Empty<Item>();

                            if (realm.RewardBuffs == null)
                                realm.RewardBuffs = Array.Empty<RealmBuff>();
                        }

                        // 修复旧配置的星宿图标
                        foreach (var sign in Instance.StarSigns)
                        {
                            if (sign.Icon == 0) // 说明是旧配置，没有Icon字段
                            {
                                // 根据名称设置默认图标
                                sign.Icon = sign.Name switch
                                {
                                    "紫微帝星" => 5005,
                                    "破军杀星" => 4956,
                                    "天机玄星" => 3459,
                                    "武曲战星" => 3456,
                                    "七杀凶星" => 3458,
                                    "太阴玄星" => 3457,
                                    _ => 0
                                };
                            }
                        }

                        TShock.Log.Info($"修仙配置已加载，服务器名称: {Instance.ServerName}");
                        TShock.Log.Info($"聊天UI偏移: X={Instance.ChatUIOffsetX}, Y={Instance.ChatUIOffsetY}");
                        TShock.Log.Info($"顶部UI偏移: X={Instance.TopUIOffsetX}, Y={Instance.TopUIOffsetY}");
                        TShock.Log.Info($"顶部UI刷新间隔: {Instance.TopUIRefreshInterval}毫秒");
                    }
                    else
                    {
                        Instance = new XiuXianConfig();
                        File.WriteAllText(path, JsonConvert.SerializeObject(Instance, Formatting.Indented));
                    }
                }
                catch (Exception ex)
                {
                    TShock.Log.Error($"加载修仙配置失败: {ex.Message}");
                    Instance = new XiuXianConfig();
                }
            }

            public static void Save(string path)
            {
                try
                {
                    File.WriteAllText(path, JsonConvert.SerializeObject(Instance, Formatting.Indented));
                    TShock.Log.Info($"修仙配置已保存，服务器名称: {Instance.ServerName}");
                    TShock.Log.Info($"聊天UI偏移: X={Instance.ChatUIOffsetX}, Y={Instance.ChatUIOffsetY}");
                    TShock.Log.Info($"顶部UI偏移: X={Instance.TopUIOffsetX}, Y={Instance.TopUIOffsetY}");
                    TShock.Log.Info($"顶部UI刷新间隔: {Instance.TopUIRefreshInterval}毫秒");
                }
                catch (Exception ex)
                {
                    TShock.Log.Error($"保存修仙配置失败: {ex.Message}");
                }
            }
        }

        public class XiuXianData
        {
            public static Dictionary<string, XiuXianData> Players = new Dictionary<string, XiuXianData>();

            public string Name { get; set; }
            public string Realm { get; set; } = "凡人";
            public int CultivationProgress { get; set; } = 0;
            public float LifeYears { get; set; } = 80;
            public int RebirthCount { get; set; } = 0;
            public bool InHolyLand { get; set; } = false;
            public string StarSign { get; set; } = "未选择";
            public int ConstellationLevel { get; set; } = 0;
            public string DharmaName { get; set; } = "";
            public string NameColor { get; set; } = "FF69B4";
            public bool LifeDepletionWarned { get; set; } = false;
            public DateTime LifeDepletionTime { get; set; } = DateTime.MinValue;

            // 玩家击杀的Boss记录
            [JsonProperty("击杀Boss")]
            public HashSet<string> KilledBosses { get; set; } = new HashSet<string>();

            public static XiuXianData GetPlayer(string name)
            {
                if (!Players.TryGetValue(name, out XiuXianData data))
                {
                    data = new XiuXianData { Name = name };
                    Players[name] = data;
                }
                return data;
            }

            public static void Load(string path)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        Players = JsonConvert.DeserializeObject<Dictionary<string, XiuXianData>>(File.ReadAllText(path))
                                 ?? new Dictionary<string, XiuXianData>();

                        foreach (var data in Players.Values)
                        {
                            if (data.LifeDepletionTime == default)
                                data.LifeDepletionTime = DateTime.MinValue;

                            // 初始化KilledBosses
                            if (data.KilledBosses == null)
                                data.KilledBosses = new HashSet<string>();
                        }

                        TShock.Log.Info($"修仙数据已加载，{Players.Count}名修士");
                    }
                }
                catch (Exception ex)
                {
                    TShock.Log.Error($"加载修仙数据失败: {ex.Message}");
                }
            }

            public static void Save(string path)
            {
                try
                {
                    File.WriteAllText(path, JsonConvert.SerializeObject(Players, Formatting.Indented));
                    TShock.Log.Info($"修仙数据已保存，{Players.Count}名修士");
                }
                catch (Exception ex)
                {
                    TShock.Log.Error($"保存修仙数据失败: {ex.Message}");
                }
            }

            public static void SavePlayer(string name)
            {
                try
                {
                    if (Players.ContainsKey(name))
                        Save(DataPath);
                }
                catch (Exception ex)
                {
                    TShock.Log.Error($"保存玩家 {name} 数据失败: {ex.Message}");
                }
            }
        }
        #endregion

        #region StatusManager 实现
        /// <summary>
        /// 状态文本管理器
        /// </summary>
        private class StatusManager
        {
            private class PlayerStatus
            {
                public string Key { get; set; }
                public string Text { get; set; }
                public Color Color { get; set; }
            }

            private readonly Dictionary<TSPlayer, List<PlayerStatus>> _playerStatuses = new Dictionary<TSPlayer, List<PlayerStatus>>();

            /// <summary>
            /// 添加或更新状态文本
            /// </summary>
            public void AddOrUpdateText(TSPlayer player, string key, string text, Color color)
            {
                if (!_playerStatuses.TryGetValue(player, out var statuses))
                {
                    statuses = new List<PlayerStatus>();
                    _playerStatuses[player] = statuses;
                }

                var existing = statuses.FirstOrDefault(s => s.Key == key);
                if (existing != null)
                {
                    existing.Text = text;
                    existing.Color = color;
                }
                else
                {
                    statuses.Add(new PlayerStatus { Key = key, Text = text, Color = color });
                }

                UpdatePlayerStatus(player);
            }

            /// <summary>
            /// 移除特定状态文本
            /// </summary>
            public void RemoveText(TSPlayer player, string key)
            {
                if (_playerStatuses.TryGetValue(player, out var statuses))
                {
                    var item = statuses.FirstOrDefault(s => s.Key == key);
                    if (item != null)
                    {
                        statuses.Remove(item);
                        UpdatePlayerStatus(player);
                    }
                }
            }

            /// <summary>
            /// 移除玩家的所有状态文本
            /// </summary>
            public void RemoveText(TSPlayer player)
            {
                if (_playerStatuses.ContainsKey(player))
                {
                    _playerStatuses.Remove(player);
                    // 清除玩家当前状态
                    player.SendData(PacketTypes.Status, "", 0, 0x1f);
                }
            }

            /// <summary>
            /// 更新玩家状态显示
            /// </summary>
            private void UpdatePlayerStatus(TSPlayer player)
            {
                if (!_playerStatuses.TryGetValue(player, out var statuses) || !statuses.Any())
                    return;

                // 组合所有状态文本
                var combined = new StringBuilder();
                foreach (var status in statuses)
                {
                    combined.AppendLine(status.Text);
                }

                // 发送状态更新
                player.SendData(PacketTypes.Status, combined.ToString(), 0, 0x1f);
            }
        }
        #endregion
    }
    
    public static class StringExtensions
    {
        public static string Color(this string text, Color color)
        {
            return $"[c/{color.R:X2}{color.G:X2}{color.B:X2}:{text}]";
        }
    }
}