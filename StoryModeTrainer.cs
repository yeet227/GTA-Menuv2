using System;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using GTA;
using GTA.Input;
using GTA.Math;
using GTA.Native;
using LemonUI;
using LemonUI.Menus;

namespace StoryModeTrainerMod
{
    /// <summary>
    /// Offline Story Mode trainer for GTA 5 Legacy (1.0.3889.0) providing player/movement/weapon
    /// enhancements, in-game character switching, vehicle physics multipliers, safe offline Online assets,
    /// camera-relative No-Clip flight, world/time/weather controls, and Story Mode heist take protection &amp; payout editing.
    /// </summary>
    public class StoryModeTrainer : Script
    {
        private const int DefaultMaxWantedLevel = 5;
        private const int CashIncrement = 100000;
        private const int QuickHeistCashBoost = 10000000;
        private const int InviteLockIntervalMs = 250;
        private const int OnlineWarningCooldownMs = 3000;

        // GTA V Legacy 1.0.3889.0 Single-Player selector.c Character Wheel UI struct globals
        private static readonly Version SupportedGlobalFileVersion = new Version(1, 0, 3889, 0);
        private const int GlobalSelectorOnlineTriggerFlag = 3;
        private const int GlobalSelectorWheelDirtyFlag = 24430 + 3;      // 24433
        private const int GlobalSelectorSelectedQuadrant = 24430 + 69;   // 24499 (2 = Bottom / Online slot)

        private static readonly int FreemodeScriptHash = ComputeJenkinsHash("freemode");
        private static readonly int CreatorScriptHash = ComputeJenkinsHash("creator");
        private static readonly int FmmcLauncherScriptHash = ComputeJenkinsHash("fmmc_launcher");
        private static readonly int AmLauncherScriptHash = ComputeJenkinsHash("am_launcher");
        private static readonly int PauseMenuMpScriptHash = ComputeJenkinsHash("pausemenu_multiplayer");
        private static readonly int MainTransitionScriptHash = ComputeJenkinsHash("maintransition");
        private static readonly int[] BlockedFrontendMenuHashes =
        {
            ComputeJenkinsHash("FE_MENU_VERSION_MP_PAUSE"),
            ComputeJenkinsHash("FE_MENU_VERSION_PRE_LOBBY"),
            ComputeJenkinsHash("FE_MENU_VERSION_LOBBY"),
            ComputeJenkinsHash("FE_MENU_VERSION_MP_CHARACTER_SELECT"),
            ComputeJenkinsHash("FE_MENU_VERSION_MP_CHARACTER_CREATION"),
            ComputeJenkinsHash("FE_MENU_VERSION_CORONA"),
            ComputeJenkinsHash("FE_MENU_VERSION_CORONA_JOINED_PLAYERS"),
            ComputeJenkinsHash("FE_MENU_VERSION_CORONA_INVITE_PLAYERS"),
            ComputeJenkinsHash("FE_MENU_VERSION_CORONA_RACE"),
            ComputeJenkinsHash("FE_MENU_VERSION_CORONA_BETTING")
        };

        private static readonly string[] VehiclePresets =
        {
            "ADDER",
            "ZENTORNO",
            "T20",
            "KURUMA2",
            "INSURGENT",
            "BUFFALO2",
            "SANCHEZ",
            "LAZER",
            "BUZZARD",
            "DINGHY"
        };

        private static readonly string[] WeatherNames =
        {
            "EXTRASUNNY",
            "CLEAR",
            "CLOUDS",
            "SMOG",
            "FOGGY",
            "OVERCAST",
            "RAIN",
            "THUNDER",
            "CLEARING",
            "NEUTRAL",
            "SNOW",
            "BLIZZARD",
            "SNOWLIGHT",
            "XMAS",
            "HALLOWEEN"
        };

        private static readonly string[] WeatherLabels =
        {
            "Extra Sunny",
            "Clear",
            "Clouds",
            "Smog",
            "Foggy",
            "Overcast",
            "Rain",
            "Thunderstorm",
            "Clearing",
            "Neutral",
            "Snow",
            "Blizzard",
            "Light Snow",
            "Christmas Snow",
            "Halloween"
        };

        private sealed class ModSliderBinding
        {
            public VehicleModType ModType { get; }
            public string Label { get; }
            public NativeListItem<int> Item { get; }

            public ModSliderBinding(VehicleModType modType, string label, NativeListItem<int> item)
            {
                ModType = modType;
                Label = label;
                Item = item;
            }
        }

        private readonly ObjectPool _pool = new ObjectPool();
        private readonly NativeMenu _mainMenu = new NativeMenu(
            "Story Trainer",
            "SINGLE-PLAYER ONLY",
            "Offline Story Mode Trainer"
        );
        private readonly NativeMenu _playerMenu = new NativeMenu(
            "Story Trainer",
            "PLAYER & MOVEMENT",
            "God mode, infinite sprint, special ability, speed/jump multipliers, health, wanted, and teleport."
        );
        private readonly NativeMenu _weaponsMenu = new NativeMenu(
            "Story Trainer",
            "WEAPONS & AMMO",
            "Grant all Story & Online DLC weapons, max bullets, attachments, and infinite ammo."
        );
        private readonly NativeMenu _characterMenu = new NativeMenu(
            "Story Trainer",
            "CHARACTER SWITCHER",
            "Switch character models in-game, wear Freemode assets, or restore Story protagonists."
        );
        private readonly NativeMenu _vehicleMenu = new NativeMenu(
            "Story Trainer",
            "VEHICLE SPAWNER",
            "Spawn Story Mode or GTA Online DLC vehicles safely offline."
        );
        private readonly NativeMenu _customsMenu = new NativeMenu(
            "Story Trainer",
            "VEHICLE PHYSICS & UPGRADES",
            "Tune acceleration, top speed, braking, traction multipliers, and customs upgrades."
        );
        private readonly NativeMenu _onlineAssetsMenu = new NativeMenu(
            "Story Trainer",
            "ONLINE ASSETS (OFFLINE ONLY)",
            "Stream GTA 5 Legacy Online interiors, islands & vehicles strictly offline in Story Mode."
        );
        private readonly NativeMenu _noClipMenu = new NativeMenu(
            "Story Trainer",
            "NO-CLIP FLIGHT",
            "Fly anywhere around the map and through walls without consequences."
        );
        private readonly NativeMenu _heistMenu = new NativeMenu(
            "Story Trainer",
            "HEIST MANAGER",
            "Edit Story Mode heist takes, payouts, and block police bag/money loss."
        );
        private readonly NativeMenu _worldMenu = new NativeMenu(
            "Story Trainer",
            "WORLD, TIME & CONFIG",
            "Control Single-Player weather, time of day, and persistent trainer INI settings."
        );

        // Player & Movement items
        private readonly NativeCheckboxItem _godModeItem = new NativeCheckboxItem(
            "God Mode (Player)",
            "Keeps your character invincible against bullets, fire, explosions, collisions, and drowning.",
            false
        );
        private readonly NativeCheckboxItem _vehicleGodModeItem = new NativeCheckboxItem(
            "God Mode (Vehicle)",
            "Keeps your current vehicle invincible against crashes, bullets, tires bursting, and explosions.",
            false
        );
        private readonly NativeCheckboxItem _playerNoClipItem = new NativeCheckboxItem(
            "No-Clip (Fly / Phase Walls)",
            "Toggle 3D flight with zero collision, ragdoll, or fall consequences.",
            false
        );
        private readonly NativeCheckboxItem _neverWantedItem = new NativeCheckboxItem(
            "Never Wanted",
            "Locks max wanted level to 0 and clears active police attention.",
            false
        );
        private readonly NativeCheckboxItem _infiniteSprintItem = new NativeCheckboxItem(
            "Infinite Sprint (Stamina)",
            "Prevents sprint fatigue and keeps player stamina permanently full.",
            false
        );
        private readonly NativeCheckboxItem _infiniteSpecialAbilityItem = new NativeCheckboxItem(
            "Infinite Special Ability",
            "Keeps Michael, Franklin, and Trevor's Special Ability meter permanently full.",
            false
        );
        private readonly NativeListItem<string> _runSpeedItem = new NativeListItem<string>(
            "Run Speed Multiplier",
            "Scale running and sprinting movement speed (1.0x up to 5.0x).",
            PlayerEnhancementsController.RunSpeedLabels
        );
        private readonly NativeListItem<string> _swimSpeedItem = new NativeListItem<string>(
            "Swim Speed Multiplier",
            "Scale surface and underwater swimming speed (1.0x up to 4.0x).",
            PlayerEnhancementsController.SwimSpeedLabels
        );
        private readonly NativeListItem<string> _jumpModeItem = new NativeListItem<string>(
            "Jump Multiplier / Super Jump",
            "Multiply jump height (1.5x-5.0x) with safe landing shield, or use Super/Beast jump.",
            PlayerEnhancementsController.JumpModeLabels
        );
        private readonly NativeListItem<int> _wantedLevelItem = new NativeListItem<int>(
            "Wanted Level",
            "Set current police wanted stars (0-5). Press Enter or Left/Right.",
            0, 1, 2, 3, 4, 5
        );

        // Weapons & Ammo items
        private readonly NativeCheckboxItem _infiniteAmmoItem = new NativeCheckboxItem(
            "Infinite Ammo",
            "Keeps reserve ammunition infinite for all equipped & carried weapons.",
            false
        );
        private readonly NativeCheckboxItem _infiniteClipItem = new NativeCheckboxItem(
            "Infinite Clip (No Reload)",
            "Fires continuously without magazine reloads.",
            false
        );

        // Character Switcher items
        private readonly NativeCheckboxItem _characterWheelSwapItem = new NativeCheckboxItem(
            "Character Wheel Swap (Alt / F5-F7)",
            "Swap freely between Michael, Franklin & Trevor on the Character Wheel at any time in Story Mode.",
            true
        );
        private readonly NativeListItem<string> _characterPresetItem = new NativeListItem<string>(
            "Character Model",
            "Cycle Story protagonists, Freemode Online peds, or NPCs and press Enter to switch.",
            PlayerEnhancementsController.GetCharacterLabels()
        );

        // No-Clip items
        private readonly NativeCheckboxItem _noClipActiveItem = new NativeCheckboxItem(
            "No-Clip Active",
            "WASD: Move | Space: Up | Ctrl/X: Down | Shift: Boost | Key: F9 (configurable).",
            false
        );
        private readonly NativeListItem<string> _noClipSpeedItem = new NativeListItem<string>(
            "Flight Speed",
            "Adjust base No-Clip flight speed preset.",
            NoClipController.SpeedLabels
        );
        private readonly NativeCheckboxItem _noClipAlignHeadingItem = new NativeCheckboxItem(
            "Align Heading With Camera",
            "Steer character or vehicle yaw smoothly with camera look direction.",
            true
        );
        private readonly NativeCheckboxItem _noClipFallShieldItem = new NativeCheckboxItem(
            "3s Exit Fall Shield",
            "Prevents fatal impact or ragdoll for 3 seconds if disabling No-Clip mid-air.",
            true
        );
        private readonly NativeCheckboxItem _noClipSnapGroundItem = new NativeCheckboxItem(
            "Snap to Ground on Exit",
            "Automatically warp onto solid ground/water directly below when exiting No-Clip.",
            false
        );

        // Heist Manager items
        private readonly NativeListItem<string> _heistTargetItem = new NativeListItem<string>(
            "Target Heist",
            "Choose which Story Mode heist to inspect or edit.",
            HeistManager.HeistLabels
        );
        private readonly NativeCheckboxItem _protectHeistTakeItem = new NativeCheckboxItem(
            "Block Police Heist Money Loss",
            "Stops police bullets, explosions, damage, and drop penalties from draining heist cash.",
            false
        );
        private readonly NativeCheckboxItem _heistIgnorePoliceItem = new NativeCheckboxItem(
            "Police Ignore Player (Heists)",
            "Makes police completely ignore you while an active Story Mode heist is running.",
            false
        );
        private readonly NativeCheckboxItem _keepFullTakeItem = new NativeCheckboxItem(
            "Keep 100% Take (Skip Cuts)",
            "Skips crew & agent cuts (such as Paleto 78% / Lester 12%) when editing heist takes.",
            true
        );
        private readonly NativeCheckboxItem _lockHeistTakeItem = new NativeCheckboxItem(
            "Lock Take Through Mission End",
            "Continuously enforces your edited take across the Heist Passed summary screen.",
            false
        );
        private readonly NativeListItem<string> _takePresetItem = new NativeListItem<string>(
            "Take Preset",
            "Cycle preset take amounts ($2.5M-$1,000M). Press Enter to apply & lock.",
            BuildTakePresetLabels()
        );

        // Vehicle Spawner items
        private readonly NativeListItem<string> _spawnListItem = new NativeListItem<string>(
            "Story / Base Model",
            "Cycle base vehicle presets (Left/Right) and press Enter to spawn.",
            VehiclePresets
        );
        private readonly NativeListItem<string> _onlineVehicleListItem = new NativeListItem<string>(
            "Online DLC Vehicle",
            "Cycle GTA Online DLC vehicles (Oppressor2, Deluxo, Toreador, Raiju...) and press Enter to spawn.",
            OnlineAssetsController.OnlineVehiclePresets
        );
        private readonly NativeCheckboxItem _spawnMaxUpgradedItem = new NativeCheckboxItem(
            "Spawn Vehicles Max Upgraded",
            "Automatically installs maximum Engine, Brakes, Transmission, Armor, and Turbo on spawned vehicles.",
            false
        );
        private readonly NativeCheckboxItem _persistOnlineVehiclesItem = new NativeCheckboxItem(
            "Keep Online Vehicles Persistent",
            "Marks spawned vehicles as owned & mission-persistent in Story Mode.",
            true
        );
        private readonly NativeCheckboxItem _replacePrevVehicleItem = new NativeCheckboxItem(
            "Replace Previous Vehicle",
            "Despawn the previous trainer-spawned vehicle when spawning a new one.",
            true
        );

        // Vehicle Physics & Upgrades items
        private readonly NativeListItem<string> _vehPowerMultItem = new NativeListItem<string>(
            "Acceleration / Power Multiplier",
            "Scale acceleration and engine torque (1.0x up to 12.0x) on your current vehicle.",
            VehiclePhysicsController.PowerLabels
        );
        private readonly NativeListItem<string> _vehTopSpeedMultItem = new NativeListItem<string>(
            "Top Speed Boost",
            "Raise maximum flat top speed (+0% up to +400% / 5x) on your current vehicle.",
            VehiclePhysicsController.TopSpeedLabels
        );
        private readonly NativeListItem<string> _vehBrakeMultItem = new NativeListItem<string>(
            "Brake Force Multiplier",
            "Scale braking & handbrake stopping power (1.0x up to 5.0x) cleanly.",
            VehiclePhysicsController.BrakeLabels
        );
        private readonly NativeListItem<string> _vehTractionMultItem = new NativeListItem<string>(
            "Traction / Grip Multiplier",
            "Increase cornering grip and downforce (1.0x up to 4.0x) cleanly.",
            VehiclePhysicsController.TractionLabels
        );
        private readonly NativeCheckboxItem _vehAutoRepairItem = new NativeCheckboxItem(
            "Auto-Repair Current Vehicle",
            "Automatically repairs body, engine, tires, and fuel tank damage while driving.",
            false
        );

        // Upgrade levels: 0 = Stock (-1 native index), 1..N = Progressive tiers (0..N-1)
        private readonly NativeListItem<int> _engineLevelItem = new NativeListItem<int>(
            "Engine Level", "Engine tune tier (0 = Stock).", 0, 1, 2, 3, 4
        );
        private readonly NativeListItem<int> _brakesLevelItem = new NativeListItem<int>(
            "Brakes Level", "Brake upgrade tier (0 = Stock).", 0, 1, 2, 3
        );
        private readonly NativeListItem<int> _transLevelItem = new NativeListItem<int>(
            "Transmission Level", "Transmission upgrade tier (0 = Stock).", 0, 1, 2, 3
        );
        private readonly NativeListItem<int> _armorLevelItem = new NativeListItem<int>(
            "Armor Level", "Vehicle armor plating tier (0 = Stock).", 0, 1, 2, 3, 4, 5
        );
        private readonly NativeCheckboxItem _turboItem = new NativeCheckboxItem(
            "Turbo Tuning",
            "Toggle turbocharger installation on current vehicle.",
            false
        );
        private readonly NativeCheckboxItem _bulletproofTiresItem = new NativeCheckboxItem(
            "Bulletproof Tires",
            "Prevents current vehicle tires from bursting from gunfire or spikes.",
            false
        );

        // Online Assets & IPL Maps (Offline Only) items
        private readonly NativeCheckboxItem _mpMapsModeItem = new NativeCheckboxItem(
            "Offline DLC Map Changesets",
            "Mounts local DLC map changesets (ON_ENTER_MP + SET_INSTANCE_PRIORITY_HINT) offline without Online loading screens.",
            false
        );
        private readonly NativeCheckboxItem _suspendShopControllerItem = new NativeCheckboxItem(
            "Block DLC Vehicle Despawn",
            "Terminates shop_controller if MP DLC cars auto-delete in SP (reload save to restore shops).",
            false
        );
        private readonly NativeCheckboxItem _cayoPericoItem = new NativeCheckboxItem(
            "Load Cayo Perico Island",
            "Streams Cayo Perico meshes, HeistIsland static collisions, open hangar, water & radar offline.",
            false
        );
        private readonly NativeCheckboxItem _diamondCasinoItem = new NativeCheckboxItem(
            "Load Diamond Casino & Resort",
            "Streams Casino main floor, garage, carpark & furnished Master Penthouse offline.",
            false
        );
        private readonly NativeCheckboxItem _aircraftCarrierItem = new NativeCheckboxItem(
            "Load USS Luxington Aircraft Carrier",
            "Streams the Online aircraft carrier exterior and hangar interiors off SE coast (auto-unloads North Yankton).",
            false
        );
        private readonly NativeCheckboxItem _superyachtItem = new NativeCheckboxItem(
            "Load Superyacht (Dignity)",
            "Streams yacht hull/deck collision (smboat) & furnished interior rooms off Pacific Bluffs.",
            false
        );
        private readonly NativeCheckboxItem _northYanktonItem = new NativeCheckboxItem(
            "Load North Yankton (Snow Map)",
            "Streams Ludendorff snow terrain, cemetery, bank & prologue roads offline (auto-unloads Cayo & Carrier).",
            false
        );
        private readonly NativeCheckboxItem _gunrunningBunkerItem = new NativeCheckboxItem(
            "Load Gunrunning Underground Bunker",
            "Streams subterranean Bunker MLO with full style, office, security & gun range entity sets offline.",
            false
        );
        private readonly NativeCheckboxItem _doomsdayFacilityItem = new NativeCheckboxItem(
            "Load Doomsday Underground Facility",
            "Streams subterranean Doomsday Facility MLO with shell, lounge, orbital room & security sets offline.",
            false
        );
        private readonly NativeCheckboxItem _cargoShipItem = new NativeCheckboxItem(
            "Load Port Cargo Ship",
            "Streams the intact freighter cargo ship deck & hull at Port of South Los Santos.",
            false
        );
        private readonly NativeCheckboxItem _storyInteriorsItem = new NativeCheckboxItem(
            "Load Story Mission Interiors",
            "Unlocks & uncaps FIB Lobby, Coroner Morgue, Lifeinvader Office, O'Neil Ranch & Cluckin' Bell Factory.",
            false
        );

        // World, Time & Config items
        private readonly NativeListItem<string> _weatherPresetItem = new NativeListItem<string>(
            "Weather Preset",
            "Cycle Single-Player weather presets (Left/Right) and press Enter to apply.",
            WeatherLabels
        );
        private readonly NativeCheckboxItem _freezeTimeItem = new NativeCheckboxItem(
            "Freeze Clock Time",
            "Pauses the Single-Player clock at the current time of day.",
            false
        );

        private readonly ModSliderBinding[] _modBindings;
        private readonly string _iniPath;
        private TrainerConfig _config;
        private readonly NoClipController _noClip;
        private readonly HeistManager _heists;
        private readonly PlayerEnhancementsController _enhancements;
        private readonly VehiclePhysicsController _vehiclePhysics;
        private readonly OnlineAssetsController _onlineAssets;
        private Keys _toggleKey;
        private Keys _noClipToggleKey;

        private Vehicle _lastSpawnedVehicle;
        private Vehicle _lastGodVehicle;
        private int _lastWheelHintGameTime = -OnlineWarningCooldownMs;
        private bool _pauseImmunityApplied;
        private bool _welcomeShown;
        private bool _suppressUiSync;
        private bool _isPromptingInput;
        private bool _aborted;
        private bool _clockFrozen;
        private bool _weatherOverridden;
        private int _nextInviteLockGameTime;
        private int _lastOnlineWarningGameTime = -OnlineWarningCooldownMs;
        private static bool? _cachedSupportedGlobalBuild;
        private bool _cachedMpBlockedThisTick;
        private bool _cleanedUp;
        private bool _isBusyOperation;
        private bool _trainerAppliedPedProtections;
        private int _lastPedProofHandle;
        private int _lastPedProofMask = -1;
        private bool _trainerAppliedVehicleGod;
        private readonly List<Vehicle> _spawnedPersistentVehicles = new List<Vehicle>();

        public StoryModeTrainer()
        {
            if (TryGetMultiplayerOrTransitionReason(out string startupTrigger))
            {
                Notify(
                    $"~r~Story Mode Trainer disabled!~s~ ({startupTrigger}) Exit GTA 5 and run 2-Disable-And-CleanForOnline.bat before playing Online.",
                    true
                );
                _aborted = true;
                return;
            }

            // Lock down realtime multiplayer & auto-lobby flags on frame 0 before any user interaction.
            Function.Call(Hash.NETWORK_DISABLE_REALTIME_MULTIPLAYER);
            Function.Call(Hash.SHUTDOWN_SESSION_CLEARS_AUTO_MULTIPLAYER, true);
            Function.Call(Hash.LOBBY_SET_AUTO_MULTIPLAYER, false);
            Function.Call(Hash.LOBBY_SET_AUTO_MULTIPLAYER_EVENT, false);
            Function.Call(Hash.LOBBY_SET_AUTO_MP_RANDOM_JOB, false);
            Function.Call(Hash.NETWORK_BLOCK_INVITES, true);

            _iniPath = string.IsNullOrEmpty(Filename) ? null : Path.ChangeExtension(Filename, ".ini");
            _config = TrainerConfig.Load(_iniPath);
            _toggleKey = _config.MenuToggleKey;
            _noClipToggleKey = _config.NoClipToggleKey;

            Func<bool> isMpBlocked = () => _aborted || _cachedMpBlockedThisTick;

            _noClip = new NoClipController(
                _config.NoClipBaseSpeed,
                _config.NoClipExitFallShield,
                _config.NoClipSnapToGroundOnExit,
                isMpBlocked
            );
            _heists = new HeistManager(
                _config.HeistProtectTakeByDefault,
                _config.HeistKeepFullTakeWithoutCuts,
                isMpBlocked
            );
            _enhancements = new PlayerEnhancementsController(
                _config.PlayerInfiniteAmmo,
                _config.PlayerInfiniteClip,
                _config.PlayerInfiniteSprint,
                isMpBlocked,
                WaitWithOnlineGuard
            );
            _vehiclePhysics = new VehiclePhysicsController(isMpBlocked);
            _onlineAssets = new OnlineAssetsController(
                _config.OnlineKeepSpawnedVehiclesPersistent,
                _config.OnlineAutoUnlockMpMaps,
                isMpBlocked
            );

            ApplyLoadedConfigToState(_config);

            _modBindings = new[]
            {
                new ModSliderBinding(VehicleModType.Engine, "Engine", _engineLevelItem),
                new ModSliderBinding(VehicleModType.Brakes, "Brakes", _brakesLevelItem),
                new ModSliderBinding(VehicleModType.Transmission, "Transmission", _transLevelItem),
                new ModSliderBinding(VehicleModType.Armor, "Armor", _armorLevelItem)
            };

            SetupMenus();

            Tick += OnTick;
            KeyDown += OnKeyDown;
            Aborted += OnAborted;
        }

        private void ApplyLoadedConfigToState(TrainerConfig cfg)
        {
            _suppressUiSync = true;
            try
            {
                _toggleKey = cfg.MenuToggleKey;
                _noClipToggleKey = cfg.NoClipToggleKey;
                _replacePrevVehicleItem.Checked = cfg.DespawnPreviousVehicle;
                _spawnMaxUpgradedItem.Checked = cfg.SpawnMaxUpgraded;
                _vehiclePhysics.AutoRepairCurrentVehicle = cfg.AutoRepairVehicle;
                _vehAutoRepairItem.Checked = cfg.AutoRepairVehicle;

                _noClip.BaseSpeed = cfg.NoClipBaseSpeed;
                _noClip.ExitFallShieldEnabled = cfg.NoClipExitFallShield;
                _noClip.SnapToGroundOnExit = cfg.NoClipSnapToGroundOnExit;
                _noClipFallShieldItem.Checked = _noClip.ExitFallShieldEnabled;
                _noClipSnapGroundItem.Checked = _noClip.SnapToGroundOnExit;
                _noClipAlignHeadingItem.Checked = _noClip.AlignHeadingWithCamera;
                _noClipSpeedItem.SelectedIndex = _noClip.SpeedPresetIndex;

                _heists.ProtectHeistTake = cfg.HeistProtectTakeByDefault;
                _heists.KeepFullTakeWithoutCuts = cfg.HeistKeepFullTakeWithoutCuts;
                _protectHeistTakeItem.Checked = _heists.ProtectHeistTake;
                _keepFullTakeItem.Checked = _heists.KeepFullTakeWithoutCuts;

                _godModeItem.Checked = cfg.PlayerGodMode;
                _vehicleGodModeItem.Checked = cfg.PlayerVehicleGodMode;
                _neverWantedItem.Checked = cfg.PlayerNeverWanted;
                if (!cfg.PlayerNeverWanted && Game.MaxWantedLevel == 0)
                {
                    Game.MaxWantedLevel = DefaultMaxWantedLevel;
                }
                _enhancements.InfiniteAmmoEnabled = cfg.PlayerInfiniteAmmo;
                _enhancements.InfiniteClipEnabled = cfg.PlayerInfiniteClip;
                _enhancements.InfiniteSprintEnabled = cfg.PlayerInfiniteSprint;
                _enhancements.InfiniteSpecialAbilityEnabled = cfg.PlayerInfiniteSpecialAbility;
                _enhancements.CharacterWheelSwapEnabled = cfg.PlayerCharacterWheelSwap;

                _infiniteAmmoItem.Checked = _enhancements.InfiniteAmmoEnabled;
                _infiniteClipItem.Checked = _enhancements.InfiniteClipEnabled;
                _infiniteSprintItem.Checked = _enhancements.InfiniteSprintEnabled;
                _infiniteSpecialAbilityItem.Checked = _enhancements.InfiniteSpecialAbilityEnabled;
                _characterWheelSwapItem.Checked = _enhancements.CharacterWheelSwapEnabled;

                _onlineAssets.KeepSpawnedVehiclesPersistent = cfg.OnlineKeepSpawnedVehiclesPersistent;
                _persistOnlineVehiclesItem.Checked = _onlineAssets.KeepSpawnedVehiclesPersistent;
                if (_onlineAssets.ExplicitMpMapsPreference != cfg.OnlineAutoUnlockMpMaps)
                {
                    _onlineAssets.SetMpMapsMode(cfg.OnlineAutoUnlockMpMaps, userExplicit: cfg.OnlineAutoUnlockMpMaps);
                }
                _mpMapsModeItem.Checked = _onlineAssets.MpMapsModeEnabled;
            }
            finally
            {
                _suppressUiSync = false;
            }
        }

        private static string[] BuildTakePresetLabels()
        {
            string[] labels = new string[HeistManager.TakePresetAmounts.Length];
            for (int i = 0; i < HeistManager.TakePresetAmounts.Length; i++)
            {
                labels[i] = HeistManager.FormatCash(HeistManager.TakePresetAmounts[i]);
            }
            return labels;
        }

        private static string GetMultiplayerActiveReason()
        {
            if (Function.Call<bool>(Hash.NETWORK_IS_SESSION_ACTIVE)) return "NETWORK_IS_SESSION_ACTIVE";
            if (Function.Call<bool>(Hash.NETWORK_IS_SESSION_STARTED)) return "NETWORK_IS_SESSION_STARTED";
            if (Function.Call<bool>(Hash.NETWORK_IS_IN_SESSION)) return "NETWORK_IS_IN_SESSION";
            if (Function.Call<bool>(Hash.NETWORK_IS_SESSION_BUSY)) return "NETWORK_IS_SESSION_BUSY";
            if (Function.Call<bool>(Hash.NETWORK_IS_IN_TRANSITION)) return "NETWORK_IS_IN_TRANSITION";
            if (Function.Call<bool>(Hash.NETWORK_IS_TRANSITION_STARTED)) return "NETWORK_IS_TRANSITION_STARTED";
            if (Function.Call<bool>(Hash.NETWORK_IS_TRANSITION_BUSY)) return "NETWORK_IS_TRANSITION_BUSY";
            if (Function.Call<bool>(Hash.NETWORK_IS_TRANSITION_MATCHMAKING)) return "NETWORK_IS_TRANSITION_MATCHMAKING";
            if (Function.Call<bool>(Hash.NETWORK_IS_TRANSITION_TO_GAME)) return "NETWORK_IS_TRANSITION_TO_GAME";
            if (Function.Call<bool>(Hash.NETWORK_IS_GAME_IN_PROGRESS)) return "NETWORK_IS_GAME_IN_PROGRESS";
            if (Function.Call<bool>(Hash.NETWORK_IS_ACTIVITY_SESSION)) return "NETWORK_IS_ACTIVITY_SESSION";
            if (Function.Call<bool>(Hash.NETWORK_IS_IN_TUTORIAL_SESSION)) return "NETWORK_IS_IN_TUTORIAL_SESSION";
            if (Function.Call<bool>(Hash.NETWORK_IS_TUTORIAL_SESSION_CHANGE_PENDING)) return "NETWORK_IS_TUTORIAL_SESSION_CHANGE_PENDING";
            if (Function.Call<bool>(Hash.NETWORK_IS_IN_MP_CUTSCENE)) return "NETWORK_IS_IN_MP_CUTSCENE";
            if (Function.Call<bool>(Hash.NETWORK_IS_PLAYER_IN_MP_CUTSCENE)) return "NETWORK_IS_PLAYER_IN_MP_CUTSCENE";
            if (Function.Call<bool>(Hash.NETWORK_IS_IN_SPECTATOR_MODE)) return "NETWORK_IS_IN_SPECTATOR_MODE";
            if (Function.Call<bool>(Hash.NETWORK_HAS_PLAYER_STARTED_TRANSITION)) return "NETWORK_HAS_PLAYER_STARTED_TRANSITION";
            if (Function.Call<bool>(Hash.NETWORK_IS_QUEUING_FOR_SESSION_JOIN)) return "NETWORK_IS_QUEUING_FOR_SESSION_JOIN";
            if (Function.Call<bool>(Hash.NETWORK_IS_FINDING_GAMERS)) return "NETWORK_IS_FINDING_GAMERS";
            if (Function.Call<bool>(Hash.NETWORK_SESSION_IS_AWAITING_INVITE_RESPONSE)) return "NETWORK_SESSION_IS_AWAITING_INVITE_RESPONSE";
            if (IsScriptRunningByHash(FreemodeScriptHash)) return "SCRIPT:freemode";
            if (IsScriptRunningByHash(CreatorScriptHash)) return "SCRIPT:creator";
            if (IsScriptRunningByHash(FmmcLauncherScriptHash)) return "SCRIPT:fmmc_launcher";
            if (IsScriptRunningByHash(AmLauncherScriptHash)) return "SCRIPT:am_launcher";
            return null;
        }

        private static bool IsMultiplayerActive()
        {
            return GetMultiplayerActiveReason() != null;
        }

        private bool TryGetMultiplayerOrTransitionReason(out string triggerReason)
        {
            triggerReason = GetMultiplayerActiveReason();
            if (triggerReason != null)
            {
                return true;
            }

            if (TryGetPlayer(out Ped player) && player.PedType == PedType.NetworkPlayer)
            {
                triggerReason = "PedType.NetworkPlayer";
                return true;
            }

            return false;
        }

        private bool IsMultiplayerOrTransitionActive()
        {
            return TryGetMultiplayerOrTransitionReason(out _);
        }

        private void WaitWithOnlineGuard(int ms)
        {
            Script.Wait(ms);
            if (_aborted)
            {
                return;
            }

            EnforceStoryModeOnlineLockout();
            if (TryGetMultiplayerOrTransitionReason(out string mpTrigger))
            {
                _cachedMpBlockedThisTick = true;
                AttemptCancelOnlineTransition();
                Notify(
                    $"~r~GTA Online transition blocked & Trainer disabled!~s~ ({mpTrigger}) Exit GTA 5 and run 2-Disable-And-CleanForOnline.bat before playing Online.",
                    true
                );
                CleanupBeforeAbort();
                Abort();
            }
        }

        private static int ComputeJenkinsHash(string value)
        {
            return unchecked((int)AtHashValue.ComputeHash(value));
        }

        private static bool IsScriptRunning(string scriptName)
        {
            return IsScriptRunningByHash(ComputeJenkinsHash(scriptName));
        }

        private static bool IsScriptRunningByHash(int scriptHash)
        {
            return Function.Call<int>(Hash.GET_NUMBER_OF_THREADS_RUNNING_THE_SCRIPT_WITH_THIS_HASH, scriptHash) > 0;
        }

        private bool IsMultiplayerFrontendMenuActive()
        {
            int activeVersion = Function.Call<int>(Hash.GET_CURRENT_FRONTEND_MENU_VERSION);
            for (int i = 0; i < BlockedFrontendMenuHashes.Length; i++)
            {
                if (activeVersion == BlockedFrontendMenuHashes[i])
                {
                    return true;
                }
            }
            return false;
        }

        private void PostRateLimitedOnlineWarning(string message)
        {
            int now = Game.GameTime;
            int elapsed = now - _lastOnlineWarningGameTime;
            if (elapsed >= OnlineWarningCooldownMs || elapsed < 0)
            {
                _lastOnlineWarningGameTime = now;
                Notify(message, true);
            }
        }

        /// <summary>
        /// Actively prevents entering GTA Online while StoryModeTrainer is running by blocking
        /// character-wheel MP selection (Slot 2), selector.c Online trigger globals, MainTransition,
        /// pause-menu Online tabs, Social Club invites, and auto-lobby flags, while keeping Single-Player
        /// character wheel slots (Michael/Franklin/Trevor) fully usable.
        /// </summary>
        private void EnforceStoryModeOnlineLockout()
        {
            Controls.DisableControlActionThisFrame(ControlType.PlayerControl, ControlAction.SelectCharacterMultiplayer, true);
            Controls.DisableControlActionThisFrame(ControlType.FrontendControl, ControlAction.SelectCharacterMultiplayer, true);
            Controls.DisableControlActionThisFrame(ControlType.FrontendControl, ControlAction.FrontendSocialClub, true);
            Controls.DisableControlActionThisFrame(ControlType.FrontendControl, ControlAction.FrontendSocialClubSecondary, true);

            if (Controls.IsDisabledControlJustPressed(ControlType.PlayerControl, ControlAction.SelectCharacterMultiplayer)
                || Controls.IsDisabledControlJustPressed(ControlType.FrontendControl, ControlAction.SelectCharacterMultiplayer)
                || Controls.IsDisabledControlJustPressed(ControlType.FrontendControl, ControlAction.FrontendSocialClub)
                || Controls.IsDisabledControlJustPressed(ControlType.FrontendControl, ControlAction.FrontendSocialClubSecondary))
            {
                PostRateLimitedOnlineWarning("~r~Blocked:~s~ GTA Online & Social Club entry is locked while mods are active. Exit GTA 5 & clean mods before going Online.");
            }

            EnforceSelectorOnlineSlotLockout();

            int now = Game.GameTime;
            if (now >= _nextInviteLockGameTime || (_nextInviteLockGameTime - now) > (InviteLockIntervalMs * 2))
            {
                _nextInviteLockGameTime = now + InviteLockIntervalMs;
                Function.Call(Hash.NETWORK_BLOCK_INVITES, true);
                Function.Call(Hash.NETWORK_BLOCK_JOIN_QUEUE_INVITES, true);
                Function.Call(Hash.NETWORK_SET_CAN_RECEIVE_RS_INVITES, false);
                Function.Call(Hash.NETWORK_SET_PRESENCE_SESSION_INVITES_BLOCKED, true);
                Function.Call(Hash.SHUTDOWN_SESSION_CLEARS_AUTO_MULTIPLAYER, true);
                Function.Call(Hash.LOBBY_SET_AUTO_MULTIPLAYER, false);
                Function.Call(Hash.LOBBY_SET_AUTO_MULTIPLAYER_EVENT, false);
                Function.Call(Hash.LOBBY_SET_AUTO_MP_RANDOM_JOB, false);
                Function.Call(Hash.NETWORK_DISABLE_REALTIME_MULTIPLAYER);
            }

            bool hasInviteOrSocialOverlay = Function.Call<bool>(Hash.IS_SOCIAL_CLUB_ACTIVE)
                || Function.Call<bool>(Hash.NETWORK_SESSION_IS_DISPLAYING_INVITE_CONFIRMATION)
                || Function.Call<bool>(Hash.NETWORK_HAS_FOLLOW_INVITE)
                || Function.Call<bool>(Hash.NETWORK_HAS_CONFIRMED_INVITE)
                || Function.Call<bool>(Hash.NETWORK_HAS_PENDING_INVITE)
                || Function.Call<bool>(Hash.NETWORK_IS_OFFLINE_INVITE_PENDING);

            if (hasInviteOrSocialOverlay)
            {
                Function.Call(Hash.CLOSE_SOCIAL_CLUB_MENU);
                Function.Call(Hash.NETWORK_CLEAR_FOLLOW_INVITE);
                Function.Call(Hash.NETWORK_CLEAR_OFFLINE_INVITE_PENDING);
                Function.Call(Hash.NETWORK_SESSION_FORCE_CANCEL_INVITE);
                Function.Call(Hash.NETWORK_CANCEL_TRANSITION_MATCHMAKING);
                PostRateLimitedOnlineWarning("~r~Online invite blocked!~s~ Mods are loaded for Story Mode only.");
            }

            bool mpFrontendActive = IsMultiplayerFrontendMenuActive();
            bool mpPauseScriptActive = PauseMenuMpScriptHash != 0 && IsScriptRunningByHash(PauseMenuMpScriptHash);
            bool mainTransitionRunningInSp = _welcomeShown && MainTransitionScriptHash != 0 && IsScriptRunningByHash(MainTransitionScriptHash) && !IsMultiplayerActive();
            bool policiesActive = Function.Call<bool>(Hash.IS_ONLINE_POLICIES_MENU_ACTIVE);
            bool commerceActive = Function.Call<bool>(Hash.IS_COMMERCE_STORE_OPEN)
                || Function.Call<bool>(Hash.IS_STORE_PENDING_NETWORK_SHUTDOWN_TO_OPEN);

            if (mpFrontendActive || mpPauseScriptActive || mainTransitionRunningInSp || policiesActive || commerceActive)
            {
                AttemptCancelOnlineTransition();
                PostRateLimitedOnlineWarning(commerceActive
                    ? "~r~Store / Commerce menu blocked!~s~ Mods are active for offline Story Mode only."
                    : "~r~GTA Online transition blocked!~s~ Remaining in offline Story Mode. Run 2-Disable-And-CleanForOnline.bat before playing Online.");
            }
        }

        /// <summary>
        /// Verifies that the running GTA5.exe matches the exact Single-Player script global layout (1.0.3889.0)
        /// before reading or writing any GlobalVariable address.
        /// </summary>
        internal static bool IsSupportedScriptGlobalBuild()
        {
            if (_cachedSupportedGlobalBuild.HasValue)
            {
                return _cachedSupportedGlobalBuild.Value;
            }

            try
            {
                _cachedSupportedGlobalBuild = Game.FileVersion == SupportedGlobalFileVersion;
            }
            catch
            {
                _cachedSupportedGlobalBuild = false;
            }

            return _cachedSupportedGlobalBuild.Value;
        }

        private void EnforceSelectorOnlineSlotLockout()
        {
            // Version-independent input guard: while holding Alt (CharacterWheel), block downward mouse/stick movement
            // across WeaponWheel, Look, and RightStick axes toward Quadrant 2 (Online) on ALL builds even if WheelSwap is off.
            if (Controls.IsControlPressed(ControlType.PlayerControl, ControlAction.CharacterWheel)
                || Controls.IsDisabledControlPressed(ControlType.PlayerControl, ControlAction.CharacterWheel))
            {
                float udWheel = Controls.GetDisabledControlNormal(ControlType.PlayerControl, ControlAction.WeaponWheelUpDown);
                float udLook = Controls.GetDisabledControlNormal(ControlType.PlayerControl, ControlAction.LookUpDown);
                float udStick = Controls.GetDisabledControlNormal(ControlType.PlayerControl, ControlAction.ScriptRightAxisY);
                float lrWheel = Controls.GetDisabledControlNormal(ControlType.PlayerControl, ControlAction.WeaponWheelLeftRight);
                float lrLook = Controls.GetDisabledControlNormal(ControlType.PlayerControl, ControlAction.LookLeftRight);

                float ud = Math.Abs(udWheel) >= Math.Abs(udLook) ? udWheel : udLook;
                if (Math.Abs(udStick) > Math.Abs(ud)) ud = udStick;
                float lr = Math.Abs(lrWheel) >= Math.Abs(lrLook) ? lrWheel : lrLook;

                if (ud > 0.04f && Math.Abs(ud) >= Math.Abs(lr) * 0.8f)
                {
                    Controls.DisableControlActionThisFrame(ControlType.PlayerControl, ControlAction.WeaponWheelUpDown, true);
                    Controls.DisableControlActionThisFrame(ControlType.PlayerControl, ControlAction.LookUpDown, true);
                    Controls.DisableControlActionThisFrame(ControlType.PlayerControl, ControlAction.LookDownOnly, true);
                    Controls.DisableControlActionThisFrame(ControlType.PlayerControl, ControlAction.ScriptRightAxisY, true);
                    Controls.DisableControlActionThisFrame(ControlType.FrontendControl, ControlAction.FrontendRightAxisY, true);
                }
            }

            if (!IsSupportedScriptGlobalBuild())
            {
                return;
            }

            try
            {
                // If selector.c wheel quadrant is 2 (Bottom / Online slot), immediately redirect to the active SP protagonist slot.
                if (GlobalVariable.Get(GlobalSelectorSelectedQuadrant).Read<int>() == 2)
                {
                    int activeProtag = TryGetPlayer(out Ped p) ? PlayerEnhancementsController.ResolveProtagonistIndex(p) : -1;
                    if (activeProtag < 0)
                    {
                        activeProtag = _enhancements != null && _enhancements.OriginalProtagonistIndex >= 0
                            ? _enhancements.OriginalProtagonistIndex
                            : 0;
                    }
                    int safeQuadrant = PlayerEnhancementsController.ProtagonistToWheelQuadrant(activeProtag);
                    GlobalVariable.Get(GlobalSelectorSelectedQuadrant).Write(safeQuadrant);
                    GlobalVariable.Get(GlobalSelectorWheelDirtyFlag).Write(1);
                    PostRateLimitedOnlineWarning("~r~Blocked:~s~ Character Wheel GTA Online slot is disabled while Story Mode Trainer is active.");
                }

                // Clear selector.c SP->MP transition trigger flag if set.
                if (GlobalVariable.Get(GlobalSelectorOnlineTriggerFlag).Read<int>() != 0)
                {
                    GlobalVariable.Get(GlobalSelectorOnlineTriggerFlag).Write(0);
                    AttemptCancelOnlineTransition();
                    PostRateLimitedOnlineWarning("~r~Blocked:~s~ Prevented script transition to GTA Online.");
                }
            }
            catch
            {
                // Ignore if script global table is not yet initialized.
            }
        }

        private static void AttemptCancelOnlineTransition()
        {
            if (Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS))
            {
                Function.Call(Hash.STOP_PLAYER_SWITCH);
            }

            if (IsSupportedScriptGlobalBuild())
            {
                try
                {
                    GlobalVariable.Get(GlobalSelectorOnlineTriggerFlag).Write(0);
                }
                catch
                {
                    // Ignore if global table is unavailable.
                }
            }

            Function.Call(Hash.TERMINATE_ALL_SCRIPTS_WITH_THIS_NAME, "MainTransition");
            Function.Call(Hash.TERMINATE_ALL_SCRIPTS_WITH_THIS_NAME, "maintransition");
            Function.Call(Hash.TERMINATE_ALL_SCRIPTS_WITH_THIS_NAME, "pausemenu_multiplayer");
            Function.Call(Hash.TERMINATE_ALL_SCRIPTS_WITH_THIS_NAME, "fmmc_launcher");
            Function.Call(Hash.TERMINATE_ALL_SCRIPTS_WITH_THIS_NAME, "am_launcher");
            Function.Call(Hash.SET_FRONTEND_ACTIVE, false);
            Function.Call(Hash.CLOSE_SOCIAL_CLUB_MENU);
            Function.Call(Hash.SHUTDOWN_SESSION_CLEARS_AUTO_MULTIPLAYER, true);
            Function.Call(Hash.NETWORK_DISABLE_REALTIME_MULTIPLAYER);
            Function.Call(Hash.NETWORK_BLOCK_INVITES, true);
            Function.Call(Hash.NETWORK_BLOCK_JOIN_QUEUE_INVITES, true);
            Function.Call(Hash.NETWORK_SET_CAN_RECEIVE_RS_INVITES, false);
            Function.Call(Hash.NETWORK_SET_PRESENCE_SESSION_INVITES_BLOCKED, true);
            Function.Call(Hash.NETWORK_CLEAR_FOLLOW_INVITE);
            Function.Call(Hash.NETWORK_CLEAR_OFFLINE_INVITE_PENDING);
            Function.Call(Hash.NETWORK_SESSION_FORCE_CANCEL_INVITE);
            Function.Call(Hash.NETWORK_CANCEL_TRANSITION_MATCHMAKING);
            if (Function.Call<bool>(Hash.NETWORK_IS_IN_TRANSITION) || Function.Call<bool>(Hash.NETWORK_IS_TRANSITION_STARTED))
            {
                Function.Call(Hash.NETWORK_BAIL_TRANSITION, 0, 0, 0);
            }
            Function.Call(Hash.NETWORK_LEAVE_TRANSITION);

            if (Game.Player != null)
            {
                Game.Player.SetControlState(true, SetPlayerControlFlags.None);
            }

            if (Function.Call<bool>(Hash.IS_SCREEN_FADED_OUT) || Function.Call<bool>(Hash.IS_SCREEN_FADING_OUT))
            {
                Function.Call(Hash.DO_SCREEN_FADE_IN, 250);
            }
        }

        private static bool TryGetPlayer(out Ped player)
        {
            Player gamePlayer = Game.Player;
            if (gamePlayer == null)
            {
                player = null;
                return false;
            }

            player = gamePlayer.Character;
            return player != null && player.Exists();
        }

        private static bool TryGetCurrentVehicle(out Vehicle vehicle)
        {
            vehicle = null;
            if (!TryGetPlayer(out Ped player) || !player.IsInVehicle())
            {
                return false;
            }

            Vehicle current = player.CurrentVehicle;
            if (current == null || !current.Exists())
            {
                return false;
            }

            vehicle = current;
            return true;
        }

        private static void SetPlayerWantedLevel(int level)
        {
            Wanted wanted = Game.Player?.Wanted;
            if (wanted == null) return;

            int clamped = Math.Max(0, Math.Min(DefaultMaxWantedLevel, level));
            wanted.SetWantedLevel(clamped, false);
            wanted.ApplyWantedLevelChangeNow(false);
        }

        private static void Notify(string message, bool isImportant = false)
        {
            GTA.UI.Notification.PostTicker(message, isImportant, false);
        }

        private void SetupMenus()
        {
            _pool.Add(_mainMenu);
            _pool.Add(_playerMenu);
            _pool.Add(_weaponsMenu);
            _pool.Add(_characterMenu);
            _pool.Add(_noClipMenu);
            _pool.Add(_heistMenu);
            _pool.Add(_vehicleMenu);
            _pool.Add(_customsMenu);
            _pool.Add(_onlineAssetsMenu);
            _pool.Add(_worldMenu);

            _mainMenu.AddSubMenu(_playerMenu);
            _mainMenu.AddSubMenu(_weaponsMenu);
            _mainMenu.AddSubMenu(_characterMenu);
            _mainMenu.AddSubMenu(_noClipMenu);
            _mainMenu.AddSubMenu(_heistMenu);
            _mainMenu.AddSubMenu(_vehicleMenu);
            _mainMenu.AddSubMenu(_customsMenu);
            _mainMenu.AddSubMenu(_onlineAssetsMenu);
            _mainMenu.AddSubMenu(_worldMenu);

            SetupPlayerMenu();
            SetupWeaponsMenu();
            SetupCharacterMenu();
            SetupNoClipMenu();
            SetupHeistMenu();
            SetupVehicleMenu();
            SetupCustomsMenu();
            SetupOnlineAssetsMenu();
            SetupWorldAndConfigMenu();

            var onlineLockInfoItem = new NativeItem(
                "Online Safeguard: LOCKED (SP Only)",
                "Pause Online tab, MP character wheel, and invites are actively blocked while modded."
            );
            onlineLockInfoItem.Activated += (s, e) =>
            {
                Notify("~g~Online Safeguard Active:~s~ Exit GTA 5 and run ~y~2-Disable-And-CleanForOnline.bat~s~ before going Online.", true);
            };
            _mainMenu.Add(onlineLockInfoItem);

            _mainMenu.Shown += (s, e) => SyncAllMenus();
        }

        private void SetupPlayerMenu()
        {
            _godModeItem.CheckboxChanged += (s, e) =>
            {
                if (_suppressUiSync) return;
                if (TryGetPlayer(out Ped player))
                {
                    ApplyPlayerAndVehicleProtections(player);
                }
                Notify(_godModeItem.Checked ? "~g~Player God Mode enabled.~s~" : "~y~Player God Mode disabled.~s~");
            };
            _playerMenu.Add(_godModeItem);

            _vehicleGodModeItem.CheckboxChanged += (s, e) =>
            {
                if (_suppressUiSync) return;
                if (TryGetPlayer(out Ped player))
                {
                    ApplyPlayerAndVehicleProtections(player);
                }
                Notify(_vehicleGodModeItem.Checked ? "~g~Vehicle God Mode enabled.~s~" : "~y~Vehicle God Mode disabled.~s~");
            };
            _playerMenu.Add(_vehicleGodModeItem);

            _playerNoClipItem.CheckboxChanged += (s, e) =>
            {
                if (_suppressUiSync) return;
                SetNoClipEnabled(_playerNoClipItem.Checked);
            };
            _playerMenu.Add(_playerNoClipItem);

            _neverWantedItem.CheckboxChanged += (s, e) =>
            {
                if (_suppressUiSync) return;
                if (_neverWantedItem.Checked)
                {
                    Game.MaxWantedLevel = 0;
                    SetPlayerWantedLevel(0);
                    SyncPlayerMenu();
                    Notify("~g~Never Wanted enabled.~s~");
                }
                else
                {
                    Game.MaxWantedLevel = DefaultMaxWantedLevel;
                    Notify("~y~Never Wanted disabled.~s~");
                }
            };
            _playerMenu.Add(_neverWantedItem);

            _infiniteSprintItem.CheckboxChanged += (s, e) =>
            {
                if (_suppressUiSync) return;
                _enhancements.InfiniteSprintEnabled = _infiniteSprintItem.Checked;
                Notify(_infiniteSprintItem.Checked ? "~g~Infinite Sprint enabled.~s~" : "~y~Infinite Sprint disabled.~s~");
            };
            _playerMenu.Add(_infiniteSprintItem);

            _infiniteSpecialAbilityItem.CheckboxChanged += (s, e) =>
            {
                if (_suppressUiSync) return;
                _enhancements.InfiniteSpecialAbilityEnabled = _infiniteSpecialAbilityItem.Checked;
                if (_infiniteSpecialAbilityItem.Checked)
                {
                    PlayerEnhancementsController.RefillSpecialAbility();
                }
                Notify(_infiniteSpecialAbilityItem.Checked ? "~g~Infinite Special Ability enabled.~s~" : "~y~Infinite Special Ability disabled.~s~");
            };
            _playerMenu.Add(_infiniteSpecialAbilityItem);

            _runSpeedItem.ItemChanged += (s, e) =>
            {
                if (_suppressUiSync) return;
                _enhancements.RunSpeedIndex = _runSpeedItem.SelectedIndex;
            };
            _playerMenu.Add(_runSpeedItem);

            _swimSpeedItem.ItemChanged += (s, e) =>
            {
                if (_suppressUiSync) return;
                _enhancements.SwimSpeedIndex = _swimSpeedItem.SelectedIndex;
            };
            _playerMenu.Add(_swimSpeedItem);

            _jumpModeItem.ItemChanged += (s, e) =>
            {
                if (_suppressUiSync) return;
                _enhancements.JumpModeIndex = _jumpModeItem.SelectedIndex;
            };
            _playerMenu.Add(_jumpModeItem);

            var healItem = new NativeItem(
                "Heal, Clean & Restore Armor",
                "Refills full health, body armor, and special ability, and clears blood/damage decals."
            );
            healItem.Activated += (s, e) =>
            {
                if (!TryGetPlayer(out Ped player)) return;
                if (player.IsDead)
                {
                    Notify("~r~Cannot heal while Wasted.~s~");
                    return;
                }
                if (player.MaxHealth < 200)
                {
                    player.MaxHealth = 200;
                }
                player.Health = player.MaxHealth;
                if (Game.Player != null)
                {
                    player.Armor = Game.Player.MaxArmor;
                }
                PlayerEnhancementsController.CleanPlayerPed(player);
                PlayerEnhancementsController.RefillSpecialAbility();
                Notify("~g~Health, armor, special ability, and appearance restored.~s~");
            };
            _playerMenu.Add(healItem);

            var addCashItem = new NativeItem("Add $100,000", "Safely adds $100,000 to the active Story character.");
            addCashItem.Activated += (s, e) => GrantActiveCharacterCash(CashIncrement);
            _playerMenu.Add(addCashItem);

            var customCashItem = new NativeItem(
                "Add Custom Cash Amount...",
                "Enter any cash amount (e.g. 500000, 5M, 250M, max $1,000,000,000) for the active Story character."
            );
            customCashItem.Activated += (s, e) =>
            {
                if (!TryPromptUserInput(out string input)) return;
                if (!TryParseCashAmount(input, out int amount) || amount <= 0)
                {
                    if (!string.IsNullOrWhiteSpace(input)) Notify("~r~Invalid cash amount entered.~s~");
                    return;
                }
                GrantActiveCharacterCash(amount);
            };
            _playerMenu.Add(customCashItem);

            _wantedLevelItem.ItemChanged += (s, e) => ApplyWantedSelection();
            _wantedLevelItem.Activated += (s, e) => ApplyWantedSelection();
            _playerMenu.Add(_wantedLevelItem);

            var teleportWaypointItem = new NativeItem("Teleport to Waypoint", "Streams collision and warps safely to your map waypoint.");
            teleportWaypointItem.Activated += (s, e) => TeleportToWaypoint();
            _playerMenu.Add(teleportWaypointItem);

            _playerMenu.Shown += (s, e) => SyncPlayerMenu();
        }

        private void SetupWeaponsMenu()
        {
            var giveAllBtn = new NativeItem(
                "Give All Weapons & Max Ammo",
                "Grants every base Story Mode and Online DLC weapon with maximum bullets, clips, and parachute."
            );
            giveAllBtn.Activated += (s, e) =>
            {
                if (!TryGetPlayer(out Ped player) || player.IsDead)
                {
                    Notify("~r~Cannot grant weapons right now.~s~");
                    return;
                }
                int count = _enhancements.GiveAllWeaponsAndMaxAmmo(player);
                Notify(string.Format("~g~Granted/filled {0} weapons with max ammo.~s~", count));
            };
            _weaponsMenu.Add(giveAllBtn);

            var giveOnlineBtn = new NativeItem(
                "Give Online DLC / Alien / Mk II Arsenal",
                "Grants Railgun, Up-n-Atomizer, Unholy Hellbringer, Widowmaker, Mk II & DLC guns offline."
            );
            giveOnlineBtn.Activated += (s, e) =>
            {
                if (!TryGetPlayer(out Ped player) || player.IsDead)
                {
                    Notify("~r~Cannot grant weapons right now.~s~");
                    return;
                }
                int count = _enhancements.GiveOnlineDlcWeapons(player);
                Notify(string.Format("~g~Granted {0} Online DLC / Mk II weapons with max ammo.~s~", count));
            };
            _weaponsMenu.Add(giveOnlineBtn);

            var refillAmmoBtn = new NativeItem(
                "Refill All Bullets & Clips",
                "Refills reserve ammunition and magazine clips on every weapon you currently own."
            );
            refillAmmoBtn.Activated += (s, e) =>
            {
                if (!TryGetPlayer(out Ped player) || player.IsDead)
                {
                    Notify("~r~Cannot refill ammo right now.~s~");
                    return;
                }
                int refilled = _enhancements.RefillAllOwnedWeaponAmmo(player);
                Notify(refilled > 0
                    ? string.Format("~g~Refilled bullets & clips on {0} weapons.~s~", refilled)
                    : "~y~No weapons owned yet.~s~");
            };
            _weaponsMenu.Add(refillAmmoBtn);

            var maxAttachAllBtn = new NativeItem(
                "Max Attachments (All Owned Weapons)",
                "Installs extended clips, scopes, barrels, grips, flashlights, and suppressors across your arsenal."
            );
            maxAttachAllBtn.Activated += (s, e) =>
            {
                if (!TryGetPlayer(out Ped player) || player.IsDead) return;
                int modified = _enhancements.MaxWeaponAttachments(player, currentWeaponOnly: false);
                Notify(modified > 0
                    ? string.Format("~g~Upgraded attachments on {0} weapons.~s~", modified)
                    : "~y~No upgradable weapons found.~s~");
            };
            _weaponsMenu.Add(maxAttachAllBtn);

            var maxAttachCurrBtn = new NativeItem(
                "Max Attachments (Current Weapon)",
                "Installs maximum attachments on the weapon you are currently holding."
            );
            maxAttachCurrBtn.Activated += (s, e) =>
            {
                if (!TryGetPlayer(out Ped player) || player.IsDead) return;
                int modified = _enhancements.MaxWeaponAttachments(player, currentWeaponOnly: true);
                Notify(modified > 0
                    ? "~g~Current weapon attachments maxed.~s~"
                    : "~y~Current weapon has no compatible attachment upgrades.~s~");
            };
            _weaponsMenu.Add(maxAttachCurrBtn);

            _infiniteAmmoItem.CheckboxChanged += (s, e) =>
            {
                if (_suppressUiSync) return;
                _enhancements.InfiniteAmmoEnabled = _infiniteAmmoItem.Checked;
                Notify(_infiniteAmmoItem.Checked ? "~g~Infinite Ammo: ON~s~" : "~y~Infinite Ammo: OFF~s~");
            };
            _weaponsMenu.Add(_infiniteAmmoItem);

            _infiniteClipItem.CheckboxChanged += (s, e) =>
            {
                if (_suppressUiSync) return;
                _enhancements.InfiniteClipEnabled = _infiniteClipItem.Checked;
                Notify(_infiniteClipItem.Checked ? "~g~Infinite Clip (No Reload): ON~s~" : "~y~Infinite Clip: OFF~s~");
            };
            _weaponsMenu.Add(_infiniteClipItem);

            var removeAllBtn = new NativeItem(
                "Remove All Weapons",
                "Clears all weapons from your current character inventory."
            );
            removeAllBtn.Activated += (s, e) =>
            {
                if (!TryGetPlayer(out Ped player)) return;
                _enhancements.RemoveAllWeapons(player);
                Notify("~y~Removed all weapons from current character.~s~");
            };
            _weaponsMenu.Add(removeAllBtn);

            _weaponsMenu.Shown += (s, e) => SyncWeaponsMenu();
        }

        private void SetupCharacterMenu()
        {
            _characterWheelSwapItem.CheckboxChanged += (s, e) =>
            {
                if (_suppressUiSync) return;
                _enhancements.CharacterWheelSwapEnabled = _characterWheelSwapItem.Checked;
                Notify(_characterWheelSwapItem.Checked
                    ? "~g~Character Wheel Swapping (Michael/Franklin/Trevor): ON~s~"
                    : "~y~Character Wheel Swapping: OFF~s~");
            };
            _characterMenu.Add(_characterWheelSwapItem);

            var quickMichaelBtn = new NativeItem(
                "Switch to Michael (Story)",
                "Immediately swaps to Michael De Santa (or hold Alt + Left / press F5)."
            );
            quickMichaelBtn.Activated += (s, e) =>
            {
                if (_enhancements.SwitchToProtagonistFromWheel(0, DisableNoClipBeforeModelSwitch, out string msg) && !string.IsNullOrEmpty(msg))
                {
                    Notify("~g~" + msg + "~s~");
                }
            };
            _characterMenu.Add(quickMichaelBtn);

            var quickFranklinBtn = new NativeItem(
                "Switch to Franklin (Story)",
                "Immediately swaps to Franklin Clinton (or hold Alt + Up / press F6)."
            );
            quickFranklinBtn.Activated += (s, e) =>
            {
                if (_enhancements.SwitchToProtagonistFromWheel(1, DisableNoClipBeforeModelSwitch, out string msg) && !string.IsNullOrEmpty(msg))
                {
                    Notify("~g~" + msg + "~s~");
                }
            };
            _characterMenu.Add(quickFranklinBtn);

            var quickTrevorBtn = new NativeItem(
                "Switch to Trevor (Story)",
                "Immediately swaps to Trevor Philips (or hold Alt + Right / press F7)."
            );
            quickTrevorBtn.Activated += (s, e) =>
            {
                if (_enhancements.SwitchToProtagonistFromWheel(2, DisableNoClipBeforeModelSwitch, out string msg) && !string.IsNullOrEmpty(msg))
                {
                    Notify("~g~" + msg + "~s~");
                }
            };
            _characterMenu.Add(quickTrevorBtn);

            _characterPresetItem.ItemChanged += (s, e) =>
            {
                if (_suppressUiSync) return;
                _enhancements.CharacterPresetIndex = _characterPresetItem.SelectedIndex;
            };
            _characterPresetItem.Activated += (s, e) => SwitchToSelectedPresetCharacter();
            _characterMenu.Add(_characterPresetItem);

            var switchBtn = new NativeItem(
                "Switch to Selected Character",
                "Swaps your active character model immediately while preserving weapons, ammo, armor, and vehicle seat."
            );
            switchBtn.Activated += (s, e) => SwitchToSelectedPresetCharacter();
            _characterMenu.Add(switchBtn);

            var customPedBtn = new NativeItem(
                "Enter Custom Ped Model...",
                "Enter any ped model name (e.g. player_zero, mp_m_freemode_01, ig_lestercrest)."
            );
            customPedBtn.Activated += (s, e) =>
            {
                if (!TryPromptUserInput(out string input)) return;
                if (string.IsNullOrWhiteSpace(input)) return;

                string modelName = input.Trim().ToLowerInvariant();
                bool isFreemode = modelName == "mp_m_freemode_01" || modelName == "mp_f_freemode_01";
                DisableNoClipBeforeModelSwitch();
                if (_enhancements.SwitchToCharacterModel(new Model(modelName), modelName, isFreemode, out string msg))
                {
                    Notify("~g~" + msg + "~s~");
                }
                else
                {
                    Notify("~r~" + msg + "~s~");
                }
            };
            _characterMenu.Add(customPedBtn);

            var randomOutfitBtn = new NativeItem(
                "Randomize Current Outfit",
                "Applies random clothes & component variations onto your active character model."
            );
            randomOutfitBtn.Activated += (s, e) =>
            {
                if (!TryGetPlayer(out Ped player) || player.IsDead) return;
                player.Style.RandomizeOutfit();
                Notify("~g~Randomized character outfit.~s~");
            };
            _characterMenu.Add(randomOutfitBtn);

            var restoreProtBtn = new NativeItem(
                "Restore Original Story Protagonist",
                "Reverts back to your Story save's native Michael/Franklin/Trevor character model."
            );
            restoreProtBtn.Activated += (s, e) =>
            {
                DisableNoClipBeforeModelSwitch();
                if (_enhancements.RestoreOriginalProtagonist(out string msg))
                {
                    Notify("~g~" + msg + "~s~");
                }
                else
                {
                    Notify("~r~" + msg + "~s~");
                }
            };
            _characterMenu.Add(restoreProtBtn);

            var unstickWheelBtn = new NativeItem(
                "Unstick Native Character Wheel",
                "Stops any stuck switch transition, restores controls & original protagonist for Alt/F5-F7 switching."
            );
            unstickWheelBtn.Activated += (s, e) =>
            {
                DisableNoClipBeforeModelSwitch();
                if (_enhancements.UnstickCharacterSwitch(out string msg))
                {
                    Notify("~g~" + msg + "~s~");
                }
                else
                {
                    Notify("~r~" + msg + "~s~");
                }
            };
            _characterMenu.Add(unstickWheelBtn);

            _characterMenu.Shown += (s, e) => SyncCharacterMenu();
        }

        private void SetupNoClipMenu()
        {
            _noClipActiveItem.CheckboxChanged += (s, e) =>
            {
                if (_suppressUiSync) return;
                SetNoClipEnabled(_noClipActiveItem.Checked);
            };
            _noClipMenu.Add(_noClipActiveItem);

            _noClipSpeedItem.ItemChanged += (s, e) =>
            {
                if (_suppressUiSync) return;
                _noClip.SpeedPresetIndex = _noClipSpeedItem.SelectedIndex;
            };
            _noClipMenu.Add(_noClipSpeedItem);

            _noClipAlignHeadingItem.CheckboxChanged += (s, e) =>
            {
                if (_suppressUiSync) return;
                _noClip.AlignHeadingWithCamera = _noClipAlignHeadingItem.Checked;
            };
            _noClipMenu.Add(_noClipAlignHeadingItem);

            _noClipFallShieldItem.CheckboxChanged += (s, e) =>
            {
                if (_suppressUiSync) return;
                _noClip.ExitFallShieldEnabled = _noClipFallShieldItem.Checked;
            };
            _noClipMenu.Add(_noClipFallShieldItem);

            _noClipSnapGroundItem.CheckboxChanged += (s, e) =>
            {
                if (_suppressUiSync) return;
                _noClip.SnapToGroundOnExit = _noClipSnapGroundItem.Checked;
            };
            _noClipMenu.Add(_noClipSnapGroundItem);

            var landBtn = new NativeItem(
                "Land on Surface Below",
                "Drops safely to solid ground or water directly under your current position."
            );
            landBtn.Activated += (s, e) =>
            {
                if (!TryGetPlayer(out Ped player)) return;
                if (_noClip.IsActive)
                {
                    _noClip.Disable(player, _godModeItem.Checked || _enhancements.IsJumpOrMovementShieldActive, _vehicleGodModeItem.Checked, _heists.IsHeistShieldedNow);
                    SyncNoClipCheckboxes();
                }

                if (_noClip.TryLandOnSurfaceBelow(player))
                {
                    Notify("~g~Landed safely on surface below.~s~");
                }
                else
                {
                    Notify("~r~Could not probe ground below right here.~s~");
                }
            };
            _noClipMenu.Add(landBtn);

            _noClipMenu.Shown += (s, e) => SyncNoClipMenu();
        }

        private void SetupHeistMenu()
        {
            _heistTargetItem.ItemChanged += (s, e) =>
            {
                if (_suppressUiSync) return;
                _heists.SelectedMenuIndex = _heistTargetItem.SelectedIndex;
            };
            _heistMenu.Add(_heistTargetItem);

            _protectHeistTakeItem.CheckboxChanged += (s, e) =>
            {
                if (_suppressUiSync) return;
                _heists.ProtectHeistTake = _protectHeistTakeItem.Checked;
                if (_heists.ProtectHeistTake)
                {
                    _heists.ClearAllHeistPenalties();
                }
                Notify(_heists.ProtectHeistTake
                    ? "~g~Police Heist Money-Loss Protection: ON~s~"
                    : "~y~Police Heist Money-Loss Protection: OFF~s~");
            };
            _heistMenu.Add(_protectHeistTakeItem);

            _heistIgnorePoliceItem.CheckboxChanged += (s, e) =>
            {
                if (_suppressUiSync) return;
                _heists.PoliceIgnoreDuringHeists = _heistIgnorePoliceItem.Checked;
                Notify(_heists.PoliceIgnoreDuringHeists
                    ? "~g~Police Ignore Player During Heists: ON~s~"
                    : "~y~Police Ignore Player During Heists: OFF~s~");
            };
            _heistMenu.Add(_heistIgnorePoliceItem);

            _keepFullTakeItem.CheckboxChanged += (s, e) =>
            {
                if (_suppressUiSync) return;
                _heists.KeepFullTakeWithoutCuts = _keepFullTakeItem.Checked;
                _heists.ReapplyCutModeToTargetHeist(_heists.ResolveTargetHeistId());
                Notify(_keepFullTakeItem.Checked
                    ? "~g~Keep 100% Heist Take (Skip Crew/Lester Cuts): ON~s~"
                    : "~y~Keep 100% Heist Take: OFF (Standard Cuts)~s~");
            };
            _heistMenu.Add(_keepFullTakeItem);

            _lockHeistTakeItem.CheckboxChanged += (s, e) =>
            {
                if (_suppressUiSync) return;
                if (_lockHeistTakeItem.Checked)
                {
                    int heistId = _heists.ResolveTargetHeistId();
                    if (_heists.TryArmCompletionLock(heistId, out string lockMsg))
                    {
                        Notify("~g~" + lockMsg + "~s~");
                    }
                    else
                    {
                        Notify("~y~" + lockMsg + "~s~");
                        SyncHeistMenu();
                    }
                }
                else
                {
                    _heists.LockTakeThroughCompletion = false;
                    Notify("~y~Heist Take Completion Lock: OFF~s~");
                }
            };
            _heistMenu.Add(_lockHeistTakeItem);

            _takePresetItem.Activated += (s, e) => ApplySelectedPresetHeistTake();
            _heistMenu.Add(_takePresetItem);

            var applyPresetBtn = new NativeItem(
                "Apply Preset Heist Take",
                "Sets & locks Potential Take, Actual Take, and shares for the Target Heist."
            );
            applyPresetBtn.Activated += (s, e) => ApplySelectedPresetHeistTake();
            _heistMenu.Add(applyPresetBtn);

            var customTakeBtn = new NativeItem(
                "Enter Custom Heist Take...",
                "Enter any amount (e.g. 50000000 or 50M, max $1,000,000,000) for Target Heist."
            );
            customTakeBtn.Activated += (s, e) =>
            {
                int heistId = _heists.ResolveTargetHeistId();
                if (!TryPromptUserInput(out string input)) return;
                if (!TryParseCashAmount(input, out int amount))
                {
                    if (!string.IsNullOrWhiteSpace(input)) Notify("~r~Invalid cash amount entered.~s~");
                    return;
                }

                _heists.SetHeistTake(heistId, amount, true);
                SyncHeistMenu();
                Notify(string.Format("~g~Set {0} take to {1} (locked).~s~",
                    HeistManager.GetHeistName(heistId), HeistManager.FormatCash(amount)));
            };
            _heistMenu.Add(customTakeBtn);

            var michaelShareBtn = new NativeItem("Edit Michael Share...", "Set custom Michael net payout for the selected heist.");
            michaelShareBtn.Activated += (s, e) => EditProtagonistShare(0, "Michael");
            _heistMenu.Add(michaelShareBtn);

            var franklinShareBtn = new NativeItem("Edit Franklin Share...", "Set custom Franklin net payout for the selected heist.");
            franklinShareBtn.Activated += (s, e) => EditProtagonistShare(1, "Franklin");
            _heistMenu.Add(franklinShareBtn);

            var trevorShareBtn = new NativeItem("Edit Trevor Share...", "Set custom Trevor net payout for the selected heist.");
            trevorShareBtn.Activated += (s, e) => EditProtagonistShare(2, "Trevor");
            _heistMenu.Add(trevorShareBtn);

            var payOutNowBtn = new NativeItem(
                "Pay Out Edited Shares to Bank Now",
                "Immediately credits Michael, Franklin, and Trevor bank accounts with Target Heist shares."
            );
            payOutNowBtn.Activated += (s, e) =>
            {
                int heistId = _heists.ResolveTargetHeistId();
                int paidTotal = _heists.PayOutHeistSharesNow(heistId);
                if (paidTotal > 0)
                {
                    Notify(string.Format("~g~Paid out {0} across protagonists~s~ ({1}).",
                        HeistManager.FormatCash(paidTotal), HeistManager.GetHeistName(heistId)));
                }
                else
                {
                    Notify("~y~Target heist shares are currently $0. Set a take or share first.~s~");
                }
            };
            _heistMenu.Add(payOutNowBtn);

            var addBigCashBtn = new NativeItem(
                "Add $10,000,000 (Active Character)",
                "Directly deposits $10,000,000 to the currently controlled Story Mode protagonist."
            );
            addBigCashBtn.Activated += (s, e) => GrantActiveCharacterCash(QuickHeistCashBoost);
            _heistMenu.Add(addBigCashBtn);

            var clearPenaltiesBtn = new NativeItem(
                "Clear Heist Drop/Damage Penalties",
                "Immediately zeroes any recorded heist bag-drop or damage penalties across all heists."
            );
            clearPenaltiesBtn.Activated += (s, e) =>
            {
                _heists.ClearAllHeistPenalties();
                Notify("~g~All recorded heist penalties cleared to $0.~s~");
            };
            _heistMenu.Add(clearPenaltiesBtn);

            var inspectBtn = new NativeItem(
                "Inspect Live Heist Values",
                "Displays live Potential Take, Actual Take, protagonist cuts, and drop penalties."
            );
            inspectBtn.Activated += (s, e) =>
            {
                int heistId = _heists.ResolveTargetHeistId();
                Notify(_heists.GetStatusSummary(heistId), true);
            };
            _heistMenu.Add(inspectBtn);

            _heistMenu.Shown += (s, e) => SyncHeistMenu();
        }

        private void SetupVehicleMenu()
        {
            _spawnListItem.Activated += (s, e) => SpawnVehicle(_spawnListItem.SelectedItem);
            _vehicleMenu.Add(_spawnListItem);

            var spawnBtn = new NativeItem("Spawn Selected Base Vehicle", "Spawns the highlighted Story Mode model and seats you in the driver seat.");
            spawnBtn.Activated += (s, e) => SpawnVehicle(_spawnListItem.SelectedItem);
            _vehicleMenu.Add(spawnBtn);

            _onlineVehicleListItem.Activated += (s, e) => SpawnVehicle(_onlineVehicleListItem.SelectedItem);
            _vehicleMenu.Add(_onlineVehicleListItem);

            var spawnOnlineBtn = new NativeItem("Spawn Selected Online DLC Vehicle", "Spawns the highlighted GTA Online DLC vehicle offline.");
            spawnOnlineBtn.Activated += (s, e) => SpawnVehicle(_onlineVehicleListItem.SelectedItem);
            _vehicleMenu.Add(spawnOnlineBtn);

            var customSpawnBtn = new NativeItem("Spawn Custom Model...", "Enter any GTA V or Online DLC vehicle model name (e.g., OPPRESSOR2, RAIJU, SULTANRS).");
            customSpawnBtn.Activated += (s, e) =>
            {
                if (!TryPromptUserInput(out string input)) return;
                if (!string.IsNullOrWhiteSpace(input))
                {
                    SpawnVehicle(input);
                }
            };
            _vehicleMenu.Add(customSpawnBtn);

            _vehicleMenu.Add(_spawnMaxUpgradedItem);

            _persistOnlineVehiclesItem.CheckboxChanged += (s, e) =>
            {
                if (_suppressUiSync) return;
                _onlineAssets.KeepSpawnedVehiclesPersistent = _persistOnlineVehiclesItem.Checked;
            };
            _vehicleMenu.Add(_persistOnlineVehiclesItem);

            _vehicleMenu.Add(_replacePrevVehicleItem);

            _vehicleMenu.Shown += (s, e) => SyncVehicleSpawnerMenu();
        }

        private void SetupCustomsMenu()
        {
            _vehPowerMultItem.ItemChanged += (s, e) =>
            {
                if (_suppressUiSync) return;
                _vehiclePhysics.PowerIndex = _vehPowerMultItem.SelectedIndex;
            };
            _customsMenu.Add(_vehPowerMultItem);

            _vehTopSpeedMultItem.ItemChanged += (s, e) =>
            {
                if (_suppressUiSync) return;
                _vehiclePhysics.TopSpeedIndex = _vehTopSpeedMultItem.SelectedIndex;
            };
            _customsMenu.Add(_vehTopSpeedMultItem);

            _vehBrakeMultItem.ItemChanged += (s, e) =>
            {
                if (_suppressUiSync) return;
                _vehiclePhysics.BrakeIndex = _vehBrakeMultItem.SelectedIndex;
            };
            _customsMenu.Add(_vehBrakeMultItem);

            _vehTractionMultItem.ItemChanged += (s, e) =>
            {
                if (_suppressUiSync) return;
                _vehiclePhysics.TractionIndex = _vehTractionMultItem.SelectedIndex;
            };
            _customsMenu.Add(_vehTractionMultItem);

            _vehAutoRepairItem.CheckboxChanged += (s, e) =>
            {
                if (_suppressUiSync) return;
                _vehiclePhysics.AutoRepairCurrentVehicle = _vehAutoRepairItem.Checked;
            };
            _customsMenu.Add(_vehAutoRepairItem);

            var resetPhysicsBtn = new NativeItem(
                "Reset Physics Multipliers to Stock",
                "Restores stock acceleration, top speed, brakes, and handling on current & cached vehicles."
            );
            resetPhysicsBtn.Activated += (s, e) =>
            {
                TryGetCurrentVehicle(out Vehicle curr);
                _vehiclePhysics.ResetToStock(curr);
                SyncCustomsMenu();
                Notify("~g~Vehicle physics multipliers reset to stock.~s~");
            };
            _customsMenu.Add(resetPhysicsBtn);

            var repairCleanBtn = new NativeItem("Repair & Clean Vehicle", "Fixes all damage and washes dirt off your current vehicle.");
            repairCleanBtn.Activated += (s, e) =>
            {
                if (!TryGetCurrentVehicle(out Vehicle veh))
                {
                    Notify("~r~Enter a vehicle first.~s~");
                    return;
                }

                veh.Repair();
                veh.DirtLevel = 0f;
                _vehiclePhysics.NotifyVehicleRepairedOrUpgraded();
                Notify("~g~Vehicle repaired and cleaned.~s~");
            };
            _customsMenu.Add(repairCleanBtn);

            var flipUprightBtn = new NativeItem("Flip Vehicle Upright", "Rights an overturned or stuck vehicle onto its wheels.");
            flipUprightBtn.Activated += (s, e) =>
            {
                if (!TryGetCurrentVehicle(out Vehicle veh))
                {
                    Notify("~r~Enter a vehicle first.~s~");
                    return;
                }

                if (VehiclePhysicsController.FlipVehicleUpright(veh))
                {
                    Notify("~g~Vehicle flipped upright.~s~");
                }
            };
            _customsMenu.Add(flipUprightBtn);

            var maxUpgradeBtn = new NativeItem("Max All Upgrades & Repair", "Repairs vehicle and installs maximum performance upgrades.");
            maxUpgradeBtn.Activated += (s, e) => ApplyMaxUpgrades();
            _customsMenu.Add(maxUpgradeBtn);

            foreach (ModSliderBinding binding in _modBindings)
            {
                ModSliderBinding captured = binding;
                captured.Item.ItemChanged += (s, e) =>
                {
                    if (_suppressUiSync) return;
                    SetVehicleModLevel(captured.ModType, captured.Label, captured.Item.SelectedItem);
                };
                _customsMenu.Add(captured.Item);
            }

            _turboItem.CheckboxChanged += (s, e) =>
            {
                if (_suppressUiSync) return;
                if (!TryGetCurrentVehicle(out Vehicle veh))
                {
                    Notify("~r~Enter a vehicle first.~s~");
                    SyncCustomsMenu();
                    return;
                }

                veh.Mods.InstallModKit();
                veh.Mods[VehicleToggleModType.Turbo].IsInstalled = _turboItem.Checked;
                _vehiclePhysics.NotifyVehicleRepairedOrUpgraded();
            };
            _customsMenu.Add(_turboItem);

            _bulletproofTiresItem.CheckboxChanged += (s, e) =>
            {
                if (_suppressUiSync) return;
                if (!TryGetCurrentVehicle(out Vehicle veh))
                {
                    Notify("~r~Enter a vehicle first.~s~");
                    SyncCustomsMenu();
                    return;
                }

                veh.CanTiresBurst = !_bulletproofTiresItem.Checked && !_vehicleGodModeItem.Checked;
                Notify(_bulletproofTiresItem.Checked ? "~g~Bulletproof Tires enabled.~s~" : "~y~Bulletproof Tires disabled.~s~");
            };
            _customsMenu.Add(_bulletproofTiresItem);

            _customsMenu.Shown += (s, e) => SyncCustomsMenu();
        }

        private void SetupOnlineAssetsMenu()
        {
            _mpMapsModeItem.CheckboxChanged += (s, e) =>
            {
                if (_suppressUiSync) return;
                if (!_mpMapsModeItem.Checked)
                {
                    SafeReturnIfInsideMpAssets();
                    _onlineAssets.RevertAllToStoryWorld();
                    Notify("~y~Offline DLC Map Changesets disabled & reverted to vanilla Story Mode.~s~");
                }
                else
                {
                    _onlineAssets.SetMpMapsMode(true, explicitUserToggle: true);
                    if (Game.IsMissionActive)
                    {
                        Notify("~y~Note:~s~ Active Story mission detected. Disable DLC Map Changesets if any mission doors conflict.");
                    }
                    else
                    {
                        Notify("~g~Offline DLC Map Changesets enabled cleanly (no Online transition).~s~");
                    }
                }
                SyncOnlineAssetsMenu();
            };
            _onlineAssetsMenu.Add(_mpMapsModeItem);

            _suspendShopControllerItem.CheckboxChanged += (s, e) =>
            {
                if (_suppressUiSync) return;
                _onlineAssets.SuspendShopControllerDespawn = _suspendShopControllerItem.Checked;
                Notify(_suspendShopControllerItem.Checked
                    ? "~g~DLC Vehicle Anti-Despawn Guard: ON~s~"
                    : "~y~DLC Vehicle Anti-Despawn Guard: OFF~s~");
            };
            _onlineAssetsMenu.Add(_suspendShopControllerItem);

            _cayoPericoItem.CheckboxChanged += (s, e) =>
            {
                if (_suppressUiSync) return;
                if (_cayoPericoItem.Checked && _onlineAssets.NorthYanktonLoaded)
                {
                    SafeReturnFromUnloadingAsset(OnlineAssetsController.SafeLosSantosReturnCoords, p => _onlineAssets.IsPlayerInNorthYanktonZone(p));
                }
                else if (!_cayoPericoItem.Checked)
                {
                    SafeReturnFromUnloadingAsset(OnlineAssetsController.SafeLosSantosReturnCoords, p => _onlineAssets.IsPlayerInCayoPericoZone(p));
                }
                _onlineAssets.SetCayoPericoEnabled(_cayoPericoItem.Checked);
                SyncOnlineAssetsMenu();
                Notify(_cayoPericoItem.Checked ? "~g~Cayo Perico Island armed offline (meshes & collisions stream in-zone).~s~" : "~y~Cayo Perico Island unloaded.~s~");
            };
            _onlineAssetsMenu.Add(_cayoPericoItem);

            AddOnlineLocationTeleportButton(
                "Teleport: Cayo Perico Airstrip",
                "Streams Cayo Perico meshes, HeistIsland static collisions & water offline and warps to the runway.",
                () => { if (!_onlineAssets.CayoPericoLoaded) _onlineAssets.SetCayoPericoEnabled(true); },
                OnlineAssetsController.CayoPericoAirstripCoords,
                isExteriorSurface: true
            );

            AddOnlineLocationTeleportButton(
                "Teleport: Cayo Perico Compound",
                "Streams Cayo Perico meshes & compound collisions offline and warps outside El Rubio's Compound.",
                () => { if (!_onlineAssets.CayoPericoLoaded) _onlineAssets.SetCayoPericoEnabled(true); },
                OnlineAssetsController.CayoPericoCompoundCoords,
                isExteriorSurface: true
            );

            AddOnlineLocationTeleportButton(
                "Teleport: Cayo Perico Beach Party",
                "Streams Cayo Perico beach stage, props & collisions offline and warps to West Beach.",
                () => { if (!_onlineAssets.CayoPericoLoaded) _onlineAssets.SetCayoPericoEnabled(true); },
                OnlineAssetsController.CayoPericoBeachPartyCoords,
                isExteriorSurface: true
            );

            _northYanktonItem.CheckboxChanged += (s, e) =>
            {
                if (_suppressUiSync) return;
                if (_northYanktonItem.Checked && (_onlineAssets.CayoPericoLoaded || _onlineAssets.AircraftCarrierLoaded))
                {
                    SafeReturnFromUnloadingAsset(
                        OnlineAssetsController.SafeLosSantosReturnCoords,
                        p => _onlineAssets.IsPlayerInCayoPericoZone(p) || _onlineAssets.IsPlayerInAircraftCarrierZone(p)
                    );
                }
                else if (!_northYanktonItem.Checked)
                {
                    SafeReturnFromUnloadingAsset(OnlineAssetsController.SafeLosSantosReturnCoords, p => _onlineAssets.IsPlayerInNorthYanktonZone(p));
                }
                _onlineAssets.SetNorthYanktonEnabled(_northYanktonItem.Checked);
                SyncOnlineAssetsMenu();
                Notify(_northYanktonItem.Checked ? "~g~North Yankton snow map loaded offline in Single-Player.~s~" : "~y~North Yankton unloaded.~s~");
            };
            _onlineAssetsMenu.Add(_northYanktonItem);

            AddOnlineLocationTeleportButton(
                "Teleport: North Yankton (Ludendorff Town)",
                "Streams North Yankton snow IPLs, bank interior & extended borders offline and warps to Ludendorff.",
                () => { if (!_onlineAssets.NorthYanktonLoaded) _onlineAssets.SetNorthYanktonEnabled(true); },
                OnlineAssetsController.NorthYanktonCoords,
                isExteriorSurface: true
            );

            AddOnlineLocationTeleportButton(
                "Teleport: North Yankton (Prologue & Cemetery)",
                "Streams North Yankton snow road & intact cemetery collisions offline and warps to Prologue road.",
                () => { if (!_onlineAssets.NorthYanktonLoaded) _onlineAssets.SetNorthYanktonEnabled(true); },
                OnlineAssetsController.NorthYanktonPrologueRoadCoords,
                isExteriorSurface: true
            );

            _diamondCasinoItem.CheckboxChanged += (s, e) =>
            {
                if (_suppressUiSync) return;
                if (!_diamondCasinoItem.Checked)
                {
                    SafeReturnFromUnloadingAsset(OnlineAssetsController.DiamondCasinoEntranceCoords, p => _onlineAssets.IsPlayerInsideLoadedCasino(p));
                }
                _onlineAssets.SetDiamondCasinoEnabled(_diamondCasinoItem.Checked);
                SyncOnlineAssetsMenu();
                Notify(_diamondCasinoItem.Checked ? "~g~Diamond Casino & Penthouse IPLs loaded offline.~s~" : "~y~Diamond Casino IPLs unloaded.~s~");
            };
            _onlineAssetsMenu.Add(_diamondCasinoItem);

            AddOnlineLocationTeleportButton(
                "Teleport: Casino Entrance (Exterior)",
                "Loads Diamond Casino & Resort offline and warps outside the main entrance.",
                () => { if (!_onlineAssets.DiamondCasinoLoaded) _onlineAssets.SetDiamondCasinoEnabled(true); },
                OnlineAssetsController.DiamondCasinoEntranceCoords,
                isExteriorSurface: true
            );

            AddOnlineLocationTeleportButton(
                "Teleport: Casino Main Floor (Interior)",
                "Streams & pins Diamond Casino MLO interior offline with verified floor collision.",
                () => { if (!_onlineAssets.DiamondCasinoLoaded) _onlineAssets.SetDiamondCasinoEnabled(true); },
                OnlineAssetsController.DiamondCasinoMainInteriorCoords,
                isExteriorSurface: false
            );

            AddOnlineLocationTeleportButton(
                "Teleport: Casino Master Penthouse",
                "Streams & pins Casino Penthouse MLO offline with full entity sets and verified floor collision.",
                () => { if (!_onlineAssets.DiamondCasinoLoaded) _onlineAssets.SetDiamondCasinoEnabled(true); },
                OnlineAssetsController.DiamondCasinoPenthouseCoords,
                isExteriorSurface: false
            );

            _aircraftCarrierItem.CheckboxChanged += (s, e) =>
            {
                if (_suppressUiSync) return;
                if (_aircraftCarrierItem.Checked && _onlineAssets.NorthYanktonLoaded)
                {
                    SafeReturnFromUnloadingAsset(OnlineAssetsController.SafeLosSantosReturnCoords, p => _onlineAssets.IsPlayerInNorthYanktonZone(p));
                }
                else if (!_aircraftCarrierItem.Checked)
                {
                    SafeReturnFromUnloadingAsset(OnlineAssetsController.SafeLosSantosReturnCoords, p => _onlineAssets.IsPlayerInAircraftCarrierZone(p));
                }
                _onlineAssets.SetAircraftCarrierEnabled(_aircraftCarrierItem.Checked);
                SyncOnlineAssetsMenu();
                Notify(_aircraftCarrierItem.Checked ? "~g~USS Luxington Aircraft Carrier loaded offline.~s~" : "~y~USS Luxington unloaded.~s~");
            };
            _onlineAssetsMenu.Add(_aircraftCarrierItem);

            AddOnlineLocationTeleportButton(
                "Teleport: USS Luxington Flight Deck",
                "Streams USS Luxington carrier deck & hangars offline and warps onto the flight deck.",
                () => { if (!_onlineAssets.AircraftCarrierLoaded) _onlineAssets.SetAircraftCarrierEnabled(true); },
                OnlineAssetsController.AircraftCarrierDeckCoords,
                isExteriorSurface: true
            );

            _superyachtItem.CheckboxChanged += (s, e) =>
            {
                if (_suppressUiSync) return;
                if (!_superyachtItem.Checked)
                {
                    SafeReturnFromUnloadingAsset(OnlineAssetsController.SafeLosSantosReturnCoords, p => _onlineAssets.IsPlayerOnSuperyacht(p));
                }
                _onlineAssets.SetSuperyachtEnabled(_superyachtItem.Checked);
                SyncOnlineAssetsMenu();
                Notify(_superyachtItem.Checked ? "~g~Superyacht (Dignity) hull & interior loaded offline.~s~" : "~y~Superyacht unloaded.~s~");
            };
            _onlineAssetsMenu.Add(_superyachtItem);

            AddOnlineLocationTeleportButton(
                "Teleport: Superyacht Deck",
                "Streams Superyacht hull collision (smboat) & interior rooms offline and warps onto the deck.",
                () => { if (!_onlineAssets.SuperyachtLoaded) _onlineAssets.SetSuperyachtEnabled(true); },
                OnlineAssetsController.SuperyachtDeckCoords,
                isExteriorSurface: true
            );

            _gunrunningBunkerItem.CheckboxChanged += (s, e) =>
            {
                if (_suppressUiSync) return;
                if (!_gunrunningBunkerItem.Checked)
                {
                    SafeReturnFromUnloadingAsset(OnlineAssetsController.SafeLosSantosReturnCoords, p => _onlineAssets.IsPlayerInsideLoadedBunker(p));
                }
                _onlineAssets.SetGunrunningBunkerEnabled(_gunrunningBunkerItem.Checked);
                SyncOnlineAssetsMenu();
                Notify(_gunrunningBunkerItem.Checked ? "~g~Gunrunning Underground Bunker loaded offline.~s~" : "~y~Gunrunning Bunker unloaded.~s~");
            };
            _onlineAssetsMenu.Add(_gunrunningBunkerItem);

            AddOnlineLocationTeleportButton(
                "Teleport: Gunrunning Bunker (Interior)",
                "Streams subterranean Gunrunning Bunker MLO & entity sets offline and warps inside.",
                () => { if (!_onlineAssets.GunrunningBunkerLoaded) _onlineAssets.SetGunrunningBunkerEnabled(true); },
                OnlineAssetsController.GunrunningBunkerCoords,
                isExteriorSurface: false
            );

            _doomsdayFacilityItem.CheckboxChanged += (s, e) =>
            {
                if (_suppressUiSync) return;
                if (!_doomsdayFacilityItem.Checked)
                {
                    SafeReturnFromUnloadingAsset(OnlineAssetsController.SafeLosSantosReturnCoords, p => _onlineAssets.IsPlayerInsideLoadedFacility(p));
                }
                _onlineAssets.SetDoomsdayFacilityEnabled(_doomsdayFacilityItem.Checked);
                SyncOnlineAssetsMenu();
                Notify(_doomsdayFacilityItem.Checked ? "~g~Doomsday Underground Facility loaded offline.~s~" : "~y~Doomsday Facility unloaded.~s~");
            };
            _onlineAssetsMenu.Add(_doomsdayFacilityItem);

            AddOnlineLocationTeleportButton(
                "Teleport: Doomsday Facility (Interior)",
                "Streams subterranean Doomsday Facility MLO & orbital/lounge sets offline and warps inside.",
                () => { if (!_onlineAssets.DoomsdayFacilityLoaded) _onlineAssets.SetDoomsdayFacilityEnabled(true); },
                OnlineAssetsController.DoomsdayFacilityCoords,
                isExteriorSurface: false
            );

            _cargoShipItem.CheckboxChanged += (s, e) =>
            {
                if (_suppressUiSync) return;
                if (!_cargoShipItem.Checked)
                {
                    SafeReturnFromUnloadingAsset(OnlineAssetsController.SafeLosSantosReturnCoords, p => _onlineAssets.IsPlayerOnCargoShip(p));
                }
                _onlineAssets.SetCargoShipEnabled(_cargoShipItem.Checked);
                SyncOnlineAssetsMenu();
                Notify(_cargoShipItem.Checked ? "~g~Port Cargo Ship IPL loaded offline.~s~" : "~y~Port Cargo Ship unloaded.~s~");
            };
            _onlineAssetsMenu.Add(_cargoShipItem);

            AddOnlineLocationTeleportButton(
                "Teleport: Port Cargo Ship Deck",
                "Streams the intact freighter cargo ship at Port of South LS and warps onto the deck.",
                () => { if (!_onlineAssets.CargoShipLoaded) _onlineAssets.SetCargoShipEnabled(true); },
                OnlineAssetsController.CargoShipDeckCoords,
                isExteriorSurface: true
            );

            _storyInteriorsItem.CheckboxChanged += (s, e) =>
            {
                if (_suppressUiSync) return;
                if (!_storyInteriorsItem.Checked)
                {
                    SafeReturnFromUnloadingAsset(OnlineAssetsController.SafeLosSantosReturnCoords, p => _onlineAssets.IsPlayerInsideLoadedStoryInterior(p));
                }
                _onlineAssets.SetStoryInteriorsEnabled(_storyInteriorsItem.Checked);
                SyncOnlineAssetsMenu();
                Notify(_storyInteriorsItem.Checked
                    ? "~g~Story Mission Interiors (FIB, Morgue, Lifeinvader, Ranch, Cluckin' Bell) unlocked.~s~"
                    : "~y~Story Mission Interiors restored to default save state.~s~");
            };
            _onlineAssetsMenu.Add(_storyInteriorsItem);

            AddOnlineLocationTeleportButton(
                "Teleport: FIB Headquarters Lobby",
                "Unlocks & uncaps FIB Headquarters lobby interior and warps inside.",
                () => { if (!_onlineAssets.StoryInteriorsLoaded) _onlineAssets.SetStoryInteriorsEnabled(true); },
                OnlineAssetsController.FibLobbyCoords,
                isExteriorSurface: false
            );

            AddOnlineLocationTeleportButton(
                "Teleport: Coroner's Morgue (Interior)",
                "Unlocks & uncaps the LS County Coroner Morgue interior and warps inside.",
                () => { if (!_onlineAssets.StoryInteriorsLoaded) _onlineAssets.SetStoryInteriorsEnabled(true); },
                OnlineAssetsController.CoronerMorgueCoords,
                isExteriorSurface: false
            );

            AddOnlineLocationTeleportButton(
                "Teleport: Lifeinvader Offices (Interior)",
                "Unlocks & uncaps the Rockford Hills Lifeinvader interior and warps inside.",
                () => { if (!_onlineAssets.StoryInteriorsLoaded) _onlineAssets.SetStoryInteriorsEnabled(true); },
                OnlineAssetsController.LifeinvaderOfficeCoords,
                isExteriorSurface: false
            );

            AddOnlineLocationTeleportButton(
                "Teleport: O'Neil Brothers Ranch (Interior)",
                "Streams the intact Grapeseed O'Neil farmhouse interior and warps inside.",
                () => { if (!_onlineAssets.StoryInteriorsLoaded) _onlineAssets.SetStoryInteriorsEnabled(true); },
                OnlineAssetsController.OneilRanchCoords,
                isExteriorSurface: false
            );

            var revertAllBtn = new NativeItem(
                "Return to Los Santos & Unload All IPL Maps",
                "Safely teleports back to Los Santos first, then unloads all DLC/Story IPLs and restores vanilla world."
            );
            revertAllBtn.Activated += (s, e) =>
            {
                SafeReturnIfInsideMpAssets();
                _onlineAssets.RevertAllToStoryWorld();
                SyncOnlineAssetsMenu();
                Notify("~g~Reverted cleanly to vanilla Story Mode Los Santos world.~s~");
            };
            _onlineAssetsMenu.Add(revertAllBtn);

            _onlineAssetsMenu.Shown += (s, e) => SyncOnlineAssetsMenu();
        }

        private void SetupWorldAndConfigMenu()
        {
            _weatherPresetItem.Activated += (s, e) => ApplySelectedWeatherPreset();
            _worldMenu.Add(_weatherPresetItem);

            var applyWeatherBtn = new NativeItem(
                "Apply Selected Weather",
                "Immediately sets and persists the selected Single-Player weather type."
            );
            applyWeatherBtn.Activated += (s, e) => ApplySelectedWeatherPreset();
            _worldMenu.Add(applyWeatherBtn);

            var clearWeatherBtn = new NativeItem(
                "Restore Dynamic Story Weather",
                "Clears weather persistence and resumes normal Single-Player weather progression."
            );
            clearWeatherBtn.Activated += (s, e) =>
            {
                Function.Call(Hash.CLEAR_OVERRIDE_WEATHER);
                Function.Call(Hash.CLEAR_WEATHER_TYPE_PERSIST);
                _weatherOverridden = false;
                Notify("~g~Restored dynamic Story Mode weather progression.~s~");
            };
            _worldMenu.Add(clearWeatherBtn);

            _freezeTimeItem.CheckboxChanged += (s, e) =>
            {
                if (_suppressUiSync) return;
                _clockFrozen = _freezeTimeItem.Checked;
                Function.Call(Hash.PAUSE_CLOCK, _clockFrozen);
                Notify(_clockFrozen ? "~g~Clock time frozen.~s~" : "~y~Clock time resumed.~s~");
            };
            _worldMenu.Add(_freezeTimeItem);

            AddClockPresetButton("Set Time: Morning (08:00)", "Sets Single-Player clock to 08:00 AM.", 8, 0);
            AddClockPresetButton("Set Time: Noon (12:00)", "Sets Single-Player clock to 12:00 PM.", 12, 0);
            AddClockPresetButton("Set Time: Sunset (19:30)", "Sets Single-Player clock to 07:30 PM.", 19, 30);
            AddClockPresetButton("Set Time: Midnight (00:00)", "Sets Single-Player clock to 12:00 AM.", 0, 0);

            var advanceHourBtn = new NativeItem("Advance Time +1 Hour", "Moves the Single-Player clock forward by 1 hour.");
            advanceHourBtn.Activated += (s, e) =>
            {
                int hour = (Function.Call<int>(Hash.GET_CLOCK_HOURS) + 1) % 24;
                int min = Function.Call<int>(Hash.GET_CLOCK_MINUTES);
                Function.Call(Hash.SET_CLOCK_TIME, hour, min, 0);
                if (_clockFrozen)
                {
                    Function.Call(Hash.PAUSE_CLOCK, true);
                }
                Notify(string.Format("~g~Clock set to {0:D2}:{1:D2}.~s~", hour, min));
            };
            _worldMenu.Add(advanceHourBtn);

            var saveConfigBtn = new NativeItem(
                "Save Current Settings to INI",
                "Saves current toggles and preferences into scripts\\StoryModeTrainer.ini."
            );
            saveConfigBtn.Activated += (s, e) => SaveCurrentStateToConfigFile();
            _worldMenu.Add(saveConfigBtn);

            var reloadConfigBtn = new NativeItem(
                "Reload Settings from INI",
                "Reloads scripts\\StoryModeTrainer.ini from disk and syncs all menus."
            );
            reloadConfigBtn.Activated += (s, e) =>
            {
                _config = TrainerConfig.Load(_iniPath);
                ApplyLoadedConfigToState(_config);
                SyncAllMenus();
                Notify("~g~Reloaded settings from StoryModeTrainer.ini.~s~");
            };
            _worldMenu.Add(reloadConfigBtn);

            _worldMenu.Shown += (s, e) => SyncWorldMenu();
        }

        private void AddClockPresetButton(string title, string description, int hour, int minute)
        {
            var btn = new NativeItem(title, description);
            btn.Activated += (s, e) =>
            {
                Function.Call(Hash.SET_CLOCK_TIME, hour, minute, 0);
                if (_clockFrozen)
                {
                    Function.Call(Hash.PAUSE_CLOCK, true);
                }
                Notify(string.Format("~g~Clock set to {0:D2}:{1:D2}.~s~", hour, minute));
            };
            _worldMenu.Add(btn);
        }

        private void ApplySelectedWeatherPreset()
        {
            int idx = _weatherPresetItem.SelectedIndex;
            if (idx < 0 || idx >= WeatherNames.Length) return;

            string weatherName = WeatherNames[idx];
            Function.Call(Hash.CLEAR_OVERRIDE_WEATHER);
            Function.Call(Hash.SET_WEATHER_TYPE_NOW_PERSIST, weatherName);
            _weatherOverridden = true;
            Notify("~g~Weather set to:~s~ " + WeatherLabels[idx]);
        }

        private void SaveCurrentStateToConfigFile()
        {
            if (string.IsNullOrEmpty(_iniPath))
            {
                Notify("~r~Could not resolve StoryModeTrainer.ini path.~s~");
                return;
            }

            _config.MenuToggleKey = _toggleKey;
            _config.NoClipToggleKey = _noClipToggleKey;
            _config.DespawnPreviousVehicle = _replacePrevVehicleItem.Checked;
            _config.SpawnMaxUpgraded = _spawnMaxUpgradedItem.Checked;
            _config.AutoRepairVehicle = _vehiclePhysics.AutoRepairCurrentVehicle;
            _config.NoClipBaseSpeed = _noClip.BaseSpeed;
            _config.NoClipExitFallShield = _noClip.ExitFallShieldEnabled;
            _config.NoClipSnapToGroundOnExit = _noClip.SnapToGroundOnExit;
            _config.HeistProtectTakeByDefault = _heists.ProtectHeistTake;
            _config.HeistKeepFullTakeWithoutCuts = _heists.KeepFullTakeWithoutCuts;
            _config.PlayerGodMode = _godModeItem.Checked;
            _config.PlayerVehicleGodMode = _vehicleGodModeItem.Checked;
            _config.PlayerNeverWanted = _neverWantedItem.Checked;
            _config.PlayerInfiniteAmmo = _enhancements.InfiniteAmmoEnabled;
            _config.PlayerInfiniteClip = _enhancements.InfiniteClipEnabled;
            _config.PlayerInfiniteSprint = _enhancements.InfiniteSprintEnabled;
            _config.PlayerInfiniteSpecialAbility = _enhancements.InfiniteSpecialAbilityEnabled;
            _config.PlayerCharacterWheelSwap = _enhancements.CharacterWheelSwapEnabled;
            _config.OnlineKeepSpawnedVehiclesPersistent = _onlineAssets.KeepSpawnedVehiclesPersistent;
            _config.OnlineAutoUnlockMpMaps = _onlineAssets.ExplicitMpMapsPreference;

            if (_config.Save(_iniPath))
            {
                Notify("~g~Saved current trainer settings to StoryModeTrainer.ini.~s~");
            }
            else
            {
                Notify("~r~Failed to write StoryModeTrainer.ini.~s~");
            }
        }

        private void AddOnlineLocationTeleportButton(
            string label,
            string description,
            Action ensureLoaded,
            Vector3 coords,
            bool isExteriorSurface)
        {
            var btn = new NativeItem(label, description);
            btn.Activated += (s, e) =>
            {
                if (!TryGetPlayer(out Ped player) || player.IsDead || Game.IsCutsceneActive)
                {
                    Notify("~r~Cannot teleport right now.~s~");
                    return;
                }

                if (_noClip.IsActive)
                {
                    _noClip.Disable(player, _godModeItem.Checked || _enhancements.IsJumpOrMovementShieldActive, _vehicleGodModeItem.Checked, _heists.IsHeistShieldedNow);
                    SyncNoClipCheckboxes();
                }

                if (!isExteriorSurface && player.IsInVehicle() && player.CurrentVehicle != null && player.CurrentVehicle.Exists())
                {
                    Function.Call(Hash.TASK_LEAVE_ANY_VEHICLE, player.Handle, 0, 16);
                    WaitWithOnlineGuard(0);
                    if (_aborted || IsMultiplayerOrTransitionActive() || !TryGetPlayer(out player) || player.IsDead)
                    {
                        return;
                    }
                }

                Entity target = isExteriorSurface && TryGetCurrentVehicle(out Vehicle veh) ? (Entity)veh : player;
                bool wasInOffMapBefore = _onlineAssets.IsPlayerInAnyLoadedDlcMap(player);
                bool wasTargetFrozen = target.IsPositionFrozen;
                target.IsPositionFrozen = true;
                target.Velocity = Vector3.Zero;

                try
                {
                    ensureLoaded();
                    _onlineAssets.PrepareZoneEnvironmentForCoords(coords);
                    SyncOnlineAssetsMenu();

                    // Yield 2 guarded frames so CExtraContentManager / CIslandHopper / REQUEST_IPL register
                    // .ymap and .ybn collision descriptors into CMapDataStore before NEW_LOAD_SCENE_START_SPHERE,
                    // then re-assert conflicting-IPL removal in case CIslandHopper refreshed default closed IPLs.
                    WaitWithOnlineGuard(0);
                    WaitWithOnlineGuard(0);
                    if (_aborted || IsMultiplayerOrTransitionActive() || !target.Exists())
                    {
                        return;
                    }

                    _onlineAssets.PrepareZoneEnvironmentForCoords(coords);
                    if (!isExteriorSurface)
                    {
                        _onlineAssets.EnsureInteriorPinnedAt(coords);
                    }
                }
                finally
                {
                    if (target.Exists())
                    {
                        target.IsPositionFrozen = wasTargetFrozen;
                    }
                }

                Vector3 safeRollbackPos = wasInOffMapBefore ? OnlineAssetsController.SafeLosSantosReturnCoords : target.Position;
                if (TeleportTarget(
                    target,
                    coords,
                    probeGround: false,
                    allowWait: true,
                    fallbackToRawDestination: true,
                    snapLocalExteriorSurface: isExteriorSurface,
                    overrideRollbackPos: safeRollbackPos))
                {
                    if (!isExteriorSurface)
                    {
                        _onlineAssets.EnsureInteriorPinnedAt(coords);
                    }
                    Notify("~g~Arrived:~s~ " + label.Replace("Teleport: ", string.Empty));
                }
            };
            _onlineAssetsMenu.Add(btn);
        }

        private void SafeReturnFromUnloadingAsset(Vector3 returnCoords, Func<Ped, bool> shouldReturnPredicate)
        {
            if (!TryGetPlayer(out Ped player) || shouldReturnPredicate == null) return;

            if (shouldReturnPredicate(player))
            {
                Entity target = TryGetCurrentVehicle(out Vehicle veh) ? (Entity)veh : player;
                TeleportToPresetCoords(target, returnCoords, isExteriorSurface: true);
            }
        }

        private void SafeReturnIfInsideMpAssets()
        {
            SafeReturnFromUnloadingAsset(
                OnlineAssetsController.SafeLosSantosReturnCoords,
                p => _onlineAssets.IsPlayerInAnyLoadedDlcMap(p)
            );
        }

        private void DisableNoClipBeforeModelSwitch()
        {
            if (!_noClip.IsActive) return;
            if (TryGetPlayer(out Ped player))
            {
                _noClip.Disable(player, _godModeItem.Checked || _enhancements.IsJumpOrMovementShieldActive, _vehicleGodModeItem.Checked, _heists.IsHeistShieldedNow);
                SyncNoClipCheckboxes();
            }
        }

        private void SwitchToSelectedPresetCharacter()
        {
            int idx = _characterPresetItem.SelectedIndex;
            if (idx < 0 || idx >= PlayerEnhancementsController.CharacterPresets.Length)
            {
                return;
            }

            PlayerEnhancementsController.CharacterPreset preset = PlayerEnhancementsController.CharacterPresets[idx];
            DisableNoClipBeforeModelSwitch();
            if (_enhancements.SwitchToCharacterModel(new Model(preset.Hash), preset.Label, preset.IsFreemodeModel, out string msg))
            {
                Notify("~g~" + msg + "~s~");
            }
            else
            {
                Notify("~r~" + msg + "~s~");
            }
        }

        private void ApplyWantedSelection()
        {
            if (_suppressUiSync || Game.Player == null) return;
            if (_neverWantedItem.Checked && _wantedLevelItem.SelectedItem > 0)
            {
                _suppressUiSync = true;
                try
                {
                    _neverWantedItem.Checked = false;
                }
                finally
                {
                    _suppressUiSync = false;
                }
                Game.MaxWantedLevel = DefaultMaxWantedLevel;
            }

            SetPlayerWantedLevel(_wantedLevelItem.SelectedItem);
        }

        private void ApplySelectedPresetHeistTake()
        {
            int index = _takePresetItem.SelectedIndex;
            if (index < 0 || index >= HeistManager.TakePresetAmounts.Length)
            {
                return;
            }

            int heistId = _heists.ResolveTargetHeistId();
            int amount = HeistManager.TakePresetAmounts[index];
            _heists.SetHeistTake(heistId, amount, true);
            SyncHeistMenu();
            Notify(string.Format("~g~Set {0} take to {1} (locked).~s~",
                HeistManager.GetHeistName(heistId), HeistManager.FormatCash(amount)));
        }

        private bool TryPromptUserInput(out string result)
        {
            result = null;
            if (_isPromptingInput || _aborted || IsMultiplayerOrTransitionActive())
            {
                return false;
            }

            _isPromptingInput = true;
            try
            {
                // Drive the onscreen keyboard manually with WaitWithOnlineGuard(0) so EnforceStoryModeOnlineLockout()
                // runs on EVERY single frame while the keyboard prompt is open (unlike unguarded synchronous helpers).
                Function.Call(Hash.DISPLAY_ONSCREEN_KEYBOARD, 1, "FMMC_KEY_TIP8", "", "", "", "", "", 64);
                while (true)
                {
                    int state = Function.Call<int>(Hash.UPDATE_ONSCREEN_KEYBOARD);
                    if (state == 1)
                    {
                        result = Function.Call<string>(Hash.GET_ONSCREEN_KEYBOARD_RESULT) ?? string.Empty;
                        return !_aborted && !IsMultiplayerOrTransitionActive();
                    }
                    if (state != 0)
                    {
                        return false;
                    }

                    WaitWithOnlineGuard(0);
                    if (_aborted || IsMultiplayerOrTransitionActive())
                    {
                        return false;
                    }
                }
            }
            finally
            {
                _isPromptingInput = false;
            }
        }

        private void EditProtagonistShare(int charIndex, string charName)
        {
            int heistId = _heists.ResolveTargetHeistId();
            if (!TryPromptUserInput(out string input)) return;
            if (!TryParseCashAmount(input, out int amount))
            {
                if (!string.IsNullOrWhiteSpace(input)) Notify("~r~Invalid cash amount entered.~s~");
                return;
            }

            _heists.SetCustomShare(heistId, charIndex, amount, true);
            SyncHeistMenu();
            Notify(string.Format("~g~Set {0} share for {1} to {2} (locked).~s~",
                charName, HeistManager.GetHeistName(heistId), HeistManager.FormatCash(amount)));
        }

        private void GrantActiveCharacterCash(int amount)
        {
            if (_aborted || IsMultiplayerOrTransitionActive())
            {
                return;
            }

            int updated = _heists.AddCharacterCash(-1, amount);
            if (updated < 0 && _enhancements.OriginalProtagonistIndex >= 0)
            {
                updated = _heists.AddCharacterCash(_enhancements.OriginalProtagonistIndex, amount);
            }
            if (updated < 0)
            {
                updated = _heists.AddCharacterCash(0, amount);
            }

            if (updated >= 0)
            {
                Notify(string.Format("~g~Added {0}~s~ (Balance: {1}).",
                    HeistManager.FormatCash(amount), HeistManager.FormatCash(updated)));
                return;
            }

            if (Game.Player != null)
            {
                long fallback = Math.Min(int.MaxValue, Math.Max(0L, (long)Game.Player.Money + amount));
                Game.Player.Money = (int)fallback;
                Notify(string.Format("~g~Added {0}~s~ to active character.", HeistManager.FormatCash(amount)));
            }
        }

        private static bool TryParseCashAmount(string raw, out int amount)
        {
            amount = 0;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            string cleaned = raw.Trim().Replace("$", string.Empty).Replace(",", string.Empty).Replace("_", string.Empty);
            if (cleaned.Length == 0)
            {
                return false;
            }

            double multiplier = 1.0;
            char last = char.ToUpperInvariant(cleaned[cleaned.Length - 1]);
            if (last == 'B')
            {
                multiplier = 1000000000.0;
                cleaned = cleaned.Substring(0, cleaned.Length - 1);
            }
            else if (last == 'M')
            {
                multiplier = 1000000.0;
                cleaned = cleaned.Substring(0, cleaned.Length - 1);
            }
            else if (last == 'K')
            {
                multiplier = 1000.0;
                cleaned = cleaned.Substring(0, cleaned.Length - 1);
            }

            if (!double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ||
                double.IsNaN(parsed) || double.IsInfinity(parsed) || parsed < 0.0)
            {
                return false;
            }

            double scaled = parsed * multiplier;
            if (scaled > 1000000000.0)
            {
                scaled = 1000000000.0;
            }

            amount = (int)Math.Round(scaled);
            return true;
        }

        private void SetNoClipEnabled(bool enable)
        {
            if (!TryGetPlayer(out Ped player))
            {
                SyncNoClipCheckboxes();
                return;
            }

            if (enable)
            {
                _noClip.Enable(player);
                if (_noClip.IsActive)
                {
                    Notify("~g~No-Clip enabled.~s~ WASD move, Space up, Ctrl/X down, Shift boost.");
                }
                else
                {
                    Notify("~y~No-Clip unavailable right now (dead, cutscene, or character switch).~s~");
                }
            }
            else
            {
                _noClip.Disable(player, _godModeItem.Checked || _enhancements.IsJumpOrMovementShieldActive, _vehicleGodModeItem.Checked, _heists.IsHeistShieldedNow);
                Notify("~y~No-Clip disabled.~s~");
            }

            SyncNoClipCheckboxes();
        }

        private void SyncAllMenus()
        {
            SyncPlayerMenu();
            SyncWeaponsMenu();
            SyncCharacterMenu();
            SyncNoClipMenu();
            SyncHeistMenu();
            SyncVehicleSpawnerMenu();
            SyncCustomsMenu();
            SyncOnlineAssetsMenu();
            SyncWorldMenu();
        }

        private void SyncNoClipCheckboxes()
        {
            _suppressUiSync = true;
            try
            {
                _playerNoClipItem.Checked = _noClip.IsActive;
                _noClipActiveItem.Checked = _noClip.IsActive;
            }
            finally
            {
                _suppressUiSync = false;
            }
        }

        private void SyncNoClipMenu()
        {
            _suppressUiSync = true;
            try
            {
                _noClipActiveItem.Checked = _noClip.IsActive;
                _playerNoClipItem.Checked = _noClip.IsActive;
                _noClipSpeedItem.SelectedIndex = _noClip.SpeedPresetIndex;
                _noClipAlignHeadingItem.Checked = _noClip.AlignHeadingWithCamera;
                _noClipFallShieldItem.Checked = _noClip.ExitFallShieldEnabled;
                _noClipSnapGroundItem.Checked = _noClip.SnapToGroundOnExit;
            }
            finally
            {
                _suppressUiSync = false;
            }
        }

        private void SyncHeistMenu()
        {
            _suppressUiSync = true;
            try
            {
                _heistTargetItem.SelectedIndex = _heists.SelectedMenuIndex;
                _protectHeistTakeItem.Checked = _heists.ProtectHeistTake;
                _heistIgnorePoliceItem.Checked = _heists.PoliceIgnoreDuringHeists;
                _keepFullTakeItem.Checked = _heists.KeepFullTakeWithoutCuts;
                _lockHeistTakeItem.Checked = _heists.LockTakeThroughCompletion;
            }
            finally
            {
                _suppressUiSync = false;
            }
        }

        private void SyncPlayerMenu()
        {
            _suppressUiSync = true;
            try
            {
                _playerNoClipItem.Checked = _noClip.IsActive;
                _infiniteSprintItem.Checked = _enhancements.InfiniteSprintEnabled;
                _infiniteSpecialAbilityItem.Checked = _enhancements.InfiniteSpecialAbilityEnabled;
                _runSpeedItem.SelectedIndex = _enhancements.RunSpeedIndex;
                _swimSpeedItem.SelectedIndex = _enhancements.SwimSpeedIndex;
                _jumpModeItem.SelectedIndex = _enhancements.JumpModeIndex;
                Wanted wanted = Game.Player?.Wanted;
                if (wanted != null)
                {
                    int level = Math.Max(0, Math.Min(DefaultMaxWantedLevel, wanted.WantedLevel));
                    _wantedLevelItem.SelectedIndex = level;
                }
            }
            finally
            {
                _suppressUiSync = false;
            }
        }

        private void SyncWeaponsMenu()
        {
            _suppressUiSync = true;
            try
            {
                _infiniteAmmoItem.Checked = _enhancements.InfiniteAmmoEnabled;
                _infiniteClipItem.Checked = _enhancements.InfiniteClipEnabled;
            }
            finally
            {
                _suppressUiSync = false;
            }
        }

        private void SyncCharacterMenu()
        {
            _suppressUiSync = true;
            try
            {
                _characterWheelSwapItem.Checked = _enhancements.CharacterWheelSwapEnabled;
                _characterPresetItem.SelectedIndex = _enhancements.CharacterPresetIndex;
            }
            finally
            {
                _suppressUiSync = false;
            }
        }

        private void SyncVehicleSpawnerMenu()
        {
            _suppressUiSync = true;
            try
            {
                _persistOnlineVehiclesItem.Checked = _onlineAssets.KeepSpawnedVehiclesPersistent;
            }
            finally
            {
                _suppressUiSync = false;
            }
        }

        private void SyncOnlineAssetsMenu()
        {
            _suppressUiSync = true;
            try
            {
                _mpMapsModeItem.Checked = _onlineAssets.MpMapsModeEnabled;
                _suspendShopControllerItem.Checked = _onlineAssets.SuspendShopControllerDespawn;
                _cayoPericoItem.Checked = _onlineAssets.CayoPericoLoaded;
                _diamondCasinoItem.Checked = _onlineAssets.DiamondCasinoLoaded;
                _aircraftCarrierItem.Checked = _onlineAssets.AircraftCarrierLoaded;
                _superyachtItem.Checked = _onlineAssets.SuperyachtLoaded;
                _northYanktonItem.Checked = _onlineAssets.NorthYanktonLoaded;
                _gunrunningBunkerItem.Checked = _onlineAssets.GunrunningBunkerLoaded;
                _doomsdayFacilityItem.Checked = _onlineAssets.DoomsdayFacilityLoaded;
                _cargoShipItem.Checked = _onlineAssets.CargoShipLoaded;
                _storyInteriorsItem.Checked = _onlineAssets.StoryInteriorsLoaded;
            }
            finally
            {
                _suppressUiSync = false;
            }
        }

        private void SyncWorldMenu()
        {
            _suppressUiSync = true;
            try
            {
                _freezeTimeItem.Checked = _clockFrozen;
            }
            finally
            {
                _suppressUiSync = false;
            }
        }

        private void SyncCustomsMenu()
        {
            _suppressUiSync = true;
            try
            {
                _vehPowerMultItem.SelectedIndex = _vehiclePhysics.PowerIndex;
                _vehTopSpeedMultItem.SelectedIndex = _vehiclePhysics.TopSpeedIndex;
                _vehBrakeMultItem.SelectedIndex = _vehiclePhysics.BrakeIndex;
                _vehTractionMultItem.SelectedIndex = _vehiclePhysics.TractionIndex;
                _vehAutoRepairItem.Checked = _vehiclePhysics.AutoRepairCurrentVehicle;

                if (!TryGetCurrentVehicle(out Vehicle veh))
                {
                    foreach (ModSliderBinding binding in _modBindings)
                    {
                        binding.Item.SelectedIndex = 0;
                    }
                    _turboItem.Checked = false;
                    _bulletproofTiresItem.Checked = false;
                    return;
                }

                foreach (ModSliderBinding binding in _modBindings)
                {
                    VehicleMod mod = veh.Mods[binding.ModType];
                    int level = mod.Count > 0 ? mod.Index + 1 : 0;
                    binding.Item.SelectedIndex = Math.Max(0, Math.Min(binding.Item.Items.Count - 1, level));
                }

                _turboItem.Checked = veh.Mods[VehicleToggleModType.Turbo].IsInstalled;
                _bulletproofTiresItem.Checked = !veh.CanTiresBurst;
            }
            finally
            {
                _suppressUiSync = false;
            }
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (_aborted) return;

            if (!_pauseImmunityApplied)
            {
                Function.Call(Hash.SET_THIS_SCRIPT_CAN_BE_PAUSED, false);
                _pauseImmunityApplied = true;
            }

            EnforceStoryModeOnlineLockout();

            if (TryGetMultiplayerOrTransitionReason(out string mpTrigger))
            {
                AttemptCancelOnlineTransition();
                Notify(
                    $"~r~GTA Online transition blocked & Trainer disabled!~s~ ({mpTrigger}) Exit GTA 5 and run 2-Disable-And-CleanForOnline.bat before playing Online.",
                    true
                );
                CleanupBeforeAbort();
                Abort();
                return;
            }

            _cachedMpBlockedThisTick = false;

            if (!_welcomeShown && TryGetPlayer(out _))
            {
                _welcomeShown = true;
                Notify(string.Format(
                    "~b~Story Mode Trainer~s~ loaded (Online Locked). Menu: ~y~{0}~s~ | No-Clip: ~y~{1}~s~.",
                    _toggleKey,
                    _noClipToggleKey
                ));
            }

            _onlineAssets.Tick();

            if (TryGetPlayer(out Ped player))
            {
                if (!Game.IsPaused)
                {
                    if (_enhancements.ProcessCharacterWheel(player, DisableNoClipBeforeModelSwitch, out string wheelSwitchMsg))
                    {
                        if (!string.IsNullOrEmpty(wheelSwitchMsg))
                        {
                            Notify("~g~" + wheelSwitchMsg + "~s~");
                        }
                        if (!TryGetPlayer(out player))
                        {
                            return;
                        }
                    }
                    else if (!_enhancements.CharacterWheelSwapEnabled
                        && _enhancements.IsModelOverriddenByTrainer
                        && Controls.IsControlJustPressed(ControlType.PlayerControl, ControlAction.CharacterWheel))
                    {
                        int now = Game.GameTime;
                        int elapsed = now - _lastWheelHintGameTime;
                        if (elapsed >= OnlineWarningCooldownMs || elapsed < 0)
                        {
                            _lastWheelHintGameTime = now;
                            Notify("~y~Tip:~s~ Enable Character Wheel Swap or Restore Original Story Protagonist to use the Character Wheel.");
                        }
                    }

                    bool wasNoClipActive = _noClip.IsActive;
                    _noClip.Tick(
                        player,
                        _godModeItem.Checked || _enhancements.IsJumpOrMovementShieldActive,
                        _vehicleGodModeItem.Checked,
                        _heists.IsHeistShieldedNow,
                        suspendMovementInput: _isPromptingInput || _pool.AreAnyVisible
                    );
                    if (wasNoClipActive && !_noClip.IsActive)
                    {
                        SyncNoClipCheckboxes();
                        Notify("~y~No-Clip auto-disabled (cutscene, character switch, or death).~s~");
                    }

                    _enhancements.Tick(
                        player,
                        _noClip.IsActive,
                        _godModeItem.Checked || _noClip.IsActive || _noClip.IsFallShieldActive,
                        _heists.IsHeistShieldedNow
                    );

                    bool noClipControllingVehicle = _noClip.IsActive && player.IsInVehicle() && _noClip.IsControllingEntity(player.CurrentVehicle);
                    _vehiclePhysics.Tick(player, isNoClipControlling: noClipControllingVehicle);

                    bool noClipShielded = _noClip.IsActive || _noClip.IsFallShieldActive;
                    _heists.Tick(player, _godModeItem.Checked, noClipShielded);
                    ApplyPlayerAndVehicleProtections(player);

                    if (_neverWantedItem.Checked)
                    {
                        if (Game.MaxWantedLevel != 0) Game.MaxWantedLevel = 0;
                        if ((Game.Player?.Wanted?.WantedLevel ?? 0) > 0)
                        {
                            SetPlayerWantedLevel(0);
                        }
                    }
                }
            }
            else
            {
                _vehiclePhysics.Tick(null);
            }

            if (!Game.IsPaused && !_isPromptingInput)
            {
                _pool.Process();
            }
        }

        private void ApplyPlayerAndVehicleProtections(Ped player)
        {
            bool userGod = _godModeItem.Checked;
            bool noClipActive = _noClip.IsActive;
            bool noClipFallShield = _noClip.IsFallShieldActive;
            bool jumpShield = _enhancements.IsJumpOrMovementShieldActive;
            bool superSprint = _enhancements.IsSuperSprintActive;
            bool heistShield = _heists.IsHeistShieldedNow;

            bool wantPedInvincible = userGod || noClipActive || noClipFallShield || jumpShield;
            bool wantBulletExplosionProof = wantPedInvincible || heistShield;
            bool wantCollisionProof = userGod || noClipActive || noClipFallShield || jumpShield;
            bool wantFireProof = userGod || noClipActive || noClipFallShield;
            bool wantMeleeProof = userGod || noClipActive;
            bool wantNoRagdoll = userGod || noClipActive || noClipFallShield || jumpShield || superSprint;
            bool wantNoDrown = userGod || noClipActive;

            bool anyPedProtection = wantPedInvincible || wantBulletExplosionProof || wantCollisionProof
                || wantFireProof || wantMeleeProof || wantNoRagdoll || wantNoDrown;

            int proofMask = (wantPedInvincible ? 1 : 0)
                | (wantBulletExplosionProof ? 2 : 0)
                | (wantCollisionProof ? 4 : 0)
                | (wantFireProof ? 8 : 0)
                | (wantMeleeProof ? 16 : 0)
                | (wantNoRagdoll ? 32 : 0)
                | (wantNoDrown ? 64 : 0);

            if (anyPedProtection)
            {
                if (player.IsInvincible != wantPedInvincible)
                {
                    player.IsInvincible = wantPedInvincible;
                }
                if (Game.Player != null && Game.Player.IsInvincible != wantPedInvincible)
                {
                    Game.Player.IsInvincible = wantPedInvincible;
                }

                if (_lastPedProofHandle != player.Handle || _lastPedProofMask != proofMask)
                {
                    Function.Call(
                        Hash.SET_ENTITY_PROOFS,
                        player.Handle,
                        wantBulletExplosionProof,
                        wantFireProof,
                        wantBulletExplosionProof,
                        wantCollisionProof,
                        wantMeleeProof,
                        false,
                        false,
                        false
                    );
                    player.CanRagdoll = !wantNoRagdoll;
                    player.DrownsInWater = !wantNoDrown;
                    player.DrownsInSinkingVehicle = !wantNoDrown;
                    _lastPedProofHandle = player.Handle;
                    _lastPedProofMask = proofMask;
                }

                if (wantNoRagdoll && player.IsRagdoll)
                {
                    player.CancelRagdoll();
                }
                _trainerAppliedPedProtections = true;
            }
            else if (_trainerAppliedPedProtections)
            {
                player.IsInvincible = false;
                if (Game.Player != null)
                {
                    Game.Player.IsInvincible = false;
                }
                Function.Call(Hash.SET_ENTITY_PROOFS, player.Handle, false, false, false, false, false, false, false, false);
                player.CanRagdoll = true;
                player.DrownsInWater = true;
                player.DrownsInSinkingVehicle = true;
                _trainerAppliedPedProtections = false;
                _lastPedProofHandle = player.Handle;
                _lastPedProofMask = 0;
            }

            bool wantVehInvincible = _vehicleGodModeItem.Checked || noClipActive || noClipFallShield;
            if (TryGetCurrentVehicle(out Vehicle veh))
            {
                if (_lastGodVehicle != null && _lastGodVehicle.Handle != veh.Handle)
                {
                    if (_trainerAppliedVehicleGod && _lastGodVehicle.Exists())
                    {
                        RestoreVehicleProtectionDefaults(_lastGodVehicle, restoreTires: true);
                    }
                    _lastGodVehicle = null;
                    _trainerAppliedVehicleGod = false;
                }

                if (wantVehInvincible)
                {
                    if (!_trainerAppliedVehicleGod || _lastGodVehicle == null || _lastGodVehicle.Handle != veh.Handle)
                    {
                        veh.IsInvincible = true;
                        Function.Call(Hash.SET_ENTITY_PROOFS, veh.Handle, true, true, true, true, true, false, false, false);
                        veh.CanTiresBurst = false;
                        veh.CanWheelsBreak = false;
                        veh.CanBeVisiblyDamaged = false;
                        _trainerAppliedVehicleGod = true;
                    }
                    _lastGodVehicle = veh;
                }
                else if (_trainerAppliedVehicleGod)
                {
                    RestoreVehicleProtectionDefaults(veh, restoreTires: true);
                    _trainerAppliedVehicleGod = false;
                    _lastGodVehicle = null;
                }
            }
            else if (_lastGodVehicle != null && !noClipActive && !noClipFallShield)
            {
                if (_trainerAppliedVehicleGod && _lastGodVehicle.Exists())
                {
                    RestoreVehicleProtectionDefaults(_lastGodVehicle, restoreTires: true);
                }
                _lastGodVehicle = null;
                _trainerAppliedVehicleGod = false;
            }
        }

        private void RestoreVehicleProtectionDefaults(Vehicle vehicle, bool restoreTires = true)
        {
            vehicle.IsInvincible = false;
            Function.Call(Hash.SET_ENTITY_PROOFS, vehicle.Handle, false, false, false, false, false, false, false, false);
            vehicle.CanWheelsBreak = true;
            vehicle.CanBeVisiblyDamaged = true;
            if (restoreTires)
            {
                vehicle.CanTiresBurst = true;
            }
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (_aborted
                || _isPromptingInput
                || _isBusyOperation
                || Game.IsPaused
                || IsMultiplayerOrTransitionActive()
                || (e.Modifiers & (Keys.Control | Keys.Alt)) != Keys.None)
            {
                return;
            }

            if (e.KeyCode == _toggleKey)
            {
                if (_pool.AreAnyVisible)
                {
                    _pool.HideAll();
                }
                else
                {
                    _pool.HideAll();
                    SyncAllMenus();
                    _mainMenu.Visible = true;
                }
                return;
            }

            if (e.KeyCode == _noClipToggleKey)
            {
                SetNoClipEnabled(!_noClip.IsActive);
            }
        }

        private void OnAborted(object sender, EventArgs e)
        {
            CleanupBeforeAbort();
        }

        private void CleanupBeforeAbort()
        {
            if (_cleanedUp)
            {
                return;
            }
            _cleanedUp = true;
            _aborted = true;
            _cachedMpBlockedThisTick = true;
            _pool.HideAll();

            Ped player;
            bool hasPlayer = TryGetPlayer(out player);

            // Warp back onto safe Los Santos ground BEFORE unloading off-map, Superyacht, or interior MP IPLs.
            if (hasPlayer && _onlineAssets.HasAnyMapLoaded && _onlineAssets.IsPlayerInAnyLoadedDlcMap(player))
            {
                Vector3 safeCoords = OnlineAssetsController.SafeLosSantosReturnCoords;
                Function.Call(Hash.REQUEST_COLLISION_AT_COORD, safeCoords.X, safeCoords.Y, safeCoords.Z);
                Entity safeTarget = TryGetCurrentVehicle(out Vehicle veh) ? (Entity)veh : player;
                safeTarget.PositionNoOffset = safeCoords;
                safeTarget.Velocity = Vector3.Zero;
            }

            _vehiclePhysics.CleanupOnAbort(hasPlayer ? player : null);
            _noClip.ForceCleanupOnAbort(hasPlayer ? player : null);
            _heists.CleanupOnAbort(hasPlayer ? player : null);
            _enhancements.CleanupOnAbort(hasPlayer ? player : null, false, false);
            _onlineAssets.CleanupOnAbort();

            if (_clockFrozen)
            {
                Function.Call(Hash.PAUSE_CLOCK, false);
                _clockFrozen = false;
            }

            if (_weatherOverridden)
            {
                Function.Call(Hash.CLEAR_OVERRIDE_WEATHER);
                Function.Call(Hash.CLEAR_WEATHER_TYPE_PERSIST);
                _weatherOverridden = false;
            }

            if (hasPlayer)
            {
                player.IsInvincible = false;
                player.IsBulletProof = false;
                player.IsExplosionProof = false;
                player.IsCollisionProof = false;
                player.IsFireProof = false;
                player.IsMeleeProof = false;
                player.CanRagdoll = true;
                player.DrownsInWater = true;
                player.DrownsInSinkingVehicle = true;
            }
            if (Game.Player != null)
            {
                Game.Player.IsInvincible = false;
            }

            _suppressUiSync = true;
            try
            {
                _bulletproofTiresItem.Checked = false;
            }
            finally
            {
                _suppressUiSync = false;
            }

            if (_trainerAppliedVehicleGod && _lastGodVehicle != null && _lastGodVehicle.Exists())
            {
                RestoreVehicleProtectionDefaults(_lastGodVehicle, restoreTires: true);
            }
            _lastGodVehicle = null;
            _trainerAppliedVehicleGod = false;

            if (_lastSpawnedVehicle != null && _lastSpawnedVehicle.Exists())
            {
                _lastSpawnedVehicle.IsPersistent = false;
            }
            _lastSpawnedVehicle = null;

            for (int i = 0; i < _spawnedPersistentVehicles.Count; i++)
            {
                Vehicle tracked = _spawnedPersistentVehicles[i];
                if (tracked != null && tracked.Exists())
                {
                    tracked.IsPersistent = false;
                }
            }
            _spawnedPersistentVehicles.Clear();

            if (_neverWantedItem.Checked)
            {
                Game.MaxWantedLevel = DefaultMaxWantedLevel;
            }
        }

        private static bool TryFindSolidSurfaceAt(
            Vector3 probeAnchor,
            Entity ignoreEntity,
            float maxRise,
            float maxDrop,
            out float surfaceZ)
        {
            RaycastResult ray = World.Raycast(
                new Vector3(probeAnchor.X, probeAnchor.Y, probeAnchor.Z + maxRise),
                new Vector3(probeAnchor.X, probeAnchor.Y, probeAnchor.Z - maxDrop),
                IntersectFlags.Map | IntersectFlags.Objects,
                ignoreEntity
            );
            if (ray.DidHit)
            {
                surfaceZ = ray.HitPosition.Z;
                return true;
            }

            if (World.GetGroundHeight(new Vector3(probeAnchor.X, probeAnchor.Y, probeAnchor.Z + maxRise), out float groundZ, GetGroundHeightMode.Normal)
                && Math.Abs(groundZ - probeAnchor.Z) <= Math.Max(maxRise, maxDrop))
            {
                surfaceZ = groundZ;
                return true;
            }

            surfaceZ = 0.0f;
            return false;
        }

        private bool TeleportToPresetCoords(Entity target, Vector3 destination, bool isExteriorSurface)
        {
            return TeleportTarget(target, destination, probeGround: false, allowWait: true, fallbackToRawDestination: true, snapLocalExteriorSurface: isExteriorSurface);
        }

        private bool TeleportTarget(
            Entity target,
            Vector3 destination,
            bool probeGround,
            bool allowWait,
            bool fallbackToRawDestination = true,
            bool snapLocalExteriorSurface = false,
            Vector3? overrideRollbackPos = null)
        {
            if (target == null || !target.Exists()) return false;

            if (TryGetPlayer(out Ped player) && _noClip.IsActive)
            {
                _noClip.Disable(player, _godModeItem.Checked || _enhancements.IsJumpOrMovementShieldActive, _vehicleGodModeItem.Checked, _heists.IsHeistShieldedNow);
                SyncNoClipCheckboxes();
            }

            Vector3 prevPos = overrideRollbackPos ?? target.Position;
            bool wasFrozen = target.IsPositionFrozen;
            target.IsPositionFrozen = true;
            target.Velocity = Vector3.Zero;
            bool focusOverridden = false;
            bool loadSceneStarted = false;

            try
            {
                if (!allowWait)
                {
                    Function.Call(Hash.REQUEST_COLLISION_AT_COORD, destination.X, destination.Y, destination.Z);
                    target.PositionNoOffset = destination;
                    target.Velocity = Vector3.Zero;
                    return true;
                }

                if (!probeGround)
                {
                    if (_onlineAssets.PrepareZoneEnvironmentForCoords(destination))
                    {
                        WaitWithOnlineGuard(0);
                        WaitWithOnlineGuard(0);
                        if (_aborted || IsMultiplayerOrTransitionActive() || !target.Exists())
                        {
                            if (target.Exists())
                            {
                                target.PositionNoOffset = prevPos;
                                target.Velocity = Vector3.Zero;
                            }
                            return false;
                        }
                    }

                    if (snapLocalExteriorSurface && Function.Call<int>(Hash.GET_INTERIOR_FROM_ENTITY, target.Handle) != 0)
                    {
                        Function.Call(Hash.CLEAR_ROOM_FOR_ENTITY, target.Handle);
                        Function.Call(Hash.CLEAR_ROOM_FOR_GAME_VIEWPORT);
                    }

                    if (Function.Call<bool>(Hash.IS_NEW_LOAD_SCENE_ACTIVE))
                    {
                        Function.Call(Hash.NEW_LOAD_SCENE_STOP);
                    }

                    Function.Call(Hash.SET_FOCUS_POS_AND_VEL, destination.X, destination.Y, destination.Z, 0.0f, 0.0f, 0.0f);
                    focusOverridden = true;
                    Function.Call(Hash.REQUEST_COLLISION_AT_COORD, destination.X, destination.Y, destination.Z);
                    Function.Call(Hash.REQUEST_ADDITIONAL_COLLISION_AT_COORD, destination.X, destination.Y, destination.Z);
                    loadSceneStarted = Function.Call<bool>(Hash.NEW_LOAD_SCENE_START_SPHERE, destination.X, destination.Y, destination.Z, 140.0f, 0);

                    if (loadSceneStarted)
                    {
                        int sceneStartMs = Game.GameTime;
                        while (!Function.Call<bool>(Hash.IS_NEW_LOAD_SCENE_LOADED)
                            && unchecked((uint)(Game.GameTime - sceneStartMs)) < 2500u)
                        {
                            Function.Call(Hash.REQUEST_COLLISION_AT_COORD, destination.X, destination.Y, destination.Z);
                            Function.Call(Hash.REQUEST_ADDITIONAL_COLLISION_AT_COORD, destination.X, destination.Y, destination.Z);
                            WaitWithOnlineGuard(0);
                            if (_aborted || IsMultiplayerOrTransitionActive())
                            {
                                if (target.Exists())
                                {
                                    target.PositionNoOffset = prevPos;
                                    target.Velocity = Vector3.Zero;
                                }
                                return false;
                            }
                            if (!target.Exists())
                            {
                                return false;
                            }
                        }

                        if (Function.Call<bool>(Hash.IS_NEW_LOAD_SCENE_ACTIVE))
                        {
                            Function.Call(Hash.NEW_LOAD_SCENE_STOP);
                        }
                        loadSceneStarted = false;
                    }

                    Function.Call(Hash.LOAD_ALL_OBJECTS_NOW);
                    target.PositionNoOffset = destination;
                    target.Velocity = Vector3.Zero;

                    float verifiedSurfaceZ = destination.Z;
                    bool hasVerifiedSurface = false;

                    // Stream up to ~4.0s (160 * 25ms) so heavy multi-IPL islands (Cayo Perico, North Yankton,
                    // USS Luxington, Superyacht) and MLO interiors (Casino, Penthouse, Bunker, Facility, Morgue)
                    // physically bind static .ybn / MLO floor collision before unfreezing the player.
                    for (int attempt = 0; attempt < 160; attempt++)
                    {
                        Function.Call(Hash.SET_FOCUS_POS_AND_VEL, destination.X, destination.Y, destination.Z, 0.0f, 0.0f, 0.0f);
                        Function.Call(Hash.REQUEST_COLLISION_AT_COORD, destination.X, destination.Y, destination.Z);
                        Function.Call(Hash.REQUEST_ADDITIONAL_COLLISION_AT_COORD, destination.X, destination.Y, destination.Z);
                        if (!snapLocalExteriorSurface)
                        {
                            _onlineAssets.EnsureInteriorPinnedAt(destination);
                        }

                        WaitWithOnlineGuard(25);
                        if (_aborted || IsMultiplayerOrTransitionActive())
                        {
                            if (target.Exists())
                            {
                                target.PositionNoOffset = prevPos;
                                target.Velocity = Vector3.Zero;
                            }
                            return false;
                        }

                        if (!target.Exists())
                        {
                            return false;
                        }

                        bool collisionLoaded = Function.Call<bool>(Hash.HAS_COLLISION_LOADED_AROUND_ENTITY, target.Handle);
                        if (snapLocalExteriorSurface)
                        {
                            if (TryFindSolidSurfaceAt(destination, target, maxRise: 8.0f, maxDrop: 14.0f, out float extZ))
                            {
                                verifiedSurfaceZ = extZ;
                                hasVerifiedSurface = true;
                                if (collisionLoaded && attempt >= 4)
                                {
                                    break;
                                }
                            }
                        }
                        else
                        {
                            bool interiorReady = _onlineAssets.IsInteriorReadyAt(destination);
                            if (TryFindSolidSurfaceAt(destination, target, maxRise: 3.5f, maxDrop: 10.0f, out float intZ))
                            {
                                verifiedSurfaceZ = intZ;
                                hasVerifiedSurface = true;
                                if ((interiorReady || attempt >= 16) && collisionLoaded && attempt >= 4)
                                {
                                    break;
                                }
                            }
                            else if (interiorReady && collisionLoaded && attempt >= 24)
                            {
                                break;
                            }
                        }
                    }

                    if (target.Exists())
                    {
                        if (hasVerifiedSurface && Math.Abs(verifiedSurfaceZ - destination.Z) <= 12.0f)
                        {
                            float offsetZ = target is Vehicle ? 0.75f : 1.0f;
                            target.PositionNoOffset = new Vector3(destination.X, destination.Y, verifiedSurfaceZ + offsetZ);
                            if (snapLocalExteriorSurface && target is Vehicle landedVeh && !landedVeh.IsInWater && !landedVeh.Model.IsBoat && !landedVeh.Model.IsSubmarine)
                            {
                                landedVeh.PlaceOnGround();
                            }
                        }
                        target.Velocity = Vector3.Zero;
                    }
                    return true;
                }

                // Waypoint / unknown-Z surface probe: pre-arm or release off-map zone water/collisions while frozen.
                if (_onlineAssets.PrepareZoneEnvironmentForCoords(new Vector3(destination.X, destination.Y, 60.0f)))
                {
                    WaitWithOnlineGuard(0);
                    WaitWithOnlineGuard(0);
                }
                else
                {
                    WaitWithOnlineGuard(0);
                }
                if (_aborted || IsMultiplayerOrTransitionActive() || !target.Exists())
                {
                    return false;
                }

                if (Function.Call<bool>(Hash.IS_NEW_LOAD_SCENE_ACTIVE))
                {
                    Function.Call(Hash.NEW_LOAD_SCENE_STOP);
                }

                Function.Call(Hash.SET_FOCUS_POS_AND_VEL, destination.X, destination.Y, 150.0f, 0.0f, 0.0f, 0.0f);
                focusOverridden = true;
                Function.Call(Hash.REQUEST_COLLISION_AT_COORD, destination.X, destination.Y, 150.0f);
                Function.Call(Hash.REQUEST_ADDITIONAL_COLLISION_AT_COORD, destination.X, destination.Y, 150.0f);
                loadSceneStarted = Function.Call<bool>(Hash.NEW_LOAD_SCENE_START_SPHERE, destination.X, destination.Y, 150.0f, 160.0f, 0);

                float[] probeHeights = { 250f, 120f, 60f, 30f, 450f, 700f, 950f, 15f, -20f };
                float resolvedGroundZ = 0f;
                bool foundGround = false;

                for (int i = 0; i < probeHeights.Length; i++)
                {
                    float probeZ = probeHeights[i];
                    Function.Call(Hash.SET_FOCUS_POS_AND_VEL, destination.X, destination.Y, probeZ, 0.0f, 0.0f, 0.0f);
                    Function.Call(Hash.REQUEST_COLLISION_AT_COORD, destination.X, destination.Y, probeZ);
                    Function.Call(Hash.REQUEST_ADDITIONAL_COLLISION_AT_COORD, destination.X, destination.Y, probeZ);
                    target.PositionNoOffset = new Vector3(destination.X, destination.Y, probeZ);
                    WaitWithOnlineGuard(40);

                    if (_aborted || IsMultiplayerOrTransitionActive())
                    {
                        if (target.Exists())
                        {
                            target.PositionNoOffset = prevPos;
                            target.Velocity = Vector3.Zero;
                        }
                        return false;
                    }

                    if (!target.Exists())
                    {
                        return false;
                    }

                    RaycastResult wpRay = World.Raycast(
                        new Vector3(destination.X, destination.Y, Math.Max(probeZ + 40.0f, 400.0f)),
                        new Vector3(destination.X, destination.Y, -40.0f),
                        IntersectFlags.Map | IntersectFlags.Objects,
                        target
                    );
                    if (wpRay.DidHit)
                    {
                        resolvedGroundZ = wpRay.HitPosition.Z;
                        foundGround = true;
                        break;
                    }

                    if (World.GetGroundHeight(new Vector3(destination.X, destination.Y, 1000.0f), out resolvedGroundZ, GetGroundHeightMode.Normal)
                        || World.GetGroundHeight(new Vector3(destination.X, destination.Y, probeZ + 25.0f), out resolvedGroundZ, GetGroundHeightMode.Normal))
                    {
                        foundGround = true;
                        break;
                    }
                }

                if (!foundGround
                    && target.Exists()
                    && World.GetGroundHeight(new Vector3(destination.X, destination.Y, 1000.0f), out float waterZ, GetGroundHeightMode.ConsiderWaterAsGround))
                {
                    resolvedGroundZ = waterZ;
                    foundGround = true;
                }

                if (foundGround && target.Exists())
                {
                    float offsetZ = target is Vehicle ? 0.75f : 1.0f;
                    Function.Call(Hash.SET_FOCUS_POS_AND_VEL, destination.X, destination.Y, resolvedGroundZ, 0.0f, 0.0f, 0.0f);
                    Function.Call(Hash.REQUEST_COLLISION_AT_COORD, destination.X, destination.Y, resolvedGroundZ);
                    target.PositionNoOffset = new Vector3(destination.X, destination.Y, resolvedGroundZ + offsetZ);
                    target.Velocity = Vector3.Zero;

                    for (int settle = 0; settle < 32; settle++)
                    {
                        Function.Call(Hash.REQUEST_COLLISION_AT_COORD, destination.X, destination.Y, resolvedGroundZ);
                        WaitWithOnlineGuard(25);
                        if (_aborted || IsMultiplayerOrTransitionActive() || !target.Exists())
                        {
                            return false;
                        }
                        if (Function.Call<bool>(Hash.HAS_COLLISION_LOADED_AROUND_ENTITY, target.Handle)
                            && TryFindSolidSurfaceAt(new Vector3(destination.X, destination.Y, resolvedGroundZ), target, maxRise: 12.0f, maxDrop: 12.0f, out float settledZ))
                        {
                            resolvedGroundZ = settledZ;
                            if (settle >= 4)
                            {
                                break;
                            }
                        }
                    }

                    target.PositionNoOffset = new Vector3(destination.X, destination.Y, resolvedGroundZ + offsetZ);
                    target.Velocity = Vector3.Zero;

                    if (target is Vehicle landedVehicle && !landedVehicle.IsInWater && !landedVehicle.Model.IsBoat && !landedVehicle.Model.IsSubmarine)
                    {
                        landedVehicle.PlaceOnGround();
                    }

                    return true;
                }

                if (target.Exists())
                {
                    target.PositionNoOffset = fallbackToRawDestination ? destination : prevPos;
                    target.Velocity = Vector3.Zero;
                }
                return fallbackToRawDestination;
            }
            finally
            {
                if (loadSceneStarted && Function.Call<bool>(Hash.IS_NEW_LOAD_SCENE_ACTIVE))
                {
                    Function.Call(Hash.NEW_LOAD_SCENE_STOP);
                }
                if (focusOverridden)
                {
                    Function.Call(Hash.CLEAR_FOCUS);
                }
                if (target.Exists())
                {
                    target.IsPositionFrozen = wasFrozen;
                }
            }
        }

        private void TeleportToWaypoint()
        {
            if (!Game.IsWaypointActive)
            {
                Notify("~r~No waypoint set on map.~s~");
                return;
            }

            if (!TryGetPlayer(out Ped player) || player.IsDead || Game.IsCutsceneActive)
            {
                Notify("~r~Cannot teleport right now.~s~");
                return;
            }

            Vector3 wp = World.WaypointPosition;
            Entity target = TryGetCurrentVehicle(out Vehicle veh) ? (Entity)veh : player;
            if (TeleportTarget(target, wp, probeGround: true, allowWait: true, fallbackToRawDestination: false))
            {
                Notify("~g~Teleported to waypoint.~s~");
            }
            else if (!_aborted && !IsMultiplayerOrTransitionActive())
            {
                Notify("~r~Could not resolve ground collision at waypoint.~s~");
            }
        }

        private void SpawnVehicle(string rawModelName)
        {
            if (string.IsNullOrWhiteSpace(rawModelName)) return;
            if (!TryGetPlayer(out Ped initialPlayer) || initialPlayer.IsDead || Game.IsCutsceneActive)
            {
                Notify("~r~Cannot spawn vehicle right now.~s~");
                return;
            }

            string modelName = rawModelName.Trim().ToUpperInvariant();
            Model model = new Model(modelName);

            if (!model.IsInCdImage || !model.IsValid || !model.IsVehicle)
            {
                Notify($"~r~Invalid vehicle model:~s~ {modelName}");
                return;
            }

            _isBusyOperation = true;
            try
            {
                model.Request();
                int deadline = Game.GameTime + 3000;
                while (!model.IsLoaded)
                {
                    int remaining = deadline - Game.GameTime;
                    if (remaining <= 0 || remaining > 10000)
                    {
                        break;
                    }
                    WaitWithOnlineGuard(10);
                    if (_aborted || IsMultiplayerOrTransitionActive()) return;
                }

                if (!model.IsLoaded)
                {
                    Notify($"~r~Timed out loading vehicle:~s~ {modelName}");
                    return;
                }

                if (_aborted || IsMultiplayerOrTransitionActive() || !TryGetPlayer(out Ped player) || player.IsDead) return;

                if (_noClip.IsActive)
                {
                    _noClip.Disable(player, _godModeItem.Checked || _enhancements.IsJumpOrMovementShieldActive, _vehicleGodModeItem.Checked, _heists.IsHeistShieldedNow);
                    SyncNoClipCheckboxes();
                }

                var dims = model.Dimensions;
                float halfLength = Math.Max(2.5f, (dims.Item2.Y - dims.Item1.Y) * 0.55f);
                Vector3 spawnPos = player.Position + player.ForwardVector * (halfLength + 2.0f);
                float spawnHeading = player.Heading;
                Vector3 preservedVelocity = Vector3.Zero;

                if (_replacePrevVehicleItem.Checked && _lastSpawnedVehicle != null && _lastSpawnedVehicle.Exists())
                {
                    if (player.IsInVehicle() && player.CurrentVehicle != null && player.CurrentVehicle.Handle == _lastSpawnedVehicle.Handle)
                    {
                        spawnPos = _lastSpawnedVehicle.Position;
                        spawnHeading = _lastSpawnedVehicle.Heading;
                        preservedVelocity = _lastSpawnedVehicle.Velocity;
                        player.Task.WarpOutOfVehicle(_lastSpawnedVehicle);
                    }
                    if (_lastGodVehicle != null && _lastGodVehicle.Handle == _lastSpawnedVehicle.Handle)
                    {
                        _lastGodVehicle = null;
                    }
                    _lastSpawnedVehicle.IsPersistent = false;
                    _lastSpawnedVehicle.Delete();
                    _lastSpawnedVehicle = null;
                }

                int playerInteriorId = Function.Call<int>(Hash.GET_INTERIOR_FROM_ENTITY, player.Handle);
                int playerRoomKey = playerInteriorId != 0 ? Function.Call<int>(Hash.GET_ROOM_KEY_FROM_ENTITY, player.Handle) : 0;

                Vehicle veh = Vehicle.Create(model, spawnPos, spawnHeading);
                if (veh == null || !veh.Exists())
                {
                    Notify($"~r~Failed to spawn vehicle:~s~ {modelName}");
                    return;
                }

                if (playerInteriorId != 0 && playerRoomKey != 0 && Function.Call<bool>(Hash.IS_VALID_INTERIOR, playerInteriorId))
                {
                    Function.Call(Hash.FORCE_ROOM_FOR_ENTITY, veh.Handle, playerInteriorId, playerRoomKey);
                }

                if (!veh.IsInWater && !model.IsBoat && !model.IsSubmarine)
                {
                    veh.PlaceOnGround();
                }
                veh.IsEngineRunning = true;
                veh.DirtLevel = 0f;

                if (_spawnMaxUpgradedItem.Checked)
                {
                    InstallMaxPerformanceUpgradesOnVehicle(veh);
                }

                _onlineAssets.ApplyOfflineVehiclePersistence(veh);
                player.SetIntoVehicle(veh, VehicleSeat.Driver);
                if (preservedVelocity.LengthSquared() > 1.0f)
                {
                    veh.Velocity = preservedVelocity;
                }

                _lastSpawnedVehicle = veh;
                if (veh.IsPersistent)
                {
                    _spawnedPersistentVehicles.RemoveAll(v => v == null || !v.Exists());
                    _spawnedPersistentVehicles.Add(veh);
                }
                _vehiclePhysics.NotifyVehicleRepairedOrUpgraded();
                SyncCustomsMenu();
                Notify(_spawnMaxUpgradedItem.Checked
                    ? $"~g~Spawned~s~ {modelName} ~c~(Max Upgraded)~s~"
                    : $"~g~Spawned~s~ {modelName}");
            }
            finally
            {
                _isBusyOperation = false;
                model.MarkAsNoLongerNeeded();
            }
        }

        private void SetVehicleModLevel(VehicleModType modType, string label, int level)
        {
            if (!TryGetCurrentVehicle(out Vehicle veh))
            {
                Notify("~r~Enter a vehicle first.~s~");
                SyncCustomsMenu();
                return;
            }

            veh.Mods.InstallModKit();
            VehicleMod mod = veh.Mods[modType];
            if (mod.Count <= 0)
            {
                Notify($"~y~This vehicle has no {label} upgrades.~s~");
                SyncCustomsMenu();
                return;
            }

            // Level 0 = Stock (-1 index in GTA V native mod indexing)
            int clampedIndex = Math.Max(-1, Math.Min(level - 1, mod.Count - 1));
            mod.Index = clampedIndex;
            _vehiclePhysics.NotifyVehicleRepairedOrUpgraded();

            int appliedLevel = clampedIndex + 1;
            if (appliedLevel != level)
            {
                Notify($"~y~{label} capped at tier {appliedLevel} for this vehicle.~s~");
                SyncCustomsMenu();
            }
        }

        private static void InstallMaxPerformanceUpgradesOnVehicle(Vehicle veh)
        {
            if (veh == null || !veh.Exists()) return;

            veh.Mods.InstallModKit();
            VehicleModType[] perfMods =
            {
                VehicleModType.Engine,
                VehicleModType.Brakes,
                VehicleModType.Transmission,
                VehicleModType.Suspension,
                VehicleModType.Armor
            };

            foreach (VehicleModType modType in perfMods)
            {
                VehicleMod mod = veh.Mods[modType];
                if (mod.Count > 0)
                {
                    mod.Index = mod.Count - 1;
                }
            }

            veh.Mods[VehicleToggleModType.Turbo].IsInstalled = true;
            veh.Mods[VehicleToggleModType.XenonHeadlights].IsInstalled = true;
        }

        private void ApplyMaxUpgrades()
        {
            if (!TryGetCurrentVehicle(out Vehicle veh))
            {
                Notify("~r~Enter a vehicle first.~s~");
                return;
            }

            veh.Repair();
            veh.DirtLevel = 0f;
            InstallMaxPerformanceUpgradesOnVehicle(veh);
            _vehiclePhysics.NotifyVehicleRepairedOrUpgraded();

            SyncCustomsMenu();
            Notify("~g~Vehicle repaired, cleaned, and maxed out.~s~");
        }
    }
}
