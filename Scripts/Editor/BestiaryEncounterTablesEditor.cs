using DaggerfallWorkshop.Game.Utility.ModSupport;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using System.Globalization;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Utility;

namespace DaggerfallBestiaryProject
{
    internal class BestiaryEncounterTablesEditor : EditorWindow
    {
        static BestiaryEncounterTablesEditor instance;

        string workingMod;
        string activeFile;
        string activeEncounterTable;

        class EncounterTable
        {
            public string Name;
            public int[] TableIndices;
            public int[] EnemyIds;
        }

        EncounterTable[] activeFileEncounterTables;

        class CustomEnemy
        {
            public int Id;
            public string Name;
            public MobileTeams Team;
            public int Level;
        }
        Dictionary<int, CustomEnemy> customEnemyDb = new Dictionary<int, CustomEnemy>();

        Dictionary<MobileTeams, int> currentTableTeamCounts;

        
        [MenuItem("Daggerfall Tools/Encounter Tables Editor")]
        static void Init()
        {
            instance = (BestiaryEncounterTablesEditor)GetWindow(typeof(BestiaryEncounterTablesEditor));
            instance.titleContent = new GUIContent("Encounter Tables Editor");
        }

        private void OnEnable()
        {
            workingMod = null;
            activeFile = null;
            activeEncounterTable = null;
            activeFileEncounterTables = null;
        }

        void OnGUI()
        {
            float baseX = 0;
            float baseY = 0;
            float availableWidth = 0; 

            GUI.Label(new Rect(baseX + 4, baseY + 4, 124, 16), "Active Mod: ");
            baseX += 128;

            if (EditorGUI.DropdownButton(new Rect(baseX + 4, baseY + 4, 160, 16), new GUIContent(workingMod), FocusType.Passive))
            {
                void OnItemClicked(object mod)
                {
                    workingMod = (string)mod;
                    activeFile = null;
                    activeEncounterTable = null;
                    currentTableTeamCounts = null;
                    LoadCustomEnemies();
                }

                GenericMenu menu = new GenericMenu();
                foreach (string mod in BestiaryModManager.GetDevMods())
                {
                    menu.AddItem(new GUIContent(mod), workingMod == mod, OnItemClicked, mod);
                }

                menu.DropDown(new Rect(92, baseY + 8, 160, 16));
            }

            baseX += 164;

            availableWidth = position.width - baseX;

            if(GUI.Button(new Rect(baseX + availableWidth - 128, baseY + 4, 124, 16), "Refresh"))
            {
                RefreshFile();
            }

            baseX = 0;
            baseY += 20;

            using(new EditorGUI.DisabledScope(string.IsNullOrEmpty(workingMod)))
            {
                GUI.Label(new Rect(baseX + 4, baseY + 4, 124, 16), "Active File: ");
                baseX += 128;

                if (EditorGUI.DropdownButton(new Rect(baseX + 4, baseY + 4, 160, 16), new GUIContent(GetEncounterTableName(activeFile)), FocusType.Passive))
                {
                    void OnItemClicked(object file)
                    {
                        activeFile = (string)file;
                        activeEncounterTable = null;
                        currentTableTeamCounts = null;
                        LoadActiveFile();
                    }

                    GenericMenu menu = new GenericMenu();

                    ModInfo workingModInfo = BestiaryModManager.GetModInfo(workingMod);
                    var encounterTableFiles = workingModInfo.Files.Where(file => file.EndsWith(".tdb.csv"));
                    foreach (string file in encounterTableFiles)
                    {
                        menu.AddItem(new GUIContent(GetEncounterTableName(file)), activeFile == file, OnItemClicked, file);
                    }

                    menu.DropDown(new Rect(92, baseY + 8, 160, 16));
                }

                baseX = 0;
                baseY += 20;

                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(activeFile)))
                {
                    GUI.Label(new Rect(baseX + 4, baseY + 4, 124, 16), "Encounter Table: ");
                    baseX += 128;

                    if (EditorGUI.DropdownButton(new Rect(baseX + 4, baseY + 4, 160, 16), new GUIContent(activeEncounterTable), FocusType.Passive))
                    {
                        void OnItemClicked(object tableName)
                        {
                            activeEncounterTable = (string)tableName;
                            LoadTableStatistics();
                        }

                        GenericMenu menu = new GenericMenu();

                        foreach (EncounterTable table in activeFileEncounterTables)
                        {
                            menu.AddItem(new GUIContent(table.Name), activeEncounterTable == table.Name, OnItemClicked, table.Name);
                        }

                        menu.DropDown(new Rect(92, baseY + 8, 160, 16));
                    }

                    baseX = 0;
                    baseY += 20;
                }

                if(!string.IsNullOrEmpty(activeEncounterTable))
                {
                    baseY += 24; // Spacing

                    EncounterTable tableData = activeFileEncounterTables.First(table => table.Name == activeEncounterTable);

                    GUI.Label(new Rect(baseX + 4, baseY + 4, 84, 16), "Tables: ");

                    for(int i = 0; i < tableData.TableIndices.Length; ++i)
                    {
                        int tableIndex = tableData.TableIndices[i];
                        GUI.Label(new Rect(baseX + 8, baseY + 24 + i * 24, 148, 20), TableIndexToName(tableIndex));
                    }

                    baseX += 160;

                    GUI.Label(new Rect(baseX + 4, baseY + 4, 84, 16), "Enemies: ");

                    for(int i = 0; i < tableData.EnemyIds.Length; ++i)
                    {
                        int enemyId = tableData.EnemyIds[i];

                        string enemyName;
                        if (Enum.IsDefined(typeof(MobileTypes), enemyId))
                        {
                            enemyName = ((MobileTypes)enemyId).ToString();
                        }
                        else if (customEnemyDb.TryGetValue(enemyId, out CustomEnemy enemy))
                        {
                            enemyName = enemy.Name;
                        }
                        else
                        {
                            enemyName = $"Unknown id '{enemyId}'";
                        }

                        GUI.Label(new Rect(baseX + 8, baseY + 24 + i * 24, 204, 20), $"{i+1:D2}. ({enemyId:D3}) {enemyName}");
                    }

                    baseX += 212;

                    

                    GUI.Label(new Rect(baseX + 4, baseY + 4, 84, 16), "Level: ");

                    for (int i = 0; i < tableData.EnemyIds.Length; ++i)
                    {
                        int enemyId = tableData.EnemyIds[i];

                        int enemyLevel;
                        if (Enum.IsDefined(typeof(MobileTypes), enemyId))
                        {
                            enemyLevel = EnemyBasics.Enemies.First(m => m.ID == enemyId).Level;
                        }
                        else if (customEnemyDb.TryGetValue(enemyId, out CustomEnemy enemy))
                        {
                            enemyLevel = enemy.Level;
                        }
                        else
                        {
                            enemyLevel = 0;
                        }

                        if (enemyLevel != 0)
                        {
                            GUI.Label(new Rect(baseX + 8, baseY + 24 + i * 24, 148, 20), enemyLevel.ToString());
                        }
                    }

                    baseX += 160;

                    if (currentTableTeamCounts != null && currentTableTeamCounts.Count > 0)
                    {
                        GUI.Label(new Rect(baseX + 4, baseY + 4, 84, 16), "Teams: ");

                        var teamCountEntries = currentTableTeamCounts.ToList();
                        teamCountEntries.Sort((tc1, tc2) => tc2.Value.CompareTo(tc1.Value));

                        for(int i = 0; i < teamCountEntries.Count; ++i)
                        {
                            var tableCountPair = teamCountEntries[i];
                            var team = tableCountPair.Key;
                            var count = tableCountPair.Value;

                            GUI.Label(new Rect(baseX + 8, baseY + 24 + i * 24, 148, 20), $"{team}: {count}");
                        }
                    }
                }
            }
        }

        string GetEncounterTableName(string tablePath)
        {
            if (string.IsNullOrEmpty(tablePath))
                return string.Empty;

            return Path.GetFileNameWithoutExtension(tablePath).Replace(".tdb", "");
        }

        string TableIndexToName(int tableIndex)
        {
            if(tableIndex < 19)
            {
                return ((DFRegion.DungeonTypes)tableIndex).ToString();
            }

            switch(tableIndex)
            {
                case 19: return "Underwater";
                case 20: return "Desert City Night";
                case 21: return "Desert Day";
                case 22: return "Desert Night";
                case 23: return "Mountain City Night";
                case 24: return "Mountain Day";
                case 25: return "Mountain Night";
                case 26: return "Rainforest City Night";
                case 27: return "Rainforest Day";
                case 28: return "Rainforest Night";
                case 29: return "Subtropical City Night";
                case 30: return "Subtropical Day";
                case 31: return "Subtropical Night";
                case 32: return "Woodlands City Night";
                case 33: return "Woodlands Day";
                case 34: return "Woodlands Night";
                case 35: return "Haunted City Night";
                case 36: return "Haunted Day";
                case 37: return "Haunted Night";
            }

            throw new Exception($"Could not find table name for index '{tableIndex}'");
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
            if (Arg[0] == '[' || Arg[0] == '{')
            {
                // Check for end bracket
                if (Arg[0] == '[' && Arg[Arg.Length - 1] != ']'
                    || Arg[0] == '{' && Arg[Arg.Length - 1] != '}')
                    throw new InvalidDataException($"Error parsing ({Context}): array argument has mismatched brackets");

                Arg = Arg.Substring(1, Arg.Length - 2);
            }

            string[] Frames = Arg.Split(',', ';');
            return Frames.Select(Frame => string.IsNullOrEmpty(Frame) ? "-1" : Frame).Select(int.Parse).ToArray();
        }

        void LoadCustomEnemies()
        {
            foreach (TextAsset asset in BestiaryModManager.FindAssets<TextAsset>(workingMod, "*.mdb.csv"))
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
                int? LevelIndex = GetIndexOpt("Level");
                int? TeamIndex = GetIndexOpt("Team");

                CultureInfo cultureInfo = new CultureInfo("en-US");
                int lineNumber = 1;
                while (stream.Peek() >= 0)
                {
                    ++lineNumber;

                    string line = stream.ReadLine();

                    try
                    {
                        string[] tokens = SplitCsvLine(line);

                        CustomEnemy enemy = new CustomEnemy();

                        int mobileID = int.Parse(tokens[IdIndex]);

                        enemy.Id = mobileID;
                        enemy.Name = tokens[NameIndex];

                        if(TeamIndex.HasValue && !string.IsNullOrEmpty(tokens[TeamIndex.Value]))
                        {
                            enemy.Team = (MobileTeams)Enum.Parse(typeof(MobileTeams), tokens[TeamIndex.Value]);
                        }

                        if(LevelIndex.HasValue && !string.IsNullOrEmpty(tokens[LevelIndex.Value]))
                        {
                            enemy.Level = int.Parse(tokens[LevelIndex.Value]);
                        }

                        customEnemyDb.Add(mobileID, enemy);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Error while parsing {asset.name}:{lineNumber}: {ex}");
                    }
                }
            }
        }

        void LoadActiveFile()
        {
            TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(activeFile);
            if(asset == null)
            {
                activeFile = null;
                return;
            }

            TextReader stream = new StringReader(asset.text);

            List<EncounterTable> encounterTables = new List<EncounterTable>();

            string header = stream.ReadLine();

            string[] fields = header.Split(';', ',');

            if (fields.Length != 22)
            {
                Debug.LogError($"Error while parsing {asset.name}: table database has invalid format (expected 22 columns)");
                return;
            }

            CultureInfo cultureInfo = new CultureInfo("en-US");
            int lineNumber = 1;
            while (stream.Peek() >= 0)
            {
                ++lineNumber;

                string line = stream.ReadLine();
                try
                {
                    string[] tokens = SplitCsvLine(line);
                    if (tokens.Length != 22)
                    {
                        Debug.LogError($"Error while parsing {asset.name}:{lineNumber}: table database line has invalid format (expected 22 columns)");
                        break;
                    }

                    EncounterTable table = new EncounterTable();
                    table.Name = tokens[0];
                    table.TableIndices = ParseArrayArg(tokens[1], $"line={lineNumber}, column=2");
                    table.EnemyIds = tokens.Skip(2).Select(id => int.Parse(id)).ToArray();

                    encounterTables.Add(table);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error while parsing {asset.name}:{lineNumber}: {ex}");
                }
            }

            activeFileEncounterTables = encounterTables.ToArray();
        }

        void RefreshFile()
        {
            int activeEncounterTableIndex = -1;
            if (!string.IsNullOrEmpty(activeEncounterTable))
                activeEncounterTableIndex = Array.FindIndex(activeFileEncounterTables, t => t.Name == activeEncounterTable);

            LoadActiveFile();

            if (activeEncounterTableIndex != -1)
            {
                activeEncounterTable = activeFileEncounterTables[activeEncounterTableIndex].Name;
                LoadTableStatistics();
            }
        }

        void LoadTableStatistics()
        {
            currentTableTeamCounts = new Dictionary<MobileTeams, int>();

            EncounterTable tableData = activeFileEncounterTables.First(table => table.Name == activeEncounterTable);

            foreach(int enemyId in tableData.EnemyIds)
            {
                if (Enum.IsDefined(typeof(MobileTypes), enemyId))
                {
                    MobileEnemy enemy = EnemyBasics.Enemies.First(e => e.ID == enemyId);
                    if(enemy.Team != MobileTeams.PlayerEnemy)
                    {
                        int count;
                        if (!currentTableTeamCounts.TryGetValue(enemy.Team, out count))
                        {
                            count = 0;
                            currentTableTeamCounts.Add(enemy.Team, count);
                        }

                        currentTableTeamCounts[enemy.Team] = count + 1;
                    }
                }
                else if(customEnemyDb.TryGetValue(enemyId, out CustomEnemy enemy))
                {
                    if (enemy.Team != MobileTeams.PlayerEnemy)
                    {
                        int count;
                        if (!currentTableTeamCounts.TryGetValue(enemy.Team, out count))
                        {
                            count = 0;
                            currentTableTeamCounts.Add(enemy.Team, count);
                        }

                        currentTableTeamCounts[enemy.Team] = count + 1;
                    }
                }
            }
        }
    }
}