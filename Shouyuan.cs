using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Timers;
using Newtonsoft.Json;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;
using TShockAPI.DB;
using Timer = System.Timers.Timer;

namespace XiuXianShouYuan
{
    [ApiVersion(2, 1)]
    public class XiuXianShouYuan : TerrariaPlugin
    {
        #region 基础配置
        private static readonly string ConfigPath = Path.Combine(TShock.SavePath, "XiuXianConfig.json");
        private static readonly string DataPath = Path.Combine(TShock.SavePath, "XiuXianData.json");
        private readonly Timer _cultivationTimer = new Timer(30000);

        public override string Name => "凡人修仙寿元系统";
        public override string Author => "泷白";
        public override Version Version => new Version(1, 2, 0);
        public override string Description => "完整的凡人修仙传寿元体系";

        public XiuXianShouYuan(Main game) : base(game)
        {
            Order = 1;
            _cultivationTimer.AutoReset = true;
        }
        #endregion

        #region 初始化与钩子注册
        public override void Initialize()
        {
            // 修正事件名称
            PlayerHooks.PlayerChat += OnPlayerChat;
            PlayerHooks.PlayerPostLogin += OnPlayerPostLogin;
            PlayerHooks.PlayerPreLogin += OnPlayerPreLogin;
            PlayerHooks.PlayerLogout += OnPlayerLogout;

            GetDataHandlers.PlayerDamage += OnPlayerDamage;

            // 使用GetDataHandlers.KillMe处理死亡事件
            GetDataHandlers.KillMe += OnDead;

            GeneralHooks.ReloadEvent += OnReload;

            ServerApi.Hooks.GameInitialize.Register(this, OnInitialize);
            ServerApi.Hooks.ServerJoin.Register(this, OnServerJoin);
            ServerApi.Hooks.ServerLeave.Register(this, OnServerLeave);
            AccountHooks.AccountCreate += OnAccountCreate;
            RegionHooks.RegionEntered += OnRegionEntered;
            RegionHooks.RegionLeft += OnRegionLeft;

            _cultivationTimer.Elapsed += Cultivate;

            // 注册指令
            Commands.ChatCommands.Add(new Command("shouyuan.player", ShowRemainingLife, "剩余寿元"));
            Commands.ChatCommands.Add(new Command("shouyuan.player", CultivationCommand, "修仙"));
            Commands.ChatCommands.Add(new Command("shouyuan.admin", AdminForceRebirth, "寿元转生"));
            Commands.ChatCommands.Add(new Command("shouyuan.admin", AdminAdjustLife, "调整寿元"));

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
                TSPlayer.All.SendSuccessMessage("[修仙体系] 天地灵气已复苏！输入 /剩余寿元 查看寿元");
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
                // 修正事件注销
                PlayerHooks.PlayerChat -= OnPlayerChat;
                PlayerHooks.PlayerPostLogin -= OnPlayerPostLogin;
                PlayerHooks.PlayerPreLogin -= OnPlayerPreLogin;
                PlayerHooks.PlayerLogout -= OnPlayerLogout;

                // 修复1：修正事件名称拼写错误
                GetDataHandlers.PlayerDamage -= OnPlayerDamage;

                // 注销GetDataHandlers.KillMe事件
                GetDataHandlers.KillMe -= OnDead;

                GeneralHooks.ReloadEvent -= OnReload;

                ServerApi.Hooks.GameInitialize.Deregister(this, OnInitialize);
                ServerApi.Hooks.ServerJoin.Deregister(this, OnServerJoin);
                ServerApi.Hooks.ServerLeave.Deregister(this, OnServerLeave);
                AccountHooks.AccountCreate -= OnAccountCreate;
                RegionHooks.RegionEntered -= OnRegionEntered;
                RegionHooks.RegionLeft -= OnRegionLeft;

                _cultivationTimer.Stop();
                _cultivationTimer.Dispose();

                // 保存所有数据
                XiuXianData.Save(DataPath);
            }
            base.Dispose(disposing);
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

        private void OnPlayerChat(PlayerChatEventArgs args)
        {
            try
            {
                if (args.RawText.Contains("寿元") || args.RawText.Contains("寿命"))
                {
                    var data = XiuXianData.GetPlayer(args.Player.Name);
                    args.Player.SendInfoMessage($"当前剩余寿元: {data.LifeYears:F1}年");
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
                player.SendWarningMessage($"根基受损！寿元减少{penalty}年 (剩余: {data.LifeYears:F1}年)");
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

                {
                    int penalty = new Random().Next(20, 51);
                    data.LifeYears = Math.Max(1, data.LifeYears - penalty);
                    player.SendWarningMessage($"O.o道陨之际！寿元减少{penalty}年 (剩余: {data.LifeYears:F1}年)");
                }
            }
            catch (Exception ex)
            {
                TShock.Log.Error($"玩家死亡事件处理失败: {ex.Message}");
            }
        }

        private void OnPlayerPreLogin(PlayerPreLoginEventArgs args)
        {
            try
            {
                // 修复2: 使用args.Player.Name获取玩家名
                var playerName = args.Player.Name;
                var data = XiuXianData.GetPlayer(playerName);

                // 修复3: 使用args.Player.Group检查权限
                bool isAdmin = args.Player.Group.HasPermission("shouyuan.admin");

                if (data.LifeYears <= 0 && !isAdmin)
                {
                    args.Handled = true;
                    args.Player.Disconnect("寿元已耗尽！请联系管理员使用/寿元转生");
                }
            }
            catch (Exception ex)
            {
                TShock.Log.Error($"玩家登录前检查失败: {ex.Message}");
            }
        }

        private void OnPlayerPostLogin(PlayerPostLoginEventArgs args)
        {
            try
            {
                var player = args.Player;
                var data = XiuXianData.GetPlayer(player.Name);

                player.SendSuccessMessage($"当前境界: {data.Realm} 寿元: {data.LifeYears:F1}年");
                if (data.LifeYears <= 0)
                    player.SendErrorMessage("★★★ 警告：寿元已耗尽！请立即转生 ★★★");

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
            try { XiuXianData.SavePlayer(args.Player.Name); }
            catch (Exception ex) { TShock.Log.Error($"玩家登出处理失败: {ex.Message}"); }
        }

        private void OnReload(ReloadEventArgs args)
        {
            try
            {
                XiuXianConfig.Load(ConfigPath);
                args.Player.SendSuccessMessage("修仙法则已重新感悟！");
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
                    player.SendInfoMessage($"新修士，初始寿元: {data.LifeYears}年");
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
                if (player != null) XiuXianData.SavePlayer(player.Name);
            }
            catch (Exception ex)
            {
                TShock.Log.Error($"玩家离开事件处理失败: {ex.Message}");
            }
        }
        #endregion

        #region 指令实现
        private void ShowRemainingLife(CommandArgs args)
        {
            try
            {
                var data = XiuXianData.GetPlayer(args.Player.Name);
                args.Player.SendSuccessMessage($"剩余寿元: {data.LifeYears:F1}年");
            }
            catch (Exception ex)
            {
                TShock.Log.Error($"显示寿元命令失败: {ex.Message}");
            }
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
            args.Player.SendSuccessMessage($"已为 {playerName} 转生");

            var onlinePlayer = TSPlayer.FindByNameOrID(playerName);
            if (onlinePlayer.Count > 0)
            {
                onlinePlayer[0].SendSuccessMessage($"管理员已为你转生！当前寿元: {targetData.LifeYears:F1}年");
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
            args.Player.SendSuccessMessage($"已将 {playerName} 寿元调整为: {targetData.LifeYears:F1}年");

            var onlinePlayer = TSPlayer.FindByNameOrID(playerName);
            if (onlinePlayer.Count > 0)
            {
                onlinePlayer[0].SendInfoMessage($"管理员已将你的寿元调整为: {targetData.LifeYears:F1}年");
            }
        }

        private void PerformRebirth(XiuXianData data, TSPlayer executor)
        {
            data.RebirthCount++;
            data.LifeYears = 60 + (data.RebirthCount * 20);
            data.CultivationProgress = 0;
            data.Realm = "凡人";
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

                    float multiplier = data.InHolyLand ? 2.0f : 1.0f;
                    data.CultivationProgress += (int)(0.5 * multiplier);
                    data.LifeYears -= 0.001f;

                    if (DateTime.Now.Minute == 0 && DateTime.Now.Second < 30)
                    {
                        player.SendInfoMessage("灵潮涌动！修炼速度大幅提升");
                        data.CultivationProgress += 20;
                    }
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
                args.Player.SendInfoMessage("修仙指令: /修仙 [查看|修炼|突破|转生|寿元]");
                return;
            }

            string subcmd = args.Parameters[0].ToLower();
            var data = XiuXianData.GetPlayer(args.Player.Name);
            var realmInfo = XiuXianConfig.Instance.GetRealmInfo(data.Realm);

            switch (subcmd)
            {
                case "查看":
                    SendCultivationInfo(args.Player, data, realmInfo);
                    break;
                case "修炼":
                    ProcessMeditation(args.Player, data);
                    break;
                case "突破":
                    ProcessBreakthrough(args.Player, data, realmInfo);
                    break;
                case "转生":
                    ProcessRebirth(args.Player, data);
                    break;
                case "寿元":
                    ShowRemainingLife(args);
                    break;
                default:
                    args.Player.SendErrorMessage("未知指令，可用: 查看, 修炼, 突破, 转生, 寿元");
                    break;
            }
        }

        private void SendCultivationInfo(TSPlayer player, XiuXianData data, RealmInfo realm)
        {
            var next = XiuXianConfig.Instance.GetNextRealm(realm);
            string msg = $"[c/FFD700:{realm.Name}境·Lv{realm.Level}]\n";
            msg += $"[c/00FF00:修为进度]: {data.CultivationProgress}%\n";
            msg += $"[c/FF4500:剩余寿元]: {data.LifeYears:F1}年\n";
            msg += next == null
                ? "[c/9370DB:已至大道巅峰]"
                : $"[c/00BFFF:下一境界]: {next.Name} (需{next.BreakthroughReq}%修为)";
            player.SendMessage(msg, Microsoft.Xna.Framework.Color.White);
        }

        private void ProcessMeditation(TSPlayer player, XiuXianData data)
        {
            if (data.LifeYears < 0.1f)
            {
                player.SendErrorMessage("寿元不足无法修炼！");
                return;
            }
            data.LifeYears -= 0.1f;
            int gain = new Random().Next(5, 15);
            float lifeGain = gain * 0.1f;
            data.CultivationProgress += gain;
            data.LifeYears += lifeGain;
            player.SendSuccessMessage($"修炼成功！修为+{gain}%，寿元+{lifeGain:F1}年");
        }

        private void ProcessBreakthrough(TSPlayer player, XiuXianData data, RealmInfo realm)
        {
            var next = XiuXianConfig.Instance.GetNextRealm(realm);
            if (next == null)
            {
                player.SendErrorMessage("已至大道巅峰，无法突破");
                return;
            }
            if (data.CultivationProgress < next.BreakthroughReq)
            {
                player.SendErrorMessage($"突破{next.Name}需要{next.BreakthroughReq}%修为");
                return;
            }
            if (new Random().NextDouble() < next.SuccessRate)
            {
                data.Realm = next.Name;
                data.CultivationProgress = 0;
                data.LifeYears += next.LifeBonus;
                player.SendMessage($"★★★★ {realm.Name}→{next.Name} ★★★★", Microsoft.Xna.Framework.Color.Gold);
                player.SendSuccessMessage($"寿元增加{next.LifeBonus}年! 当前: {data.LifeYears:F1}年");
                if (XiuXianConfig.Instance.BroadcastBreakthrough)
                    TSPlayer.All.SendMessage($"{player.Name}突破{next.Name}境，天地异象！", Microsoft.Xna.Framework.Color.Yellow);
            }
            else
            {
                int damage = new Random().Next(10, 30);
                data.LifeYears = Math.Max(1, data.LifeYears - damage);
                player.SendErrorMessage($"★★ 天劫降临！损失{damage}年寿元 ★★");
            }
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
            player.SendSuccessMessage($"★ 转世成功！获得新生寿元: {data.LifeYears:F1}年 ★");
        }
        #endregion

        #region 配置与数据
        public class RealmInfo
        {
            public string Name { get; set; } = "凡人";
            public int Level { get; set; } = 0;
            public int LifeBonus { get; set; } = 0;
            public int BreakthroughReq { get; set; } = 0;
            public double SuccessRate { get; set; } = 1.0;
        }

        public class XiuXianConfig
        {
            public static XiuXianConfig Instance;

            public int RebirthCost { get; set; } = 200;
            public bool BroadcastBreakthrough { get; set; } = true;
            public List<RealmInfo> Realms { get; set; } = new List<RealmInfo>();

            public XiuXianConfig()
            {
                Realms.Add(new RealmInfo { Name = "凡人", Level = 0, BreakthroughReq = 50 });
                Realms.Add(new RealmInfo { Name = "炼气", Level = 1, LifeBonus = 50, BreakthroughReq = 100, SuccessRate = 0.9 });
                Realms.Add(new RealmInfo { Name = "筑基", Level = 2, LifeBonus = 100, BreakthroughReq = 150, SuccessRate = 0.8 });
                Realms.Add(new RealmInfo { Name = "金丹", Level = 3, LifeBonus = 200, BreakthroughReq = 200, SuccessRate = 0.7 });
                Realms.Add(new RealmInfo { Name = "元婴", Level = 4, LifeBonus = 400, BreakthroughReq = 300, SuccessRate = 0.6 });
                Realms.Add(new RealmInfo { Name = "化神", Level = 5, LifeBonus = 800, BreakthroughReq = 400, SuccessRate = 0.5 });
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
                    }
                    else
                    {
                        Instance = new XiuXianConfig();
                        File.WriteAllText(path, JsonConvert.SerializeObject(Instance, Formatting.Indented));
                    }
                    TShock.Log.Info($"修仙配置已加载，包含{Instance.Realms.Count}个境界");
                }
                catch (Exception ex)
                {
                    TShock.Log.Error($"加载修仙配置失败: {ex.Message}");
                    Instance = new XiuXianConfig();
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
    }
}