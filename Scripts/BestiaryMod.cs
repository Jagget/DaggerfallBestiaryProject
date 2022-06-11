using UnityEngine;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using DaggerfallConnect;
using System.Collections.Generic;
using System.IO;
using System;
using System.Text.RegularExpressions;
using System.Globalization;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Utility;
using System.Linq;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallWorkshop.Game.Utility;
using DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects;
using DaggerfallConnect.Save;

namespace DaggerfallBestiaryProject
{
    public class BestiaryMod : MonoBehaviour
    {
        private static Mod mod;

        public static BestiaryMod Instance { get; private set; }

        public struct CustomCareer
        {
            public DFCareer dfCareer;
        }

        private Dictionary<string, CustomCareer> customCareers = new Dictionary<string, CustomCareer>(StringComparer.OrdinalIgnoreCase);

        public struct CustomEnemy
        {
            public MobileEnemy mobileEnemy;
            public string name;
            public string career;
            public int[] spellbook;
            public int onHitEffect;
        }

        private Dictionary<int, CustomEnemy> customEnemies = new Dictionary<int, CustomEnemy>();

        public Dictionary<int, CustomEnemy> CustomEnemies { get { return customEnemies; } }

        // Returns true if the id is a monster id. False if it's a career id
        // The id doesn't have to refer to an actual enemy
        public static bool IsMonster(int enemyId)
        {
            // Ids 0 to 127 are monsters
            // 128 to 255 is classes
            // and these two alternate every 128 ids
            return ((enemyId / 128) % 2) == 0;
        }


        [Invoke(StateManager.StateTypes.Start, 0)]
        public static void Init(InitParams initParams)
        {
            mod = initParams.Mod;

            var go = new GameObject(mod.Title);
            Instance = go.AddComponent<BestiaryMod>();

            mod.IsReady = true;
        }

        private void Start()
        {
            ParseDfCareers();
            ParseCustomCareers();
            ParseCustomEnemies();

            EnemyEntity.OnLootSpawned += OnEnemySpawn;
            FormulaHelper.RegisterOverride<Action<EnemyEntity, DaggerfallEntity, int>>(mod, "OnMonsterHit", OnMonsterHit);
        }

        IEnumerable<TextAsset> GetDBAssets(string extension)
        {
            HashSet<string> names = new HashSet<string>();
            foreach(Mod mod in ModManager.Instance.EnumerateEnabledModsReverse())
            {
                foreach(string file in mod.ModInfo.Files.Where(filePath => filePath.EndsWith(extension)).Select(filePath => Path.GetFileName(filePath)))
                {
                    names.Add(file);
                }
            }

            foreach(string name in names)
            {
                ModManager.Instance.TryGetAsset(name, clone: false, out TextAsset asset);
                yield return asset;
            }
        }

        void ParseDfCareers()
        {
            foreach(MonsterCareers career in Enum.GetValues(typeof(MonsterCareers)).Cast<MonsterCareers>().Skip(1))
            {
                DFCareer dfCareer = DaggerfallEntity.GetMonsterCareerTemplate(career);
                if(dfCareer != null)
                {
                    customCareers.Add(career.ToString(), new CustomCareer { dfCareer = dfCareer });
                }
            }

            foreach (ClassCareers career in Enum.GetValues(typeof(ClassCareers)).Cast<ClassCareers>().Skip(1))
            {
                DFCareer dfCareer = DaggerfallEntity.GetClassCareerTemplate(career);
                if (dfCareer != null)
                {
                    customCareers.Add(career.ToString(), new CustomCareer { dfCareer = dfCareer });
                }
            }
        }

        void ParseCustomCareers()
        {
            foreach (TextAsset asset in GetDBAssets(".cdb.csv"))
            {
                var stream = new StreamReader(new MemoryStream(asset.bytes));

                string header = stream.ReadLine();

                string[] fields = header.Split(';', ',');

                bool GetIndex(string fieldName, out int index)
                {
                    index = -1;
                    for (int i = 0; i < fields.Length; ++i)
                    {
                        if (fields[i].Equals(fieldName, StringComparison.InvariantCultureIgnoreCase))
                        {
                            index = i;
                            break;
                        }
                    }
                    if (index == -1)
                    {
                        Debug.LogError($"Career DB file '{asset.name}': could not find field '{fieldName}' in header");
                        return false;
                    }
                    return true;
                }

                if (!GetIndex("Name", out int NameIndex)) continue;
                if (!GetIndex("HitPointsPerLevel", out int HPIndex)) continue;
                if (!GetIndex("Strength", out int StrengthIndex)) continue;
                if (!GetIndex("Intelligence", out int IntelligenceIndex)) continue;
                if (!GetIndex("Willpower", out int WillpowerIndex)) continue;
                if (!GetIndex("Agility", out int AgilityIndex)) continue;
                if (!GetIndex("Endurance", out int EnduranceIndex)) continue;
                if (!GetIndex("Personality", out int PersonalityIndex)) continue;
                if (!GetIndex("Speed", out int SpeedIndex)) continue;
                if (!GetIndex("Luck", out int LuckIndex)) continue;

                CultureInfo cultureInfo = new CultureInfo("en-US");
                int lineNumber = 1;
                while (stream.Peek() >= 0)
                {
                    ++lineNumber;

                    string line = stream.ReadLine();

                    try
                    {
                        string[] tokens = SplitCsvLine(line);

                        DFCareer career = new DFCareer();

                        career.Name = tokens[NameIndex];

                        // Skip careers we've already assigned
                        // FindAssets traverses mods from high to low priority, so the first mod in this list wins
                        if (customCareers.ContainsKey(career.Name))
                            continue;

                        career.HitPointsPerLevel = int.Parse(tokens[HPIndex], cultureInfo);
                        career.Strength = int.Parse(tokens[StrengthIndex], cultureInfo);
                        career.Intelligence = int.Parse(tokens[IntelligenceIndex], cultureInfo);
                        career.Willpower = int.Parse(tokens[WillpowerIndex], cultureInfo);
                        career.Agility = int.Parse(tokens[AgilityIndex], cultureInfo);
                        career.Endurance = int.Parse(tokens[EnduranceIndex], cultureInfo);
                        career.Personality = int.Parse(tokens[PersonalityIndex], cultureInfo);
                        career.Speed = int.Parse(tokens[SpeedIndex], cultureInfo);
                        career.Luck = int.Parse(tokens[LuckIndex], cultureInfo);

                        CustomCareer customCareer = new CustomCareer();
                        customCareer.dfCareer = career;

                        customCareers.Add(career.Name, customCareer);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Error while parsing {asset.name}:{lineNumber}: {ex}");
                    }
                }
            }
        }

        static Regex CsvSplit = new Regex("(?:^|,)(\"(?:\\\\\"|[^\"])*\"|[^,]*)", RegexOptions.Compiled);

        static string[] SplitCsvLine(string line)
        {
            List<string> list = new List<string>();
            foreach (Match match in CsvSplit.Matches(line))
            {
                string curr = match.Value;
                if (0 == curr.Length)
                {
                    list.Add("");
                }

                list.Add(curr.TrimStart(',', ';').Replace("\\\"", "\"").Trim('\"'));
            }

            return list.ToArray();
        }

        int[] ParseArrayArg(string Arg, string Context)
        {
            if (string.IsNullOrEmpty(Arg))
                return Array.Empty<int>();

            // Strip brackets
            if(Arg[0] == '[' || Arg[0] == '{')
            {
                // Check for end bracket
                if(Arg[0] == '[' && Arg[Arg.Length - 1] != ']'
                    || Arg[0] == '{' && Arg[Arg.Length - 1] != '}')
                    throw new InvalidDataException($"Error parsing ({Context}): array argument has mismatched brackets");

                Arg = Arg.Substring(1, Arg.Length - 2);
            }

            string[] Frames = Arg.Split(',', ';');
            return Frames.Select(Frame => string.IsNullOrEmpty(Frame) ? "-1" : Frame).Select(int.Parse).ToArray();
        }

        bool ParseBool(string Value, string Context)
        {
            if (string.IsNullOrEmpty(Value))
                return false;

            if (string.Equals(Value, "true", StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(Value, "false", StringComparison.OrdinalIgnoreCase))
                return false;

            throw new InvalidDataException($"Error parsing ({Context}): invalid boolean value '{Value}'");
        }

        void ParseCustomEnemies()
        {
            List<MobileEnemy> enemies = EnemyBasics.Enemies.ToList();
            List<string> questEnemyLines = new List<string>();

            foreach (TextAsset asset in GetDBAssets(".mdb.csv"))
            {
                var stream = new StreamReader(new MemoryStream(asset.bytes));

                string header = stream.ReadLine();

                string[] fields = header.Split(';', ',');

                bool GetIndex(string fieldName, out int index)
                {
                    index = -1;
                    for (int i = 0; i < fields.Length; ++i)
                    {
                        if (fields[i].Equals(fieldName, StringComparison.InvariantCultureIgnoreCase))
                        {
                            index = i;
                            break;
                        }
                    }
                    if (index == -1)
                    {
                        Debug.LogError($"Monster DB file '{asset.name}': could not find field '{fieldName}' in header");
                        return false;
                    }
                    return true;
                }

                int? GetIndexOpt(string fieldName)
                {
                    int index = -1;
                    for (int i = 0; i < fields.Length; ++i)
                    {
                        if (fields[i].Equals(fieldName, StringComparison.InvariantCultureIgnoreCase))
                        {
                            index = i;
                            break;
                        }
                    }
                    if (index == -1)
                    {
                        return null;
                    }
                    return index;
                }

                if (!GetIndex("ID", out int IdIndex)) continue;
                if (!GetIndex("Name", out int NameIndex)) continue;
                if (!GetIndex("Career", out int CareerIndex)) continue;
                if (!GetIndex("MaleTexture", out int MaleTextureIndex)) continue;
                if (!GetIndex("FemaleTexture", out int FemaleTextureIndex)) continue;
                if (!GetIndex("CorpseTextureArchive", out int CorpseTextureArchiveIndex)) continue;
                if (!GetIndex("CorpseTextureRecord", out int CorpseTextureRecordIndex)) continue;
                if (!GetIndex("HasIdle", out int HasIdleIndex)) continue;
                if (!GetIndex("CastsMagic", out int CastsMagicIndex)) continue;
                if (!GetIndex("HasRangedAttack", out int HasRangedAttackIndex)) continue;
                if (!GetIndex("PrimaryAttackAnimFrames", out int PrimaryAttackAnimFramesIndex)) continue;
                if (!GetIndex("Team", out int TeamIndex)) continue;

                int? LevelIndex = GetIndexOpt("Level");
                int? BehaviourIndex = GetIndexOpt("Behaviour");
                int? AffinityIndex = GetIndexOpt("Affinity");
                int? MinDamageIndex = GetIndexOpt("MinDamage");
                int? MaxDamageIndex = GetIndexOpt("MaxDamage");
                int? MinHealthIndex = GetIndexOpt("MinHealth");
                int? MaxHealthIndex = GetIndexOpt("MaxHealth");
                int? ArmorValueIndex = GetIndexOpt("ArmorValue");
                int? MinMetalToHitIndex = GetIndexOpt("MinMetalToHit");
                int? WeightIndex = GetIndexOpt("Weight");
                int? SeesThroughInvisibilityIndex = GetIndexOpt("SeesThroughInvisibility");
                int? MoveSoundIndex = GetIndexOpt("MoveSound");
                int? BarkSoundIndex = GetIndexOpt("BarkSound");
                int? AttackSoundIndex = GetIndexOpt("AttackSound");
                int? ParrySoundsIndex = GetIndexOpt("ParrySounds");
                int? CanOpenDoorsIndex = GetIndexOpt("CanOpenDoors");
                int? LootTableKeyIndex = GetIndexOpt("LootTableKey");
                int? MapChanceIndex = GetIndexOpt("MapChance");
                int? SpellBookIndex = GetIndexOpt("Spellbook");
                int? OnHitIndex = GetIndexOpt("OnHit");

                CultureInfo cultureInfo = new CultureInfo("en-US");
                int lineNumber = 1;
                while (stream.Peek() >= 0)
                {
                    ++lineNumber;

                    string line = stream.ReadLine();

                    try
                    {
                        string[] tokens = SplitCsvLine(line);

                        MobileEnemy mobile = new MobileEnemy();

                        mobile.ID = int.Parse(tokens[IdIndex]);
                        mobile.Behaviour = (BehaviourIndex.HasValue && !string.IsNullOrEmpty(tokens[BehaviourIndex.Value]))
                            ? (MobileBehaviour)Enum.Parse(typeof(MobileBehaviour), tokens[BehaviourIndex.Value], ignoreCase: true)
                            : MobileBehaviour.General;
                        mobile.Affinity = (AffinityIndex.HasValue && !string.IsNullOrEmpty(tokens[AffinityIndex.Value]))
                            ? (MobileAffinity)Enum.Parse(typeof(MobileAffinity), tokens[AffinityIndex.Value], ignoreCase: true)
                            : MobileAffinity.None;
                        mobile.Team = (MobileTeams)Enum.Parse(typeof(MobileTeams), tokens[TeamIndex], ignoreCase: true);
                        mobile.MaleTexture = int.Parse(tokens[MaleTextureIndex]);
                        mobile.FemaleTexture = int.Parse(tokens[FemaleTextureIndex]);
                        int CorpseArchive = int.Parse(tokens[CorpseTextureArchiveIndex]);
                        int CorpseRecord = int.Parse(tokens[CorpseTextureRecordIndex]);
                        mobile.CorpseTexture = EnemyBasics.CorpseTexture(CorpseArchive, CorpseRecord);
                        mobile.HasIdle = ParseBool(tokens[HasIdleIndex], $"line={lineNumber}, column={HasIdleIndex}");
                        mobile.CastsMagic = ParseBool(tokens[CastsMagicIndex], $"line={lineNumber}, column={CastsMagicIndex}");
                        mobile.HasRangedAttack1 = ParseBool(tokens[HasRangedAttackIndex], $"line={lineNumber}, column={HasRangedAttackIndex}");

                        if(mobile.HasRangedAttack1)
                        {
                            mobile.RangedAttackAnimFrames = new int[] { 3, 2, 0, 0, 0, -1, 1, 1, 2, 3 };
                        }

                        mobile.PrimaryAttackAnimFrames = ParseArrayArg(tokens[PrimaryAttackAnimFramesIndex], $"line={lineNumber}, column={PrimaryAttackAnimFramesIndex}");

                        if(mobile.CastsMagic)
                        {
                            if(mobile.HasRangedAttack1)
                            {
                                // We have both ranged and casting
                                mobile.HasRangedAttack2 = true;
                            }
                            else
                            {
                                // Casting is our only ranged attack
                                mobile.HasRangedAttack1 = true;
                            }
                            mobile.SpellAnimFrames = new int[] { 0, 1, 2, 3, 3 };
                        }

                        if(LevelIndex.HasValue && !string.IsNullOrEmpty(tokens[LevelIndex.Value]))
                        {
                            mobile.Level = int.Parse(tokens[LevelIndex.Value]);
                        }
                        else if(IsMonster(mobile.ID))
                        {
                            Debug.LogWarning($"Monster '{mobile.ID}' did not have a level specified. Defaulting to 1");
                            mobile.Level = 1;
                        }

                        if(MinDamageIndex.HasValue && !string.IsNullOrEmpty(tokens[MinDamageIndex.Value]))
                        {
                            mobile.MinDamage = int.Parse(tokens[MinDamageIndex.Value]);
                        }
                        else if(IsMonster(mobile.ID))
                        {
                            Debug.LogWarning($"Monster '{mobile.ID}' did not have a min damage specified. Defaulting to 1");
                            mobile.MinDamage = 1;
                        }

                        if(MaxDamageIndex.HasValue && !string.IsNullOrEmpty(tokens[MaxDamageIndex.Value]))
                        {
                            mobile.MaxDamage = int.Parse(tokens[MaxDamageIndex.Value]);
                        }
                        else if (IsMonster(mobile.ID))
                        {
                            Debug.LogWarning($"Monster '{mobile.ID}' did not have a max damage specified. Defaulting to {mobile.MinDamage + 1}");
                            mobile.MaxDamage = mobile.MinDamage + 1;
                        }

                        if (MinHealthIndex.HasValue && !string.IsNullOrEmpty(tokens[MinHealthIndex.Value]))
                        {
                            mobile.MinHealth = int.Parse(tokens[MinHealthIndex.Value]);
                        }
                        else if (IsMonster(mobile.ID))
                        {
                            Debug.LogWarning($"Monster '{mobile.ID}' did not have a min health specified. Defaulting to 1");
                            mobile.MinHealth = 1;
                        }

                        if (MaxHealthIndex.HasValue && !string.IsNullOrEmpty(tokens[MaxHealthIndex.Value]))
                        {
                            mobile.MaxHealth = int.Parse(tokens[MaxHealthIndex.Value]);
                        }
                        else if (IsMonster(mobile.ID))
                        {
                            Debug.LogWarning($"Monster '{mobile.ID}' did not have a max health specified. Defaulting to {mobile.MinHealth + 1}");
                            mobile.MaxHealth = mobile.MinHealth + 1;
                        }

                        if (ArmorValueIndex.HasValue && !string.IsNullOrEmpty(tokens[ArmorValueIndex.Value]))
                        {
                            mobile.ArmorValue = int.Parse(tokens[ArmorValueIndex.Value]);
                        }
                        else if (IsMonster(mobile.ID))
                        {
                            Debug.LogWarning($"Monster '{mobile.ID}' did not have an armor value specified. Defaulting to 0");
                            mobile.ArmorValue = 0;
                        }

                        if (MinMetalToHitIndex.HasValue && !string.IsNullOrEmpty(tokens[MinMetalToHitIndex.Value]))
                        {
                            mobile.MinMetalToHit = (WeaponMaterialTypes)Enum.Parse(typeof(WeaponMaterialTypes), tokens[MinMetalToHitIndex.Value], ignoreCase: true);
                        }
                        else
                        {
                            mobile.MinMetalToHit = WeaponMaterialTypes.None;
                        }

                        if (WeightIndex.HasValue && !string.IsNullOrEmpty(tokens[WeightIndex.Value]))
                        {
                            mobile.Weight = int.Parse(tokens[WeightIndex.Value]);
                        }
                        else if (IsMonster(mobile.ID))
                        {
                            Debug.LogWarning($"Monster '{mobile.ID}' did not have a weight specified. Defaulting to 100");
                            mobile.Weight = 100;
                        }

                        if (SeesThroughInvisibilityIndex.HasValue && !string.IsNullOrEmpty(tokens[SeesThroughInvisibilityIndex.Value]))
                        {
                            mobile.SeesThroughInvisibility = ParseBool(tokens[SeesThroughInvisibilityIndex.Value], $"line={lineNumber},column={SeesThroughInvisibilityIndex.Value}");
                        }

                        if(MoveSoundIndex.HasValue && !string.IsNullOrEmpty(tokens[MoveSoundIndex.Value]))
                        {
                            mobile.MoveSound = int.Parse(tokens[MoveSoundIndex.Value]);
                        }
                        else
                        {
                            Debug.LogWarning($"Monster '{mobile.ID}' has no move sound");
                            mobile.MoveSound = -1;
                        }

                        if (BarkSoundIndex.HasValue && !string.IsNullOrEmpty(tokens[BarkSoundIndex.Value]))
                        {
                            mobile.BarkSound = int.Parse(tokens[BarkSoundIndex.Value]);
                        }
                        else
                        {
                            Debug.LogWarning($"Monster '{mobile.ID}' has no bark sound");
                            mobile.BarkSound = -1;
                        }

                        if (AttackSoundIndex.HasValue && !string.IsNullOrEmpty(tokens[AttackSoundIndex.Value]))
                        {
                            mobile.AttackSound = int.Parse(tokens[AttackSoundIndex.Value]);
                        }
                        else
                        {
                            Debug.LogWarning($"Monster '{mobile.ID}' has no attack sound");
                            mobile.AttackSound = -1;
                        }

                        if (ParrySoundsIndex.HasValue && !string.IsNullOrEmpty(tokens[ParrySoundsIndex.Value]))
                        {
                            mobile.ParrySounds = ParseBool(tokens[ParrySoundsIndex.Value], $"line={lineNumber},column={ParrySoundsIndex.Value}");
                        }

                        if (CanOpenDoorsIndex.HasValue && !string.IsNullOrEmpty(tokens[CanOpenDoorsIndex.Value]))
                        {
                            mobile.CanOpenDoors = ParseBool(tokens[CanOpenDoorsIndex.Value], $"line={lineNumber},column={CanOpenDoorsIndex.Value}");
                        }

                        if(LootTableKeyIndex.HasValue && !string.IsNullOrEmpty(tokens[LootTableKeyIndex.Value]))
                        {
                            mobile.LootTableKey = tokens[LootTableKeyIndex.Value];
                        }

                        if (MapChanceIndex.HasValue && !string.IsNullOrEmpty(tokens[MapChanceIndex.Value]))
                        {
                            mobile.MapChance = int.Parse(tokens[MapChanceIndex.Value]);
                        }

                        if (customEnemies.ContainsKey(mobile.ID))
                            continue;

                        CustomEnemy customEnemy = new CustomEnemy();
                        customEnemy.mobileEnemy = mobile;
                        customEnemy.name = tokens[NameIndex];
                        customEnemy.career = tokens[CareerIndex];

                        if(SpellBookIndex.HasValue)
                        {
                            customEnemy.spellbook = ParseArrayArg(tokens[SpellBookIndex.Value], $"line={lineNumber}, column={SpellBookIndex.Value}");
                        }

                        if(OnHitIndex.HasValue && !string.IsNullOrEmpty(tokens[OnHitIndex.Value]))
                        {
                            customEnemy.onHitEffect = int.Parse(tokens[OnHitIndex.Value]);
                        }

                        if(!customCareers.TryGetValue(customEnemy.career, out CustomCareer customCareer))
                        {
                            Debug.LogError($"Monster '{mobile.ID}' has unknown career '{customEnemy.career}'");
                            continue;
                        }

                        customEnemies.Add(mobile.ID, customEnemy);

                        enemies.Add(mobile);
                        DaggerfallEntity.RegisterCustomCareerTemplate(mobile.ID, customCareer.dfCareer);
                        questEnemyLines.Add($"{mobile.ID}, {customEnemy.name.Replace(' ', '_')}");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Error while parsing {asset.name}:{lineNumber}: {ex}");
                    }
                }
            }

            EnemyBasics.Enemies = enemies.ToArray();
            QuestMachine.Instance.FoesTable.AddIntoTable(questEnemyLines.ToArray());
        }

        void OnEnemySpawn(object source, EnemyLootSpawnedEventArgs args)
        {
            var enemyEntity = source as EnemyEntity;
            if (enemyEntity == null)
                return;

            if (!customEnemies.TryGetValue(args.MobileEnemy.ID, out CustomEnemy customEnemy))
                return;

            if (customEnemy.spellbook == null || customEnemy.spellbook.Length == 0)
                return;

            // Reset spells, just in case
            while (enemyEntity.SpellbookCount() > 0)
                enemyEntity.DeleteSpell(enemyEntity.SpellbookCount() - 1);

            byte[] spellIndices = customEnemy.spellbook.Select(id => (byte)id).ToArray();
            enemyEntity.SetEnemySpells(spellIndices);
        }

        public void OnMonsterHit(EnemyEntity attacker, DaggerfallEntity target, int damage)
        {
            Diseases[] diseaseListA = { Diseases.Plague };
            Diseases[] diseaseListB = { Diseases.Plague, Diseases.StomachRot, Diseases.BrainFever };
            Diseases[] diseaseListC =
            {
                Diseases.Plague, Diseases.YellowFever, Diseases.StomachRot, Diseases.Consumption,
                Diseases.BrainFever, Diseases.SwampRot, Diseases.Cholera, Diseases.Leprosy, Diseases.RedDeath,
                Diseases.TyphoidFever, Diseases.Dementia
            };

            int customEffect = 0;
            if(customEnemies.TryGetValue(attacker.MobileEnemy.ID, out CustomEnemy customEnemy))
            {
                customEffect = customEnemy.onHitEffect;
            }

            float random;
            if(attacker.MobileEnemy.ID == (int)MonsterCareers.Rat || customEffect == 1)
			{
                // In classic rat can only give plague (diseaseListA), but DF Chronicles says plague, stomach rot and brain fever (diseaseListB).
                // Don't know which was intended. Using B since it has more variety.
                if (Dice100.SuccessRoll(5))
                    FormulaHelper.InflictDisease(attacker, target, diseaseListB);
            }
			else if(attacker.MobileEnemy.ID == (int)MonsterCareers.GiantBat || customEffect == 2)
			{
                // Classic uses 2% chance, but DF Chronicles says 5% chance. Not sure which was intended.
                if (Dice100.SuccessRoll(2))
                    FormulaHelper.InflictDisease(attacker, target, diseaseListB);
            }
			else if(attacker.MobileEnemy.ID == (int)MonsterCareers.Spider
                || attacker.MobileEnemy.ID == (int)MonsterCareers.GiantScorpion
                || customEffect == 3)
			{
                EntityEffectManager targetEffectManager = target.EntityBehaviour.GetComponent<EntityEffectManager>();
                if (targetEffectManager.FindIncumbentEffect<Paralyze>() == null)
                {
                    SpellRecord.SpellRecordData spellData;
                    GameManager.Instance.EntityEffectBroker.GetClassicSpellRecord(66, out spellData);
                    EffectBundleSettings bundle;
                    GameManager.Instance.EntityEffectBroker.ClassicSpellRecordDataToEffectBundleSettings(spellData, BundleTypes.Spell, out bundle);
                    EntityEffectBundle spell = new EntityEffectBundle(bundle, attacker.EntityBehaviour);
                    EntityEffectManager attackerEffectManager = attacker.EntityBehaviour.GetComponent<EntityEffectManager>();
                    attackerEffectManager.SetReadySpell(spell, true);
                }
            }
			else if(attacker.MobileEnemy.ID == (int)MonsterCareers.Werewolf
                || customEffect == 4)
			{
                random = UnityEngine.Random.Range(0f, 100f);
                if (random <= FormulaHelper.specialInfectionChance && target.EntityBehaviour.EntityType == EntityTypes.Player)
                {
                    // Werewolf
                    EntityEffectBundle bundle = GameManager.Instance.PlayerEffectManager.CreateLycanthropyDisease(LycanthropyTypes.Werewolf);
                    GameManager.Instance.PlayerEffectManager.AssignBundle(bundle, AssignBundleFlags.SpecialInfection);
                }
            }
			else if(attacker.MobileEnemy.ID == (int)MonsterCareers.Nymph
                || attacker.MobileEnemy.ID == (int)MonsterCareers.Lamia
                || customEffect == 5)
			{
                FormulaHelper.FatigueDamage(attacker, target, damage);
            }
			else if(attacker.MobileEnemy.ID == (int)MonsterCareers.Wereboar
                || customEffect == 6)
			{
                random = UnityEngine.Random.Range(0f, 100f);
                if (random <= FormulaHelper.specialInfectionChance && target.EntityBehaviour.EntityType == EntityTypes.Player)
                {
                    // Wereboar
                    EntityEffectBundle bundle = GameManager.Instance.PlayerEffectManager.CreateLycanthropyDisease(LycanthropyTypes.Wereboar);
                    GameManager.Instance.PlayerEffectManager.AssignBundle(bundle, AssignBundleFlags.SpecialInfection);
                }
            }
			else if(attacker.MobileEnemy.ID == (int)MonsterCareers.Zombie
                || customEffect == 7)
			{
                // Nothing in classic. DF Chronicles says 2% chance of disease, which seems like it was probably intended.
                // Diseases listed in DF Chronicles match those of mummy (except missing cholera, probably a mistake)
                if (Dice100.SuccessRoll(2))
                    FormulaHelper.InflictDisease(attacker, target, diseaseListC);
            }
			else if(attacker.MobileEnemy.ID == (int)MonsterCareers.Mummy
                || customEffect == 8)
			{
                if (Dice100.SuccessRoll(5))
                    FormulaHelper.InflictDisease(attacker, target, diseaseListC);
            }
			else if(attacker.MobileEnemy.ID == (int)MonsterCareers.Vampire
                || attacker.MobileEnemy.ID == (int)MonsterCareers.VampireAncient
                || customEffect == 9)
			{
                random = UnityEngine.Random.Range(0f, 100f);
                if (random <= FormulaHelper.specialInfectionChance && target.EntityBehaviour.EntityType == EntityTypes.Player)
                {
                    // Inflict stage one vampirism disease
                    EntityEffectBundle bundle = GameManager.Instance.PlayerEffectManager.CreateVampirismDisease();
                    GameManager.Instance.PlayerEffectManager.AssignBundle(bundle, AssignBundleFlags.SpecialInfection);
                }
                else if (random <= 2.0f)
                {
                    FormulaHelper.InflictDisease(attacker, target, diseaseListA);
                }
            }
        }
    }
}
