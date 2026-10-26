using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Localization;
using Kingmaker.Localization.Shared;
using Kingmaker.PubSubSystem;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.RuleSystem.Rules.Abilities;
using Kingmaker.Settings;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Buffs;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;
using Kingmaker.Utility;
using Newtonsoft.Json;
using UnityEngine;

namespace SpellSequencers
{
    // =========================================================================
    //  BG2 风格序列器
    //  法术序列器 2/4/6/8 环：吸收 3 发「不超过自身环级」的法术，标准动作手动倾泻
    //  法术触发器 2/4/6/8 环：同样吸收，但你在被攻击命中时自动向攻击者倾泻
    //  可多个同时待发；一次施法优先被吸入「能容纳它的最低环」序列器
    // =========================================================================

    public class Main
    {
        public static void Load()
        {
            try
            {
                var harmony = new Harmony("com.custom.spellsequencers");
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                Runner.Create();
                Log("=== SpellSequencers loaded ===");
            }
            catch (Exception e)
            {
                Log("FATAL: " + e);
            }
        }

        private static bool s_LogChecked;

        [System.Diagnostics.Conditional("SEQ_DEBUG")]
        public static void Log(string message)
        {
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SpellSequencers.log");
                if (!s_LogChecked)
                {
                    s_LogChecked = true;
                    var fi = new FileInfo(logPath);
                    if (fi.Exists && fi.Length > 4 * 1024 * 1024) fi.Delete();
                }
                File.AppendAllText(logPath, DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + Environment.NewLine);
            }
            catch { }
        }
    }

    // =========================================================================
    //  存档数据
    // =========================================================================

    public class StoredSpell
    {
        [JsonProperty] public string Spell = "";
        [JsonProperty] public string Spellbook = "";
        [JsonProperty] public int Level;
        [JsonProperty] public int MetaMask;
        [JsonProperty] public int MetaHeighten;
        [JsonProperty] public int MetaCost;
    }

    public class SequencerState
    {
        /// <summary>装填它的那个序列器/触发器法术的蓝图 GUID（"N" 格式）</summary>
        [JsonProperty] public string SpellGuid = "";
        /// <summary>装填时施法所用的法术书 GUID，用于计算施法者等级（持续时间 = 每级 1 天）</summary>
        [JsonProperty] public string Spellbook = "";
        /// <summary>到期时刻（GameTime.Ticks）；0 = 尚未计算</summary>
        [JsonProperty] public long ExpireTicks;
        [JsonProperty] public List<StoredSpell> Spells = new List<StoredSpell>();
        /// <summary>每个槽位已选定的目标（仅运行时，不入存档；读档后需重新选目标）</summary>
        [JsonIgnore] public TargetWrapper[] SlotTargets = new TargetWrapper[Ids.MaxStore];
    }

    public class SequencerPart : UnitPart
    {
        [JsonProperty] public List<SequencerState> States = new List<SequencerState>();

        public SequencerState FindState(string spellGuid)
        {
            if (States == null) States = new List<SequencerState>();
            return States.Find(s => s != null && s.SpellGuid == spellGuid);
        }
    }

    // =========================================================================
    //  一个「环级 + 类型」的法术定义
    // =========================================================================

    public class TierDef
    {
        public int Level;
        public bool IsTrigger;
        public string Icon = "";
        /// <summary>该容器最多储存的法术数量（2/4 环序列器 2、触发器 1；6/8 环序列器 3、触发器 2）</summary>
        public int Capacity = 3;

        public string SpellGuid = "";
        public string BuffGuid = "";
        public string ReleaseGuid = "";
        /// <summary>槽位能力的 GUID（每环级固定生成 MaxStore 个；释放能力只引用 Capacity 个）</summary>
        public string[] SlotGuids = new string[Ids.MaxStore];
        public string[] SlotNameKeys = new string[Ids.MaxStore];

        public string NameKey = "";
        public string DescKey = "";
        public string BuffNameKey = "";
        public string BuffDescKey = "";
        public string RelNameKey = "";
        public string RelDescKey = "";

        public BlueprintGuid SpellBp;
        public BlueprintGuid BuffBp;
        public BlueprintGuid ReleaseBp;
        public BlueprintGuid[] SlotBps = new BlueprintGuid[Ids.MaxStore];

        public string Key { get { return (IsTrigger ? "trigger" : "sequencer") + Level; } }
    }

    // =========================================================================
    //  常量与确定性 GUID
    // =========================================================================

    internal static class Ids
    {
        /// <summary>每个序列器最多储存的法术数量</summary>
        public const int MaxStore = 3;

        /// <summary>法师法术列表（术士/奥能师共用）</summary>
        public const string WizardSpellList = "ba0401fdeb4062f40a7aa95b6f07fe89";
        /// <summary>牧师法术列表</summary>
        public const string ClericSpellList = "8443ce803d2d31347897a3d85cc32f53";
        /// <summary>德鲁伊法术列表</summary>
        public const string DruidSpellList = "bad8638d40639d04fa2f80a1cac67d6b";

        public const string IconSequencer = "c9f183e1d73013c4e871045c7e6b48a9"; // 法术解除
        public const string IconTrigger = "4206a1359428b2a449e65c28ae98b760";   // 反震屏障
        public const string IconRelease = "c9f183e1d73013c4e871045c7e6b48a9";   // 解除魔法

        /// <summary>法术详情面板共用的「持续时间」本地化键</summary>
        public static readonly string DurKey = Hash("shared:duration");
        /// <summary>法术详情面板共用的「豁免检定」本地化键</summary>
        public static readonly string SaveKey = Hash("shared:savethrow");

        public static readonly List<TierDef> Tiers = new List<TierDef>();

        private static readonly Dictionary<string, TierDef> BySpell = new Dictionary<string, TierDef>();
        private static readonly Dictionary<string, TierDef> ByRelease = new Dictionary<string, TierDef>();
        /// <summary>槽位 GUID → (tier, slotIndex)</summary>
        private static readonly Dictionary<string, KeyValuePair<TierDef, int>> BySlot =
            new Dictionary<string, KeyValuePair<TierDef, int>>();

        static Ids()
        {
            int[] levels = { 2, 4, 6, 8 };
            foreach (int level in levels)
            {
                Tiers.Add(Make(level, false));
                Tiers.Add(Make(level, true));
            }
        }

        private static TierDef Make(int level, bool trigger)
        {
            string key = (trigger ? "trigger" : "sequencer") + level;
            var d = new TierDef
            {
                Level = level,
                IsTrigger = trigger,
                // 环级越低容量越小：2/4 环序列器 2、触发器 1；6/8 环序列器 3、触发器 2
                Capacity = trigger ? (level <= 4 ? 1 : 2) : (level <= 4 ? 2 : 3),
                Icon = trigger ? IconTrigger : IconSequencer,
                SpellGuid = Hash(key + ":spell"),
                BuffGuid = Hash(key + ":buff"),
                ReleaseGuid = Hash(key + ":release"),
                NameKey = Hash(key + ":name"),
                DescKey = Hash(key + ":desc"),
                BuffNameKey = Hash(key + ":buffname"),
                BuffDescKey = Hash(key + ":buffdesc"),
                RelNameKey = Hash(key + ":relname"),
                RelDescKey = Hash(key + ":reldesc"),
            };
            for (int i = 0; i < MaxStore; i++)
            {
                d.SlotGuids[i] = Hash(key + ":slot" + i);
                d.SlotNameKeys[i] = Hash(key + ":slotname" + i);
                d.SlotBps[i] = new BlueprintGuid(new Guid(d.SlotGuids[i]));
                BySlot[d.SlotGuids[i]] = new KeyValuePair<TierDef, int>(d, i);
            }
            d.SpellBp = new BlueprintGuid(new Guid(d.SpellGuid));
            d.BuffBp = new BlueprintGuid(new Guid(d.BuffGuid));
            d.ReleaseBp = new BlueprintGuid(new Guid(d.ReleaseGuid));
            BySpell[d.SpellGuid] = d;
            ByRelease[d.ReleaseGuid] = d;
            return d;
        }

        public static TierDef FindSpell(string guid)
        {
            TierDef d;
            return (guid != null && BySpell.TryGetValue(guid, out d)) ? d : null;
        }

        public static TierDef FindRelease(string guid)
        {
            TierDef d;
            return (guid != null && ByRelease.TryGetValue(guid, out d)) ? d : null;
        }

        /// <summary>返回 (tier, slotIndex) 或 null</summary>
        public static KeyValuePair<TierDef, int>? FindSlot(string guid)
        {
            if (guid == null) return null;
            KeyValuePair<TierDef, int> kv;
            if (BySlot.TryGetValue(guid, out kv)) return kv;
            return null;
        }

        /// <summary>
        /// MD5("SpellSequencers:" + seed) → 确定性 GUID 字符串。
        /// 必须用 "N" 格式（32 位无连字符）：BlueprintGuid.ToString() 返回的就是 "N"，
        /// 用 "D" 会导致按 GUID 查表全部落空。
        /// </summary>
        public static string Hash(string seed)
        {
            using (var md5 = MD5.Create())
            {
                byte[] h = md5.ComputeHash(Encoding.UTF8.GetBytes("SpellSequencers:" + seed));
                return new Guid(h).ToString("N");
            }
        }

        /// <summary>当前游戏时间；不在游戏内时返回 NULL</summary>
        public static TimeSpan? Now()
        {
            try
            {
                if (Game.Instance != null && Game.Instance.Player != null)
                    return Game.Instance.Player.GameTime;
            }
            catch { }
            return null;
        }
    }

    // =========================================================================
    //  蓝图构建：模板 .jbp 文本替换 → 反序列化 → 注册进蓝图库
    // =========================================================================

    internal static class Blueprints
    {
        public static bool Ready;

        private static readonly Dictionary<string, string> Templates = new Dictionary<string, string>();

        public static void BuildAll()
        {
            try
            {
                LoadTemplates();
                if (Templates.Count < 3)
                {
                    Main.Log("BuildAll: templates missing (" + Templates.Count + ")");
                    return;
                }

                foreach (TierDef d in Ids.Tiers)
                {
                    Make("spell", d, d.SpellGuid, d.Icon, d.NameKey, d.DescKey);
                    Make("buff", d, d.BuffGuid, d.Icon, d.BuffNameKey, d.BuffDescKey);
                    Make("release", d, d.ReleaseGuid, Ids.IconRelease, d.RelNameKey, d.RelDescKey);
                    for (int i = 0; i < Ids.MaxStore; i++)
                    {
                        Make("slot", d, d.SlotGuids[i], Ids.IconRelease, d.SlotNameKeys[i], d.RelDescKey);
                    }
                }

                AddToSpellLists();
                Ready = true;
                Main.Log("Blueprints built: " + (Ids.Tiers.Count * (3 + Ids.MaxStore)) + " blueprints");
            }
            catch (Exception e)
            {
                Main.Log("BuildAll failed: " + e);
            }
        }

        private static void LoadTemplates()
        {
            if (Templates.Count >= 3) return;
            var asm = Assembly.GetExecutingAssembly();
            foreach (string res in asm.GetManifestResourceNames())
            {
                if (!res.EndsWith(".jbp", StringComparison.OrdinalIgnoreCase)) continue;
                string kind = res.StartsWith("template_", StringComparison.OrdinalIgnoreCase)
                    ? res.Substring("template_".Length, res.Length - "template_".Length - 4)
                    : null;
                if (kind == null) continue;
                using (var stream = asm.GetManifestResourceStream(res))
                {
                    if (stream == null) continue;
                    using (var sr = new StreamReader(stream))
                    {
                        Templates[kind.ToLowerInvariant()] = sr.ReadToEnd();
                    }
                }
            }
        }

        private static void Make(string kind, TierDef d, string guid, string icon, string nameKey, string descKey)
        {
            string tpl;
            if (!Templates.TryGetValue(kind, out tpl))
            {
                Main.Log("template '" + kind + "' not found");
                return;
            }

            // 「释放」能力的变体列表按该环级的容量生成（2/4 环序列器只有 2 个槽位）。
            var variants = new StringBuilder();
            for (int i = 0; i < d.Capacity; i++)
            {
                if (i > 0) variants.Append(", ");
                variants.Append("\"!bp_").Append(d.SlotGuids[i]).Append('"');
            }

            string json = tpl
                .Replace("__GUID__", guid)
                .Replace("__BUFFGUID__", d.BuffGuid)
                .Replace("__LEVEL__", d.Level.ToString())
                .Replace("__ICON__", icon)
                .Replace("__NAMEKEY__", nameKey)
                .Replace("__DESCKEY__", descKey)
                .Replace("__VARIANTS__", variants.ToString())
                .Replace("__SLOT0GUID__", d.SlotGuids[0])
                .Replace("__SLOT1GUID__", d.SlotGuids[1])
                .Replace("__SLOT2GUID__", d.SlotGuids[2])
                .Replace("__DURKEY__", Ids.DurKey)
                .Replace("__SAVEKEY__", Ids.SaveKey);

            try
            {
                var bpGuid = new BlueprintGuid(new Guid(guid));
                if (ResourcesLibrary.TryGetBlueprint<BlueprintScriptableObject>(bpGuid) != null) return;

                using (var jr = new JsonTextReader(new StringReader(json)))
                {
                    var wrapper = Json.Serializer.Deserialize<BlueprintJsonWrapper>(jr);
                    var bp = wrapper == null ? null : wrapper.Data as BlueprintScriptableObject;
                    if (bp == null) { Main.Log("bad " + kind + " for " + d.Key); return; }
                    bp.name = "SpellSequencers." + d.Key + "." + kind;
                    bp.AssetGuid = bpGuid;
                    bp.OnEnable();
                    ResourcesLibrary.BlueprintsCache.AddCachedBlueprint(bpGuid, bp);
                }
            }
            catch (Exception e)
            {
                Main.Log("make " + kind + " " + d.Key + " failed: " + e.Message);
            }
        }

        private static void AddToSpellLists()
        {
            string[] lists = { Ids.WizardSpellList, Ids.ClericSpellList, Ids.DruidSpellList };
            foreach (TierDef d in Ids.Tiers)
            {
                foreach (string listGuid in lists)
                {
                    AddSpell(listGuid, d.Level, d.SpellBp);
                }
            }
        }

        private static void AddSpell(string listGuid, int level, BlueprintGuid guid)
        {
            try
            {
                var list = ResourcesLibrary.TryGetBlueprint<BlueprintSpellList>(listGuid);
                if (list == null) { Main.Log("spell list not found: " + listGuid); return; }
                var bp = ResourcesLibrary.TryGetBlueprint<BlueprintAbility>(guid);
                if (bp == null) return;
                if (list.SpellsByLevel == null || level < 0 || level >= list.SpellsByLevel.Length) return;
                var lvl = list.SpellsByLevel[level];
                if (lvl == null) return;
                if (!lvl.Spells.Contains(bp)) lvl.Spells.Add(bp);
                var filtered = lvl.SpellsFiltered;
                if (filtered != null && !filtered.Contains(bp)) filtered.Add(bp);
            }
            catch (Exception e)
            {
                Main.Log("AddSpell failed: " + e.Message);
            }
        }
    }

    // =========================================================================
    //  本地化
    // =========================================================================

    internal static class Loc
    {
        private static bool s_Subscribed;

        private static string Title(TierDef d)
        {
            return (d.IsTrigger ? "Spell Trigger (Level " : "Spell Sequencer (Level ") + d.Level + ")";
        }

        private static string TitleZh(TierDef d)
        {
            return (d.IsTrigger ? "法术触发器（" : "法术序列器（") + d.Level + "环）";
        }

        public static void Register()
        {
            try
            {
                var pack = LocalizationManager.CurrentPack;
                if (pack != null)
                {
                    bool zh = LocalizationManager.CurrentLocale == Locale.zhCN;
                    pack.PutString(Ids.DurKey, zh ? "1 天/施法者等级" : "1 day/caster level");
                    pack.PutString(Ids.SaveKey, zh ? "无" : "None");
                    foreach (TierDef d in Ids.Tiers)
                    {
                        pack.PutString(d.NameKey, zh ? TitleZh(d) : Title(d));
                        pack.PutString(d.BuffNameKey, zh ? "待发：" + TitleZh(d) : "Armed: " + Title(d));
                        pack.PutString(d.RelNameKey, zh ? "释放" + TitleZh(d) : "Release " + Title(d));

                        if (d.IsTrigger) TriggerTexts(pack, d, zh);
                        else SequencerTexts(pack, d, zh);

                        // 槽位默认名；SyncUnit 会在装填后用储存法术的名字覆盖。
                        for (int i = 0; i < Ids.MaxStore; i++)
                        {
                            pack.PutString(d.SlotNameKeys[i],
                                zh ? ("空槽位 " + (i + 1)) : ("Empty Slot " + (i + 1)));
                        }
                    }
                    Main.Log("loc registered, zh=" + zh);
                }

                if (!s_Subscribed)
                {
                    s_Subscribed = true;
                    SettingsRoot.Game.Main.Localization.OnValueChanged += OnLocaleChanged;
                }
            }
            catch (Exception e)
            {
                Main.Log("Loc.Register failed: " + e.Message);
            }
        }

        private static void SequencerTexts(LocalizationPack pack, TierDef d, bool zh)
        {
            int n = d.Level;
            int c = d.Capacity;
            if (zh)
            {
                pack.PutString(d.DescKey,
                    "变化系 " + n + " 环法术。施放后进入「待发」状态：你接下来施放的 " + c + " 个环级不超过 " + n +
                    " 环的法术会被吸入序列器——法术位与材料成分照常消耗、施法动画照常播放，但法术不产生任何效果。" +
                    "待发期间动作条会出现「释放" + TitleZh(d) + "」：点开它会出现 " + c + " 个槽位（每个槽位显示已储存的法术），" +
                    "依次为每个法术选择目标——全部选定后，所有法术立即倾泻到各自选定的目标身上（消耗一个标准动作），随后序列器解除。仅限自身的法术总是以你为目标。" +
                    "待发状态休息不解除；再次施放本法术会清空并重新装填。若同时有多个序列器待发，施法会优先被吸入能容纳它的最低环序列器。" +
                    "\n\n注意：要充能的法术如果只能以单个敌人为目标，充能时要对准自己施法；释放序列器时一定要为每个槽位指定敌人，否则法术会打在自己身上。");
                pack.PutString(d.BuffDescKey,
                    "层数等于已储存的法术数量（最多 " + c + " 个，每个不超过 " + n + " 环）。点开动作条上的「释放" + TitleZh(d) + "」逐槽选定目标后倾泻；再次施放本法术会重新装填。");
                pack.PutString(d.RelDescKey,
                    "点开出现 " + c + " 个槽位，每个槽位显示已储存的法术。逐一点击槽位为其选择目标（自由动作）；全部选定后，所有法术同时倾泻到各自目标身上（消耗一个标准动作），随后序列器解除。");
            }
            else
            {
                pack.PutString(d.DescKey,
                    "Transmutation, level " + n + ". You become armed: the next " + c + " spells of level " + n +
                    " or lower that you cast are absorbed into the sequencer — their spell slots and material components are spent and the casting animation plays, but they have no effect." +
                    " While armed, \"Release " + Title(d) + "\" appears on your action bar: open it to reveal " + c + " slots showing the stored spells, then pick a target for each spell one by one. Once every stored spell has a target, they all fire at their chosen targets (a standard action) and the sequencer is discharged. Personal spells always target you." +
                    " The armed state persists through rest; casting this spell again clears and re-arms it. If several sequencers are armed, a spell is absorbed by the lowest-level one that can hold it." +
                    "\n\nNote: if a spell you want to store can only target a single enemy, cast it at yourself while priming; when releasing, always designate an enemy for each slot, or the stored spells will hit you.");
                pack.PutString(d.BuffDescKey,
                    "Rank equals the number of stored spells (max " + c + ", each of level " + n + " or lower). Open \"Release " + Title(d) + "\" on the action bar, pick a target for each slot, and all spells fire at once; casting the spell again re-arms it.");
                pack.PutString(d.RelDescKey,
                    "Opens " + c + " slots showing the stored spells. Click each slot to choose its target (free action); once all stored spells have targets, they all fire at their chosen targets (a standard action) and the sequencer is discharged.");
            }
        }

        private static void TriggerTexts(LocalizationPack pack, TierDef d, bool zh)
        {
            int n = d.Level;
            int c = d.Capacity;
            if (zh)
            {
                pack.PutString(d.DescKey,
                    "变化系 " + n + " 环法术。施放后进入「待发」状态：你接下来施放的 " + c + " 个环级不超过 " + n +
                    " 环的法术会被吸入触发器——法术位与材料成分照常消耗、施法动画照常播放，但法术不产生任何效果。" +
                    "当你被一次攻击命中时，触发器会自动把储存的全部法术倾泻到攻击者身上，随后解除。仅限自身的法术总是以你为目标。" +
                    "待发状态休息不解除；再次施放本法术会清空并重新装填。若同时有多个触发器待发，受击时它们会一起发动。" +
                    "\n\n注意：要充能的法术如果只能以单个敌人为目标，充能时要对准自己施法。");
                pack.PutString(d.BuffDescKey,
                    "层数等于已储存的法术数量（最多 " + c + " 个，每个不超过 " + n + " 环）。你被攻击命中时，储存的法术会自动射向攻击者。");
                pack.PutString(d.RelDescKey, "触发器没有手动释放能力。");
            }
            else
            {
                pack.PutString(d.DescKey,
                    "Transmutation, level " + n + ". You become armed: the next " + c + " spells of level " + n +
                    " or lower that you cast are absorbed into the trigger — their spell slots and material components are spent and the casting animation plays, but they have no effect." +
                    " When you are hit by an attack, the trigger automatically unloads every stored spell onto the attacker, then it is discharged. Personal spells always target you." +
                    " The armed state persists through rest; casting this spell again clears and re-arms it. If several triggers are armed, they all fire on the same hit." +
                    "\n\nNote: if a spell you want to store can only target a single enemy, cast it at yourself while priming.");
                pack.PutString(d.BuffDescKey,
                    "Rank equals the number of stored spells (max " + c + ", each of level " + n + " or lower). The stored spells are fired at whoever hits you.");
                pack.PutString(d.RelDescKey, "Triggers have no manual release ability.");
            }
        }

        private static void OnLocaleChanged(LocaleExtensions.UILocale _)
        {
            Register();
        }
    }

    // =========================================================================
    //  运行时逻辑
    // =========================================================================

    internal static class SequencerLogic
    {
        /// <summary>正在倾泻法术，防止规则重入导致的无限循环</summary>
        private static bool s_Busy;

        /// <summary>BlueprintUnitFact.m_Icon 字段（private Sprite），用于运行时修改槽位图标的反射缓存。</summary>
        private static System.Reflection.FieldInfo s_IconField;
        private static System.Reflection.FieldInfo IconField
        {
            get
            {
                if (s_IconField == null)
                {
                    s_IconField = typeof(Kingmaker.Blueprints.Facts.BlueprintUnitFact)
                        .GetField("m_Icon", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                }
                return s_IconField;
            }
        }

        /// <summary>把某个蓝图能力的图标改成指定法术蓝图的图标。</summary>
        private static void SetBlueprintIcon(BlueprintAbility target, BlueprintAbility source)
        {
            try
            {
                var f = IconField;
                if (f != null && source != null) f.SetValue(target, source.Icon);
            }
            catch (Exception e)
            {
                Main.Log("SetBlueprintIcon failed: " + e.Message);
            }
        }

        /// <summary>RuleCastSpell.OnTrigger 前缀。返回 false = 跳过原版施法效果。</summary>
        public static bool Handle(RuleCastSpell rule)
        {
            var data = rule.Spell;
            var bp = data == null ? null : data.Blueprint;
            if (bp == null || !Blueprints.Ready) return true;
            var caster = data.Caster == null ? null : data.Caster.Unit;
            if (caster == null) return true;
            string guid = bp.AssetGuid.ToString();
            Main.Log("cast " + bp.name + " guid=" + guid + " ready=" + Blueprints.Ready);

            // --- 序列器/触发器法术本体：装填（重新施放则清空重来） ---
            var armed = Ids.FindSpell(guid);
            if (armed != null)
            {
                var part0 = caster.Parts.Ensure<SequencerPart>();
                var state0 = part0.FindState(armed.SpellGuid);
                if (state0 == null)
                {
                    state0 = new SequencerState { SpellGuid = armed.SpellGuid };
                    part0.States.Add(state0);
                }
                state0.Spells.Clear();
                if (state0.SlotTargets != null) Array.Clear(state0.SlotTargets, 0, state0.SlotTargets.Length);
                state0.Spellbook = data.Spellbook != null ? data.Spellbook.Blueprint.AssetGuid.ToString() : "";
                SetExpiry(state0, caster);
                SyncUnit(caster, part0);
                Main.Log("armed " + armed.Key + " on " + caster.CharacterName + " guid=" + guid);
                // 跳过法术自带的 ContextActionApplyBuff——由我们的 SetBuff 完全控制 buff 的层数与时长，
                // 否则 ApplyBuff 会在 SyncUnit 之后重跑，把 rank 覆盖成默认值（Ranks=3）。
                rule.SetSuccess(true);
                return false;
            }

            // --- 释放能力（仅序列器有） ---
            var released = Ids.FindRelease(guid);
            if (released != null)
            {
                ManualRelease(caster, released, rule.SpellTarget);
                rule.SetSuccess(true);
                return false;
            }

            // --- 槽位点击：为该槽位储存的法术选定目标；全部选定后一次性倾泻 ---
            var slot = Ids.FindSlot(guid);
            if (slot.HasValue)
            {
                var slotDef = slot.Value.Key;
                int slotIdx = slot.Value.Value;
                if (slotIdx >= slotDef.Capacity)
                {
                    rule.SetSuccess(true);
                    return false;
                }
                var slotPart = caster.Parts.Get<SequencerPart>();
                var slotState = slotPart != null ? slotPart.FindState(slotDef.SpellGuid) : null;
                if (slotState == null || slotState.Spells.Count == 0 || slotIdx >= slotState.Spells.Count)
                {
                    // 空槽位：什么都不做（槽位能力是自由动作，无消耗）
                    rule.SetSuccess(true);
                    return false;
                }
                if (slotState.SlotTargets == null) slotState.SlotTargets = new TargetWrapper[Ids.MaxStore];
                slotState.SlotTargets[slotIdx] = rule.SpellTarget;
                Main.Log(slotDef.Key + " slot " + slotIdx + " target set: " + DescribeTarget(rule.SpellTarget));

                // 检查所有已装填槽位是否都已选定目标
                bool allSet = true;
                for (int i = 0; i < slotState.Spells.Count; i++)
                {
                    if (slotState.SlotTargets[i] == null) { allSet = false; break; }
                }

                if (!allSet)
                {
                    // 还没选完：刷新槽位名字（显示已选目标），不释放
                    SyncUnit(caster, slotPart);
                    rule.SetSuccess(true);
                    return false;
                }

                // 全部选定 → 每个法术打各自的目标，倾泻后解除，并消耗一个标准动作
                var fireSpells = new List<StoredSpell>(slotState.Spells);
                var fireTargets = (TargetWrapper[])slotState.SlotTargets.Clone();
                slotPart.States.Remove(slotState);
                SyncUnit(caster, slotPart);
                Main.Log("all slots targeted — firing " + fireSpells.Count + " spells from " + slotDef.Key);
                for (int i = 0; i < fireSpells.Count; i++)
                {
                    TryFire(caster, fireSpells[i], fireTargets[i] ?? (TargetWrapper)caster);
                }
                caster.SpendAction(UnitCommand.CommandType.Standard, false, 0f);
                rule.SetSuccess(true);
                return false;
            }

            // --- 吸收 ---
            if (data.IsChildSpell) return true;                        // 我们自己释放出去的法术不再吸收
            if (data.SourceItem != null) return true;                  // 卷轴/法杖/药水不算
            if (data.Spellbook == null) return true;                   // 只吸收法术书施法

            var part = caster.Parts.Get<SequencerPart>();
            if (part == null || part.States.Count == 0) return true;

            int lvl = data.SpellLevel;
            SequencerState best = null;
            TierDef bestDef = null;
            int bestLevel = int.MaxValue;
            foreach (var st in part.States)
            {
                var def = Ids.FindSpell(st.SpellGuid);
                if (def == null) continue;
                if (st.Spells.Count >= def.Capacity) continue;          // 该容器已满
                if (lvl > def.Level) continue;                          // 超环
                if (def.Level < bestLevel) { best = st; bestDef = def; bestLevel = def.Level; }
            }
            if (best == null) return true;                              // 没有能吸收它的 → 正常施放

            var md = data.MetamagicData;
            best.Spells.Add(new StoredSpell
            {
                Spell = guid,
                Spellbook = data.Spellbook.Blueprint.AssetGuid.ToString(),
                Level = lvl,
                MetaMask = md != null ? (int)md.MetamagicMask : 0,
                MetaHeighten = md != null ? md.HeightenLevel : 0,
                MetaCost = md != null ? md.SpellLevelCost : 0,
            });
            SyncUnit(caster, part);
            Main.Log("absorbed " + bp.name + " into " + bestLevel + " (" + best.Spells.Count + "/" + bestDef.Capacity + ")");
            rule.SetSuccess(true);   // 动作照扣、法术位照扣、施法特效照播
            return false;            // 但不产生任何法术效果
        }

        private static void SetExpiry(SequencerState state, UnitEntityData caster)
        {
            var now = Ids.Now();
            if (!now.HasValue) return;
            state.ExpireTicks = (now.Value + TimeSpan.FromDays(CasterLevel(caster, state.Spellbook))).Ticks;
        }

        /// <summary>取该法术书的施法者等级；取不到则退回角色等级。</summary>
        private static int CasterLevel(UnitEntityData caster, string spellbookGuid)
        {
            try
            {
                if (!string.IsNullOrEmpty(spellbookGuid))
                {
                    var sbBp = ResourcesLibrary.TryGetBlueprint<BlueprintSpellbook>(spellbookGuid);
                    if (sbBp != null)
                    {
                        var sb = caster.Descriptor.GetSpellbook(sbBp);
                        if (sb != null && sb.CasterLevel > 0) return sb.CasterLevel;
                    }
                }
            }
            catch { }
            try
            {
                return Math.Max(1, caster.Descriptor.Progression.CharacterLevel);
            }
            catch { }
            return 1;
        }

        private static string DescribeTarget(TargetWrapper t)
        {
            if (t == null) return "<null>";
            if (t.Unit != null) return t.Unit.CharacterName;
            return "point";
        }

        private static void ManualRelease(UnitEntityData caster, TierDef def, TargetWrapper target)
        {
            var part = caster.Parts.Get<SequencerPart>();
            if (part == null) return;
            var state = part.FindState(def.SpellGuid);
            if (state == null) return;
            // 没有储存法术就不解除待发（点到空槽位不应浪费动作）
            if (state.Spells.Count == 0) return;
            var spells = new List<StoredSpell>(state.Spells);
            var targets = state.SlotTargets != null ? (TargetWrapper[])state.SlotTargets.Clone() : null;
            part.States.Remove(state);
            SyncUnit(caster, part);
            Main.Log("manual release " + def.Key + ": " + spells.Count + " spells");
            for (int i = 0; i < spells.Count; i++)
            {
                var t = (targets != null && i < targets.Length && targets[i] != null) ? targets[i] : target;
                TryFire(caster, spells[i], t ?? (TargetWrapper)caster);
            }
            caster.SpendAction(UnitCommand.CommandType.Standard, false, 0f);
        }

        /// <summary>被攻击命中时，自动引爆所有已装填且已有存货的触发器。</summary>
        public static void OnAttacked(RuleAttackRoll rule)
        {
            if (!Blueprints.Ready || s_Busy) return;
            var defender = rule.Target;
            var attacker = rule.Initiator;
            if (defender == null || attacker == null) return;
            if (ReferenceEquals(defender, attacker)) return;
            if (!rule.IsHit) return;

            var part = defender.Parts.Get<SequencerPart>();
            if (part == null || part.States.Count == 0) return;

            List<SequencerState> fire = null;
            foreach (var st in part.States)
            {
                var def = Ids.FindSpell(st.SpellGuid);
                if (def == null || !def.IsTrigger || st.Spells.Count == 0) continue;
                if (fire == null) { fire = new List<SequencerState>(); s_Busy = true; }
                fire.Add(st);
            }
            if (fire == null) return;

            try
            {
                foreach (var st in fire)
                {
                    var spells = new List<StoredSpell>(st.Spells);
                    part.States.Remove(st);
                    SyncUnit(defender, part);
                    Main.Log("auto trigger fired: " + spells.Count + " spells at " + attacker.CharacterName);
                    FireAll(defender, spells, (TargetWrapper)attacker);
                }
            }
            finally
            {
                s_Busy = false;
            }
        }

        private static void FireAll(UnitEntityData caster, List<StoredSpell> spells, TargetWrapper target)
        {
            foreach (var s in spells)
            {
                TryFire(caster, s, target);
            }
        }

        private static void TryFire(UnitEntityData caster, StoredSpell s, TargetWrapper target)
        {
            try
            {
                var bp = ResourcesLibrary.TryGetBlueprint<BlueprintAbility>(s.Spell);
                if (bp == null) return;
                var ad = new AbilityData(bp, caster.Descriptor);

                var sbBp = ResourcesLibrary.TryGetBlueprint<BlueprintSpellbook>(s.Spellbook);
                if (sbBp != null) ad.OverrideSpellbook = caster.Descriptor.GetSpellbook(sbBp);

                if (s.MetaMask != 0)
                {
                    var md = new MetamagicData();
                    md.Add((Metamagic)s.MetaMask);
                    md.HeightenLevel = s.MetaHeighten;
                    md.SpellLevelCost = s.MetaCost;
                    ad.MetamagicData = md;
                }
                ad.IsChildSpell = true;

                TargetWrapper t = target;
                bool selfOnly = bp.Range == AbilityRange.Personal
                    || (!bp.CanTargetEnemies && !bp.CanTargetFriends && !bp.CanTargetPoint);
                if (selfOnly)
                {
                    t = (TargetWrapper)caster;
                }
                else if (t == null || !ad.CanTarget(t))
                {
                    if (t != null && t.Unit != null && ad.CanTarget(t.Unit))
                        t = (TargetWrapper)t.Unit;
                    else if (ad.CanTarget((TargetWrapper)caster))
                        t = (TargetWrapper)caster;
                    else
                    {
                        Main.Log("no valid target for stored " + bp.name + ", skipped");
                        return;
                    }
                }

                Rulebook.Trigger(new RuleCastSpell(ad, t));
            }
            catch (Exception e)
            {
                Main.Log("TryFire failed: " + e.Message);
            }
        }

        /// <summary>同步动作条上的释放能力、槽位能力与待发 buff。幂等，可随时调用；同时处理到期解除。</summary>
        public static void SyncUnit(UnitEntityData unit, SequencerPart part)
        {
            try
            {
                var d = unit.Descriptor;
                var now = Ids.Now();
                List<SequencerState> expired = null;

                // 清理重复/失效状态：旧版本存档可能残留多个同 tier 状态。
                // 若不清掉，释放后残留的那个状态会让 SyncUnit 重新把待发 buff 加回来（表现为「buff 图标不消失」）。
                if (part.States != null && part.States.Count > 0)
                {
                    for (int i = part.States.Count - 1; i >= 0; i--)
                    {
                        var s = part.States[i];
                        if (s == null)
                        {
                            part.States.RemoveAt(i);
                            continue;
                        }
                        if (Ids.FindSpell(s.SpellGuid) == null)
                        {
                            part.States.RemoveAt(i);
                            Main.Log("dropped unknown state: " + s.SpellGuid);
                            continue;
                        }
                        int first = part.States.FindIndex(o => o != null && o.SpellGuid == s.SpellGuid);
                        if (first >= 0 && first != i)
                        {
                            part.States.RemoveAt(i);
                            Main.Log("dropped duplicate state: " + s.SpellGuid);
                        }
                    }
                }

                foreach (TierDef def in Ids.Tiers)
                {
                    var st = part.FindState(def.SpellGuid);
                    if (st != null && now.HasValue && st.ExpireTicks > 0 && now.Value.Ticks >= st.ExpireTicks)
                    {
                        if (expired == null) expired = new List<SequencerState>();
                        expired.Add(st);
                        st = null;
                    }
                    // 旧存档里可能存了这个环级现在装不下的数量，超出部分直接丢弃，
                    // 否则多余的槽位无法选目标，释放时会打到自己。
                    if (st != null && st.Spells.Count > def.Capacity)
                    {
                        st.Spells.RemoveRange(def.Capacity, st.Spells.Count - def.Capacity);
                        Main.Log("trimmed over-capacity spells: " + def.Key);
                    }
                    bool isArmed = st != null;
                    int count = isArmed ? st.Spells.Count : 0;
                    int cl = isArmed ? CasterLevel(unit, st.Spellbook) : 0;
                    SetBuff(d, def.BuffBp, isArmed, count, cl, def.Capacity);
                    // 待发即显示「释放」按钮（带槽位弹窗），即使没有存货也能打开查看
                    SetAbility(d, def.ReleaseBp, isArmed && !def.IsTrigger);

                    // 槽位能力是「释放」能力的 variants，无需作为 fact 添加；
                    // 但槽位蓝图是全局共享的，图标/名字只对「当前选中角色」同步，避免多名队员互相覆盖。
                    if (!def.IsTrigger && isArmed && IsSelected(unit))
                    {
                        UpdateSlotDisplay(def, st);
                    }

                    // 更新 buff 描述，列出已储存的法术，方便悬停查看。
                    UpdateBuffDescription(def, st);
                }

                if (expired != null)
                {
                    foreach (var st in expired) part.States.Remove(st);
                    Main.Log("state expired on " + unit.CharacterName + " (" + expired.Count + ")");
                }
            }
            catch (Exception e)
            {
                Main.Log("SyncUnit failed: " + e.Message);
            }
        }

        /// <summary>把 buff 描述更新为「已储存：法术1、法术2…」的形式。</summary>
        private static void UpdateBuffDescription(TierDef def, SequencerState st)
        {
            try
            {
                var pack = LocalizationManager.CurrentPack;
                if (pack == null) return;
                bool zh = LocalizationManager.CurrentLocale == Locale.zhCN;
                string baseDesc;
                if (def.IsTrigger)
                {
                    baseDesc = zh
                        ? ("层数等于已储存的法术数量（最多 " + def.Capacity + " 个，每个不超过 " + def.Level + " 环）。你被攻击命中时，储存的法术会自动射向攻击者。")
                        : ("Rank equals the number of stored spells (max " + def.Capacity + ", each of level " + def.Level + " or lower). The stored spells are fired at whoever hits you.");
                }
                else
                {
                    baseDesc = zh
                        ? ("层数等于已储存的法术数量（最多 " + def.Capacity + " 个，每个不超过 " + def.Level + " 环）。使用「释放" + LocTitle(def, zh) + "」倾泻全部储存的法术；再次施放本法术会重新装填。")
                        : ("Rank equals the number of stored spells (max " + def.Capacity + ", each of level " + def.Level + " or lower). Use \"Release " + LocTitle(def, zh) + "\" to unload them; casting the spell again re-arms it.");
                }

                string storedList = "";
                if (st != null && st.Spells.Count > 0)
                {
                    var names = new List<string>();
                    foreach (var s in st.Spells)
                    {
                        var bp = ResourcesLibrary.TryGetBlueprint<BlueprintAbility>(s.Spell);
                        names.Add(bp != null && bp.Name != null ? bp.Name.ToString() : "?");
                    }
                    string label = zh ? "已储存" : "Stored";
                    storedList = "\n\n" + label + " (" + names.Count + "/" + def.Capacity + "): " + string.Join(", ", names.ToArray());
                }
                pack.PutString(def.BuffDescKey, baseDesc + storedList);
            }
            catch { }
        }

        /// <summary>该角色是否为当前选中角色（决定共享槽位蓝图显示谁的内容）。</summary>
        private static bool IsSelected(UnitEntityData unit)
        {
            try
            {
                var sel = Game.Instance != null ? Game.Instance.SelectionCharacter : null;
                return sel != null && ReferenceEquals(sel.CurrentSelectedCharacter, unit);
            }
            catch { return false; }
        }

        /// <summary>把容量内的槽位蓝图的图标/名字同步为该序列器当前的内容（含已选目标后缀）。</summary>
        private static void UpdateSlotDisplay(TierDef def, SequencerState st)
        {
            try
            {
                var pack = LocalizationManager.CurrentPack;
                bool zh = LocalizationManager.CurrentLocale == Locale.zhCN;
                for (int i = 0; i < def.Capacity; i++)
                {
                    var slotBp = ResourcesLibrary.TryGetBlueprint<BlueprintAbility>(def.SlotBps[i]);
                    if (slotBp == null) continue;
                    bool filled = i < st.Spells.Count;
                    if (filled)
                    {
                        var storedBp = ResourcesLibrary.TryGetBlueprint<BlueprintAbility>(st.Spells[i].Spell);
                        if (storedBp != null)
                        {
                            SetBlueprintIcon(slotBp, storedBp);
                            string name = storedBp.Name != null ? storedBp.Name.ToString() : storedBp.name;
                            var t = st.SlotTargets != null && i < st.SlotTargets.Length ? st.SlotTargets[i] : null;
                            if (t != null)
                            {
                                string tname = t.Unit != null ? t.Unit.CharacterName : (zh ? "指定地点" : "point");
                                name = name + " → " + tname;
                            }
                            if (pack != null) pack.PutString(def.SlotNameKeys[i], name);
                            continue;
                        }
                    }
                    // 空槽位：用释放图标 +「空槽位」文字
                    var relBp = ResourcesLibrary.TryGetBlueprint<BlueprintAbility>(def.ReleaseBp);
                    if (relBp != null) SetBlueprintIcon(slotBp, relBp);
                    if (pack != null) pack.PutString(def.SlotNameKeys[i], zh ? ("空槽位 " + (i + 1)) : ("Empty Slot " + (i + 1)));
                }
            }
            catch (Exception e)
            {
                Main.Log("UpdateSlotDisplay failed: " + e.Message);
            }
        }

        private static string LocTitle(TierDef d, bool zh)
        {
            return zh
                ? ((d.IsTrigger ? "法术触发器（" : "法术序列器（") + d.Level + "环）")
                : ((d.IsTrigger ? "Spell Trigger (Level " : "Spell Sequencer (Level ") + d.Level + ")");
        }

        private static void SetAbility(UnitDescriptor d, BlueprintGuid guid, bool want)
        {
            var bp = ResourcesLibrary.TryGetBlueprint<BlueprintAbility>(guid);
            if (bp == null) return;
            UnitFact found = null;
            foreach (var f in d.Facts.List)
            {
                var uf = f as UnitFact;
                if (uf != null && ReferenceEquals(uf.Blueprint, bp)) { found = uf; break; }
            }
            if (want && found == null) d.AddFact(bp);
            else if (!want && found != null) d.RemoveFact(found);
        }

        private static void SetBuff(UnitDescriptor d, BlueprintGuid guid, bool want, int rank, int casterLevel, int capacity)
        {
            var bp = ResourcesLibrary.TryGetBlueprint<BlueprintBuff>(guid);
            if (bp == null) return;

            // 用 BuffCollection（游戏原生 buff 集合）枚举，与 ContextActionRemoveBuff 完全一致；
            // 先收集再改动，避免在枚举过程中修改集合。
            var found = new List<Buff>();
            foreach (var b in d.Buffs)
            {
                if (b != null && !b.IsDisposed && ReferenceEquals(b.Blueprint, bp)) found.Add(b);
            }

            if (want)
            {
                // 旧存档可能残留多个同蓝图实例：只保留一个
                for (int i = found.Count - 1; i >= 1; i--) RemoveBuff(d, found[i]);
                Buff buff = found.Count > 0 ? found[0] : null;
                if (buff == null)
                {
                    buff = d.AddBuff(bp, d.Unit, null);
                    if (buff == null) { Main.Log("buff add failed: " + bp.name); return; }
                    Main.Log("buff added: " + bp.name + " on " + d.CharacterName);
                }
                // 原来由 ContextActionApplyBuff 设置的标记，现在手动补上。
                buff.IsNotDispelable = true;
                buff.IsFromSpell = true;
                // 把 buff 改造成「每级 1 天」的有限时长。
                // 用 IsPermanent 判断是否已设置过结束时间：SetDuration 只写 m_EndTime，不写 PlannedDuration，
                // 所以不能用 PlannedDuration==null 判断，否则每秒都会把结束时间往后推导致永不结束。
                if (buff.IsPermanent && casterLevel > 0)
                {
                    buff.SetDuration(TimeSpan.FromDays(Math.Max(1, casterLevel)));
                }
                // 只有层数变化时才 Deactivate/Activate，避免每秒无意义地重启组件
                int wantRank = Math.Min(rank, Math.Max(1, capacity));
                if (buff.GetRank() != wantRank) buff.SetRank(wantRank, false);
            }
            else
            {
                if (found.Count == 0) return;
                foreach (var b in found) RemoveBuff(d, b);
                Main.Log("buff removed: " + bp.name + " x" + found.Count + " from " + d.CharacterName);

                // 移除后审计：确认真的从 buff 集合里消失了，方便下次日志定位
                int left = 0;
                foreach (var b in d.Buffs)
                {
                    if (b != null && !b.IsDisposed && ReferenceEquals(b.Blueprint, bp)) left++;
                }
                if (left > 0) Main.Log("WARN buff still present after remove: " + bp.name + " left=" + left);
            }
        }

        /// <summary>
        /// 用游戏原生路径移除一个 buff：Buff.Remove() → BuffManager.Remove → BuffCollection.OnFactWillDetach
        /// → HandleBuffDidRemoved（UI 图标随之消失）。
        /// 另外做三步兜底：提前通知 UI、标记立即过期让 Tick 收尾、最后强制从 Facts 列表移除。
        /// </summary>
        private static void RemoveBuff(UnitDescriptor d, Buff b)
        {
            try
            {
                b.IsSuppressed = false;

                // 兜底 1：先手动通知 UI（若原生事件已触发，UI 找不到对应 VM 时会自然忽略）
                try
                {
                    EventBus.RaiseEvent(delegate (IUnitBuffHandler h) { h.HandleBuffDidRemoved(b); });
                }
                catch { }

                b.Remove();

                // 兜底 2：若因为 Activating 等原因被延迟移除，把结束时间设到过去并刷新 buff 表，让 Tick 立刻清掉
                if (d.Facts.Contains(b))
                {
                    var now = Ids.Now();
                    b.SetEndTime(now.HasValue ? now.Value : TimeSpan.Zero);
                    d.Buffs.UpdateNextEvent();
                }
                // 兜底 3：仍然在列表里就直接从 Facts 集合移除
                if (d.Facts.Contains(b))
                {
                    d.Facts.Remove(b);
                    Main.Log("force-removed buff: " + b.Blueprint.name);
                }
            }
            catch (Exception e)
            {
                Main.Log("RemoveBuff failed: " + e.Message);
            }
        }
    }

    // =========================================================================
    //  后台同步器
    // =========================================================================

    public class Runner : MonoBehaviour
    {
        private float _acc;

        public static void Create()
        {
            try
            {
                var go = new GameObject("SpellSequencers.Runner");
                DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                go.AddComponent<Runner>();
            }
            catch (Exception e)
            {
                Main.Log("Runner.Create failed: " + e.Message);
            }
        }

        private void Update()
        {
            _acc += Time.unscaledDeltaTime;
            if (_acc < 1f) return;
            _acc = 0f;
            try
            {
                var game = Game.Instance;
                if (game == null || game.Player == null) return;
                foreach (var unit in game.Player.Party)
                {
                    if (unit == null) continue;
                    var part = unit.Parts.Get<SequencerPart>();
                    if (part != null) SequencerLogic.SyncUnit(unit, part);
                }
            }
            catch { }
        }
    }

    // =========================================================================
    //  Harmony 补丁
    // =========================================================================

    /// <summary>蓝图缓存初始化完成后：注册全部蓝图 + 注入三个法术列表。</summary>
    [HarmonyPatch(typeof(BlueprintsCache), "Init")]
    internal static class BlueprintsCache_Init_Patch
    {
        private static void Postfix()
        {
            Blueprints.BuildAll();
        }
    }

    /// <summary>本地化初始化完成后：注入字符串（并订阅语言切换）。</summary>
    [HarmonyPatch(typeof(LocalizationManager), "WaitForInit")]
    internal static class LocalizationManager_WaitForInit_Patch
    {
        private static void Postfix()
        {
            Loc.Register();
        }
    }

    /// <summary>施法规则拦截：装填 / 吸收 / 手动释放。</summary>
    [HarmonyPatch(typeof(RuleCastSpell), "OnTrigger")]
    internal static class RuleCastSpell_OnTrigger_Patch
    {
        private static bool Prefix(RuleCastSpell __instance)
        {
            try
            {
                return SequencerLogic.Handle(__instance);
            }
            catch (Exception e)
            {
                Main.Log("RuleCastSpell prefix error: " + e);
                return true;
            }
        }
    }

    /// <summary>攻击检定结算后：触发器自动反击。</summary>
    [HarmonyPatch(typeof(RuleAttackRoll), "OnTrigger")]
    internal static class RuleAttackRoll_OnTrigger_Patch
    {
        private static void Postfix(RuleAttackRoll __instance)
        {
            try
            {
                SequencerLogic.OnAttacked(__instance);
            }
            catch (Exception e)
            {
                Main.Log("RuleAttackRoll postfix error: " + e);
            }
        }
    }
}
