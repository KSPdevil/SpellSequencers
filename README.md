# Spell Sequencers (BG2-style) — 法术序列器 / 法术触发器（博德2风格）

A mod for **Pathfinder: Wrath of the Righteous** (game version 2.7.0) that brings back the
Baldur's Gate 2 style **Spell Sequencer** and **Spell Trigger**, added at 2nd / 4th / 6th / 8th
level so they stay useful before the late-game one-shot meta.

一个《开拓者：正义之怒》（游戏版本 2.7.0）的 mod，复刻博德之门2风格的
**法术序列器**与**法术触发器**，并提供 2/4/6/8 环四个版本，让它们在后期"互秒"之前就能派上用场。

> 本 mod 完全开源（MIT）。**任何人都可以自由修改源码、重新编译并发布自己的修改版**，无需征得作者同意；
> 修改版请保留 [LICENSE](LICENSE) 中的版权声明。源码结构与扩展方法见 [技术文档.md](技术文档.md)。

---

## 中文说明

### 新增法术（所有职业法术列表；神话法术列表除外）

每个环级各有一个「法术序列器」和一个「法术触发器」，都能储存若干个**环级不超过自身环级**的法术。
环级越低容量越小：2/4 环序列器存 2 个、触发器存 1 个；6/8 环序列器存 3 个、触发器存 2 个。

| 法术 | 环级 | 学派 | 释放方式 | 可吸收的法术环级 | 容量 |
| --- | --- | --- | --- | --- | --- |
| 法术序列器（2 环） | 2 | 变化系 | 标准动作手动释放 | ≤ 2 环 | 2 |
| 法术序列器（4 环） | 4 | 变化系 | 标准动作手动释放 | ≤ 4 环 | 2 |
| 法术序列器（6 环） | 6 | 变化系 | 标准动作手动释放 | ≤ 6 环 | 3 |
| 法术序列器（8 环） | 8 | 变化系 | 标准动作手动释放 | ≤ 8 环 | 3 |
| 法术触发器（2 环） | 2 | 变化系 | **被攻击命中时自动释放** | ≤ 2 环 | 1 |
| 法术触发器（4 环） | 4 | 变化系 | **被攻击命中时自动释放** | ≤ 4 环 | 1 |
| 法术触发器（6 环） | 6 | 变化系 | **被攻击命中时自动释放** | ≤ 6 环 | 2 |
| 法术触发器（8 环） | 8 | 变化系 | **被攻击命中时自动释放** | ≤ 8 环 | 2 |

### 使用方式

1. **装填**：施放任意一个序列器/触发器法术。施法者获得「待发」状态效果（每施法者等级 1 天），层数徽章显示当前储存数量（从 0 开始，上限见上表）。
2. **吸收**：待发期间，你施放的合规法术会被吸入——**法术位、材料与动作正常消耗，施法动画照常播放，但法术不产生任何效果**。状态层数 +1。
3. **释放**
   - **序列器**：动作条上出现「释放法术序列器」按钮。点开它会出现对应数量的**槽位**（每个槽位显示已储存的法术），
     依次点击每个槽位、为该法术**单独指定目标**（自由动作）；全部选定后，所有法术同时倾泻到**各自选定的目标**上，
     消耗一个**标准动作**，随后序列器解除。不消耗法术位。
   - **触发器**：不需要操作。当你**被一次攻击命中**时，储存的全部法术自动射向**攻击者**，随后触发器解除。

> **重要提示**
>
> - 要充能的法术如果**只能以单个敌人为目标**（例如解法术、单体控制），充能时**必须对准自己施法**，
>   否则它不会进入序列器，而是直接打出去。
> - 释放序列器时**一定要为每个槽位指定敌人**。没有指定目标（或目标已死亡/消失）的槽位，
>   其法术会**打在自己身上**。
> - **诈欺师（MagicDeceiver）的融合法术无法储存**：融合法术在施法时动态生成，
>   序列器只能存下槽位壳，释放时没有实际效果。因此诈欺师的法术列表里不会出现序列器/触发器法术。

### 规则细节

- **可以同时共存**：8 个法术互不干扰，你可以同时挂着多个序列器与触发器（比如 2 环序列器 + 8 环触发器），各有各的储存与状态。
- **吸收优先级**：一次施法只会被一个容器吸收，优先进入**能容纳它的最低环**那个（把高环容量留给大法术）。
- **重新装填**：再次施放同一个序列器/触发器法术，会清空该容器已储存的法术并重新装填，不影响其他容器。
- **吸收范围**：只吸收来自**法术书**的施法；卷轴、药水、法杖等道具法术与类法术能力不会被吸收。超环级或容器已满时法术正常施放。
- **超魔**：吸收时记录法术的全部超魔调整（含升阶环级），释放时原样生效。
- **目标**：序列器释放后，每个槽位的法术打向**该槽位单独指定的目标**；触发器全部射向攻击者。仅限自身的法术（如法师护甲）自动以施法者为目标。未指定目标的槽位会以施法者自己为目标。
- **持续**：待发状态持续 **每施法者等级 1 天**，随存档保存，不可被解除魔法驱散，**休息不会解除**；到期后自动解除，已储存的法术随之丢失。
- **豁免**：本法术以及储存、释放的全过程**均不允许豁免**。

### 安装

1. 安装 [Unity Mod Manager](https://www.nexusmods.com/site/mods/21) 并配置到游戏。
2. 用 UMM 的 *Mods → Install* 选择 `SpellSequencers-UMM.zip`，或将 `SpellSequencers.dll` + `Info.json`
   放入 `Mods\SpellSequencers\`。
3. 启动游戏，在 UMM 界面确认 **Spell Sequencers (BG2-style)** 已启用。

### 卸载

在 UMM 中禁用并删除本 mod 即可。已储存待发状态的角色读档后会自动清理残留状态。

### 兼容性

- 无任何前置依赖（不依赖 ToyBox / 百宝袋）。
- 使用 Harmony 补丁与运行时注入蓝图，不修改任何游戏文件；与绝大多数 mod 兼容。
- 已在游戏版本 **2.7.0** 上构建与测试。

### 从源码构建

需要 PowerShell 7 与本机 .NET（用于运行随附的 Roslyn 编译器）。修改 `build.ps1` 顶部的 `$gameDir`
为你的游戏安装目录后运行：

```powershell
pwsh -ExecutionPolicy Bypass -File build.ps1 -NoPause
```

- 日志默认关闭。在游戏根目录（与 exe 同级）新建一个空文件 `SpellSequencers.debug`，
  或放在 `Mods\SpellSequencers\debug.flag`，日志就会写到游戏根目录的 `SpellSequencers.log`；
  删掉文件即关闭（每 2 秒复查一次，无需重启游戏）。
- 编译产物：`SpellSequencers.dll`、`SpellSequencers-UMM.zip`，并自动复制到游戏的 `Mods\SpellSequencers\`。

---

## English

### New spells (all class spell lists; mythic lists excluded)

Each spell level has one *Spell Sequencer* and one *Spell Trigger*; both store spells of a
level no higher than their own. Lower-level containers hold fewer spells: sequencers hold 2 at
levels 2/4 and 3 at levels 6/8; triggers hold 1 at levels 2/4 and 2 at levels 6/8.

| Spell | Level | School | Release | Absorbs | Capacity |
| --- | --- | --- | --- | --- | --- |
| Spell Sequencer (Level 2) | 2 | Transmutation | standard action, manual | ≤ 2nd | 2 |
| Spell Sequencer (Level 4) | 4 | Transmutation | standard action, manual | ≤ 4th | 2 |
| Spell Sequencer (Level 6) | 6 | Transmutation | standard action, manual | ≤ 6th | 3 |
| Spell Sequencer (Level 8) | 8 | Transmutation | standard action, manual | ≤ 8th | 3 |
| Spell Trigger (Level 2) | 2 | Transmutation | **auto, when hit** | ≤ 2nd | 1 |
| Spell Trigger (Level 4) | 4 | Transmutation | **auto, when hit** | ≤ 4th | 1 |
| Spell Trigger (Level 6) | 6 | Transmutation | **auto, when hit** | ≤ 6th | 2 |
| Spell Trigger (Level 8) | 8 | Transmutation | **auto, when hit** | ≤ 8th | 2 |

### How it works

1. **Prime**: Cast any sequencer/trigger spell. The caster gains an *Armed* status effect
   (**1 day per caster level**) whose rank badge shows how many spells are stored (starts at 0;
   capacity is listed in the table above).
2. **Absorb**: While armed, eligible spells you cast are drawn in — the **spell slot, components
   and action are consumed, the casting animation plays, but the spell has no effect**. The rank
   increases by 1.
3. **Release**
   - **Sequencer**: a *Release* ability appears on the action bar. Open it to reveal as many
     **slots** as its capacity (each showing its stored spell), then click each slot to pick that
     spell's **own target** (free action). Once every slot has a target, all spells fire at
     **their chosen targets** simultaneously (**standard action**), and the sequencer is
     discharged. No spell slots are spent.
   - **Trigger**: no action needed. The moment you are **hit by an attack**, all stored spells are
     fired at the **attacker**, then the trigger is discharged.

> **Important**
>
> - If a spell you want to store can **only target a single enemy** (e.g. *Dispel Magic*, single-target
>   control), you must **cast it at yourself while priming**; otherwise it is not absorbed and fires normally.
> - When releasing a sequencer you **must designate an enemy for every slot**. Any slot without a
>   (living) target will have its spell **hit yourself**.
> - **Deceiver (MagicDeceiver) fusion spells cannot be stored**: they are dynamically generated at cast
>   time, so the sequencer would only capture an empty slot shell with no actual effect. The sequencer/trigger
>   spells are therefore **hidden from the Deceiver's spellbook**.

### Rules

- **They coexist**: all 8 spells are independent. You can keep several sequencers and triggers
  armed at once (e.g. a level 2 sequencer plus a level 8 trigger), each with its own storage.
- **Absorb priority**: a single cast is absorbed by only one container — the **lowest-level** one
  that can hold it, so higher-level containers keep their capacity for big spells.
- **Re-priming**: casting the same sequencer/trigger again clears that container and starts over;
  other containers are untouched.
- **Absorb scope**: only spells cast from a **spellbook** are absorbed; scrolls, potions, wands and
  spell-like abilities are never absorbed. Over-level spells, or casts beyond the 3-spell capacity,
  resolve normally.
- **Metamagic**: metamagic (including heightened level) is recorded on absorb and applied on release.
- **Targeting**: each slot's spell is fired at **the target picked for that slot**; triggers fire
  everything at the attacker. Personal-range spells (e.g. *Mage Armor*) always target the caster,
  and a slot with no target falls back to the caster.
- **Duration**: the armed state lasts **1 day per caster level**, persists through rest, is
  stored in the save, and cannot be dispelled; when it expires it is discharged and the stored
  spells are lost.
- **Saving throws**: neither the spell itself nor absorbing / releasing allows a **saving throw**.

### Install

1. Install [Unity Mod Manager](https://www.nexusmods.com/site/mods/21) and point it at the game.
2. Use UMM *Mods → Install* with `SpellSequencers-UMM.zip`, or drop `SpellSequencers.dll` +
   `Info.json` into `Mods\SpellSequencers\`.
3. Launch the game and make sure **Spell Sequencers (BG2-style)** is enabled in UMM.

### Uninstall

Disable/remove the mod in UMM. Characters with armed states are cleaned up automatically on the
next save load.

### Compatibility

- No dependencies (does **not** require ToyBox).
- Implemented with Harmony patches and runtime-injected blueprints; no game files are modified.
- Built and tested against game version **2.7.0**.

### Build from source

Requires PowerShell 7 and .NET (to run the bundled Roslyn compiler). Set `$gameDir` at the top of
`build.ps1` to your game folder, then run:

```powershell
pwsh -ExecutionPolicy Bypass -File build.ps1 -NoPause
```

- Logging is off by default. Create an empty file named `SpellSequencers.debug` in the game root
  (next to the exe) or `Mods\SpellSequencers\debug.flag`; logs then go to `SpellSequencers.log` in
  the game root. Delete the file to turn it off (re-checked every 2 seconds, no restart needed).
- Outputs `SpellSequencers.dll` and `SpellSequencers-UMM.zip`, and also copies them into the game's
  `Mods\SpellSequencers\`.

---

## License

[MIT](LICENSE) © KSPdevil

This mod is **fully open source**. Anyone may modify the source, rebuild it, and publish their own
version — no permission required. Modified versions must keep the copyright notice in
[LICENSE](LICENSE). See [技术文档.md](技术文档.md) for architecture and extension notes.

本 mod **完全开源**。任何人都可以修改源码、重新编译并发布自己的修改版，无需征得作者同意；
修改版请保留 [LICENSE](LICENSE) 中的版权声明。架构与扩展说明见 [技术文档.md](技术文档.md)。
