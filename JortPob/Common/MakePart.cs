using SoulsFormats;
using System;
using System.Collections.Generic;
using System.Numerics;
using static SoulsIds.Events;

namespace JortPob.Common
{
    /* Makes generic parts for MSBE */
    /* Generated parts have standardized fields, you can then set the important bits and gg ez */
    /* The reason I made this it's own class is because generating parts is very bulky in Elden Ring and this is cleaner than doing it inline */
    public class MakePart
    {
        public static Dictionary<ModelInfo, int> AssetInstances = new(); // counts instances of assets
        public static Dictionary<PickableInfo, int> PickableInstances = new(); // counts instances of pickables
        public static Dictionary<EmitterInfo, int> EmitterInstances = new(); // counts instances of emitter assets
        public static Dictionary<string, int> EnemyInstances = new();      // counts instances of enemies
        public static Dictionary<LiquidInfo, int> WaterInstances = new();
        public static Dictionary<string, int> VanillaAssetInstances = new();
        public static int PlayerInstances = 9000;

        /* Makes simple collideable asset */
        /* Values for this generic asset generator are taken from a random stone ruin in the church of elleh area 'AEG007_077' */
        public static MSBE.Part.Asset Asset(ModelInfo modelInfo)
        {
            MSBE.Part.Asset asset = new();

            /* Instance */
            int inst;
            if(AssetInstances.ContainsKey(modelInfo)) { inst = ++AssetInstances[modelInfo]; }
            else { inst = 0; AssetInstances.Add(modelInfo, inst); }
            asset.InstanceID = inst;

            /* Model Stuff */
            asset.Name = $"{modelInfo.AssetName().ToUpper()}_{inst.ToString("D4")}";
            asset.ModelName = modelInfo.AssetName().ToUpper();

            /* Top stuff */
            asset.AssetSfxParamRelativeID = -1;
            asset.MapStudioLayer = 4294967295;
            asset.IsShadowDest = true;

            /* Gparam */
            asset.Gparam.FogParamID = -1;
            asset.Gparam.LightSetID = -1;

            /* Various Unks */
            asset.UnkE0F = 1;
            asset.UnkE3C = -1;
            asset.UnkT12 = 255;
            asset.UnkT1E = -1;
            asset.UnkT24 = -1;
            asset.UnkT30 = -1;
            asset.UnkT34 = -1;

            /* Display Groups */
            asset.Unk1.DisplayGroups[0] = 16;
            asset.Unk1.UnkC4 = -1;

            /* Unk Groups */
            asset.Unk2.Condition = -1;
            asset.Unk2.Unk26 = -1;

            /* TileLoad */
            asset.TileLoad.MapID = new byte[] { 255, 255, 255, 255 };
            asset.TileLoad.CullingHeightBehavior = -1;

            /* Grass */
            asset.Grass.Unk18 = -1;

            /* Asset Partnames */
            asset.UnkT54PartName = asset.Name;
            asset.UnkPartNames[4] = asset.Name;
            asset.UnkPartNames[5] = asset.Name;
            asset.UnkModelMaskAndAnimID = -1;
            asset.UnkT5C = -1;
            asset.UnkT60 = -1;
            asset.UnkT64 = -1;

            /* AssetUnk1 */
            asset.AssetUnk1.Unk1C = -1;
            asset.AssetUnk1.Unk24 = -1;
            asset.AssetUnk1.Unk26 = -1;
            asset.AssetUnk1.Unk28 = -1;
            asset.AssetUnk1.Unk2C = -1;

            /* AssetUnk2 */
            asset.AssetUnk2.Unk04 = 100;
            asset.AssetUnk2.Unk14 = -1f;
            asset.AssetUnk2.Unk1C = 255;
            asset.AssetUnk2.Unk1D = 255;
            asset.AssetUnk2.Unk1E = 255;
            asset.AssetUnk2.Unk1F = 255;

            /* AssetUnk3 */
            asset.AssetUnk3.Unk04 = 64.808716f;
            asset.AssetUnk3.Unk09 = 255;
            asset.AssetUnk3.Unk0B = 255;
            asset.AssetUnk3.Unk0C = -1;
            asset.AssetUnk3.Unk10 = -1f;
            asset.AssetUnk3.DisableWhenMapLoadedMapID = new sbyte[] { -1, -1, -1, -1 };
            asset.AssetUnk3.Unk18 = -1;
            asset.AssetUnk3.Unk1C = -1;
            asset.AssetUnk3.Unk20 = -1;
            asset.AssetUnk3.Unk24 = 255;

            /* AssetUnk4 */
            asset.AssetUnk4.Unk01 = 255;
            asset.AssetUnk4.Unk02 = 255;

            return asset;
        }

        public static MSBE.Part.Asset Asset(PickableInfo pickableInfo)
        {
            MSBE.Part.Asset asset = Asset(pickableInfo.model);  // kind of lazy but it works guh

            /* Instance */
            if (!PickableInstances.TryGetValue(pickableInfo, out var inst)) { inst = 0; }
            asset.InstanceID = inst;
            PickableInstances[pickableInfo] = ++inst;

            /* Model Stuff */
            asset.Name = $"{pickableInfo.AssetName().ToUpper()}_{asset.InstanceID.ToString("D4")}";
            asset.ModelName = pickableInfo.AssetName().ToUpper();

            /* Asset Partnames */
            asset.UnkT54PartName = asset.Name;
            asset.UnkPartNames[4] = asset.Name;
            asset.UnkPartNames[5] = asset.Name;

            return asset;
        }

        /* Makes asset with some sfx stuff */
        /* Values for this generic asset generator are taken from a random stone ruin in the church of elleh area 'AEG007_077' */
        public static MSBE.Part.Asset Asset(EmitterInfo emitterInfo)
        {
            MSBE.Part.Asset asset = new();

            /* Instance */
            int inst;
            if (EmitterInstances.ContainsKey(emitterInfo)) { inst = ++EmitterInstances[emitterInfo]; }
            else { inst = 0; EmitterInstances.Add(emitterInfo, inst); }
            asset.InstanceID = inst;

            /* Model Stuff */
            asset.Name = $"{emitterInfo.AssetName().ToUpper()}_{inst.ToString("D4")}";
            asset.ModelName = emitterInfo.AssetName().ToUpper();

            /* Top stuff */
            asset.AssetSfxParamRelativeID = 0;
            asset.MapStudioLayer = 4294967295;
            asset.IsShadowDest = true;

            /* Gparam */
            asset.Gparam.FogParamID = -1;
            asset.Gparam.LightSetID = -1;

            /* Various Unks */
            asset.UnkE0F = 1;
            asset.UnkE3C = -1;
            asset.UnkT12 = 255;
            asset.UnkT1E = -1;
            asset.UnkT24 = -1;
            asset.UnkT30 = -1;
            asset.UnkT34 = -1;

            /* Display Groups */
            asset.Unk1.DisplayGroups[0] = 16;
            asset.Unk1.UnkC4 = -1;

            /* Unk Groups */
            asset.Unk2.Condition = -1;
            asset.Unk2.Unk26 = -1;

            /* TileLoad */
            asset.TileLoad.MapID = new byte[] { 255, 255, 255, 255 };
            asset.TileLoad.CullingHeightBehavior = -1;

            /* Grass */
            asset.Grass.Unk18 = -1;

            /* Asset Partnames */
            asset.UnkT54PartName = asset.Name;
            asset.UnkPartNames[4] = asset.Name;
            asset.UnkPartNames[5] = asset.Name;
            asset.UnkModelMaskAndAnimID = -1;
            asset.UnkT5C = -1;
            asset.UnkT60 = -1;
            asset.UnkT64 = -1;

            /* AssetUnk1 */
            asset.AssetUnk1.Unk1C = -1;
            asset.AssetUnk1.Unk24 = -1;
            asset.AssetUnk1.Unk26 = -1;
            asset.AssetUnk1.Unk28 = -1;
            asset.AssetUnk1.Unk2C = -1;

            /* AssetUnk2 */
            asset.AssetUnk2.Unk04 = 100;
            asset.AssetUnk2.Unk14 = -1f;
            asset.AssetUnk2.Unk1C = 255;
            asset.AssetUnk2.Unk1D = 255;
            asset.AssetUnk2.Unk1E = 255;
            asset.AssetUnk2.Unk1F = 255;

            /* AssetUnk3 */
            asset.AssetUnk3.Unk04 = 64.808716f;
            asset.AssetUnk3.Unk09 = 255;
            asset.AssetUnk3.Unk0B = 255;
            asset.AssetUnk3.Unk0C = -1;
            asset.AssetUnk3.Unk10 = -1f;
            asset.AssetUnk3.DisableWhenMapLoadedMapID = new sbyte[] { -1, -1, -1, -1 };
            asset.AssetUnk3.Unk18 = -1;
            asset.AssetUnk3.Unk1C = -1;
            asset.AssetUnk3.Unk20 = -1;
            asset.AssetUnk3.Unk24 = 255;

            /* AssetUnk4 */
            asset.AssetUnk4.Unk01 = 255;
            asset.AssetUnk4.Unk02 = 255;

            return asset;
        }


        /* Make water plane asset */
        /* Values taken from AEG097_000_9900 in superoverworld */
        public static MSBE.Part.Asset Asset(LiquidInfo waterInfo)
        {
            //MSBE EXAMPLE = MSBE.Read(@"I:\SteamLibrary\steamapps\common\ELDEN RING\Game\map\mapstudio\m60_00_00_99.msb.dcx");

            MSBE.Part.Asset asset = new();

            /* Instance */
            int inst;
            if (WaterInstances.ContainsKey(waterInfo)) { inst = ++WaterInstances[waterInfo]; }
            else { inst = 0; WaterInstances.Add(waterInfo, inst); }
            asset.InstanceID = inst;

            /* Model Stuff */
            asset.Name = $"{waterInfo.AssetName().ToUpper()}_{inst.ToString("D4")}";
            asset.ModelName = waterInfo.AssetName().ToUpper();

            /* Top stuff */
            asset.AssetSfxParamRelativeID = -1;
            asset.MapStudioLayer = 4294967295;
            asset.IsShadowDest = true;

            /* Gparam */
            asset.Gparam.FogParamID = 0;
            asset.Gparam.LightSetID = 0;

            /* Various Unks */
            asset.UnkE0F = 1;
            asset.UnkE3C = 2;
            asset.UnkT12 = 255;
            asset.UnkT1E = -1;
            asset.UnkT24 = -1;
            asset.UnkT30 = -1;
            asset.UnkT34 = -1;

            /* Display Groups */
            asset.Unk1.DisplayGroups[0] = 16;
            asset.Unk1.UnkC4 = -1;

            /* Unk Groups */
            asset.Unk2.Condition = -1;
            asset.Unk2.Unk26 = -1;

            /* TileLoad */
            asset.TileLoad.MapID = new byte[] { 255, 255, 255, 255 };
            asset.TileLoad.Unk0C = -1;
            asset.TileLoad.CullingHeightBehavior = -1;

            /* Grass */
            asset.Grass.Unk18 = -1;

            /* Asset Partnames */
            asset.UnkT54PartName = null;
            asset.UnkModelMaskAndAnimID = -1;
            asset.UnkT5C = -1;
            asset.UnkT60 = -1;
            asset.UnkT64 = -1;

            /* AssetUnk1 */
            asset.AssetUnk1.Unk1C = -1;
            asset.AssetUnk1.Unk24 = -1;
            asset.AssetUnk1.Unk26 = -1;
            asset.AssetUnk1.Unk28 = -1;
            asset.AssetUnk1.Unk2C = -1;

            /* AssetUnk2 */
            asset.AssetUnk2.Unk04 = -1;
            asset.AssetUnk2.Unk14 = -1;
            asset.AssetUnk2.Unk1C = 255;
            asset.AssetUnk2.Unk1D = 255;
            asset.AssetUnk2.Unk1E = 255;
            asset.AssetUnk2.Unk1F = 255;

            /* AssetUnk3 */
            asset.AssetUnk3.Unk04 = 0f;
            asset.AssetUnk3.Unk09 = 255;
            asset.AssetUnk3.Unk0B = 255;
            asset.AssetUnk3.Unk0C = -1;
            asset.AssetUnk3.Unk10 = -1f;
            asset.AssetUnk3.DisableWhenMapLoadedMapID = new sbyte[] { -1, -1, -1, -1 };
            asset.AssetUnk3.Unk18 = -1;
            asset.AssetUnk3.Unk1C = -1;
            asset.AssetUnk3.Unk20 = -1;
            asset.AssetUnk3.Unk24 = 255;

            /* AssetUnk4 */
            asset.AssetUnk4.Unk01 = 255;
            asset.AssetUnk4.Unk02 = 255;

            return asset;
        }


        /* Make a map piece for use as terrain */
        public static MSBE.Part.MapPiece MapPiece()
        {
            MSBE.Part.MapPiece map = new();

            /* Some Stuff */
            map.MapStudioLayer = 4294967295;
            map.isUsePartsDrawParamID = 1;
            map.PartsDrawParamID = 0;       // set to default, this value should be filled out by the param that parammanger makes

            /* Gparam */
            map.Gparam.FogParamID = -1;
            map.Gparam.LightSetID = -1;

            /* More stuff */
            map.IsShadowDest = true;

            /* TileLoad */
            map.TileLoad.MapID = new byte[] { 255, 255, 255, 255 };
            map.TileLoad.CullingHeightBehavior = -1;
            map.TileLoad.Unk0C = -1;

            /* Display Groups */
            map.Unk1.UnkC4 = -1;

            /* Random Unks */
            map.UnkE0F = 1;
            map.UnkE3C = -1;
            map.UnkE3E = 1;

            return map;
        }

        /* Make a collision for use as terrain */
        public static MSBE.Part.Collision Collision()
        {
            MSBE.Part.Collision collision = new();

            collision.MapStudioLayer = 4294967295;
            collision.PlayRegionID = -1;
            collision.LocationTextID = -1;
            collision.InstanceID = -1;
            collision.SceneGparam.TransitionTime = 10f;
            collision.TileLoad.CullingHeightBehavior = -1;
            collision.TileLoad.MapID = new byte[] { 255, 255, 255, 255 };
            collision.TileLoad.Unk0C = -1;
            collision.Unk1.UnkC4 = -1;
            collision.Unk2.Condition = -1;
            collision.Unk2.Unk26 = -1;
            collision.UnkE0F = 1;
            collision.UnkE3C = -1;
            collision.UnkT01 = 255;
            collision.UnkT02 = 255;
            collision.UnkT04 = 64.8087158f;
            collision.UnkT14 = -1;
            collision.UnkT1C = -1;
            collision.UnkT24 = -1;
            collision.UnkT30 = -1;
            collision.UnkT35 = 255;
            collision.UnkT3C = -1;
            collision.UnkT3E = -1;
            collision.UnkT4E = -1;

            return collision;
        }

        /* Make a collision for use as water splashy */
        public static MSBE.Part.Collision WaterCollision()
        {
            MSBE.Part.Collision collision = new();

            collision.Gparam.FogParamID = -1;
            collision.Gparam.LightSetID = -1;
            collision.SceneGparam.TransitionTime = -1;

            collision.HitFilterID = SoulsFormats.MSBE.Part.Collision.HitFilterType.Unk16; // pretty sure this sets it to water splash collision

            collision.MapStudioLayer = 4294967295;
            collision.PlayRegionID = -1;
            collision.LocationTextID = -1;
            collision.InstanceID = -1;
            collision.TileLoad.CullingHeightBehavior = -1;
            collision.TileLoad.MapID = new byte[] { 255, 255, 255, 255 };
            collision.TileLoad.Unk0C = -1;
            collision.Unk1.UnkC4 = -1;
            collision.Unk2.Condition = -1;
            collision.Unk2.Unk26 = -1;
            collision.UnkE0F = 1;
            collision.UnkE3C = -1;
            collision.UnkT01 = 255;
            collision.UnkT02 = 255;
            collision.UnkT04 = 0;
            collision.UnkT14 = -1;
            collision.UnkT1C = -1;
            collision.UnkT24 = -1;
            collision.UnkT30 = -1;
            collision.UnkT35 = 255;
            collision.UnkT3C = -1;
            collision.UnkT3E = -1;
            collision.UnkT4E = -1;

            return collision;
        }

        /* Makes a c000 enemy part */
        public static MSBE.Part.Enemy Npc()
        {
            MSBE.Part.Enemy enemy = new();

            /* Instance */
            int inst;
            if (EnemyInstances.ContainsKey("c0000")) { inst = ++EnemyInstances["c0000"]; }
            else { inst = 0; EnemyInstances.Add("c0000", inst); }
            enemy.InstanceID = inst;

            /* Model and Enemy Stuff */
            enemy.Name = $"c0000_{inst.ToString("D4")}";
            enemy.ModelName = "c0000";
            enemy.CharaInitID = 23150;
            enemy.NPCParamID = 523010010;
            enemy.EntityID = 0;
            enemy.PlatoonID = 0;
            enemy.ThinkParamID = 523011000;

            /* In Alphabetical Order... */
            /* Gparam */
            enemy.Gparam.FogParamID = -1;
            enemy.Gparam.LightSetID = -1;

            /* Stuff */
            enemy.IsShadowDest = true;
            enemy.MapStudioLayer = 4294967295;

            /* TileLoad */
            enemy.TileLoad.MapID = new byte[] { 255, 255, 255, 255 };
            enemy.TileLoad.CullingHeightBehavior = -1;
            enemy.TileLoad.Unk0C = -1;

            /* Display Groups */
            enemy.Unk1.DisplayGroups[0] = 16;
            enemy.Unk1.UnkC4 = -1;

            /* Random Unks */
            enemy.UnkE0F = 1;
            enemy.UnkE3C = -1;

            return enemy;
        }

        /* makes a goat */
        public static MSBE.Part.Enemy Creature()
        {
            MSBE.Part.Enemy enemy = new();

            /* Instance */
            int inst;
            if (EnemyInstances.ContainsKey("c0000")) { inst = ++EnemyInstances["c0000"]; }
            else { inst = 0; EnemyInstances.Add("c0000", inst); }
            enemy.InstanceID = inst;

            /* Model and Enemy Stuff */
            enemy.Name = $"c6060_{inst.ToString("D4")}";
            enemy.ModelName = "c6060";
            enemy.NPCParamID = 60600010;
            enemy.EntityID = 0;
            enemy.PlatoonID = 0;
            enemy.ThinkParamID = 60600000;

            /* In Alphabetical Order... */
            /* Gparam */
            enemy.Gparam.FogParamID = -1;
            enemy.Gparam.LightSetID = -1;

            /* Stuff */
            enemy.IsShadowDest = true;
            enemy.MapStudioLayer = 4294967295;

            /* TileLoad */
            enemy.TileLoad.MapID = new byte[] { 255, 255, 255, 255 };
            enemy.TileLoad.CullingHeightBehavior = -1;
            enemy.TileLoad.Unk0C = -1;

            /* Display Groups */
            enemy.Unk1.DisplayGroups[0] = 16;
            enemy.Unk1.UnkC4 = -1;

            /* Random Unks */
            enemy.UnkE0F = 1;
            enemy.UnkE3C = -1;
            enemy.UnkT84 = 1;

            return enemy;
        }

        /* Create generic player starting point. */
        public static MSBE.Part.Player Player()
        {
            MSBE.Part.Player player = new();
            int inst = PlayerInstances++;

            player.Name = $"c0000_{inst.ToString("D4")}";
            player.ModelName = "c0000";
            player.InstanceID = inst;
            player.MapStudioLayer = 4294967295;
            player.Unk1.DisplayGroups[0] = 16;
            player.IsShadowDest = true;

            /* TileLoad */
            player.TileLoad.MapID = new byte[] { 255, 255, 255, 255 };
            player.TileLoad.CullingHeightBehavior = -1;
            player.TileLoad.Unk0C = -1;

            /* No idea */
            player.Unk00 = 6;

            /* Display Groups */
            player.Unk1.UnkC4 = -1;

            /* Random Unks */
            player.UnkE0F = 1;
            player.UnkE3C = -1;

            return player;
        }

        /* Create generic envbox */
        public static MSBE.Region.EnvironmentMapEffectBox EnvBox()
        {
            MSBE.Region.EnvironmentMapEffectBox envBox = new();
            envBox.Rotation = Vector3.Zero;
            envBox.ActivationPartName = null;
            envBox.EntityID = 0;
            envBox.IsModifyLight = true;
            envBox.MapID = -1;
            envBox.MapStudioLayer = 4294967295;
            envBox.PointLightMult = 1f;
            envBox.RegionID = 0;
            envBox.SpecularLightMult = 1f;
            envBox.Unk40 = 0;
            envBox.UnkE08 = 255;
            envBox.UnkS04 = 0;
            envBox.UnkS0C = -1;
            envBox.UnkT08 = 0;
            envBox.UnkT09 = 10;
            envBox.UnkT0A = -1;
            envBox.UnkT2C = 0;
            envBox.UnkT2F = false;
            envBox.UnkT30 = -1;
            envBox.UnkT32 = false;
            envBox.UnkT33 = true;
            envBox.UnkT34 = 1;
            envBox.UnkT36 = 1;
            return envBox;
        }

        /* Create generic envpoint */
        public static MSBE.Region.EnvironmentMapPoint EnvPoint()
        {
            MSBE.Region.EnvironmentMapPoint envPoint = new();
            envPoint.Shape = new MSB.Shape.Point();
            envPoint.Rotation = Vector3.Zero;
            envPoint.ActivationPartName = null;
            envPoint.EntityID = 0;
            envPoint.MapID = -1;
            envPoint.MapStudioLayer = 4294967295;
            envPoint.RegionID = 0;
            envPoint.Unk40 = 0;
            envPoint.UnkE08 = 255;
            envPoint.UnkS04 = 0;
            envPoint.UnkS0C = -1;
            envPoint.UnkT00 = 200;
            envPoint.UnkT04 = 2;
            envPoint.UnkT0D = true;
            envPoint.UnkT0E = true;
            envPoint.UnkT0F = true;
            envPoint.UnkT10 = 1;
            envPoint.UnkT14 = 1;
            envPoint.UnkT20 = 512;
            envPoint.UnkT24 = 64;
            envPoint.UnkT28 = 5;
            envPoint.UnkT2C = 0;
            envPoint.UnkT2D = 1;
            return envPoint;
        }

        public static MSBE.Event.Treasure Treasure()
        {
            MSBE.Event.Treasure treasure = new();
            return treasure;
        }

        // invis object to put a treasure on
        public static MSBE.Part.Asset TreasureAsset()
        {
            MSBE.Part.Asset asset = new();

            /* Model Stuff */
            asset.ModelName = "AEG099_090";

            /* Instance */
            int inst;
            if (VanillaAssetInstances.ContainsKey(asset.ModelName)) { inst = ++VanillaAssetInstances[asset.ModelName]; }
            else { inst = 0; VanillaAssetInstances.Add(asset.ModelName, inst); }
            asset.Name = $"{asset.ModelName}_{inst.ToString("D4")}";
            asset.InstanceID = inst;

            /* Top stuff */
            asset.AssetSfxParamRelativeID = -1;
            asset.MapStudioLayer = 4294967295;
            asset.IsShadowDest = true;

            /* Gparam */
            asset.Gparam.FogParamID = -1;
            asset.Gparam.LightSetID = -1;

            /* Various Unks */
            asset.UnkE0F = 1;
            asset.UnkE3C = -1;
            asset.UnkT12 = 255;
            asset.UnkT1E = -1;
            asset.UnkT24 = -1;
            asset.UnkT30 = -1;
            asset.UnkT34 = -1;

            /* Display Groups */
            asset.Unk1.DisplayGroups[0] = 16;
            asset.Unk1.UnkC4 = -1;

            /* Unk Groups */
            asset.Unk2.Condition = -1;
            asset.Unk2.Unk26 = -1;

            /* TileLoad */
            asset.TileLoad.MapID = new byte[] { 255, 255, 255, 255 };
            asset.TileLoad.CullingHeightBehavior = -1;

            /* Grass */
            asset.Grass.Unk18 = -1;

            /* Asset Partnames */
            asset.UnkT54PartName = asset.Name;
            asset.UnkPartNames[4] = asset.Name;
            asset.UnkPartNames[5] = asset.Name;
            asset.UnkModelMaskAndAnimID = -1;
            asset.UnkT5C = -1;
            asset.UnkT60 = -1;
            asset.UnkT64 = -1;

            /* AssetUnk1 */
            asset.AssetUnk1.Unk1C = -1;
            asset.AssetUnk1.Unk24 = -1;
            asset.AssetUnk1.Unk26 = -1;
            asset.AssetUnk1.Unk28 = -1;
            asset.AssetUnk1.Unk2C = -1;

            /* AssetUnk2 */
            asset.AssetUnk2.Unk04 = 100;
            asset.AssetUnk2.Unk14 = -1f;
            asset.AssetUnk2.Unk1C = 255;
            asset.AssetUnk2.Unk1D = 255;
            asset.AssetUnk2.Unk1E = 255;
            asset.AssetUnk2.Unk1F = 255;

            /* AssetUnk3 */
            asset.AssetUnk3.Unk04 = 64.808716f;
            asset.AssetUnk3.Unk09 = 255;
            asset.AssetUnk3.Unk0B = 255;
            asset.AssetUnk3.Unk0C = -1;
            asset.AssetUnk3.Unk10 = -1f;
            asset.AssetUnk3.DisableWhenMapLoadedMapID = new sbyte[] { -1, -1, -1, -1 };
            asset.AssetUnk3.Unk18 = -1;
            asset.AssetUnk3.Unk1C = -1;
            asset.AssetUnk3.Unk20 = -1;
            asset.AssetUnk3.Unk24 = 255;

            /* AssetUnk4 */
            asset.AssetUnk4.Unk01 = 255;
            asset.AssetUnk4.Unk02 = 255;

            return asset;
        }
    }
}


// If ya need to compare some values....
//            //MSBE TESTO = MSBE.Read(@"I:\SteamLibrary\steamapps\common\ELDEN RING\Game\map\mapstudio\m60_42_36_00.msb.dcx");
