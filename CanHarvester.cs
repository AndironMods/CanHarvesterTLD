using System;
using System.Collections.Generic;
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
		// UI References
		private Panel_Inventory _panelInventory = null;
		private object _lastSelectedItem = null;
		
		// Harvest system
		private GearItem _cookingPotTemplate = null;
		private object _cachedHarvest = null;
		private bool _harvestInitialized = false;
		private string _lastScene = "";
		private float _hookCheckTimer = 0f;
		private const float HOOK_CHECK_INTERVAL = 2f; // Check for recycled cans every 2 seconds
		private HashSet<GearItem> _processedItems = new HashSet<GearItem>(); // Track already injected items
		private float _cleanupTimer = 0f;
		private const float CLEANUP_INTERVAL = 10f; // Clean up dead references every 10 seconds

		// Reflection caching
		private PropertyInfo _harvestProperty = null;
		private PropertyInfo _durationProperty = null;
		private PropertyInfo _audioProperty = null;
		private PropertyInfo _yieldCountProperty = null;

		// === CONFIGURABLE HARVEST PROPERTIES ===
		public int HarvestDurationMinutes = 10;
		public string HarvestAudio = "Play_HarvestingMetalSaw";
		public int YieldCount = 1;
		// ========================================

		public override void OnInitializeMelon()
		{
			MelonLogger.Msg("[CanHarvester] *** CanHarvester Mod initialized ***");
			MelonLogger.Msg($"[CanHarvester] Harvest config: Duration={HarvestDurationMinutes}min, Audio={HarvestAudio}, YieldCount={YieldCount}");
		}

		public override void OnUpdate()
		{
			try
			{
				// Detect scene changes
				string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
				if (_lastScene != currentScene)
				{
					_lastScene = currentScene;
					CleanupScene();
				}

				// Only initialize when Panel_Inventory is found (means we're in-game)
				if (_panelInventory == null)
				{
					_panelInventory = UnityEngine.Object.FindObjectOfType<Panel_Inventory>();
					if (_panelInventory != null && !_harvestInitialized)
					{
						InitializeHarvest();
					}
					return;
				}

				CheckSelectedItem();

				// Periodically check for recycled cans (not every frame)
				_hookCheckTimer += Time.deltaTime;
				if (_hookCheckTimer >= HOOK_CHECK_INTERVAL)
				{
					_hookCheckTimer = 0f;
					HookPanelEnable();
				}

				// Periodically clean up dead references to prevent memory leak
				_cleanupTimer += Time.deltaTime;
				if (_cleanupTimer >= CLEANUP_INTERVAL)
				{
					_cleanupTimer = 0f;
					CleanupDeadReferences();
				}
			}
			catch (Exception ex)
			{
				MelonLogger.Error($"[CanHarvester] OnUpdate error: {ex.Message}\n{ex.StackTrace}");
			}
		}

		private void InitializeHarvest()
		{
			try
			{
				MelonLogger.Msg("[CanHarvester] Initializing Harvest object in scene");
				
				var inventory = GameManager.GetInventoryComponent();
				if (inventory == null)
				{
					MelonLogger.Warning("[CanHarvester] Inventory not ready yet");
					return;
				}

				// Cache reflection properties
				CacheReflectionProperties();
				if (_harvestProperty == null)
				{
					MelonLogger.Error("[CanHarvester] m_Harvest property not found! Cannot initialize.");
					_harvestInitialized = false;
					return;
				}

				// Spawn Cooking Pot
				GearItem cookingPotItem = GearItem.InstantiateGearItem("Gear_CookingPot");
				if (cookingPotItem == null || cookingPotItem.gameObject == null)
				{
					MelonLogger.Error("[CanHarvester] Failed to instantiate harvest object!");
					_harvestInitialized = false;
					return;
				}

				// Add to inventory to trigger full initialization
				inventory.AddGear(cookingPotItem);

				// Extract Harvest from spawned Pot
				object harvest = _harvestProperty.GetValue(cookingPotItem);
				if (harvest == null)
				{
					MelonLogger.Error("[CanHarvester] Spawned Harvest object is null!");
					inventory.RemoveGear(cookingPotItem.gameObject);
					_harvestInitialized = false;
					return;
				}

				// Remove from inventory but keep GearItem reference alive!
				inventory.RemoveGear(cookingPotItem.gameObject);

				// Keep reference to spawned Pot alive and cache Harvest
				_cookingPotTemplate = cookingPotItem;
				_cachedHarvest = harvest;
				_harvestInitialized = true;

				MelonLogger.Msg("[CanHarvester] Initialization successful - mod is ready!");
			}
			catch (Exception ex)
			{
				MelonLogger.Error($"[CanHarvester] InitializeHarvest EXCEPTION: {ex.Message}\n{ex.StackTrace}");
				_harvestInitialized = false;
			}
		}

		private void CacheReflectionProperties()
		{
			if (_harvestProperty != null)
				return; // Already cached

			try
			{
				_harvestProperty = typeof(GearItem).GetProperty("m_Harvest", 
					BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
				
				Type harvestType = _harvestProperty?.PropertyType;
				if (harvestType != null)
				{
					_durationProperty = harvestType.GetProperty("m_DurationMinutes", 
						BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
					_audioProperty = harvestType.GetProperty("m_Audio", 
						BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
					_yieldCountProperty = harvestType.GetProperty("YieldCount", 
						BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
				}
			}
			catch (Exception ex)
			{
				MelonLogger.Error($"[CanHarvester] Failed to cache reflection properties: {ex.Message}");
			}
		}

		private void CleanupScene()
		{
			try
			{
				_harvestInitialized = false;
				_panelInventory = null;
				_cookingPotTemplate = null;
				_cachedHarvest = null;
				_lastSelectedItem = null;
				_processedItems.Clear();
				_hookCheckTimer = 0f;
				_cleanupTimer = 0f;
			}
			catch (Exception ex)
			{
				MelonLogger.Error($"[CanHarvester] CleanupScene error: {ex.Message}");
			}
		}

		private void CleanupDeadReferences()
		{
			try
			{
				// Remove destroyed objects from the processed items set
				int initialCount = _processedItems.Count;
				_processedItems.RemoveWhere(item => item == null || item.gameObject == null);
				int removedCount = initialCount - _processedItems.Count;
				
				if (removedCount > 0)
				{
					//MelonLogger.Msg($"[CanHarvester] Cleaned up {removedCount} dead object reference(s)");
				}
			}
			catch (Exception ex)
			{
				MelonLogger.Error($"[CanHarvester] CleanupDeadReferences error: {ex.Message}");
			}
		}

		private void HookPanelEnable()
		{
			try
			{
				if (!_harvestInitialized)
					return;

				var allItems = UnityEngine.Object.FindObjectsOfType<GearItem>();
				if (allItems == null || allItems.Length == 0)
					return;

				foreach (var item in allItems)
				{
					// Skip null or destroyed items
					if (item == null || item.gameObject == null)
						continue;

					// Skip if already processed
					if (_processedItems.Contains(item))
						continue;

					if (item.name.Equals("GEAR_RecycledCan", StringComparison.OrdinalIgnoreCase))
					{
						InjectHarvest(item);
						_processedItems.Add(item);
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
				if (_panelInventory == null || !_harvestInitialized)
					return;

				var selectedItem = _panelInventory.GetCurrentlySelectedItem();

				if (selectedItem != null && !selectedItem.Equals(_lastSelectedItem))
				{
					_lastSelectedItem = selectedItem;

					var gearItem = selectedItem.m_GearItem;
					if (gearItem != null && gearItem.gameObject != null && gearItem.name.Equals("GEAR_RecycledCan", StringComparison.OrdinalIgnoreCase))
					{
						InjectHarvest(gearItem);
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
				if (gearItem == null || gearItem.gameObject == null || _harvestProperty == null)
					return;

				// Check if already has harvest
				object existing = _harvestProperty.GetValue(gearItem);
				if (existing != null)
					return;

				// Use cached Harvest from our spawned Pot reference
				if (_cachedHarvest == null)
					return;

				// Inject harvest
				if (_harvestProperty.CanWrite)
				{
					_harvestProperty.SetValue(gearItem, _cachedHarvest);
					TrySetCustomProperties(_cachedHarvest);
				}
			}
			catch (Exception ex)
			{
				MelonLogger.Error($"[CanHarvester] InjectHarvest error: {ex.Message}");
			}
		}

		private void TrySetCustomProperties(object harvestInstance)
		{
			if (harvestInstance == null)
				return;

			try
			{
				if (_durationProperty != null && _durationProperty.CanWrite)
				{
					_durationProperty.SetValue(harvestInstance, HarvestDurationMinutes);
				}
			}
			catch (Exception ex)
			{
				MelonLogger.Error($"[CanHarvester] Failed to set Duration: {ex.Message}");
			}

			try
			{
				if (_audioProperty != null && _audioProperty.CanWrite)
				{
					_audioProperty.SetValue(harvestInstance, HarvestAudio);
				}
			}
			catch (Exception ex)
			{
				MelonLogger.Error($"[CanHarvester] Failed to set Audio: {ex.Message}");
			}

			try
			{
				if (_yieldCountProperty != null && _yieldCountProperty.CanWrite)
				{
					_yieldCountProperty.SetValue(harvestInstance, YieldCount);
				}
			}
			catch (Exception ex)
			{
				MelonLogger.Error($"[CanHarvester] Failed to set YieldCount: {ex.Message}");
			}
		}
	}
}