# ShouYuan 寿元插件

- 作者: 泷白
- 出处: Tshock官方群
- 这是一个为 TShock Terraria 服务器设计的修仙插件，基于完美世界修炼体系与星宿流派系统。玩家可以通过修炼提升境界、选择星宿流派、管理寿元，体验完整的修仙玩法

## 更新日志

```
v1.2.2
泷白
新增/重读修仙(重读配置文件)
/散尽修为(玩家散尽修为) 
/仙道重开(管理员指令)
重构配置文件系统，支持实时重载
修复管理员指令权限异常问题
修改了pcUI已知的问题
```
##安装方法

1. 将插件 DLL 文件放入 TShock 服务器的 ServerPlugins 文件夹
2. 启动服务器自动生成配置文件
3. 重启服务器或使用 /reload 命令加载插件
```
权限系统

权限组

· 修仙弟子 (shouyuan.player) - 普通玩家权限
· 修仙仙尊 (shouyuan.admin) - 管理员权限

权限命令

```
# 创建权限组（首次运行自动创建）
/group add 修仙弟子 default 0,255,0 修仙系统玩家权限
/group add 修仙仙尊 修仙弟子 255,0,0 修仙系统管理员权限

# 分配权限
/group addperm 修仙弟子 shouyuan.player
/group addperm 修仙仙尊 shouyuan.admin
/group addperm 修仙仙尊 shouyuan.player

# 将玩家分配到权限组
/user group <玩家名> 修仙弟子
/user group <玩家名> 修仙仙尊
```
玩家指令

基础指令

指令 权限 描述 示例
/状态 shouyuan.player 显示完整的修仙状态信息 /状态
/修仙 shouyuan.player 打开修仙主菜单 /修仙
/修仙 状态 shouyuan.player 显示状态 /修仙 状态
/修仙 修炼 shouyuan.player 进行修炼，增加修为 /修仙 修炼
/修仙 突破 shouyuan.player 尝试突破到下一境界 /修仙 突破
/修仙 转生 shouyuan.player 转世重修（需凡人境界） /修仙 转生
/修仙 星宿 shouyuan.player 选择星宿流派 /修仙 星宿
/选择星宿 shouyuan.player 选择星宿流派 /选择星宿 紫微帝星
/法号 shouyuan.player 设置自定义法号 /法号 逍遥子
/命座 shouyuan.player 查看命座等级和效果 /命座
/散尽修为 shouyuan.player 重置境界到凡人 /散尽修为 确认

星宿流派

1. 紫微帝星 (全能) - 全面均衡发展
2. 破军杀星 (攻击) - 极致攻击路线
3. 天机玄星 (辅助) - 辅助与阵法专精
4. 武曲战星 (防御) - 极致防御路线
5. 七杀凶星 (攻击) - 狂暴攻击路线
6. 太阴玄星 (辅助) - 治疗与恢复专精

管理员指令

指令 权限 描述 示例
/寿元转生 shouyuan.admin 强制为玩家转生 /寿元转生 玩家名
/调整寿元 shouyuan.admin 调整玩家寿元 /调整寿元 玩家名 100
/设置服务器名称 shouyuan.admin 设置服务器显示名称 /设置服务器名称 我的修仙服务器
/设置聊天ui偏移 shouyuan.admin 调整聊天UI位置 /设置聊天ui偏移 2 1
/设置顶部ui偏移 shouyuan.admin 调整顶部UI位置 /设置顶部ui偏移 -13 10
/设置顶部ui刷新间隔 shouyuan.admin 设置UI刷新频率 /设置顶部ui刷新间隔 10000
/设置星宿图标 shouyuan.admin 设置星宿显示图标 /设置星宿图标 紫微帝星 5005
/添加境界条件 shouyuan.admin 添加境界突破条件 /添加境界条件 筑基 克苏鲁之眼
/添加境界奖励 shouyuan.admin 添加境界突破奖励 /添加境界奖励 筑基 123 5 0
/添加境界buff shouyuan.admin 添加境界Buff奖励 /添加境界buff 筑基 1 300
/重读修仙 shouyuan.admin 重新加载配置文件 /重读修仙
/仙道重开 shouyuan.admin 重置所有玩家境界 /仙道重开 确认

配置文件

位置

· 配置文件: tshock/XiuXianConfig.json
· 数据文件: tshock/XiuXianData.json

主要配置项

```json
{
  "ServerName": "泰拉修仙传",
  "RebirthCost": 200,
  "BroadcastBreakthrough": true,
  "ChatUIOffsetX": 2,
  "ChatUIOffsetY": 1,
  "TopUIOffsetX": -13,
  "TopUIOffsetY": 10,
  "TopUIRefreshInterval": 10000
}
```

境界体系

19个境界从低到高：

1. 凡人 → 2. 搬血 → 3. 洞天 → 4. 化灵 → 5. 铭纹 → 6. 列阵
2. 尊者 → 8. 神火 → 9. 真一 → 10. 圣祭 → 11. 天神 → 12. 虚道
3. 斩我 → 14. 遁一 → 15. 至尊 → 16. 真仙 → 17. 仙王 → 18. 准仙帝 → 19. 仙帝

每个境界都有不同的寿元奖励、突破要求和成功率。

特色功能

1. 双UI系统 - 聊天框UI和顶部状态栏UI
2. 星宿流派 - 6种不同修炼路线
3. 命座系统 - 7级命座提供各种加成
4. Boss进度 - 击败Boss解锁更高境界
5. 区域加成 - 灵脉区域修炼速度加倍
6. 时间事件 - 整点灵潮涌动修炼加速
7. 转生系统 - 多次转生获得更多寿元

注意事项

1. 寿元耗尽会导致玩家被踢出服务器
2. 需要击败特定Boss才能突破某些境界
3. 管理员可以调整所有设置和玩家数据
4. 配置文件修改后需要使用/重读修仙生效
5. 插件会自动创建权限组和默认配置

故障排除

1. 插件不加载: 检查TShock版本是否兼容
2. 权限无效: 手动创建权限组并分配权限
3. UI不显示: 检查偏移量设置是否合适
4. 数据丢失: 定期备份XiuXianData.json文件

如有其他问题，请查看服务器日志获取详细错误信息
```
## 反馈
- 优先发issued -> 共同维护的插件库：https://github.com/UnrealMultiple/TShockPlugin
- 次优先：TShock官方群：816771079
- 大概率看不到但是也可以：国内社区trhub.cn ，bbstr.net , tr.monika.love