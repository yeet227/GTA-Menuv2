using System;
using System.Collections.Generic;
using GTA;
using GTA.Input;
using GTA.Math;
using GTA.Native;

namespace StoryModeTrainerMod
{
    /// <summary>
    /// Manages player weapons, infinite ammo/clip, infinite sprint, special ability, run/swim/jump multipliers,
    /// and safe in-game Story Mode character switching with weapon/vehicle state preservation.
    /// </summary>
    internal sealed class PlayerEnhancementsController
    {
        public sealed class CharacterPreset
        {
            public string Label { get; }
            public PedHash Hash { get; }
            public bool IsFreemodeModel { get; }

            public CharacterPreset(string label, PedHash hash, bool isFreemodeModel = false)
            {
                Label = label;
                Hash = hash;
                IsFreemodeModel = isFreemodeModel;
            }
        }

        private sealed class WeaponSnapshot
        {
            public WeaponHash Hash { get; }
            public int Ammo { get; }
            public WeaponTint Tint { get; }
            public List<WeaponComponentHash> Components { get; }

            public WeaponSnapshot(WeaponHash hash, int ammo, WeaponTint tint, List<WeaponComponentHash> components)
            {
                Hash = hash;
                Ammo = ammo;
                Tint = tint;
                Components = components;
            }
        }

        public static readonly string[] RunSpeedLabels =
        {
            "1.0x (Normal)",
            "1.25x (Quick)",
            "1.49x (Max Native)",
            "2.0x (Fast)",
            "3.0x (Super)",
            "5.0x (Ultra)"
        };

        private static readonly float[] RunSpeedMultipliers =
        {
            1.0f,
            1.25f,
            1.49f,
            2.0f,
            3.0f,
            5.0f
        };

        public static readonly string[] SwimSpeedLabels =
        {
            "1.0x (Normal)",
            "1.25x (Quick)",
            "1.49x (Max Native)",
            "2.5x (Fast)",
            "4.0x (Torpedo)"
        };

        private static readonly float[] SwimSpeedMultipliers =
        {
            1.0f,
            1.25f,
            1.49f,
            2.5f,
            4.0f
        };

        public static readonly string[] JumpModeLabels =
        {
            "1.0x (Normal)",
            "1.5x (Hop)",
            "2.0x (High)",
            "3.0x (Mega)",
            "5.0x (Sky)",
            "Super Jump (Native)",
            "Beast Jump (Native)"
        };

        private static readonly float[] JumpBoostFactors =
        {
            1.0f,
            1.5f,
            2.0f,
            3.0f,
            5.0f,
            1.0f,
            1.0f
        };

        public static readonly CharacterPreset[] CharacterPresets =
        {
            new CharacterPreset("Michael (Story)", PedHash.Michael),
            new CharacterPreset("Franklin (Story)", PedHash.Franklin),
            new CharacterPreset("Trevor (Story)", PedHash.Trevor),
            new CharacterPreset("Freemode Male (Online Asset)", PedHash.FreemodeMale01, true),
            new CharacterPreset("Freemode Female (Online Asset)", PedHash.FreemodeFemale01, true),
            new CharacterPreset("Lamar Davis", PedHash.LamarDavis),
            new CharacterPreset("Lester Crest", PedHash.LesterCrest),
            new CharacterPreset("LSPD Officer", PedHash.Cop01SMY),
            new CharacterPreset("NOOSE SWAT", PedHash.Swat01SMY),
            new CharacterPreset("US Marine", PedHash.Marine01SMY),
            new CharacterPreset("FIB Security", PedHash.FibSec01),
            new CharacterPreset("Highway Patrol", PedHash.Hwaycop01SMY),
            new CharacterPreset("Alien", PedHash.MovAlien01),
            new CharacterPreset("Clown", PedHash.Clown01SMY),
            new CharacterPreset("Zombie", PedHash.Zombie01)
        };

        private static readonly WeaponHash[] OnlineDlcArsenal =
        {
            WeaponHash.Railgun,
            WeaponHash.RailgunXmas3,
            WeaponHash.UpNAtomizer,
            WeaponHash.UnholyHellbringer,
            WeaponHash.Widowmaker,
            WeaponHash.HeavyRifle,
            WeaponHash.ServiceCarbine,
            WeaponHash.TacticalSMG,
            WeaponHash.BattleRifle,
            WeaponHash.PrecisionRifle,
            WeaponHash.CompactEMPLauncher,
            WeaponHash.PericoPistol,
            WeaponHash.CombatShotgun,
            WeaponHash.MilitaryRifle,
            WeaponHash.NavyRevolver,
            WeaponHash.DoubleActionRevolver,
            WeaponHash.StoneHatchet,
            WeaponHash.AssaultrifleMk2,
            WeaponHash.CarbineRifleMk2,
            WeaponHash.SpecialCarbineMk2,
            WeaponHash.BullpupRifleMk2,
            WeaponHash.CombatMGMk2,
            WeaponHash.HeavySniperMk2,
            WeaponHash.MarksmanRifleMk2,
            WeaponHash.PumpShotgunMk2,
            WeaponHash.SMGMk2,
            WeaponHash.PistolMk2,
            WeaponHash.SNSPistolMk2,
            WeaponHash.RevolverMk2,
            WeaponHash.StunGunMultiplayer,
            WeaponHash.CandyCane,
            WeaponHash.SnowballLauncher,
            WeaponHash.WM29Pistol
        };

        private static readonly HashSet<WeaponHash> ExcludedGrantWeapons = new HashSet<WeaponHash>
        {
            WeaponHash.Unarmed,
            WeaponHash.NightVision,
            WeaponHash.HackingDevice,
            WeaponHash.MetalDetector
        };

        private const int JumpFallShieldMaxMs = 12000;
        private const int JumpFallShieldMinAirMs = 220;
        private const float NativeSpeedCap = 1.49f;

        // GTA V Legacy 1.0.3889.0 Story Mode protagonist & selector.c wheel globals
        private const int GlobalCurrentProtagonistIndex = 114963 + 2370 + 539 + 4321; // 122193 (0=Michael, 1=Franklin, 2=Trevor)
        private const int GlobalLastProtagonistIndex = 114963 + 2370 + 539 + 4323;    // 122195
        private const int GlobalSelectorWheelDirtyFlag = 24430 + 3;                   // 24433
        private const int GlobalSelectorActiveProtagonist = 24430 + 9;                // 24439
        private const int GlobalSelectorSelectedQuadrant = 24430 + 69;                // 24499 (0=Up/Franklin, 1=Right/Trevor, 2=Down/MP, 3=Left/Michael)
        private const int GlobalSelectorScaleformHandle = 24430 + 73;                 // 24503
        private const int GlobalSelectorWheelVisible = 24430 + 124;                   // 24554
        private const int GlobalSwitchStructState = 99939 + 44;                       // 99983
        private const int GlobalSwitchInProgressFlag = 16;

        private readonly Func<bool> _shouldAbortWait;
        private readonly Action<int> _waitWithGuard;
        private readonly HashSet<WeaponHash> _infiniteAmmoAppliedWeapons = new HashSet<WeaponHash>();

        private int _runSpeedIndex;
        private int _swimSpeedIndex;
        private int _jumpModeIndex;
        private int _characterPresetIndex;
        private int _lastAppliedPedHandle;
        private int _originalProtagonistModelHash;
        private int _originalProtagonistIndex = -1;
        private int _jumpShieldStartGameTime;
        private int _wheelHoldStartGameTime;
        private int _postWheelSwapSuppressUntilGameTime;
        private int _wheelSelectedProtagonist = -1;
        private int _wheelSelectedQuadrant = -1;
        private int _fallbackWheelScaleformHandle;
        private bool _wasCharacterWheelHeld;
        private bool _appliedInfiniteClip;
        private bool _appliedNativeRunMult;
        private bool _appliedNativeSwimMult;
        private bool _wasJumping;
        private bool _jumpFallShieldActive;
        private bool _superSprintActive;
        private bool _isSwitchingModel;
        private bool _trainerAppliedCustomModel;

        public PlayerEnhancementsController(
            bool infiniteAmmoByDefault,
            bool infiniteClipByDefault,
            bool infiniteSprintByDefault,
            Func<bool> shouldAbortWait,
            Action<int> waitWithGuard = null)
        {
            InfiniteAmmoEnabled = infiniteAmmoByDefault;
            InfiniteClipEnabled = infiniteClipByDefault;
            InfiniteSprintEnabled = infiniteSprintByDefault;
            _shouldAbortWait = shouldAbortWait ?? (() => false);
            _waitWithGuard = waitWithGuard ?? Script.Wait;
        }

        public bool InfiniteAmmoEnabled { get; set; }
        public bool InfiniteClipEnabled { get; set; }
        public bool InfiniteSprintEnabled { get; set; }
        public bool InfiniteSpecialAbilityEnabled { get; set; }
        public bool CharacterWheelSwapEnabled { get; set; } = true;
        public bool HasOriginalProtagonistCaptured => _originalProtagonistModelHash != 0;
        public int OriginalProtagonistIndex => _originalProtagonistIndex;
        public bool IsJumpOrMovementShieldActive => _jumpFallShieldActive;
        public bool IsSuperSprintActive => _superSprintActive;

        public bool IsModelOverriddenByTrainer
        {
            get
            {
                if (!_trainerAppliedCustomModel || _originalProtagonistModelHash == 0) return false;
                Ped player = Game.Player?.Character;
                return player != null && player.Exists() && player.Model.Hash != _originalProtagonistModelHash;
            }
        }

        public int RunSpeedIndex
        {
            get { return _runSpeedIndex; }
            set { _runSpeedIndex = ClampIndex(value, RunSpeedMultipliers.Length); }
        }

        public int SwimSpeedIndex
        {
            get { return _swimSpeedIndex; }
            set { _swimSpeedIndex = ClampIndex(value, SwimSpeedMultipliers.Length); }
        }

        public int JumpModeIndex
        {
            get { return _jumpModeIndex; }
            set { _jumpModeIndex = ClampIndex(value, JumpModeLabels.Length); }
        }

        public int CharacterPresetIndex
        {
            get { return _characterPresetIndex; }
            set { _characterPresetIndex = ClampIndex(value, CharacterPresets.Length); }
        }

        public static string[] GetCharacterLabels()
        {
            string[] labels = new string[CharacterPresets.Length];
            for (int i = 0; i < CharacterPresets.Length; i++)
            {
                labels[i] = CharacterPresets[i].Label;
            }
            return labels;
        }

        /// <summary>
        /// Refills the active Single-Player protagonist's Special Ability meter immediately.
        /// </summary>
        public static void RefillSpecialAbility()
        {
            Player gamePlayer = Game.Player;
            if (gamePlayer == null) return;
            Function.Call(Hash.SPECIAL_ABILITY_FILL_METER, gamePlayer.Handle, true);
        }

        /// <summary>
        /// Clears blood, visible wound decals, dirt, wetness, and fire from the player ped.
        /// </summary>
        public static void CleanPlayerPed(Ped player)
        {
            if (player == null || !player.Exists()) return;
            player.ClearBloodDamage();
            player.ClearLastWeaponDamage();
            Function.Call(Hash.RESET_PED_VISIBLE_DAMAGE, player.Handle);
            Function.Call(Hash.CLEAR_PED_WETNESS, player.Handle);
            Function.Call(Hash.CLEAR_PED_ENV_DIRT, player.Handle);
            if (player.IsOnFire)
            {
                Function.Call(Hash.STOP_ENTITY_FIRE, player.Handle);
            }
        }

        /// <summary>
        /// Grants every valid base Story Mode and Online DLC weapon with maximum reserve and clip ammo.
        /// </summary>
        public int GiveAllWeaponsAndMaxAmmo(Ped player)
        {
            if (_shouldAbortWait() || player == null || !player.Exists()) return 0;

            HashSet<WeaponHash> candidates = new HashSet<WeaponHash>();
            try
            {
                WeaponHash[] engineWeapons = Weapon.GetAllWeaponHashesForHumanPeds();
                if (engineWeapons != null)
                {
                    for (int i = 0; i < engineWeapons.Length; i++)
                    {
                        candidates.Add(engineWeapons[i]);
                    }
                }
            }
            catch
            {
                // Fall back safely to managed enum table if engine query fails.
            }

            Array enumValues = Enum.GetValues(typeof(WeaponHash));
            for (int i = 0; i < enumValues.Length; i++)
            {
                candidates.Add((WeaponHash)enumValues.GetValue(i));
            }

            int granted = 0;
            foreach (WeaponHash hash in candidates)
            {
                if (ExcludedGrantWeapons.Contains(hash)) continue;
                if (TryGrantAndFillWeapon(player, hash))
                {
                    granted++;
                }
            }

            if (Game.Player != null)
            {
                Function.Call(Hash.SET_PLAYER_HAS_RESERVE_PARACHUTE, Game.Player.Handle);
            }

            return granted;
        }

        /// <summary>
        /// Grants the curated Online DLC, Alien, and Mk II weapon arsenal with full ammo.
        /// </summary>
        public int GiveOnlineDlcWeapons(Ped player)
        {
            if (_shouldAbortWait() || player == null || !player.Exists()) return 0;

            int granted = 0;
            for (int i = 0; i < OnlineDlcArsenal.Length; i++)
            {
                if (TryGrantAndFillWeapon(player, OnlineDlcArsenal[i]))
                {
                    granted++;
                }
            }

            return granted;
        }

        /// <summary>
        /// Refills reserve ammo and magazine clip across all weapons currently owned by the ped.
        /// </summary>
        public int RefillAllOwnedWeaponAmmo(Ped player)
        {
            if (player == null || !player.Exists()) return 0;

            int refilled = 0;
            HashSet<WeaponHash> ownedHashes = GetOwnedWeaponHashes(player);
            foreach (WeaponHash hash in ownedHashes)
            {
                Weapon w = player.Weapons[hash];
                if (w != null && w.IsPresent)
                {
                    FillWeaponAmmoToMax(w);
                    refilled++;
                }
            }

            Weapon current = player.Weapons.Current;
            if (current != null && current.IsPresent && current.Hash != WeaponHash.Unarmed)
            {
                FillWeaponAmmoToMax(current);
                Function.Call(Hash.REFILL_AMMO_INSTANTLY, player.Handle);
            }

            return refilled;
        }

        /// <summary>
        /// Equips maximum clip/scope/barrel/suppressor/flashlight/grip components onto current or all owned weapons.
        /// </summary>
        public int MaxWeaponAttachments(Ped player, bool currentWeaponOnly)
        {
            if (player == null || !player.Exists()) return 0;

            if (currentWeaponOnly)
            {
                Weapon current = player.Weapons.Current;
                if (current == null || !current.IsPresent || current.Hash == WeaponHash.Unarmed) return 0;
                return ApplyMaxComponents(current) > 0 ? 1 : 0;
            }

            int modifiedWeapons = 0;
            HashSet<WeaponHash> ownedHashes = GetOwnedWeaponHashes(player);
            foreach (WeaponHash hash in ownedHashes)
            {
                Weapon w = player.Weapons[hash];
                if (w != null && w.IsPresent && ApplyMaxComponents(w) > 0)
                {
                    modifiedWeapons++;
                }
            }

            return modifiedWeapons;
        }

        public void RemoveAllWeapons(Ped player)
        {
            if (player == null || !player.Exists()) return;

            ClearInfiniteAmmoFromPed(player);
            player.Weapons.RemoveAll();
        }

        /// <summary>
        /// Runs per frame to apply stamina, special ability, infinite ammo/clip, run/swim speed multipliers, and jump boosts.
        /// Automatically re-synchronizes whenever the player switches Story Mode characters.
        /// </summary>
        public void Tick(Ped player, bool noClipActive, bool userGodMode, bool heistShielded)
        {
            if (_shouldAbortWait() || player == null || !player.Exists())
            {
                _jumpFallShieldActive = false;
                _superSprintActive = false;
                _wasJumping = false;
                return;
            }

            bool handleChanged = player.Handle != _lastAppliedPedHandle;
            CaptureOrUpdateProtagonist(player, handleChanged);

            if (handleChanged)
            {
                _infiniteAmmoAppliedWeapons.Clear();
                _appliedInfiniteClip = false;
                _wasJumping = false;
                _jumpFallShieldActive = false;
                _superSprintActive = false;
                _lastAppliedPedHandle = player.Handle;
            }

            if (_postWheelSwapSuppressUntilGameTime != 0)
            {
                int remainingSuppress = _postWheelSwapSuppressUntilGameTime - Game.GameTime;
                if (remainingSuppress > 0 && remainingSuppress <= 2000)
                {
                    CancelNativeSkySwoopIfActive();
                }
                else
                {
                    _postWheelSwapSuppressUntilGameTime = 0;
                }
            }

            TickAmmoAndClip(player);
            TickSprintAndMovement(player, noClipActive);
            TickJumpAndFallShield(player, noClipActive, userGodMode, heistShielded);
        }

        /// <summary>
        /// Safely swaps the player character model while preserving weapons, ammo, components, armor, and vehicle seat/motion.
        /// </summary>
        public bool SwitchToCharacterModel(Model model, string displayLabel, bool isFreemodeModel, out string message, bool suppressNativeWheelSwoop = false)
        {
            if (_isSwitchingModel)
            {
                message = "Character switch already in progress.";
                return false;
            }

            Player gamePlayer = Game.Player;
            Ped initialPed = gamePlayer?.Character;
            if (gamePlayer == null || initialPed == null || !initialPed.Exists() || initialPed.IsDead || Game.IsCutsceneActive)
            {
                message = "Cannot switch character right now.";
                return false;
            }

            if (suppressNativeWheelSwoop)
            {
                CancelNativeSkySwoopIfActive();
            }
            else if (Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS))
            {
                message = "Wait for current Story Mode character switch to finish.";
                return false;
            }

            if (!model.IsInCdImage || !model.IsValid || !model.IsPed)
            {
                message = "Invalid ped model: " + displayLabel;
                return false;
            }

            CaptureOrUpdateProtagonist(initialPed, false);

            _isSwitchingModel = true;
            try
            {
                model.Request(1500);
                int deadline = Game.GameTime + 3000;
                while (!model.IsLoaded)
                {
                    int remaining = deadline - Game.GameTime;
                    if (remaining <= 0 || remaining > 10000) break;
                    if (suppressNativeWheelSwoop)
                    {
                        CancelNativeSkySwoopIfActive();
                    }
                    _waitWithGuard(10);
                    if (_shouldAbortWait())
                    {
                        message = "Character switch aborted.";
                        return false;
                    }
                }

                if (!model.IsLoaded)
                {
                    message = "Timed out loading ped model: " + displayLabel;
                    return false;
                }

                if (suppressNativeWheelSwoop)
                {
                    CancelNativeSkySwoopIfActive();
                }

                initialPed = Game.Player?.Character;
                if (initialPed == null || !initialPed.Exists() || initialPed.IsDead || _shouldAbortWait())
                {
                    message = "Character switch canceled.";
                    return false;
                }

                int savedArmor = initialPed.Armor;
                int savedHealth = initialPed.Health;
                int savedMaxHealth = initialPed.MaxHealth;
                int savedInteriorId = Function.Call<int>(Hash.GET_INTERIOR_FROM_ENTITY, initialPed.Handle);
                int savedRoomKey = savedInteriorId != 0 ? Function.Call<int>(Hash.GET_ROOM_KEY_FROM_ENTITY, initialPed.Handle) : 0;
                Vehicle savedVeh = initialPed.IsInVehicle() ? initialPed.CurrentVehicle : null;
                bool hadValidVeh = savedVeh != null && savedVeh.Exists();
                VehicleSeat savedSeat = hadValidVeh ? initialPed.SeatIndex : VehicleSeat.None;
                Vector3 savedVehVelocity = hadValidVeh ? savedVeh.Velocity : Vector3.Zero;
                bool savedVehEngineRunning = hadValidVeh && savedVeh.IsEngineRunning;
                WeaponHash savedActiveWeapon = initialPed.Weapons.Current?.Hash ?? WeaponHash.Unarmed;
                List<WeaponSnapshot> arsenal = CaptureWeaponsSnapshot(initialPed);

                bool changed = Game.Player.ChangeModel(model);
                if (!changed)
                {
                    if (hadValidVeh && savedVeh.Exists() && savedSeat != VehicleSeat.None && initialPed.Exists() && !initialPed.IsInVehicle(savedVeh))
                    {
                        initialPed.SetIntoVehicle(savedVeh, savedSeat);
                    }
                    message = "Engine rejected ChangeModel for: " + displayLabel;
                    return false;
                }

                Ped newPed = Game.Player?.Character;
                if (newPed != null && newPed.Exists())
                {
                    if (savedInteriorId != 0 && savedRoomKey != 0 && Function.Call<bool>(Hash.IS_VALID_INTERIOR, savedInteriorId))
                    {
                        Function.Call(Hash.FORCE_ROOM_FOR_ENTITY, newPed.Handle, savedInteriorId, savedRoomKey);
                        Function.Call(Hash.FORCE_ROOM_FOR_GAME_VIEWPORT, savedInteriorId, savedRoomKey);
                    }

                    InitializeSwitchedPedAppearance(newPed, model.Hash, isFreemodeModel);
                    RestoreWeaponsSnapshot(newPed, arsenal, savedActiveWeapon);

                    int targetMaxHealth = Math.Max(200, Math.Max(newPed.MaxHealth, savedMaxHealth));
                    if (newPed.MaxHealth < targetMaxHealth)
                    {
                        newPed.MaxHealth = targetMaxHealth;
                    }
                    newPed.Health = Math.Max(150, Math.Min(newPed.MaxHealth, savedHealth));
                    newPed.Armor = Math.Max(0, Math.Min(Game.Player.MaxArmor, savedArmor));

                    if (hadValidVeh && savedVeh.Exists() && savedSeat != VehicleSeat.None)
                    {
                        newPed.SetIntoVehicle(savedVeh, savedSeat);
                        if (savedSeat == VehicleSeat.Driver)
                        {
                            savedVeh.IsEngineRunning = savedVehEngineRunning;
                            savedVeh.Velocity = savedVehVelocity;
                        }
                    }

                    int targetProtagonistIdx = ResolveProtagonistIndexByModelHash(model.Hash);
                    if (targetProtagonistIdx >= 0)
                    {
                        _trainerAppliedCustomModel = false;
                        _originalProtagonistModelHash = model.Hash;
                        _originalProtagonistIndex = targetProtagonistIdx;
                        SyncStoryProtagonistGlobals(targetProtagonistIdx);
                        RefillSpecialAbility();
                    }
                    else
                    {
                        _trainerAppliedCustomModel = true;
                    }

                    _infiniteAmmoAppliedWeapons.Clear();
                    _appliedInfiniteClip = false;
                    _lastAppliedPedHandle = newPed.Handle;
                }

                message = "Switched character to " + displayLabel + ".";
                return true;
            }
            finally
            {
                model.MarkAsNoLongerNeeded();
                _isSwitchingModel = false;
            }
        }

        /// <summary>
        /// Swaps to Michael (0), Franklin (1), or Trevor (2) via the Character Wheel,
        /// canceling any unwanted native sky-swoop or Online transition and playing the authentic switch effect.
        /// </summary>
        public bool SwitchToProtagonistFromWheel(int protagonistIndex, Action beforeSwitchCallback, out string message)
        {
            message = null;
            if (protagonistIndex < 0 || protagonistIndex > 2)
            {
                return false;
            }

            CancelNativeSkySwoopIfActive();

            PedHash targetHash;
            string label;
            string postFx;
            switch (protagonistIndex)
            {
                case 0:
                    targetHash = PedHash.Michael;
                    label = "Michael (Story)";
                    postFx = "SwitchHUDMichaelOut";
                    break;
                case 1:
                    targetHash = PedHash.Franklin;
                    label = "Franklin (Story)";
                    postFx = "SwitchHUDFranklinOut";
                    break;
                default:
                    targetHash = PedHash.Trevor;
                    label = "Trevor (Story)";
                    postFx = "SwitchHUDTrevorOut";
                    break;
            }

            Ped current = Game.Player?.Character;
            if (current != null && current.Exists() && current.Model.Hash == unchecked((int)targetHash) && !_trainerAppliedCustomModel)
            {
                SyncStoryProtagonistGlobals(protagonistIndex);
                return false;
            }

            beforeSwitchCallback?.Invoke();

            bool ok = SwitchToCharacterModel(new Model(targetHash), label, false, out message, suppressNativeWheelSwoop: true);
            if (ok)
            {
                _postWheelSwapSuppressUntilGameTime = Game.GameTime + 600;
                CancelNativeSkySwoopIfActive();
                SyncStoryProtagonistGlobals(protagonistIndex);
                Function.Call(Hash.ANIMPOSTFX_PLAY, postFx, 0, false);
                Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "Hit_1", "LONG_PLAYER_SWITCH_SOUNDS", true);
            }
            return ok;
        }

        /// <summary>
        /// Processes the Single-Player Character Wheel (Alt / F5 / F6 / F7) each frame so the player can
        /// freely swap between Michael, Franklin, and Trevor at any point in Story Mode (including on DLC maps,
        /// early story saves, wanted levels, or custom skins) with zero risk of triggering GTA Online.
        /// </summary>
        public bool ProcessCharacterWheel(Ped player, Action beforeSwitchCallback, out string switchedMessage)
        {
            switchedMessage = null;
            if (!CharacterWheelSwapEnabled
                || _isSwitchingModel
                || player == null
                || !player.Exists()
                || player.IsDead
                || Game.IsCutsceneActive
                || Game.IsPaused)
            {
                _wasCharacterWheelHeld = false;
                if (!CharacterWheelSwapEnabled)
                {
                    ReleaseFallbackWheelScaleform();
                }
                return false;
            }

            bool wheelHeld = IsControlPressedAny(ControlAction.CharacterWheel);

            if (!wheelHeld && !_wasCharacterWheelHeld)
            {
                if (IsControlJustReleasedAny(ControlAction.SelectCharacterMichael))
                {
                    return SwitchToProtagonistFromWheel(0, beforeSwitchCallback, out switchedMessage);
                }
                if (IsControlJustReleasedAny(ControlAction.SelectCharacterFranklin))
                {
                    return SwitchToProtagonistFromWheel(1, beforeSwitchCallback, out switchedMessage);
                }
                if (IsControlJustReleasedAny(ControlAction.SelectCharacterTrevor))
                {
                    return SwitchToProtagonistFromWheel(2, beforeSwitchCallback, out switchedMessage);
                }
                return false;
            }

            if (wheelHeld && !_wasCharacterWheelHeld)
            {
                _wasCharacterWheelHeld = true;
                _wheelHoldStartGameTime = Game.GameTime;
                int currentIdx = ResolveProtagonistIndex(player);
                if (currentIdx < 0)
                {
                    currentIdx = _originalProtagonistIndex >= 0 ? _originalProtagonistIndex : 0;
                }
                _wheelSelectedProtagonist = currentIdx;
                _wheelSelectedQuadrant = ProtagonistToWheelQuadrant(currentIdx);
            }

            if (wheelHeld)
            {
                Controls.DisableControlActionThisFrame(ControlType.PlayerControl, ControlAction.LookLeftRight, true);
                Controls.DisableControlActionThisFrame(ControlType.PlayerControl, ControlAction.LookUpDown, true);
                Controls.DisableControlActionThisFrame(ControlType.PlayerControl, ControlAction.Attack, true);
                Controls.DisableControlActionThisFrame(ControlType.PlayerControl, ControlAction.Aim, true);

                if (IsControlPressedAny(ControlAction.SelectCharacterMichael))
                {
                    _wheelSelectedProtagonist = 0;
                    _wheelSelectedQuadrant = 3;
                }
                else if (IsControlPressedAny(ControlAction.SelectCharacterFranklin))
                {
                    _wheelSelectedProtagonist = 1;
                    _wheelSelectedQuadrant = 0;
                }
                else if (IsControlPressedAny(ControlAction.SelectCharacterTrevor))
                {
                    _wheelSelectedProtagonist = 2;
                    _wheelSelectedQuadrant = 1;
                }
                else
                {
                    float lr = GetMaxAxisInput(ControlAction.WeaponWheelLeftRight, ControlAction.LookLeftRight);
                    float ud = GetMaxAxisInput(ControlAction.WeaponWheelUpDown, ControlAction.LookUpDown);
                    if ((lr * lr) + (ud * ud) > 0.05f)
                    {
                        if (Math.Abs(lr) >= Math.Abs(ud))
                        {
                            if (lr < 0.0f)
                            {
                                _wheelSelectedProtagonist = 0; // Left = Michael
                                _wheelSelectedQuadrant = 3;
                            }
                            else
                            {
                                _wheelSelectedProtagonist = 2; // Right = Trevor
                                _wheelSelectedQuadrant = 1;
                            }
                        }
                        else if (ud < 0.0f)
                        {
                            _wheelSelectedProtagonist = 1; // Up = Franklin
                            _wheelSelectedQuadrant = 0;
                        }
                        // Down (ud > 0) is Quadrant 2 (Online) — intentionally ignored so Online can never be selected.
                    }
                }

                SyncAndRenderCharacterWheelOverlay(_wheelSelectedQuadrant);
                return false;
            }

            // Alt (CharacterWheel) was just released this frame.
            _wasCharacterWheelHeld = false;

            int heldDuration = Game.GameTime - _wheelHoldStartGameTime;
            int targetProtag = _wheelSelectedProtagonist;
            _wheelSelectedProtagonist = -1;
            _wheelSelectedQuadrant = -1;

            if (heldDuration >= 60 && targetProtag >= 0 && targetProtag <= 2)
            {
                return SwitchToProtagonistFromWheel(targetProtag, beforeSwitchCallback, out switchedMessage);
            }

            return false;
        }

        public bool RestoreOriginalProtagonist(out string message)
        {
            if (_originalProtagonistModelHash == 0)
            {
                message = "Already playing as original Story Mode protagonist.";
                return true;
            }

            Ped current = Game.Player?.Character;
            if (current != null && current.Exists() && current.Model.Hash == _originalProtagonistModelHash)
            {
                _trainerAppliedCustomModel = false;
                message = "Already playing as original Story Mode protagonist.";
                return true;
            }

            return SwitchToCharacterModel(
                new Model(_originalProtagonistModelHash),
                "Original Story Protagonist",
                false,
                out message
            );
        }

        public bool UnstickCharacterSwitch(out string message)
        {
            CancelNativeSkySwoopIfActive();

            if (Game.Player != null)
            {
                Game.Player.SetControlState(true, SetPlayerControlFlags.None);
            }

            if (IsModelOverriddenByTrainer)
            {
                return RestoreOriginalProtagonist(out message);
            }

            message = "Native Story Character Wheel & controls unlocked.";
            return true;
        }

        /// <summary>
        /// Immediately resets infinite ammo, movement multipliers, and jump fall shields on script unload.
        /// </summary>
        public void CleanupOnAbort(Ped player, bool userGodMode, bool otherShieldActive)
        {
            _runSpeedIndex = 0;
            _swimSpeedIndex = 0;
            _jumpModeIndex = 0;
            InfiniteAmmoEnabled = false;
            InfiniteClipEnabled = false;
            InfiniteSprintEnabled = false;
            InfiniteSpecialAbilityEnabled = false;
            _jumpFallShieldActive = false;
            _superSprintActive = false;
            _wasJumping = false;
            _wasCharacterWheelHeld = false;
            ReleaseFallbackWheelScaleform();

            if ((_appliedNativeRunMult || _appliedNativeSwimMult) && Game.Player != null)
            {
                Game.Player.RunSprintSpeedMultiplier = 1.0f;
                Game.Player.SwimSpeedMultiplier = 1.0f;
            }
            _appliedNativeRunMult = false;
            _appliedNativeSwimMult = false;

            if (player != null && player.Exists())
            {
                ClearInfiniteAmmoFromPed(player);
                Function.Call(Hash.SET_PED_INFINITE_AMMO_CLIP, player.Handle, false);
                if (!userGodMode && !otherShieldActive)
                {
                    player.CanRagdoll = true;
                    player.IsCollisionProof = false;
                    player.IsInvincible = false;
                }
            }

            _appliedInfiniteClip = false;
            _infiniteAmmoAppliedWeapons.Clear();
            _lastAppliedPedHandle = 0;
        }

        private void CaptureOrUpdateProtagonist(Ped player, bool handleChanged)
        {
            if (_isSwitchingModel) return;

            int idx = ResolveProtagonistIndex(player);
            if (idx >= 0)
            {
                _trainerAppliedCustomModel = false;
                if (_originalProtagonistModelHash == 0 || handleChanged || _originalProtagonistModelHash != player.Model.Hash)
                {
                    _originalProtagonistModelHash = player.Model.Hash;
                    _originalProtagonistIndex = idx;
                }
            }
        }

        public static int ResolveProtagonistIndex(Ped player)
        {
            if (player == null || !player.Exists()) return -1;

            int byHash = ResolveProtagonistIndexByModelHash(player.Model.Hash);
            if (byHash >= 0) return byHash;

            switch (player.PedType)
            {
                case PedType.Player0: return 0;
                case PedType.Player1: return 1;
                case PedType.Player2: return 2;
                default: return -1;
            }
        }

        private static int ResolveProtagonistIndexByModelHash(int modelHash)
        {
            if (modelHash == unchecked((int)PedHash.Michael)) return 0;
            if (modelHash == unchecked((int)PedHash.Franklin)) return 1;
            if (modelHash == unchecked((int)PedHash.Trevor)) return 2;
            return -1;
        }

        public static int ProtagonistToWheelQuadrant(int protagonistIndex)
        {
            switch (protagonistIndex)
            {
                case 0: return 3; // Left = Michael
                case 1: return 0; // Up = Franklin
                case 2: return 1; // Right = Trevor
                default: return 3;
            }
        }

        private void SyncStoryProtagonistGlobals(int protagonistIndex)
        {
            if (protagonistIndex < 0 || protagonistIndex > 2) return;
            if (!StoryModeTrainer.IsSupportedScriptGlobalBuild()) return;

            try
            {
                GlobalVariable.Get(GlobalCurrentProtagonistIndex).Write(protagonistIndex);
                GlobalVariable.Get(GlobalLastProtagonistIndex).Write(protagonistIndex);
                GlobalVariable.Get(GlobalSelectorActiveProtagonist).Write(protagonistIndex);
                GlobalVariable.Get(GlobalSelectorSelectedQuadrant).Write(ProtagonistToWheelQuadrant(protagonistIndex));
                GlobalVariable.Get(GlobalSelectorWheelDirtyFlag).Write(1);
            }
            catch
            {
                // Ignore if script global table is unavailable.
            }
        }

        private static void CancelNativeSkySwoopIfActive()
        {
            if (Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS))
            {
                Function.Call(Hash.STOP_PLAYER_SWITCH);
            }

            if (!StoryModeTrainer.IsSupportedScriptGlobalBuild())
            {
                return;
            }

            try
            {
                if (GlobalVariable.Get(GlobalSwitchStructState).Read<int>() != 0)
                {
                    GlobalVariable.Get(GlobalSwitchStructState).Write(0);
                }
                if (GlobalVariable.Get(GlobalSwitchInProgressFlag).Read<int>() != 0)
                {
                    GlobalVariable.Get(GlobalSwitchInProgressFlag).Write(0);
                }
            }
            catch
            {
                // Ignore if script global table is unavailable.
            }
        }

        private void SyncAndRenderCharacterWheelOverlay(int selectedQuadrant)
        {
            if (selectedQuadrant < 0 || selectedQuadrant == 2 || selectedQuadrant > 3)
            {
                selectedQuadrant = 3;
            }

            bool nativeWheelVisible = false;
            int scaleformHandle = 0;

            if (StoryModeTrainer.IsSupportedScriptGlobalBuild())
            {
                try
                {
                    int validProtag = _originalProtagonistIndex >= 0 && _originalProtagonistIndex <= 2 ? _originalProtagonistIndex : 0;
                    if (GlobalVariable.Get(GlobalCurrentProtagonistIndex).Read<int>() > 2)
                    {
                        GlobalVariable.Get(GlobalCurrentProtagonistIndex).Write(validProtag);
                    }

                    GlobalVariable.Get(GlobalSelectorSelectedQuadrant).Write(selectedQuadrant);
                    GlobalVariable.Get(GlobalSelectorWheelDirtyFlag).Write(1);
                    nativeWheelVisible = GlobalVariable.Get(GlobalSelectorWheelVisible).Read<int>() != 0;
                    scaleformHandle = GlobalVariable.Get(GlobalSelectorScaleformHandle).Read<int>();
                }
                catch
                {
                    nativeWheelVisible = false;
                }
            }

            if (scaleformHandle == 0 || !Function.Call<bool>(Hash.HAS_SCALEFORM_MOVIE_LOADED, scaleformHandle))
            {
                if (_fallbackWheelScaleformHandle == 0)
                {
                    _fallbackWheelScaleformHandle = Function.Call<int>(Hash.REQUEST_SCALEFORM_MOVIE, "PLAYER_SWITCH");
                }
                if (_fallbackWheelScaleformHandle != 0 && Function.Call<bool>(Hash.HAS_SCALEFORM_MOVIE_LOADED, _fallbackWheelScaleformHandle))
                {
                    scaleformHandle = _fallbackWheelScaleformHandle;
                }
            }

            if (scaleformHandle != 0 && Function.Call<bool>(Hash.HAS_SCALEFORM_MOVIE_LOADED, scaleformHandle))
            {
                // Populate all 3 Single-Player slots as unlocked (state=1) and lock Slot 2 (Online, state=2).
                // Head indices in PLAYER_SWITCH scaleform: 0=Michael, 1=Trevor, 2=Franklin, 3=MP.
                SetWheelSwitchSlot(scaleformHandle, 0, 1, 2, selectedQuadrant == 0); // Top: Franklin
                SetWheelSwitchSlot(scaleformHandle, 1, 1, 1, selectedQuadrant == 1); // Right: Trevor
                SetWheelSwitchSlot(scaleformHandle, 2, 2, 3, false);                 // Bottom: Online (Locked)
                SetWheelSwitchSlot(scaleformHandle, 3, 1, 0, selectedQuadrant == 3); // Left: Michael

                Function.Call(Hash.BEGIN_SCALEFORM_MOVIE_METHOD, scaleformHandle, "SET_MP_LABEL");
                Function.Call(Hash.SCALEFORM_MOVIE_METHOD_ADD_PARAM_PLAYER_NAME_STRING, "");
                Function.Call(Hash.END_SCALEFORM_MOVIE_METHOD);

                Function.Call(Hash.BEGIN_SCALEFORM_MOVIE_METHOD, scaleformHandle, "SET_PLAYER_SELECTED");
                Function.Call(Hash.SCALEFORM_MOVIE_METHOD_ADD_PARAM_INT, selectedQuadrant);
                Function.Call(Hash.END_SCALEFORM_MOVIE_METHOD);

                if (!nativeWheelVisible && (Game.GameTime - _wheelHoldStartGameTime) >= 140)
                {
                    Function.Call(Hash.BEGIN_SCALEFORM_MOVIE_METHOD, scaleformHandle, "SET_SWITCH_VISIBLE");
                    Function.Call(Hash.SCALEFORM_MOVIE_METHOD_ADD_PARAM_BOOL, true);
                    Function.Call(Hash.END_SCALEFORM_MOVIE_METHOD);

                    Function.Call(
                        Hash.DRAW_SCALEFORM_MOVIE,
                        scaleformHandle,
                        0.906f,
                        0.838f,
                        0.14f,
                        0.248f,
                        255,
                        255,
                        255,
                        255,
                        0
                    );
                }
            }
        }

        private static void SetWheelSwitchSlot(int scaleformHandle, int quadrantIndex, int stateEnum, int charHeadIndex, bool selected)
        {
            Function.Call(Hash.BEGIN_SCALEFORM_MOVIE_METHOD, scaleformHandle, "SET_SWITCH_SLOT");
            Function.Call(Hash.SCALEFORM_MOVIE_METHOD_ADD_PARAM_INT, quadrantIndex);
            Function.Call(Hash.SCALEFORM_MOVIE_METHOD_ADD_PARAM_INT, stateEnum);
            Function.Call(Hash.SCALEFORM_MOVIE_METHOD_ADD_PARAM_INT, charHeadIndex);
            Function.Call(Hash.SCALEFORM_MOVIE_METHOD_ADD_PARAM_BOOL, selected);
            Function.Call(Hash.END_SCALEFORM_MOVIE_METHOD);
        }

        private void ReleaseFallbackWheelScaleform()
        {
            if (_fallbackWheelScaleformHandle != 0)
            {
                OutputArgument arg = new OutputArgument(_fallbackWheelScaleformHandle);
                Function.Call(Hash.SET_SCALEFORM_MOVIE_AS_NO_LONGER_NEEDED, arg);
                _fallbackWheelScaleformHandle = 0;
            }
        }

        private static bool IsControlPressedAny(ControlAction action)
        {
            return Controls.IsControlPressed(ControlType.PlayerControl, action)
                || Controls.IsDisabledControlPressed(ControlType.PlayerControl, action)
                || Controls.IsControlPressed(ControlType.FrontendControl, action)
                || Controls.IsDisabledControlPressed(ControlType.FrontendControl, action);
        }

        private static bool IsControlJustReleasedAny(ControlAction action)
        {
            return Controls.IsControlJustReleased(ControlType.PlayerControl, action)
                || Controls.IsDisabledControlJustReleased(ControlType.PlayerControl, action)
                || Controls.IsControlJustReleased(ControlType.FrontendControl, action)
                || Controls.IsDisabledControlJustReleased(ControlType.FrontendControl, action);
        }

        private static float GetMaxAxisInput(ControlAction primary, ControlAction secondary)
        {
            float a = Controls.GetDisabledControlNormal(ControlType.PlayerControl, primary);
            float b = Controls.GetControlNormal(ControlType.PlayerControl, primary);
            float c = Controls.GetDisabledControlNormal(ControlType.PlayerControl, secondary);
            float d = Controls.GetControlNormal(ControlType.PlayerControl, secondary);

            float best = Math.Abs(a) >= Math.Abs(b) ? a : b;
            float alt = Math.Abs(c) >= Math.Abs(d) ? c : d;
            return Math.Abs(best) >= Math.Abs(alt) ? best : alt;
        }

        private void TickAmmoAndClip(Ped player)
        {
            if (InfiniteAmmoEnabled)
            {
                Weapon current = player.Weapons.Current;
                if (current != null && current.IsPresent && current.Hash != WeaponHash.Unarmed)
                {
                    if (!_infiniteAmmoAppliedWeapons.Contains(current.Hash))
                    {
                        Function.Call(Hash.SET_PED_INFINITE_AMMO, player.Handle, true, (uint)current.Hash);
                        _infiniteAmmoAppliedWeapons.Add(current.Hash);
                    }
                    // Keep throwables and single-shot launchers above 1 round so the weapon never auto-unequips at 0.
                    if (current.MaxAmmo > 0 && current.Ammo < Math.Min(2, current.MaxAmmo))
                    {
                        current.Ammo = current.MaxAmmo;
                    }
                }
            }
            else if (_infiniteAmmoAppliedWeapons.Count > 0)
            {
                ClearInfiniteAmmoFromPed(player);
            }

            if (InfiniteClipEnabled)
            {
                if (!_appliedInfiniteClip)
                {
                    Function.Call(Hash.SET_PED_INFINITE_AMMO_CLIP, player.Handle, true);
                    _appliedInfiniteClip = true;
                }
            }
            else if (_appliedInfiniteClip)
            {
                Function.Call(Hash.SET_PED_INFINITE_AMMO_CLIP, player.Handle, false);
                _appliedInfiniteClip = false;
            }
        }

        private void TickSprintAndMovement(Ped player, bool noClipActive)
        {
            Player gamePlayer = Game.Player;
            if (gamePlayer == null) return;

            if (InfiniteSprintEnabled)
            {
                Function.Call(Hash.RESET_PLAYER_STAMINA, gamePlayer.Handle);
            }

            if (InfiniteSpecialAbilityEnabled)
            {
                Function.Call(Hash.SPECIAL_ABILITY_FILL_METER, gamePlayer.Handle, true);
            }

            float runMult = RunSpeedMultipliers[_runSpeedIndex];
            bool superSprintNow = false;
            if (runMult > 1.001f)
            {
                gamePlayer.RunSprintSpeedMultiplier = Math.Min(NativeSpeedCap, runMult);
                _appliedNativeRunMult = true;

                if (runMult > NativeSpeedCap
                    && !noClipActive
                    && !player.IsInVehicle()
                    && !player.IsRagdoll
                    && !player.IsClimbing
                    && !player.IsFalling
                    && !player.IsJumping
                    && !player.IsInWater
                    && !player.IsAiming
                    && !player.IsAimingFromCover
                    && !player.IsShooting
                    && !player.HasCollidedWithBuildingOrAnimatedBuilding
                    && !Game.IsCutsceneActive
                    && (player.IsRunning || player.IsSprinting))
                {
                    superSprintNow = true;
                    ApplyHorizontalMoveScale(player, 7.0f * runMult, 2.5f, hugGround: true);
                }
            }
            else if (_appliedNativeRunMult)
            {
                gamePlayer.RunSprintSpeedMultiplier = 1.0f;
                _appliedNativeRunMult = false;
            }
            _superSprintActive = superSprintNow;

            float swimMult = SwimSpeedMultipliers[_swimSpeedIndex];
            if (swimMult > 1.001f)
            {
                gamePlayer.SwimSpeedMultiplier = Math.Min(NativeSpeedCap, swimMult);
                _appliedNativeSwimMult = true;

                if (swimMult > NativeSpeedCap
                    && !noClipActive
                    && !player.IsInVehicle()
                    && !player.IsRagdoll
                    && player.IsInWater
                    && !player.IsAiming
                    && !player.IsShooting
                    && !Game.IsCutsceneActive
                    && (player.IsSprinting || player.IsRunning))
                {
                    ApplyHorizontalMoveScale(player, 4.5f * swimMult, 1.2f, hugGround: false);
                }
            }
            else if (_appliedNativeSwimMult)
            {
                gamePlayer.SwimSpeedMultiplier = 1.0f;
                _appliedNativeSwimMult = false;
            }
        }

        private void TickJumpAndFallShield(Ped player, bool noClipActive, bool userGodMode, bool heistShielded)
        {
            Player gamePlayer = Game.Player;
            if (gamePlayer == null) return;

            if (!noClipActive && !player.IsDead && !player.IsInVehicle())
            {
                if (_jumpModeIndex == 5)
                {
                    gamePlayer.SetSuperJumpThisFrame();
                }
                else if (_jumpModeIndex == 6)
                {
                    Function.Call(Hash.SET_BEAST_JUMP_THIS_FRAME, gamePlayer.Handle);
                }
            }

            bool isJumping = !noClipActive
                && !player.IsDead
                && !player.IsInVehicle()
                && !player.IsRagdoll
                && !player.IsClimbing
                && !player.IsInWater
                && (player.IsJumping || Function.Call<bool>(Hash.IS_PED_JUMPING, player.Handle));

            if (isJumping && !_wasJumping && _jumpModeIndex >= 1 && _jumpModeIndex <= 4)
            {
                float jumpMult = JumpBoostFactors[_jumpModeIndex];
                Vector3 vel = player.Velocity;
                float boostedZ = Math.Min(36.0f, Math.Max(vel.Z, 4.5f) * jumpMult);
                player.Velocity = new Vector3(vel.X, vel.Y, boostedZ);
                ArmJumpFallShield(player);
            }
            else if (isJumping && !_wasJumping && (_jumpModeIndex == 5 || _jumpModeIndex == 6))
            {
                ArmJumpFallShield(player);
            }
            else if (_runSpeedIndex >= 3
                && !noClipActive
                && !player.IsDead
                && !player.IsInVehicle()
                && !player.IsInWater
                && player.IsFalling
                && !_jumpFallShieldActive)
            {
                ArmJumpFallShield(player);
            }

            _wasJumping = isJumping;

            if (_jumpFallShieldActive)
            {
                int elapsed = Game.GameTime - _jumpShieldStartGameTime;
                bool stillAirborne = player.IsAlive
                    && !noClipActive
                    && !player.IsInVehicle()
                    && (player.IsJumping || player.IsFalling || player.IsInParachuteFreeFall);

                if (elapsed < 0 || (elapsed >= JumpFallShieldMinAirMs && (!stillAirborne || elapsed >= JumpFallShieldMaxMs)))
                {
                    _jumpFallShieldActive = false;
                    if (!noClipActive && player.Exists())
                    {
                        player.IsCollisionProof = userGodMode;
                        player.CanRagdoll = !userGodMode && !_superSprintActive;
                        if (!userGodMode && !heistShielded)
                        {
                            player.IsInvincible = false;
                        }
                    }
                }
                else if (player.Exists())
                {
                    player.IsInvincible = true;
                    player.IsCollisionProof = true;
                    player.CanRagdoll = false;
                }
            }
        }

        private void ArmJumpFallShield(Ped player)
        {
            _jumpFallShieldActive = true;
            _jumpShieldStartGameTime = Game.GameTime;
            player.IsInvincible = true;
            player.IsCollisionProof = true;
            player.CanRagdoll = false;
        }

        private static void ApplyHorizontalMoveScale(Ped player, float targetSpeed, float minActiveSpeed, bool hugGround)
        {
            Vector3 vel = player.Velocity;
            Vector3 horiz = new Vector3(vel.X, vel.Y, 0f);
            float currentSpeed = horiz.Length();
            if (currentSpeed < minActiveSpeed || currentSpeed >= targetSpeed)
            {
                return;
            }

            Vector3 velDir = horiz / currentSpeed;
            Vector3 fwd = player.ForwardVector;
            Vector3 fwdHoriz = new Vector3(fwd.X, fwd.Y, 0f);
            Vector3 steerDir = fwdHoriz.LengthSquared() > 0.01f
                ? Vector3.Normalize((velDir * 0.55f) + (Vector3.Normalize(fwdHoriz) * 0.45f))
                : velDir;

            float nextSpeed = Math.Min(targetSpeed, currentSpeed + ((targetSpeed - currentSpeed) * 0.35f));
            float nextZ = hugGround && vel.Z <= 0.15f && vel.Z >= -3.0f
                ? Math.Min(vel.Z, -0.6f)
                : vel.Z;
            player.Velocity = new Vector3(steerDir.X * nextSpeed, steerDir.Y * nextSpeed, nextZ);
        }

        private void ClearInfiniteAmmoFromPed(Ped player)
        {
            foreach (WeaponHash hash in _infiniteAmmoAppliedWeapons)
            {
                if (player.Weapons.HasWeapon(hash))
                {
                    Function.Call(Hash.SET_PED_INFINITE_AMMO, player.Handle, false, (uint)hash);
                }
            }
            _infiniteAmmoAppliedWeapons.Clear();
        }

        private bool TryGrantAndFillWeapon(Ped player, WeaponHash hash)
        {
            if (!player.Weapons.IsWeaponValid(hash))
            {
                return false;
            }

            Weapon weapon = player.Weapons.Give(hash, 9999, false, false);
            if (weapon == null || !weapon.IsPresent)
            {
                return false;
            }

            FillWeaponAmmoToMax(weapon);
            if (InfiniteAmmoEnabled && !_infiniteAmmoAppliedWeapons.Contains(hash))
            {
                Function.Call(Hash.SET_PED_INFINITE_AMMO, player.Handle, true, (uint)hash);
                _infiniteAmmoAppliedWeapons.Add(hash);
            }

            return true;
        }

        private static void FillWeaponAmmoToMax(Weapon weapon)
        {
            int maxAmmo = weapon.MaxAmmo;
            if (maxAmmo > 0)
            {
                weapon.Ammo = maxAmmo;
            }

            int maxClip = weapon.MaxAmmoInClip;
            if (maxClip > 0)
            {
                weapon.AmmoInClip = maxClip;
            }
        }

        private static bool TryActivateComponent(WeaponComponent comp)
        {
            if (comp == null || comp.ComponentHash == WeaponComponentHash.Invalid)
            {
                return false;
            }

            comp.Active = true;
            return true;
        }

        private static int ApplyMaxComponents(Weapon weapon)
        {
            WeaponComponentCollection comps = weapon.Components;
            if (comps == null || comps.Count <= 0)
            {
                return 0;
            }

            int activated = 0;

            if (comps.ClipVariationsCount > 0 && TryActivateComponent(comps.GetClipComponent(comps.ClipVariationsCount - 1)))
            {
                activated++;
            }

            if (comps.ScopeVariationsCount > 0 && TryActivateComponent(comps.GetScopeComponent(comps.ScopeVariationsCount - 1)))
            {
                activated++;
            }

            if (comps.BarrelVariationsCount > 0 && TryActivateComponent(comps.GetBarrelComponent(comps.BarrelVariationsCount - 1)))
            {
                activated++;
            }

            if (TryActivateComponent(comps.GetFlashLightComponent()))
            {
                activated++;
            }

            if (TryActivateComponent(comps.GetSuppressorComponent()))
            {
                activated++;
            }
            else if (comps.SuppressorAndMuzzleBrakeVariationsCount > 0
                && TryActivateComponent(comps.GetSuppressorOrMuzzleBrakeComponent(comps.SuppressorAndMuzzleBrakeVariationsCount - 1)))
            {
                activated++;
            }

            for (int i = 0; i < comps.Count; i++)
            {
                WeaponComponent comp = comps[i];
                if (comp != null && comp.ComponentHash != WeaponComponentHash.Invalid)
                {
                    WeaponAttachmentPoint pt = comp.AttachmentPoint;
                    if (pt == WeaponAttachmentPoint.Grip || pt == WeaponAttachmentPoint.Grip2)
                    {
                        comp.Active = true;
                        activated++;
                    }
                }
            }

            FillWeaponAmmoToMax(weapon);
            return activated;
        }

        private static HashSet<WeaponHash> GetOwnedWeaponHashes(Ped ped)
        {
            HashSet<WeaponHash> owned = new HashSet<WeaponHash>();
            try
            {
                WeaponHash[] directHashes = ped.Weapons.GetAllWeaponHashes();
                if (directHashes != null)
                {
                    for (int i = 0; i < directHashes.Length; i++)
                    {
                        if (directHashes[i] != WeaponHash.Unarmed)
                        {
                            owned.Add(directHashes[i]);
                        }
                    }
                }
            }
            catch
            {
                // Fall back to enum scan below if direct inventory read fails.
            }

            Array values = Enum.GetValues(typeof(WeaponHash));
            for (int i = 0; i < values.Length; i++)
            {
                WeaponHash hash = (WeaponHash)values.GetValue(i);
                if (hash != WeaponHash.Unarmed && ped.Weapons.HasWeapon(hash))
                {
                    owned.Add(hash);
                }
            }

            return owned;
        }

        private static List<WeaponSnapshot> CaptureWeaponsSnapshot(Ped ped)
        {
            List<WeaponSnapshot> list = new List<WeaponSnapshot>();
            HashSet<WeaponHash> ownedHashes = GetOwnedWeaponHashes(ped);
            foreach (WeaponHash hash in ownedHashes)
            {
                Weapon w = ped.Weapons[hash];
                if (w == null || !w.IsPresent) continue;

                List<WeaponComponentHash> activeComps = new List<WeaponComponentHash>();
                if (w.Components != null)
                {
                    for (int c = 0; c < w.Components.Count; c++)
                    {
                        WeaponComponent comp = w.Components[c];
                        if (comp != null && comp.ComponentHash != WeaponComponentHash.Invalid && comp.Active)
                        {
                            activeComps.Add(comp.ComponentHash);
                        }
                    }
                }

                list.Add(new WeaponSnapshot(hash, w.Ammo, w.Tint, activeComps));
            }
            return list;
        }

        private static void RestoreWeaponsSnapshot(Ped ped, List<WeaponSnapshot> snapshots, WeaponHash activeWeapon)
        {
            for (int i = 0; i < snapshots.Count; i++)
            {
                WeaponSnapshot snap = snapshots[i];
                if (!ped.Weapons.IsWeaponValid(snap.Hash)) continue;

                Weapon restored = ped.Weapons.Give(snap.Hash, Math.Max(0, snap.Ammo), false, false);
                if (restored == null || !restored.IsPresent) continue;

                for (int c = 0; c < snap.Components.Count; c++)
                {
                    WeaponComponent comp = restored.Components[snap.Components[c]];
                    if (comp != null && comp.ComponentHash != WeaponComponentHash.Invalid)
                    {
                        comp.Active = true;
                    }
                }

                if (restored.TintCount > 0)
                {
                    restored.Tint = snap.Tint;
                }

                if (restored.MaxAmmo > 0)
                {
                    restored.Ammo = Math.Min(restored.MaxAmmo, Math.Max(0, snap.Ammo));
                }

                if (restored.MaxAmmoInClip > 0 && restored.Ammo > 0)
                {
                    restored.AmmoInClip = Math.Min(restored.MaxAmmoInClip, restored.Ammo);
                }
            }

            if (activeWeapon != WeaponHash.Unarmed && ped.Weapons.HasWeapon(activeWeapon))
            {
                ped.Weapons.Select(activeWeapon, true);
            }
        }

        private static void InitializeSwitchedPedAppearance(Ped newPed, int modelHash, bool explicitFreemode)
        {
            bool isFreemode = explicitFreemode
                || modelHash == unchecked((int)PedHash.FreemodeMale01)
                || modelHash == unchecked((int)PedHash.FreemodeFemale01);

            if (isFreemode)
            {
                Function.Call(Hash.SET_PED_HEAD_BLEND_DATA, newPed.Handle, 0, 0, 0, 0, 0, 0, 0.5f, 0.5f, 0.0f, false);
                Function.Call(Hash.SET_PED_RANDOM_COMPONENT_VARIATION, newPed.Handle, 0);
            }
            else
            {
                Function.Call(Hash.SET_PED_DEFAULT_COMPONENT_VARIATION, newPed.Handle);
            }
        }

        private static int ClampIndex(int index, int length)
        {
            if (index < 0) return 0;
            if (index >= length) return length - 1;
            return index;
        }
    }
}
