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

namespace XiuXianShouYuan
{
    [ApiVersion(2, 1)]
    public class XiuXianShouYuan : TerrariaPlugin
    {
        #region 基础配置
        private static readonly string ConfigPath = Path.Combine(TShock.SavePath, "XiuXianConfig.json");
        private static readonly string DataPath = Path.Combine(TShock.SavePath, "XiuXianData.json");
        private readonly Timer _cultivationTimer = new Timer(30000);

        public override string Name => "完美修仙星宿系统";
        public override string Author => "泷白";
        public override Version Version => new Version(4, 1, 0);
        public override string Description => "完美世界修炼体系与星宿流派系统";

        public XiuXianShouYuan(Main game) : base(game)
        {
            Order = 1;
            _cultivationTimer.AutoReset = true;
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
            AccountHooks.AccountCreate += OnAccountCreate;
            RegionHooks.RegionEntered += OnRegionEntered;
            RegionHooks.RegionLeft += OnRegionLeft;

            _cultivationTimer.Elapsed += Cultivate;

            Commands.ChatCommands.Add(new Command("shouyuan.player", ShowStatus, "状态"));
            Commands.ChatCommands.Add(new Command("shouyuan.player", CultivationCommand, "修仙"));
            Commands.ChatCommands.Add(new Command("shouyuan.admin", AdminForceRebirth, "寿元转生"));
            Commands.ChatCommands.Add(new Command("shouyuan.admin", AdminAdjustLife, "调整寿元"));
            Commands.ChatCommands.Add(new Command("shouyuan.player", SetDharmaName, "法号"));
            Commands.ChatCommands.Add(new Command("shouyuan.player", ChooseStarSign, "选择星宿"));
            Commands.ChatCommands.Add(new Command("shouyuan.player", ViewConstellation, "命座"));
            Commands.ChatCommands.Add(new Command("shouyuan.admin", SetServerName, "设置服务器名"));

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
                AccountHooks.AccountCreate -= OnAccountCreate;
                RegionHooks.RegionEntered -= OnRegionEntered;
                RegionHooks.RegionLeft -= OnRegionLeft;

                _cultivationTimer.Stop();
                _cultivationTimer.Dispose();

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
                data.StarSign = "未选择";
                data.DharmaName = "";

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
                var data = XiuXianData.GetPlayer(args.Player.Name);
                
                // 添加法号前缀
                if (!string.IsNullOrEmpty(data.DharmaName))
                {
                    // 构建带颜色的法号前缀
                    string dharmaPrefix = $"[c/{data.NameColor}:{data.DharmaName}] ";
                    
                    // 修改原始消息，添加法号前缀
                    args.RawText = dharmaPrefix + args.RawText;
                }
                
                // 寿元查询
                if (args.RawText.Contains("寿元") || args.RawText.Contains("寿命"))
                {
                    args.Player.SendInfoMessage($"当前剩余寿元: {data.LifeYears:F1}年");
                    args.Handled = true; // 阻止消息广播
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
                UpdateStatusBar(player, data);
                
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
                int penalty = new Random().Next(20, 51);
                data.LifeYears = Math.Max(1, data.LifeYears - penalty);
                
                // 显示状态更新
                UpdateStatusBar(player, data);
                
                player.SendWarningMessage($"道陨之际！寿元减少{penalty}年 (剩余: {data.LifeYears:F1}年)");
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
                var playerName = args.Player.Name;
                var data = XiuXianData.GetPlayer(playerName);
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
                
                // 显示欢迎信息和状态面板
                player.SendMessage("====================================", Microsoft.Xna.Framework.Color.Gold);
                player.SendMessage($"欢迎来到 [c/FF69B4:{XiuXianConfig.Instance.ServerName}]", Microsoft.Xna.Framework.Color.White);
                player.SendMessage("====================================", Microsoft.Xna.Framework.Color.Gold);
                
                // 显示完整状态面板
                ShowFullStatus(player, data);
                
                if (data.StarSign == "未选择")
                {
                    player.SendMessage("★★★ 请使用 /选择星宿 开启修仙之旅 ★★★", Microsoft.Xna.Framework.Color.Yellow);
                }
                
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
                    
                    // 更新状态栏
                    UpdateStatusBar(player, data);
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
                    
                    // 更新状态栏
                    UpdateStatusBar(player, data);
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
                    
                    // 显示欢迎信息
                    player.SendMessage("====================================", Microsoft.Xna.Framework.Color.Gold);
                    player.SendMessage($"欢迎来到 [c/FF69B4:{XiuXianConfig.Instance.ServerName}]", Microsoft.Xna.Framework.Color.White);
                    player.SendMessage("====================================", Microsoft.Xna.Framework.Color.Gold);
                    player.SendMessage($"新修士，初始寿元: {data.LifeYears}年", Microsoft.Xna.Framework.Color.LightGreen);
                    player.SendMessage("使用 /选择星宿 开启你的修仙之路", Microsoft.Xna.Framework.Color.Yellow);
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

        #region 可视化功能
        // 显示完整状态面板
        private void ShowFullStatus(TSPlayer player, XiuXianData data)
        {
            var realmInfo = XiuXianConfig.Instance.GetRealmInfo(data.Realm);
            var starSign = XiuXianConfig.Instance.StarSigns.FirstOrDefault(s => s.Name == data.StarSign);
            
            // 计算进度条
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
            
            // 根据寿元显示不同颜色
            var lifeColor = data.LifeYears > 100 ? "00FF00" : 
                            data.LifeYears > 50 ? "FFFF00" : "FF0000";
            player.SendMessage($"剩余寿元: [c/{lifeColor}:{data.LifeYears:F1}年]", Microsoft.Xna.Framework.Color.White);
            
            if (!string.IsNullOrEmpty(data.DharmaName))
            {
                player.SendMessage($"法号: [c/FF69B4:{data.DharmaName}]", Microsoft.Xna.Framework.Color.White);
            }
            
            player.SendMessage($"修炼进度: [c/00BFFF:{data.CultivationProgress}%]", Microsoft.Xna.Framework.Color.White);
            player.SendMessage(progressBar, Microsoft.Xna.Framework.Color.LightBlue);
            
            player.SendMessage($"命座: [c/9370DB:{data.ConstellationLevel}/7]", Microsoft.Xna.Framework.Color.White);
            player.SendMessage($"转生次数: [c/FFD700:{data.RebirthCount}]", Microsoft.Xna.Framework.Color.White);
            
            if (data.InHolyLand)
            {
                player.SendMessage("修炼环境: [c/00FF00:灵脉之地]", Microsoft.Xna.Framework.Color.White);
            }
            
            player.SendMessage("══════════════════════════════", Microsoft.Xna.Framework.Color.Cyan);
        }
        
        // 更新状态栏（简洁版）
        private void UpdateStatusBar(TSPlayer player, XiuXianData data)
        {
            var realmInfo = XiuXianConfig.Instance.GetRealmInfo(data.Realm);
            var nextRealm = XiuXianConfig.Instance.GetNextRealm(realmInfo);
            int nextReq = nextRealm?.BreakthroughReq ?? 100;
            
            // 创建简洁状态栏
            var sb = new StringBuilder();
            sb.Append($"[c/FF69B4:{XiuXianConfig.Instance.ServerName}] ");
            
            if (!string.IsNullOrEmpty(data.DharmaName))
            {
                sb.Append($"[c/{data.NameColor}:{data.DharmaName}] ");
            }
            
            sb.Append($"[c/00FF00:{data.Realm}境] ");
            
            // 星宿信息
            if (data.StarSign != "未选择")
            {
                var starSign = XiuXianConfig.Instance.StarSigns.FirstOrDefault(s => s.Name == data.StarSign);
                if (starSign != null)
                {
                    sb.Append($"[c/{starSign.Color.Hex3()}:★] ");
                }
            }
            
            // 寿元信息
            var lifeColor = data.LifeYears > 100 ? "00FF00" : 
                            data.LifeYears > 50 ? "FFFF00" : "FF0000";
            sb.Append($"[c/{lifeColor}:寿元:{data.LifeYears:F1}年] ");
            
            // 修炼进度
            sb.Append($"[c/00BFFF:修为:{data.CultivationProgress}/{nextReq}]");
            
            player.SendData(PacketTypes.Status, sb.ToString());
        }
        
        // 生成进度条
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

        #region 指令实现
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
                args.Player.SendInfoMessage("用法: /设置服务器名 <新名称>");
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
                UpdateStatusBar(onlinePlayer[0], targetData);
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
                UpdateStatusBar(onlinePlayer[0], targetData);
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
            
            // 设置名字颜色
            data.NameColor = GetRandomColorHex();
            
            args.Player.SendSuccessMessage($"法号已更新为: {newName}");
            
            // 更新状态栏
            UpdateStatusBar(args.Player, data);
        }
        
        // 获取随机颜色（十六进制）
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
            
            // 更新状态栏
            UpdateStatusBar(args.Player, data);
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
                    
                    // 跳过未选择星宿的玩家
                    if (data.StarSign == "未选择") continue;

                    var starSign = XiuXianConfig.Instance.StarSigns.FirstOrDefault(s => s.Name == data.StarSign);
                    if (starSign == null) continue;

                    float multiplier = data.InHolyLand ? 2.0f : 1.0f;
                    float signBonus = starSign.CultivationBonus;
                    
                    // 命座加成
                    float constellationBonus = 1.0f + (data.ConstellationLevel * 0.05f);
                    
                    data.CultivationProgress += (int)(0.5 * multiplier * constellationBonus * (1 + signBonus));
                    data.LifeYears -= 0.001f;

                    // 命座升级
                    if (data.CultivationProgress > 1000 && data.ConstellationLevel < 7)
                    {
                        data.ConstellationLevel++;
                        data.CultivationProgress = 0;
                        player.SendMessage($"★★★ 命座突破: {data.ConstellationLevel}/7 ★★★", Microsoft.Xna.Framework.Color.Cyan);
                        player.SendInfoMessage($"获得命座效果: {XiuXianConfig.Instance.Constellations[data.ConstellationLevel-1]}");
                    }

                    // 每小时灵潮涌动
                    if (DateTime.Now.Minute == 0 && DateTime.Now.Second < 30)
                    {
                        player.SendInfoMessage("灵潮涌动！修炼速度大幅提升");
                        data.CultivationProgress += 30;
                    }
                    
                    // 更新状态栏
                    UpdateStatusBar(player, data);
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
            
            // 检查是否已选择星宿
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
            
            // 星宿加成
            int gain = (int)(baseGain * (1 + starSign.CultivationBonus));
            
            // 命座加成
            gain = (int)(gain * (1.0f + data.ConstellationLevel * 0.05f));
            
            float lifeGain = gain * 0.1f;
            data.CultivationProgress += gain;
            data.LifeYears += lifeGain;
            
            player.SendSuccessMessage($"修炼成功！修为+{gain}%，寿元+{lifeGain:F1}年");
            player.SendMessage($"星宿「{starSign.Name}」加成: +{(starSign.CultivationBonus*100):F0}%", starSign.Color);
            
            // 更新状态栏
            UpdateStatusBar(player, data);
        }

        private void ProcessBreakthrough(TSPlayer player, XiuXianData data, RealmInfo realm, StarSignInfo starSign)
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
            
            // 星宿加成突破成功率
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
                
                if (XiuXianConfig.Instance.BroadcastBreakthrough)
                    TSPlayer.All.SendMessage($"{player.Name}突破{next.Name}境，天地异象！", Microsoft.Xna.Framework.Color.Yellow);
            }
            else
            {
                int damage = new Random().Next(10, 30);
                data.LifeYears = Math.Max(1, data.LifeYears - damage);
                player.SendErrorMessage($"★★ 天劫降临！损失{damage}年寿元 ★★");
                player.SendMessage($"星宿「{starSign.Name}」为你抵挡部分天劫", starSign.Color);
            }
            
            // 更新状态栏
            UpdateStatusBar(player, data);
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
            player.SendSuccessMessage($"★ 转世成功！获得新生寿元: {data.LifeYears:F1}年 ★");
            
            // 更新状态栏
            UpdateStatusBar(player, data);
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

        public class StarSignInfo
        {
            public string Name { get; set; }
            public string Type { get; set; } // 攻击/防御/辅助/全能
            public string Description { get; set; }
            public string ShortDesc { get; set; }
            public float CultivationBonus { get; set; } = 0f;
            public float LifeBonus { get; set; } = 0f;
            public float DamageBonus { get; set; } = 0f;
            public float DefenseBonus { get; set; } = 0f;
            public Microsoft.Xna.Framework.Color Color { get; set; }
            
            // 添加十六进制颜色代码
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

            public XiuXianConfig()
            {
                // 完美世界境界体系
                Realms.Add(new RealmInfo { Name = "搬血", Level = 1, LifeBonus = 50, BreakthroughReq = 100, SuccessRate = 0.95 });
                Realms.Add(new RealmInfo { Name = "洞天", Level = 2, LifeBonus = 100, BreakthroughReq = 150, SuccessRate = 0.9 });
                Realms.Add(new RealmInfo { Name = "化灵", Level = 3, LifeBonus = 200, BreakthroughReq = 200, SuccessRate = 0.85 });
                Realms.Add(new RealmInfo { Name = "铭纹", Level = 4, LifeBonus = 400, BreakthroughReq = 300, SuccessRate = 0.8 });
                Realms.Add(new RealmInfo { Name = "列阵", Level = 5, LifeBonus = 800, BreakthroughReq = 400, SuccessRate = 0.75 });
                Realms.Add(new RealmInfo { Name = "尊者", Level = 6, LifeBonus = 1600, BreakthroughReq = 500, SuccessRate = 0.7 });
                Realms.Add(new RealmInfo { Name = "神火", Level = 7, LifeBonus = 3200, BreakthroughReq = 600, SuccessRate = 0.65 });
                Realms.Add(new RealmInfo { Name = "真一", Level = 8, LifeBonus = 6400, BreakthroughReq = 700, SuccessRate = 0.6 });
                Realms.Add(new RealmInfo { Name = "圣祭", Level = 9, LifeBonus = 12800, BreakthroughReq = 800, SuccessRate = 0.55 });
                Realms.Add(new RealmInfo { Name = "天神", Level = 10, LifeBonus = 25600, BreakthroughReq = 900, SuccessRate = 0.5 });
                Realms.Add(new RealmInfo { Name = "虚道", Level = 11, LifeBonus = 51200, BreakthroughReq = 1000, SuccessRate = 0.45 });
                Realms.Add(new RealmInfo { Name = "斩我", Level = 12, LifeBonus = 102400, BreakthroughReq = 1100, SuccessRate = 0.4 });
                Realms.Add(new RealmInfo { Name = "遁一", Level = 13, LifeBonus = 204800, BreakthroughReq = 1200, SuccessRate = 0.35 });
                Realms.Add(new RealmInfo { Name = "至尊", Level = 14, LifeBonus = 409600, BreakthroughReq = 1300, SuccessRate = 0.3 });
                Realms.Add(new RealmInfo { Name = "真仙", Level = 15, LifeBonus = 819200, BreakthroughReq = 1400, SuccessRate = 0.25 });
                Realms.Add(new RealmInfo { Name = "仙王", Level = 16, LifeBonus = 1638400, BreakthroughReq = 1500, SuccessRate = 0.2 });
                Realms.Add(new RealmInfo { Name = "准仙帝", Level = 17, LifeBonus = 3276800, BreakthroughReq = 1600, SuccessRate = 0.15 });
                Realms.Add(new RealmInfo { Name = "仙帝", Level = 18, LifeBonus = 6553600, BreakthroughReq = 1700, SuccessRate = 0.1 });

                // 星宿流派系统
                StarSigns.Add(new StarSignInfo {
                    Name = "紫微帝星", Type = "全能", 
                    Description = "帝王之相，统御四方，全面提升修炼效率",
                    ShortDesc = "全面均衡发展",
                    CultivationBonus = 0.15f,
                    LifeBonus = 0.1f,
                    DamageBonus = 0.1f,
                    DefenseBonus = 0.1f,
                    Color = new Microsoft.Xna.Framework.Color(255, 215, 0) // 金色
                });
                
                StarSigns.Add(new StarSignInfo {
                    Name = "破军杀星", Type = "攻击", 
                    Description = "主杀伐征战，攻击力冠绝天下",
                    ShortDesc = "极致攻击路线",
                    CultivationBonus = 0.1f,
                    LifeBonus = 0.05f,
                    DamageBonus = 0.3f,
                    DefenseBonus = -0.1f,
                    Color = new Microsoft.Xna.Framework.Color(220, 20, 60) // 猩红
                });
                
                StarSigns.Add(new StarSignInfo {
                    Name = "天机玄星", Type = "辅助", 
                    Description = "洞悉天机，擅长辅助与阵法",
                    ShortDesc = "辅助与阵法专精",
                    CultivationBonus = 0.2f,
                    LifeBonus = 0.15f,
                    DamageBonus = -0.05f,
                    DefenseBonus = 0.1f,
                    Color = new Microsoft.Xna.Framework.Color(138, 43, 226) // 紫罗兰
                });
                
                StarSigns.Add(new StarSignInfo {
                    Name = "武曲战星", Type = "防御", 
                    Description = "战神护体，铜墙铁壁般的防御",
                    ShortDesc = "极致防御路线",
                    CultivationBonus = 0.1f,
                    LifeBonus = 0.25f,
                    DamageBonus = 0.05f,
                    DefenseBonus = 0.3f,
                    Color = new Microsoft.Xna.Framework.Color(0, 191, 255) // 深天蓝
                });
                
                StarSigns.Add(new StarSignInfo {
                    Name = "七杀凶星", Type = "攻击", 
                    Description = "以杀证道，越战越勇",
                    ShortDesc = "狂暴攻击路线",
                    CultivationBonus = 0.05f,
                    LifeBonus = -0.1f,
                    DamageBonus = 0.4f,
                    DefenseBonus = -0.15f,
                    Color = new Microsoft.Xna.Framework.Color(178, 34, 34) // 火砖红
                });
                
                StarSigns.Add(new StarSignInfo {
                    Name = "太阴玄星", Type = "辅助", 
                    Description = "月华之力，治疗与恢复",
                    ShortDesc = "治疗与恢复专精",
                    CultivationBonus = 0.15f,
                    LifeBonus = 0.3f,
                    DamageBonus = -0.1f,
                    DefenseBonus = 0.15f,
                    Color = new Microsoft.Xna.Framework.Color(173, 216, 230) // 淡蓝
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
                        
                        // 迁移旧配置：如果旧配置没有ServerName，使用默认值
                        if (string.IsNullOrWhiteSpace(Instance.ServerName))
                        {
                            Instance.ServerName = "泰拉修仙传";
                        }
                    }
                    else
                    {
                        Instance = new XiuXianConfig();
                        File.WriteAllText(path, JsonConvert.SerializeObject(Instance, Formatting.Indented));
                    }
                    TShock.Log.Info($"修仙配置已加载，服务器名称: {Instance.ServerName}");
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
            public string NameColor { get; set; } = "FF69B4"; // 默认粉色

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