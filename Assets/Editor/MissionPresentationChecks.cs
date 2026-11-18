using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Combat;
using Game.Core;
using Game.Player;
using UnityEditor;
using UnityEngine;

// Checks skill topology, target acquisition, support effects and mission UI against live game state.
public static class MissionPresentationChecks
{
    [MenuItem("Tools/Group Game/Check mission presentation systems")]
    public static void Run()
    {
        bool suppressed = GameSession.SuppressSave;
        GameObject pilot = null;
        try
        {
            GameSession.SuppressSave = true;
            pilot = new GameObject("Skill revision check");
            var tree = pilot.AddComponent<SkillTree>();
            typeof(SkillTree).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(tree, null);

            // Validate the five skill branches and retain compatibility with the existing network bit layout.
            var nodes = tree.AllNodes.Where(skillNode => skillNode.id != SkillId.Root).ToArray();
            Require(nodes.Length == 25, "Expected exactly 25 branch skills");

            for (int branch = 0; branch < 5; branch++)
            {
                var group = nodes.Where(skillNode2 => SkillTree.Branch(skillNode2.id) == branch).ToArray();
                Require(group.Length == 5, "Every branch needs five skills");
                Require(group.Count(skillNode3 => skillNode3.parent == SkillId.Root) == 1, "Branch must start at root");
                Require(group.GroupBy(skillNode4 => skillNode4.parent).Max(skillGroup => skillGroup.Count()) == 2, "Branch must end in two siblings");
            }

            var owned = (HashSet<SkillId>)typeof(SkillTree).GetField("unlocked", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(tree);
            owned.Add(SkillId.Cannon);
            Require((tree.NetworkMask & (1u << (int)SkillId.Cannon)) != 0, "Legacy unlock lost in network mask");
            Require((tree.NetworkMask & 0x80000000u) == 0, "Skill mask exceeds protocol capacity");

            // Moving, removing and rebasing a radar contact must not leave stale spatial entries.
            var octree = new SpatialOctree<int>(Vector3.zero, 16, 2);
            octree.Upsert(1, new Vector3(2, 0, 0));
            octree.Upsert(2, new Vector3(40, 0, 0));
            var found = new List<int>();
            octree.Query(Vector3.zero, 3, found);
            Require(found.SequenceEqual(new[] { 1 }), "Range query leaked distant object");
            octree.Upsert(1, new Vector3(42, 0, 0));
            found.Clear();
            octree.Query(Vector3.zero, 3, found);
            Require(found.Count == 0, "Moving contact left stale index entry");
            octree.Remove(2);
            found.Clear();
            octree.Query(new Vector3(42, 0, 0), 3, found);
            Require(found.SequenceEqual(new[] { 1 }), "Removal lost or retained wrong contact");
            octree.Shift(new Vector3(-1000, 0, 0));
            found.Clear();
            octree.Query(new Vector3(-958, 0, 0), 0, found);
            Require(found.SequenceEqual(new[] { 1 }), "Origin shift broke exact-boundary query");
            CheckAcquisitionAndSupport();
            CheckLateSkillAttachment();
            CheckMissionTelemetry();
            CheckHangarMap();
            Require(Resources.Load<Sprite>("UI/FlightCrosshair") != null, "Supplied crosshair was not imported as a sprite");
            Debug.Log("MISSION PRESENTATION CHECKS PASSED: 25 skills, legacy mask, octree motion/removal/origin, real acquisition range/LOS, support effects and live mission card");
        }
        finally
        {
            if (pilot != null)
            {
                UnityEngine.Object.DestroyImmediate(pilot);
            }

            GameSession.SuppressSave = suppressed;
        }
    }

    static void CheckLateSkillAttachment()
    {
        var pilot = new GameObject("Late skills pilot");
        var target = new GameObject("Sensor boundary target");
        try
        {
            var barrel = new GameObject("Weapon child");
            barrel.transform.SetParent(pilot.transform, false);
            var weapon = barrel.AddComponent<Weapon>();
            typeof(Weapon).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(weapon, null);
            pilot.transform.position = new Vector3(10000, 12000, 10000);
            target.transform.position = pilot.transform.position + Vector3.forward * 150;
            target.layer = LayerMask.NameToLayer(GameLayers.Enemy);
            var health = target.AddComponent<Health>();
            health.ResetHealth();
            RadarContact.Track(target, RadarKind.Hostile);
            RadarIndex.Synchronize(true);
            weapon.AcquireDirection(pilot.transform.position, Vector3.forward);
            Require(weapon.AcquiredTarget == null, "Baseline weapon acquired beyond 100 metres");

            // Attach the sensor skill after the weapon has already initialized to exercise late component discovery.
            var tree = pilot.AddComponent<SkillTree>();
            typeof(SkillTree).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(tree, null);
            var owned = (HashSet<SkillId>)typeof(SkillTree).GetField("unlocked", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(tree);
            owned.Clear();
            owned.Add(SkillId.AttackCore);
            var skills = pilot.AddComponent<PlayerSkills>();
            typeof(PlayerSkills).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(skills, null);
            weapon.AcquireDirection(pilot.transform.position, Vector3.forward);
            Require(weapon.AcquiredTarget == health, "Sensors attached after Weapon.Awake did not extend acquisition to 150 metres");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(pilot);
            UnityEngine.Object.DestroyImmediate(target);
            RadarIndex.Synchronize(true);
        }
    }

    // Keep every jump card inside the map and separate from every other card.
    static void CheckHangarMap()
    {
        for (int firstSector = 1; firstSector <= 8; firstSector++)
        {
            Vector2 firstCardPosition = Game.Progression.HangarMenu.JumpCardPosition(firstSector);
            var bounds = new Rect(firstCardPosition.x - 76, firstCardPosition.y - 28, 152, 56);
            Require(bounds.xMin >= 34 && bounds.xMax <= 830 && bounds.yMin >= 250 && bounds.yMax <= 770, "Jump card crosses map/title/footer bounds");

            for (int secondSector = firstSector + 1; secondSector <= 8; secondSector++)
            {
                Vector2 secondCardPosition = Game.Progression.HangarMenu.JumpCardPosition(secondSector);
                Require(!bounds.Overlaps(new Rect(secondCardPosition.x - 76, secondCardPosition.y - 28, 152, 56)), "Jump cards overlap");
            }
        }
    }

    static void CheckAcquisitionAndSupport()
    {
        var made = new List<GameObject>();
        Vector3 origin = new Vector3(10000, 10000, 10000);
        try
        {
            var pilot = new GameObject("Sensor pilot");
            made.Add(pilot);
            pilot.transform.position = origin;
            var weapon = pilot.AddComponent<Weapon>();
            var target = new GameObject("Near target");
            made.Add(target);
            target.layer = LayerMask.NameToLayer(GameLayers.Enemy);
            target.transform.position = origin + Vector3.forward * 40;
            var health = target.AddComponent<Health>();
            health.maxHealth = 10;
            health.ResetHealth();
            RadarContact.Track(target, RadarKind.Hostile);
            var distant = new GameObject("Distant target");
            made.Add(distant);
            distant.layer = target.layer;
            distant.transform.position = origin + Vector3.forward * 200;
            var farHp = distant.AddComponent<Health>();
            farHp.ResetHealth();
            RadarContact.Track(distant, RadarKind.Hostile);
            RadarIndex.Synchronize(true);
            weapon.AcquireDirection(origin, Vector3.forward);
            Require(weapon.AcquiredTarget == health, "Weapon failed to acquire in-range contact or selected out-of-range contact");

            // An acquired target must be rejected after moving out of range, becoming obscured or being disabled.
            target.transform.position = origin + Vector3.right * 300;
            RadarIndex.Synchronize(true);
            weapon.AcquireDirection(origin, Vector3.forward);
            Require(weapon.AcquiredTarget == null, "Weapon used stale moving contact or acquired beyond range");
            target.transform.position = origin + Vector3.forward * 40;
            var rock = GameObject.CreatePrimitive(PrimitiveType.Cube);
            made.Add(rock);
            rock.layer = LayerMask.NameToLayer(GameLayers.LevelGeometry);
            rock.transform.position = origin + Vector3.forward * 20;
            rock.transform.localScale = Vector3.one * 8;
            Physics.SyncTransforms();
            RadarIndex.Synchronize(true);
            weapon.AcquireDirection(origin, Vector3.forward);
            Require(weapon.AcquiredTarget == null, "Weapon acquired through level geometry");
            rock.SetActive(false);
            target.SetActive(false);
            RadarIndex.Synchronize(true);
            weapon.AcquireDirection(origin, Vector3.forward);
            Require(weapon.AcquiredTarget == null, "Disabled contact remained targetable");

            // Apply support skills to actual health components for both the pilot and a nearby ally.
            var ally = new GameObject("Escort support target");
            made.Add(ally);
            ally.layer = LayerMask.NameToLayer(GameLayers.Player);
            ally.transform.position = origin + Vector3.right * 20;
            var allyHp = ally.AddComponent<Health>();
            allyHp.maxHealth = 10;
            allyHp.ResetHealth();
            allyHp.SetNetworkHealth(5);
            RadarContact.Track(ally, RadarKind.Friendly);
            var pilotHp = pilot.AddComponent<Health>();
            pilotHp.maxHealth = 10;
            pilotHp.ResetHealth();
            pilotHp.SetNetworkHealth(5);
            RadarContact.Track(pilot, RadarKind.Friendly);
            uint unlockedSkills = (1u << (int)SkillId.Damage1) | (1u << (int)SkillId.Damage2) | (1u << (int)SkillId.Damage3);
            Physics.SyncTransforms();
            RadarIndex.Synchronize(true);
            SkillSupportEffects.Repair(pilot, pilotHp, unlockedSkills);
            Require(pilotHp.CurrentHealth == 7 && allyHp.CurrentHealth == 6, "Repair Kit / Wing Repair did not affect real health");
            Require(Mathf.Approximately(SkillSupportEffects.Cooldown(unlockedSkills, SkillId.Repair), 10.2f), "Recovery cooldown not applied");
            Require(SkillSupportEffects.SensorRange(1u << (int)SkillId.AttackCore) == 240 && SkillSupportEffects.TractorRange(1u << (int)SkillId.Speed4) == 32, "Utility range effects missing");
        }
        finally
        {
            foreach (var sceneObject in made)
            {
                if (sceneObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(sceneObject);
                }
            }

            RadarIndex.Synchronize(true);
        }
    }

    static void CheckMissionTelemetry()
    {
        var host = new GameObject("Mission HUD telemetry check");
        var pilot = new GameObject("Mission pilot");
        var core = new GameObject("Navigation core");
        Game.Campaign.MissionHud missionHud = null;
        try
        {
            var mission = host.AddComponent<Game.Campaign.CampaignMission>();
            typeof(Game.Campaign.CampaignMission).GetField("pilot", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(mission, pilot.transform);
            var beacons = (List<Transform>)typeof(Game.Campaign.CampaignMission).GetField("beacons", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(mission);
            beacons.Add(core.transform);
            core.transform.position = pilot.transform.position + Vector3.forward * 78;
            missionHud = Game.Campaign.MissionHud.Attach(mission);
            var update = typeof(Game.Campaign.MissionHud).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
            var metric = (UnityEngine.UI.Text)typeof(Game.Campaign.MissionHud).GetField("metric", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(missionHud);
            update.Invoke(missionHud, null);
            Require(metric.text == "78 m", "Recovery HUD does not use live core distance");

            // Change live targets and mission stages to ensure the card is not showing baked presentation values.
            core.transform.position = pilot.transform.position + Vector3.forward * 11;
            update.Invoke(missionHud, null);
            Require(metric.text == "11 m", "Recovery HUD did not follow moving objective");
            typeof(Game.Campaign.CampaignMission).GetProperty("Stage").SetValue(mission, 1);
            typeof(Game.Campaign.CampaignMission).GetField("relayStep", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(mission, 2);
            update.Invoke(missionHud, null);
            Require(metric.text == "2 / 3", "Relay card does not follow the real ordered progress");
            typeof(Game.Campaign.CampaignMission).GetField("escort", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(mission, core.transform);
            var transportHp = core.AddComponent<Health>();
            transportHp.maxHealth = 20;
            transportHp.ResetHealth();
            transportHp.SetNetworkHealth(7);
            typeof(Game.Campaign.CampaignMission).GetField("escortHealth", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(mission, transportHp);
            update.Invoke(missionHud, null);
            var hull = (UnityEngine.UI.Text)typeof(Game.Campaign.MissionHud).GetField("hullValue", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(missionHud);
            Require(hull.text == "35%", "Escort card does not follow live transport health");
            typeof(Game.Campaign.CampaignMission).GetProperty("Failed").SetValue(mission, true);
            update.Invoke(missionHud, null);
            var action = (UnityEngine.UI.Text)typeof(Game.Campaign.MissionHud).GetField("action", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(missionHud);
            Require(action.text == "[ ENTER ] RETRY", "Failed mission card omits the real retry control");
            typeof(Game.Campaign.MissionHud).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(missionHud, null);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(host);
            UnityEngine.Object.DestroyImmediate(pilot);
            UnityEngine.Object.DestroyImmediate(core);
        }
    }

    static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
