using System;
using MelonLoader;
using Il2Cpp;
using UnityEngine;
using System.Reflection;

[assembly: MelonInfo(typeof(CanHarvester.CanHarvesterMod), "Can Harvester", "1.0.0", "Andiron")]
[assembly: MelonGame("Hinterland", "TheLongDark")]

namespace CanHarvester
{
	public class CanHarvesterMod : MelonMod
	{
		private Panel_Inventory _panelInventory = null;
		private object _lastSelectedItem = null;
		
		// Hardcoded Harvest object pointer from Cooking Pot
		private static IntPtr _harvestPointer = new IntPtr(2391598821952);
		private static object _hardcodedHarvest = null;
		private static bool _harvestLoaded = false;

		// === CONFIGURABLE HARVEST PROPERTIES ===
		public int HarvestDurationMinutes = 10;
		public string HarvestAudio = "Play_HarvestingMetalSaw";
		public int YieldCount = 1;
		// ========================================

		public override void OnInitializeMelon()
		{
			MelonLogger.Msg("[CanHarvester] Mod initialized");
		}

		public override void OnUpdate()
		{
			try
			{
				if (_panelInventory == null)
				{
					_panelInventory = UnityEngine.Object.FindObjectOfType<Panel_Inventory>();
					if (_panelInventory != null)
					{
						HookPanelEnable();
					}
					return;
				}

				CheckSelectedItem();
			}
			catch (Exception ex)
			{
				MelonLogger.Error($"[CanHarvester] OnUpdate error: {ex.Message}");
			}
		}

		private void HookPanelEnable()
		{
			try
			{
				var allItems = UnityEngine.Object.FindObjectsOfType<GearItem>();
				foreach (var item in allItems)
				{
					if (item.m_GearItemData != null && item.m_GearItemData.name.Equals("GearItemData_RecycledCan"))
					{
						InjectHarvest(item);
					}
				}
			}
			catch (Exception ex)
			{
				MelonLogger.Error($"[CanHarvester] HookPanelEnable error: {ex.Message}");
			}
		}

		private void CheckSelectedItem()
		{
			try
			{
				var selectedItem = _panelInventory.GetCurrentlySelectedItem();

				if (selectedItem != null && !selectedItem.Equals(_lastSelectedItem))
				{
					_lastSelectedItem = selectedItem;

					var gearItem = selectedItem.m_GearItem;
					if (gearItem != null && gearItem.m_GearItemData != null)
					{
						if (gearItem.m_GearItemData.name.Equals("GearItemData_RecycledCan"))
						{
							InjectHarvest(gearItem);
						}
					}
				}
				else if (selectedItem == null && _lastSelectedItem != null)
				{
					_lastSelectedItem = null;
				}
			}
			catch (Exception ex)
			{
				MelonLogger.Error($"[CanHarvester] CheckSelectedItem error: {ex.Message}");
			}
		}

		private void InjectHarvest(GearItem gearItem)
		{
			try
			{
				var harvestProp = typeof(GearItem).GetProperty("m_Harvest", 
					System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
				
				if (harvestProp == null || !harvestProp.CanWrite)
					return;

				// Check if already has harvest
				var existing = harvestProp.GetValue(gearItem);
				if (existing != null)
					return;

				// Load hardcoded harvest on first use
				if (!_harvestLoaded)
				{
					LoadHarvestFromPointer();
					_harvestLoaded = true;
				}

				if (_hardcodedHarvest == null)
					return;

				// Inject the hardcoded harvest
				harvestProp.SetValue(gearItem, _hardcodedHarvest);
				
				// Apply custom properties
				TrySetCustomProperties(_hardcodedHarvest, _hardcodedHarvest.GetType());
			}
			catch (Exception ex)
			{
				MelonLogger.Error($"[CanHarvester] InjectHarvest error: {ex.Message}");
			}
		}

		private void LoadHarvestFromPointer()
		{
			try
			{
				if (_harvestPointer == IntPtr.Zero)
				{
					MelonLogger.Error("[CanHarvester] Harvest pointer is invalid");
					return;
				}

				// Search scene for an item with Harvest to use as reference type
				var allItems = UnityEngine.Object.FindObjectsOfType<GearItem>();
				var harvestProp = typeof(GearItem).GetProperty("m_Harvest", 
					System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
				
				foreach (var item in allItems)
				{
					var harvest = harvestProp?.GetValue(item);
					if (harvest != null)
					{
						_hardcodedHarvest = harvest;
						break;
					}
				}

				if (_hardcodedHarvest != null)
				{
					MelonLogger.Msg("[CanHarvester] Loaded hardcoded Harvest");
				}
			}
			catch (Exception ex)
			{
				MelonLogger.Error($"[CanHarvester] LoadHarvestFromPointer error: {ex.Message}");
			}
		}

		private void TrySetCustomProperties(object harvestInstance, Type harvestType)
		{
			if (harvestInstance == null)
				return;

			try
			{
				var durationProp = harvestType.GetProperty("m_DurationMinutes", 
					System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
				if (durationProp != null && durationProp.CanWrite)
					durationProp.SetValue(harvestInstance, HarvestDurationMinutes);
			}
			catch { }

			try
			{
				var audioProp = harvestType.GetProperty("m_Audio", 
					System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
				if (audioProp != null && audioProp.CanWrite)
					audioProp.SetValue(harvestInstance, HarvestAudio);
			}
			catch { }

			try
			{
				var yieldCountProp = harvestType.GetProperty("YieldCount", 
					System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
				if (yieldCountProp != null && yieldCountProp.CanWrite)
					yieldCountProp.SetValue(harvestInstance, YieldCount);
			}
			catch { }
		}
	}
}