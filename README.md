# ShouYuan 寿元插件

- 作者: 泷白
- 出处: Tshock官方群
- 这是一个Tshock服务器插件，功能概述：寿元体系：为玩家构建完整的修仙寿元体系，通过境界提升、转生、修炼等机制动态管理角色寿命

境界突破：从凡人到化神共6大境界，突破需消耗修为并面临天劫风险

灵脉之地：特定区域（如灵脉/洞天）加速修炼，全局灵潮事件提升修为

损伤惩罚：战斗受伤或死亡将折损寿元，深度绑定生存玩法

转生机制：寿元耗尽需转世重修，保留部分属性重新开始修仙之旅

## 更新日志

```
v1.2.0
泷白
新增灵潮涌动事件（每小时触发），修炼速度×300%

灵脉区域修炼效率翻倍

重构配置文件系统，支持实时重载

修复管理员指令权限异常问题
```

## 指令

| 语法                             | 别名  |       权限       |                   说明                   |
| -------------------------------- | :---: | :--------------: | :--------------------------------------: |
| /剩余寿元  | 无 |  shouyuan.player       |    查看当前寿元及境界    |
| /修仙 查看 | /修仙 |  shouyuan.player | 显示修炼状态与突破事件
| /修仙 修炼 | 无 | shouyuan.player | 消耗寿元进行修炼(+5-15%修为)
| /修仙 突破 | 无 | shouyuan.player | 尝试突破至下一境界(可能引发天劫)
| /修仙 转生 | 无 | shouyuan.player | 散功转世(需凡人境+200年寿元)
| /寿元转生<玩家名> | 无 | shouyuan.admin | 强制为玩家转生(管理员专用)
| /调整寿元<玩家名><值> | 无 | shouyuan.admin | 增减玩家寿元(可负值 管理员专用)

## 配置
> 配置文件位置： tshock/XiuXianConfig.json 
```json
{
  "RebirthCost": 200,    
  "BroadcastBreakthrough": true,
  "Realms": [
    {
      "Name": "凡人",
      "Level": 0,
      "LifeBonus": 0,           
      "BreakthroughReq": 50,     
      "SuccessRate": 1.0         
    },
    {
      "Name": "炼气",
      "Level": 1,
      "LifeBonus": 50,
      "BreakthroughReq": 100,
      "SuccessRate": 0.9
    },
  ]
}
```
## 反馈
- 优先发issued -> 共同维护的插件库：https://github.com/UnrealMultiple/TShockPlugin
- 次优先：TShock官方群：816771079
- 大概率看不到但是也可以：国内社区trhub.cn ，bbstr.net , tr.monika.love
