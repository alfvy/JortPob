using JortPob.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JortPob
{
    /* Loads override json files so we can referenec them */
    /* Static class since everything here is going to be static and readonly */
    public class Override
    {
        private static HashSet<string> DO_NOT_PLACE;
        private static HashSet<string> STATIC_COLLISION;
        private static HashSet<string> ITEMS_TO_SKIP;
        private static HashSet<string> CUSTOM_VOICES;

        private static List<PlayerClass> CHARACTER_CREATION_CLASS;
        private static List<PlayerRace> CHARACTER_CREATION_RACE;
        private static List<Gift> CHARACTER_CREATION_GIFT;
        private static Dictionary<string, FaceData> FACE_REMAP;
        private static Dictionary<string, Hair> HAIR_REMAP;
        private static Dictionary<string, ItemRemap> ITEM_REMAPS_BY_ID;
        private static Dictionary<string, ItemDefinition> ITEM_DEFINITIONS_BY_ID;
        private static Dictionary<string, SpeffDefinition> SPEFF_DEFINITIONS_BY_ID;
        private static Dictionary<string, SpellRemap> SPELL_REMAPS_BY_ID;
        private static List<SkillInfo> SKILL_INFOS;
        private static List<AlchemyInfo> ALCHEMY_INFOS;
        private static List<EnemyRemap> ENEMY_REMAPS;
        private static Dictionary<string, Layout.MapPoint.Icon> MAP_ICONS;
        private static List<LoadingTip> LOADING_TIPS;

        public static bool CheckDoNotPlace(string id)
        {
            return DO_NOT_PLACE.Contains(id.ToLower());
        }

        public static bool CheckStaticCollision(string id)
        {
            return STATIC_COLLISION.Contains(id.ToLower());
        }

        public static bool CheckSkipItem(string id)
        {
            return ITEMS_TO_SKIP.Contains(id.ToLower());
        }

        public static bool CheckCustomVoice(string id)
        {
            return CUSTOM_VOICES.Contains(id.ToLower());
        }

        public static List<PlayerClass> GetCharacterCreationClasses()
        {
            return CHARACTER_CREATION_CLASS;
        }

        public static List<PlayerRace> GetCharacterCreationRaces()
        {
            return CHARACTER_CREATION_RACE;
        }

        public static List<Gift> GetCharacterCreationGifts()
        {
            return CHARACTER_CREATION_GIFT;
        }

        public static ItemRemap GetItemRemap(string id)
        {
            return ITEM_REMAPS_BY_ID.TryGetValue(id, out ItemRemap remapped) ? remapped : null;
        }

        public static ItemDefinition GetItemDefinition(string id)
        {
            return ITEM_DEFINITIONS_BY_ID.TryGetValue(id, out ItemDefinition definition) ? definition : null;
        }

        public static SpeffDefinition GetSpeffDefinition(string id)
        {
            return SPEFF_DEFINITIONS_BY_ID.TryGetValue(id, out SpeffDefinition definition) ? definition : null;
        }

        public static List<SpeffDefinition> GetSpeffDefinitions()
        {
            return SPEFF_DEFINITIONS_BY_ID.Values.ToList();
        }

        public static SpellRemap GetSpellRemap(string id)
        {
            return SPELL_REMAPS_BY_ID.TryGetValue(id, out SpellRemap remap) ? remap : null;
        }

        public static List<SkillInfo> GetSkills()
        {
            return SKILL_INFOS;
        }

        public static List<SkillInfo> GetSkills(CharacterContent.Stats.Tier tier)
        {
            return SKILL_INFOS.Where(skill => skill.tier <= tier).ToList();
        }

        public static List<AlchemyInfo> GetAlchemy()
        {
            return ALCHEMY_INFOS;
        }

        public static EnemyRemap GetEnemyRemap(string id)
        {
            foreach (EnemyRemap remap in ENEMY_REMAPS)
            {
                if (remap.id == id.ToLower().Trim()) { return remap; }
            }
            return new();
        }

        public static List<LoadingTip> GetLoadingTips()
        {
            return LOADING_TIPS;
        }

        public static Layout.MapPoint.Icon GetMapIcon(string name)
        {
            string n = name.ToLower().ToString();
            if(MAP_ICONS.ContainsKey(n)) { return MAP_ICONS[n]; }
            else { return Layout.MapPoint.Icon.Auto; }
        }

        public static Hair GetHair(string name)
        {
            if(HAIR_REMAP.ContainsKey(name.ToLower().Trim())) { return HAIR_REMAP[name.ToLower().Trim()]; }
            else { return new Hair(0, Hair.Color.Black); }  // BALD!
        }

        public static FaceData GetFace(string name)
        {
            if(FACE_REMAP.ContainsKey(name.ToLower().Trim())) { return FACE_REMAP[name.ToLower().Trim()]; }
            else { return FACE_REMAP["default"]; }
        }

        /* load all the override jsons into this class */
        public static void Initialize()
        {
            /* Load do_not_place overrides */
            JsonNode jsonDoNotPlace = JsonNode.Parse(File.ReadAllText(Utility.ResourcePath(@"overrides\do_not_place.json")));
            DO_NOT_PLACE = jsonDoNotPlace != null
                ? jsonDoNotPlace.AsArray().Select(node => node.ToString().ToLower()).ToHashSet()
                : [];

            /* Load static_collision overrides */
            JsonNode jsonStaticCollision = JsonNode.Parse(File.ReadAllText(Utility.ResourcePath(@"overrides\static_collision.json")));
            STATIC_COLLISION = jsonStaticCollision != null
                ? jsonStaticCollision.AsArray().Select(node => node.ToString().ToLower()).ToHashSet()
                : [];

            /* Load items_to_skip overrides */
            JsonNode jsonItemsToSkip = JsonNode.Parse(File.ReadAllText(Utility.ResourcePath(@"overrides\items_to_skip.json")));
            ITEMS_TO_SKIP = jsonItemsToSkip != null
                ? jsonItemsToSkip.AsArray().Select(node => node.ToString().ToLower()).ToHashSet()
                : [];

            /* Load items_to_skip overrides */
            JsonNode jsonCustomVoiceList = JsonNode.Parse(File.ReadAllText(Utility.ResourcePath(@"overrides\custom_voice_list.json")));
            CUSTOM_VOICES = jsonCustomVoiceList != null
                ? jsonCustomVoiceList.AsArray().Select(node => node.ToString().ToLower()).ToHashSet()
                : [];

            /* Load character creation class overrides */
            CHARACTER_CREATION_CLASS = new();
            JsonArray jsonCharCreaClass = JsonNode.Parse(File.ReadAllText(Utility.ResourcePath(@"overrides\character_creation_class.json"))).AsArray();
            foreach(JsonNode node in jsonCharCreaClass)
            {
                PlayerClass pc = new(node);
                CHARACTER_CREATION_CLASS.Add(pc);
            }

            /* Load character creation gift overrides */
            CHARACTER_CREATION_GIFT = new();
            JsonArray jsonCharCreaGift = JsonNode.Parse(File.ReadAllText(Utility.ResourcePath(@"overrides\character_creation_gift.json"))).AsArray();
            foreach (JsonNode node in jsonCharCreaGift)
            {
                Gift gift = new(node);
                CHARACTER_CREATION_GIFT.Add(gift);
            }

            /* Load character creation race overrides */
            CHARACTER_CREATION_RACE = JsonSerializer.Deserialize<List<PlayerRace>>(File.ReadAllText(Utility.ResourcePath(@"overrides\character_creation_race.json")), new JsonSerializerOptions { IncludeFields = true });

            /* Load alchemy recipe information */
            ALCHEMY_INFOS = new();
            JsonNode jsonAlchemyInfo = JsonNode.Parse(File.ReadAllText(Utility.ResourcePath(@"overrides\alchemy.json")));
            foreach (var property in jsonAlchemyInfo.AsObject())
            {
                JsonNode jsonNode = property.Value;
                AlchemyInfo alchy = new(property.Key, jsonNode);
                ALCHEMY_INFOS.Add(alchy);
            }

            /* Load weapon skill information list */
            SKILL_INFOS = new();
            JsonNode jsonSkillInfo = JsonNode.Parse(File.ReadAllText(Utility.ResourcePath(@"overrides\weapon_skill_info.json")));
            foreach(JsonNode jsonNode in jsonSkillInfo.AsArray())
            {
                SkillInfo skillInfo = new(jsonNode);
                SKILL_INFOS.Add(skillInfo);
            }

            /* Load spell remapping list */
            SPELL_REMAPS_BY_ID = new();
            JsonNode jsonSpellRemap = JsonNode.Parse(File.ReadAllText(Utility.ResourcePath(@"overrides\spell_remap.json")));
            foreach (var property in jsonSpellRemap.AsObject())
            {
                JsonNode jsonNode = property.Value;
                SpellRemap sprmo = new(property.Key, jsonNode);
                SPELL_REMAPS_BY_ID.Add(sprmo.id, sprmo);
            }

            /* Load item remapping list */
            ITEM_REMAPS_BY_ID = new();
            string[] itemRemapFiles = Directory.GetFiles(Utility.ResourcePath(@"overrides\items\remap"));
            foreach (string itemRemapFile in itemRemapFiles)
            {
                JsonNode itemRemapJson = JsonNode.Parse(File.ReadAllText(itemRemapFile));
                foreach (var property in itemRemapJson.AsObject())
                {
                    JsonNode jsonNode = property.Value;
                    ItemRemap itrmo = new(property.Key, jsonNode);
                    ITEM_REMAPS_BY_ID.Add(itrmo.id, itrmo);
                }
            }

            /* Load all item definitinos from resources/override/items */
            string[] itemFiles = Directory.GetFiles(Utility.ResourcePath(@"overrides\items"));
            ITEM_DEFINITIONS_BY_ID = new();
            foreach (string itemFile in itemFiles)
            {
                ItemDefinition definition = new(itemFile);
                ITEM_DEFINITIONS_BY_ID.Add(definition.id, definition);
            }

            /* Load all speff definitinos from resources/override/speffs */
            string[] speffFiles = Directory.GetFiles(Utility.ResourcePath(@"overrides\speffs"));
            SPEFF_DEFINITIONS_BY_ID = new();
            foreach (string speffFile in speffFiles)
            {
                SpeffDefinition definition = new(speffFile);
                SPEFF_DEFINITIONS_BY_ID.Add(definition.id, definition);
            }

            /* Load enemy remap list */
            ENEMY_REMAPS = new();
            JsonNode jsonEnemyRemaps = JsonNode.Parse(File.ReadAllText(Utility.ResourcePath(@"overrides\enemy_remap.json")));
            foreach (var property in jsonEnemyRemaps.AsObject())
            {
                JsonNode jsonNode = property.Value;
                EnemyRemap enemyRemap = new(property.Key, jsonNode);
                ENEMY_REMAPS.Add(enemyRemap);
            }

            /* Load map icon overrides */
            MAP_ICONS = new();
            JsonNode jsonMapIcons = JsonNode.Parse(File.ReadAllText(Utility.ResourcePath(@"overrides\map_icons.json")));
            foreach (var property in jsonMapIcons.AsObject())
            {
                string iconName = property.Value.GetValue<string>();
                MAP_ICONS.Add(property.Key.ToLower().Trim(), Enum.Parse<Layout.MapPoint.Icon>(iconName));
            }

            /* Load hair id remap to elden ring hair part ids */
            HAIR_REMAP = new();
            JsonNode jsonHairRemap = JsonNode.Parse(File.ReadAllText(Utility.ResourcePath(@"overrides\face\hair.json")));
            foreach (var property in jsonHairRemap.AsObject())
            {
                JsonNode node = property.Value;

                string id = property.Key.ToLower().Trim();
                byte partId = node["part"].GetValue<byte>();
                string colorName = node["color"].GetValue<string>().Replace(" ", "");
                Hair.Color color = Enum.Parse<Hair.Color>(colorName, true);

                HAIR_REMAP.Add(id, new(partId, color));
            }

            /* Load all json files in overrides/face and stick them in a dictionary for us to grab from */
            FACE_REMAP = new();
            string[] faceRemapFiles = Directory.GetFiles(Utility.ResourcePath(@"overrides\face"));
            foreach (string faceRemapFile in faceRemapFiles)
            {
                string fileName = Path.GetFileNameWithoutExtension(faceRemapFile).ToLower().Trim();
                if (fileName == "hair") { continue; } // skip hair json file

                JsonNode faceRemapJson = JsonNode.Parse(File.ReadAllText(faceRemapFile));
                FaceData faceData = new(fileName, faceRemapJson);
                FACE_REMAP.Add(faceData.id, faceData);
            }

            /* Loading tips overrides */
            LOADING_TIPS = new();
            JsonNode jsonLoadingTips = JsonNode.Parse(File.ReadAllText(Utility.ResourcePath(@"overrides\loading_tips.json")));
            foreach (var property in jsonLoadingTips.AsObject())
            {
                JsonNode jsonNode = property.Value;
                LoadingTip loadingTip = new(property.Key, property.Value.GetValue<string>());
                LOADING_TIPS.Add(loadingTip);
            }
        }

        /* Classes for serializing */
        public class PlayerClass
        {
            public string name, description;
            public readonly Dictionary<string, int> data;

            public PlayerClass(JsonNode json)
            {
                name = json["name"].GetValue<string>();
                description = json["description"].GetValue<string>();

                data = new();
                foreach (var property in json["data"].AsObject())
                {
                    data.Add(property.Key, property.Value.GetValue<int>());
                }
            }
        }

        public class PlayerRace
        {
            public string name, description;
            public byte id;  // this id matches the values of the CharacterContent.Race enums

            public PlayerRace() { }
        }

        public class Gift
        {
            public string name, description;
            public readonly Dictionary<string, int> data;

            public Gift(JsonNode json)
            {
                name = json["name"].GetValue<string>();
                description = json["description"].GetValue<string>();

                data = new();
                foreach (var property in json["data"].AsObject())
                {
                    data.Add(property.Key, property.Value.GetValue<int>());
                }
            }
        }

        public class AlchemyInfo
        {
            public readonly string comment;
            public readonly string id;
            public readonly CharacterContent.Stats.Tier tier;
            public readonly List<string> ingredients;

            public AlchemyInfo(string id, JsonNode json)
            {
                this.id = id;
                comment = json["comment"]?.GetValue<string>();
                tier = (CharacterContent.Stats.Tier)System.Enum.Parse(typeof(CharacterContent.Stats.Tier), json["tier"].GetValue<string>());

                ingredients = new();
                JsonArray jsonArray = json["ingredients"].AsArray();
                for(int i=0;i<jsonArray.Count;i++)
                {
                    ingredients.Add(jsonArray[i].GetValue<string>());
                }
            }
        }

        public class SkillInfo
        {
            public readonly string comment;
            public readonly int row;    // gemparam row
            public readonly CharacterContent.Stats.Tier tier;  // strength and rarity of skill
            public readonly int value;  // value is how much merchants will sell it for

            public readonly ItemText text;

            public SkillInfo(JsonNode json)
            {
                comment = json["comment"]?.GetValue<string>();
                row = json["row"].GetValue<int>();
                tier = (CharacterContent.Stats.Tier)System.Enum.Parse(typeof(CharacterContent.Stats.Tier), json["tier"].GetValue<string>());
                value = json["value"].GetValue<int>();

                if (json["text"] != null)
                {
                    text = new();
                    text.name = json["text"]["name"] != null ? json["text"]["name"].GetValue<string>() : null;
                    text.summary = json["text"]["summary"] != null ? json["text"]["summary"].GetValue<string>() : null;
                    text.description = json["text"]["description"] != null ? json["text"]["description"].GetValue<string>() : null;
                }
                else
                {
                    text = null;
                }
            }

            public bool HasTextChanges()
            {
                return text != null && (text.name != null || text.summary != null || text.description != null || text.effect != null);
            }
        }

        public class SpellRemap
        {
            public readonly string id, comment;
            public readonly int row;  // both the GoodsParam for the spell item and the MagicParam for the actual spell share the same row so we good

            public readonly ItemText text;

            public SpellRemap(string id, JsonNode json)
            {
                this.id = id;
                row = json["row"].GetValue<int>();
                comment = json["comment"]?.GetValue<string>();

                if (json["text"] != null)
                {
                    text = new();
                    text.name = json["text"]["name"] != null ? json["text"]["name"].GetValue<string>() : null;
                    text.summary = json["text"]["summary"] != null ? json["text"]["summary"].GetValue<string>() : null;
                    text.description = json["text"]["description"] != null ? json["text"]["description"].GetValue<string>() : null;
                }
                else
                {
                    text = null;
                }
            }

            public bool HasTextChanges()
            {
                return text != null && (text.name != null || text.summary != null || text.description != null || text.effect != null);
            }
        }

        public class ItemRemap
        {
            public readonly string id, comment;
            public readonly ItemManager.Type type;
            public readonly int row;

            // these 3 fields are only used for the CustomWeapon type
            public readonly ItemManager.Infusion infusion;
            public readonly int skill, upgrade;

            public readonly ItemText text;

            public ItemRemap(string id, JsonNode json)
            {
                this.id = id;
                type = (ItemManager.Type)System.Enum.Parse(typeof(ItemManager.Type), json["type"].GetValue<string>());
                row = json["row"].GetValue<int>();
                comment = json["comment"]?.GetValue<string>();

                if (json["infusion"] != null)
                {
                    infusion = (ItemManager.Infusion)System.Enum.Parse(typeof(ItemManager.Infusion), json["infusion"].GetValue<string>());
                }
                else
                {
                    infusion = ItemManager.Infusion.None;
                }

                skill = json["skill"] != null ? json["skill"].GetValue<int>() : -1;
                upgrade = json["upgrade"] != null ? json["upgrade"].GetValue<int>() : 0;

                if (json["text"] != null)
                {
                    text = new();
                    text.name = json["text"]["name"] != null ? json["text"]["name"].GetValue<string>() : null;
                    text.summary = json["text"]["summary"] != null ? json["text"]["summary"].GetValue<string>() : null;
                    text.description = json["text"]["description"] != null ? json["text"]["description"].GetValue<string>() : null;
                    text.effect = json["text"]["effect"] != null ? json["text"]["effect"].GetValue<string>() : null;
                    text.enchant = json["text"]["enchant"] != null ? json["text"]["enchant"].AsArray().Select(node => node.GetValue<string>()).ToArray() : null;
                }
                else
                {
                    text = null;
                }
            }

            public bool HasTextChanges()
            {
                return text != null && (text.name != null || text.summary != null || text.description != null || text.effect != null);
            }
        }

        public class ItemText
        {
            public string name, summary, description, effect;
            public string[] enchant;

            public ItemText() { }
        }

        public class SpeffDefinition
        {
            public readonly string id, comment;
            public readonly int row;

            public readonly SpeffManager.Speff.Effect.MagicEffect icon; // buff icon to use. 'None' is valid

            public readonly Dictionary<string, string> data;

            public SpeffDefinition(string jsonPath)
            {
                id = Path.GetFileNameWithoutExtension(jsonPath);

                JsonNode json = JsonNode.Parse(File.ReadAllText(jsonPath));

                comment = json["comment"]?.GetValue<string>();
                row = json["row"].GetValue<int>();

                if (json["icon"] != null && json["icon"].GetValue<string>().Trim().ToLower() != "none")
                {
                    icon = (SpeffManager.Speff.Effect.MagicEffect)System.Enum.Parse(typeof(SpeffManager.Speff.Effect.MagicEffect), json["icon"].GetValue<string>());
                }
                else
                {
                    icon = SpeffManager.Speff.Effect.MagicEffect.None;
                }

                data = new();
                foreach (var property in json["data"].AsObject())
                {
                    data.Add(property.Key, property.Value.ToString());
                }
            }
        }

        public class ItemDefinition
        {
            public readonly string id, comment;
            public readonly ItemManager.Type type;
            public readonly int row;                   // row we copy as our base

            // these 3 fields are only used for the CustomWeapon type
            public readonly ItemManager.Infusion infusion;
            public readonly int skill, upgrade;

            public readonly bool useIcon; // use morrowind item icon if true, otherwise use whatever is set in the param

            public readonly ItemText text;
            public readonly Dictionary<string, string> data;

            public ItemDefinition(string jsonPath)
            {
                id = Path.GetFileNameWithoutExtension(jsonPath);

                JsonNode json = JsonNode.Parse(File.ReadAllText(jsonPath));

                comment = json["comment"]?.GetValue<string>();
                type = Enum.Parse<ItemManager.Type>(json["type"].GetValue<string>());
                row = json["row"].GetValue<int>();

                if (json["infusion"] != null)
                {
                    infusion = Enum.Parse<ItemManager.Infusion>(json["infusion"].GetValue<string>());
                }
                else
                {
                    infusion = ItemManager.Infusion.None;
                }

                skill = json["skill"] != null ? json["skill"].GetValue<int>() : -1;
                upgrade = json["upgrade"] != null ? json["upgrade"].GetValue<int>() : 0;

                useIcon = json["useIcon"] != null ? json["useIcon"].GetValue<bool>() : false;

                text = new();
                text.name = json["text"]["name"] != null ? json["text"]["name"].GetValue<string>() : null;
                text.summary = json["text"]["summary"] != null ? json["text"]["summary"].GetValue<string>() : null;
                text.description = json["text"]["description"] != null ? json["text"]["description"].GetValue<string>() : null;
                text.effect = json["text"]["effect"] != null ? json["text"]["effect"].GetValue<string>() : null;
                text.enchant = json["text"]["enchant"] != null ? json["text"]["enchant"].AsArray().Select(node => node.GetValue<string>()).ToArray() : null;

                data = new();
                foreach(var property in json["data"].AsObject())
                {
                    data.Add(property.Key, property.Value.ToString());
                }
            }
        }

        public class EnemyRemap
        {
            public readonly string id, comment, character;
            public readonly EnemyRemapData npc, think;

            public EnemyRemap(string id, JsonNode json)
            {
                this.id = id.ToLower().Trim();
                character = json["character"]?.GetValue<string>();
                comment = json["comment"]?.GetValue<string>();

                npc = new(json["npc"]);
                think = new(json["think"]);
            }

            /* Default constructor, points to a Goat */
            public EnemyRemap()
            {
                id = "DEFAULT";
                character = "c6060";
                comment = "Default constructor, used when no remap found. Creates a goat.";

                npc = new(60600010);
                think = new(60600000);
            }

            public class EnemyRemapData
            {
                public readonly int row;
                public readonly Dictionary<string, string> data;

                public EnemyRemapData(JsonNode json)
                {
                    row = json["row"].GetValue<int>();

                    data = new();
                    foreach (var property in json["data"].AsObject())
                    {
                        data.Add(property.Key, property.Value.ToString());
                    }
                }

                public EnemyRemapData(int row)
                {
                    this.row = row;
                    data = new();
                }
            }
        }

        public class FaceData
        {
            public readonly string id;
            public readonly Dictionary<string, byte> data;

            public FaceData(string id, JsonNode json)
            {
                this.id = id.ToLower().Trim();

                data = new();
                foreach (var property in json.AsObject())
                {
                    data.Add(property.Key, property.Value.GetValue<byte>());
                }
            }
        }

        public record Hair(byte part, Hair.Color color)
        {
            public enum Color
            {
                Black, DarkBrown, Brown, LightBrown, DirtyBlonde, Blonde, White, Gray, Grey, Red
            }

            public byte[] GetColor()
            {
                switch (color)
                {
                    case Color.Black: return new byte[] { 0, 0, 0 };         // @TODO: ai autocomplete set these values and they are fine for testing but CHANGE THEM LATER
                    case Color.DarkBrown: return new byte[] { 34, 17, 0 };
                    case Color.Brown: return new byte[] { 85, 42, 0 };
                    case Color.LightBrown: return new byte[] { 170, 85, 0 };
                    case Color.DirtyBlonde: return new byte[] { 221, 170, 85 };
                    case Color.Blonde: return new byte[] { 255, 255, 170 };
                    case Color.White: return new byte[] { 255, 255, 255 };
                    case Color.Grey:
                    case Color.Gray: return new byte[] { 170, 170, 170 };
                    case Color.Red: return new byte[] { 170, 0, 0 };
                    default: return new byte[] { 0, 0, 0 };
                }
            }
        }

        public class LoadingTip
        {
            public readonly string title, text;

            public LoadingTip(string title, string text)
            {
                this.title = title;
                this.text = text;
            }
        }
    }
}
