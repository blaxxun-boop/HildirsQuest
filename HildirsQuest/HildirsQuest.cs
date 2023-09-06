using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using JetBrains.Annotations;
using ServerSync;
using UnityEngine;

namespace HildirsQuest;

[BepInPlugin(ModGUID, ModName, ModVersion)]
[BepInIncompatibility("org.bepinex.plugins.valheim_plus")]
public class HildirsQuest : BaseUnityPlugin
{
	private const string ModName = "HildirsQuest";
	private const string ModVersion = "1.0.1";
	private const string ModGUID = "org.bepinex.plugins.hildirsquest";

	private static readonly ConfigSync configSync = new(ModName) { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };

	private static ConfigEntry<Toggle> serverConfigLocked = null!;
	private static ConfigEntry<Toggle> multiDrop = null!;
	private static ConfigEntry<int> multiDropRange = null!;

	private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description, bool synchronizedSetting = true)
	{
		ConfigEntry<T> configEntry = Config.Bind(group, name, value, description);

		SyncedConfigEntry<T> syncedConfigEntry = configSync.AddConfigEntry(configEntry);
		syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

		return configEntry;
	}

	private ConfigEntry<T> config<T>(string group, string name, T value, string description, bool synchronizedSetting = true) => config(group, name, value, new ConfigDescription(description), synchronizedSetting);

	private enum Toggle
	{
		On = 1,
		Off = 0,
	}

	public void Awake()
	{
		serverConfigLocked = config("1 - General", "Lock Configuration", Toggle.On, "If on, the configuration is locked and can be changed by server admins only.");
		configSync.AddLockingConfigEntry(serverConfigLocked);
		multiDrop = config("1 - General", "Drop per Player", Toggle.On, "If on, the mini bosses drop one chest per nearby player.");
		multiDropRange = config("1 - General", "Drop Range", 20, new ConfigDescription("If drop per player is on, this is the range to check for nearby players.", new AcceptableValueRange<int>(1, 100)));

		Assembly assembly = Assembly.GetExecutingAssembly();
		Harmony harmony = new(ModGUID);
		harmony.PatchAll(assembly);
	}

	[HarmonyPatch]
	private static class StorePlayerKey
	{
		private static IEnumerable<MethodInfo> TargetMethods() => new[]
		{
			AccessTools.DeclaredMethod(typeof(Trader), nameof(Trader.UseItem)),
			AccessTools.DeclaredMethod(typeof(Trader), nameof(Trader.GetAvailableItems)),
		};

		private static readonly MethodInfo getKey = AccessTools.DeclaredMethod(typeof(ZoneSystem), nameof(ZoneSystem.GetGlobalKey), new[] { typeof(string) });
		private static readonly MethodInfo setKey = AccessTools.DeclaredMethod(typeof(ZoneSystem), nameof(ZoneSystem.SetGlobalKey), new[] { typeof(string) });

		[UsedImplicitly]
		private static void setPlayerKey(ZoneSystem _, string key) => Player.m_localPlayer.m_customData[key] = "";

		private static bool getPlayerKey(ZoneSystem zoneSystem, string key, Trader trader)
		{
			if (Utils.GetPrefabName(trader.gameObject) == "Hildir")
			{
				return Player.m_localPlayer.m_customData.ContainsKey(key);
			}
			return zoneSystem.GetGlobalKey(key);
		}

		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			foreach (CodeInstruction instruction in instructions)
			{
				if (instruction.Calls(getKey))
				{
					yield return new CodeInstruction(OpCodes.Ldarg_0);
					yield return new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(StorePlayerKey), nameof(getPlayerKey)));
				}
				else if (instruction.Calls(setKey))
				{
					yield return new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(StorePlayerKey), nameof(setPlayerKey)));
				}
				else
				{
					yield return instruction;
				}
			}
		}
	}

	[HarmonyPatch(typeof(ConditionalObject), nameof(ConditionalObject.ShouldBeVisible))]
	private static class HideHildirChest
	{
		private static void Postfix(ConditionalObject __instance, ref bool __result)
		{
			if (__instance.name.CustomStartsWith("hildir_chest"))
			{
				__result = Player.m_localPlayer && Player.m_localPlayer.m_customData.ContainsKey(__instance.m_globalKeyCondition);
			}
		}
	}

	[HarmonyPatch(typeof(CharacterDrop), nameof(CharacterDrop.GenerateDropList))]
	private static class DropOneChestPerPlayer
	{
		private static void Postfix(CharacterDrop __instance, List<KeyValuePair<GameObject, int>> __result)
		{
			if (multiDrop.Value == Toggle.Off)
			{
				return;
			}

			for (int i = 0; i < __result.Count; ++i)
			{
				if (__result[i].Key.name.CustomStartsWith("chest_hildir"))
				{
					__result[i] = new KeyValuePair<GameObject, int>(__result[i].Key, Player.GetPlayersInRangeXZ(__instance.m_character.transform.position, multiDropRange.Value));
				}
			}
		}
	}
}
