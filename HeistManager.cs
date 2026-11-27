using System;
using System.Collections.Generic;
using System.Globalization;
using GTA;
using GTA.Math;
using GTA.Native;

namespace StoryModeTrainerMod
{
    /// <summary>
    /// Controls Story Mode heist money overrides, anti-police bag/damage loss protection, and direct cash payouts.
    /// Script global indices verified directly against GTA 5 Legacy build 1.0.3889.0 (1.73-3889.0) YSC scripts.
    /// </summary>
    internal sealed class HeistManager
    {
        private struct AllyProtectionBaseline
        {
            public bool WasInvincible { get; }
            public bool WasBulletProof { get; }
            public bool WasExplosionProof { get; }

            public AllyProtectionBaseline(Ped ped)
            {
                WasInvincible = ped.IsInvincible;
                WasBulletProof = ped.IsBulletProof;
                WasExplosionProof = ped.IsExplosionProof;
            }
        }

        public static readonly string[] HeistLabels =
        {
            "Auto (Active / Big Score)",
            "Jewel Store Job (0)",
            "The Merryweather Heist (1)",
            "The Paleto Score (2)",
            "The Bureau Raid (3)",
            "The Big Score (4)"
        };

        public static readonly int[] TakePresetAmounts =
        {
            2500000,
            5000000,
            10000000,
            25000000,
            50000000,
            100000000,
            200000000,
            500000000,
            1000000000
        };

        // Verified 1.73-3889.0 globals from mission_stat_watcher.c, code_controller.c, rural_bank_heist.c, jewelry_heist.c
        private const int GlobalSavedBase = 114963;
        private const int GlobalSavedHeistField1 = 1;
        private const int GlobalSavedHeistBoardArrayField = 126;
        private const int HeistBoardItemSize = 23;
        private const int GlobalCurrentProtagonistIndex = 114963 + 2370 + 539 + 4321; // 122193 (0=Michael, 1=Franklin, 2=Trevor)

        private const int GlobalHeistPenaltyArray = 98691;
        private const int HeistPenaltyItemSize = 38;
        private const int PenaltyTypesField = 25;
        private const int PenaltyAmountsField = 31;
        private const int PenaltyCountField = 37;

        private const int GlobalMissionPassPending = 65066;
        private const int GlobalMissionPassHeistId = 65067;
        private const int GlobalMissionWatcherState = 98681;
        private const int WatcherProcessedField = 3;
        private const int WatcherLesterPctField = 6;
        private const int WatcherLesterCutField = 7;

        private const int GlobalPaletoHudTake = 98887;
        private const int GlobalActiveHeistTake = 102486;
        private const int ActiveTakeArrayField = 12;

        private const int GlobalProtagonistCashBase = 62237;
        private const int MaxSafeTake = 1000000000;
        private const int PedSweepIntervalMs = 120;
        private const float PedSweepRadius = 115.0f;
        private const int DefaultPedShootRate = 100;

        private static readonly string[] HeistDisplayNames =
        {
            "Jewel Store Job",
            "Merryweather Heist",
            "The Paleto Score",
            "The Bureau Raid",
            "The Big Score"
        };

        private static readonly string[] ProtagonistCashStats =
        {
            "sp0_total_cash",
            "sp1_total_cash",
            "sp2_total_cash"
        };

        private readonly Dictionary<string, int> _scriptHashCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, AllyProtectionBaseline> _shieldedAllies = new Dictionary<int, AllyProtectionBaseline>();
        private readonly Dictionary<int, int> _suppressedHostileAccuracy = new Dictionary<int, int>();
        private readonly List<int> _pruneHandlesBuffer = new List<int>(32);

        private readonly Func<bool> _isMultiplayerBlocked;
        private int _selectedMenuIndex;
        private int _lastActiveHeistId = -1;
        private int _peakPaletoTake;
        private int _peakJewelTake;
        private int _nextPedSweepGameTime;
        private bool _appliedPoliceIgnoreFlag;
        private bool _appliedPlayerCombatProofs;
        private bool _hasCustomShares;
        private int _customSharesHeistId = -1;
        private readonly int[] _customShares = new int[3];

        public HeistManager(bool protectTakeByDefault, bool keepFullTakeWithoutCuts, Func<bool> isMultiplayerBlocked = null)
        {
            ProtectHeistTake = protectTakeByDefault;
            KeepFullTakeWithoutCuts = keepFullTakeWithoutCuts;
            _isMultiplayerBlocked = isMultiplayerBlocked ?? (() => false);
        }

        public bool ProtectHeistTake { get; set; }
        public bool PoliceIgnoreDuringHeists { get; set; }
        public bool KeepFullTakeWithoutCuts { get; set; }
        public bool LockTakeThroughCompletion { get; set; }
        public int LockedHeistId { get; private set; } = -1;
        public int LockedGrossTake { get; private set; }
        public bool IsHeistShieldedNow { get; private set; }

        public int SelectedMenuIndex
        {
            get { return _selectedMenuIndex; }
            set
            {
                if (value < 0) value = 0;
                if (value >= HeistLabels.Length) value = HeistLabels.Length - 1;
                _selectedMenuIndex = value;
            }
        }

        public static string FormatCash(int amount)
        {
            return "$" + amount.ToString("N0", CultureInfo.InvariantCulture);
        }

        public static string GetHeistName(int heistId)
        {
            if (heistId >= 0 && heistId < HeistDisplayNames.Length)
            {
                return HeistDisplayNames[heistId];
            }

            return "Heist #" + heistId;
        }

        public int ResolveTargetHeistId()
        {
            if (_selectedMenuIndex > 0)
            {
                return _selectedMenuIndex - 1;
            }

            if (TryGetActiveHeistId(out int activeId))
            {
                return activeId;
            }

            if (StoryModeTrainer.IsSupportedScriptGlobalBuild()
                && GlobalVariable.Get(GlobalMissionPassPending).Read<int>() != 0)
            {
                int passId = GlobalVariable.Get(GlobalMissionPassHeistId).Read<int>();
                if (passId >= 0 && passId <= 4)
                {
                    return passId;
                }
            }

            return _lastActiveHeistId >= 0 && _lastActiveHeistId <= 4 ? _lastActiveHeistId : 4;
        }

        public bool TryGetActiveHeistId(out int heistId)
        {
            if (IsScriptActive("jewelry_heist"))
            {
                heistId = 0;
                return true;
            }
            if (IsScriptActive("docks_heista") || IsScriptActive("docks_heistb"))
            {
                heistId = 1;
                return true;
            }
            if (IsScriptActive("rural_bank_heist"))
            {
                heistId = 2;
                return true;
            }
            if (IsScriptActive("agency_heist3a") || IsScriptActive("agency_heist3b"))
            {
                heistId = 3;
                return true;
            }
            if (IsScriptActive("finale_heist2a") || IsScriptActive("finale_heist2b"))
            {
                heistId = 4;
                return true;
            }

            heistId = -1;
            return false;
        }

        /// <summary>
        /// Updates potential/actual take and protagonist shares in Story Mode heist globals,
        /// and updates active HUD counters if Jewel Store or Paleto Score is currently running.
        /// </summary>
        public void SetHeistTake(int heistId, int requestedTake, bool armCompletionLock)
        {
            if (_isMultiplayerBlocked() || heistId < 0 || heistId > 4 || !StoryModeTrainer.IsSupportedScriptGlobalBuild())
            {
                return;
            }

            int safeTake = ClampTake(requestedTake);
            _hasCustomShares = false;
            _customSharesHeistId = -1;
            ApplyTakeToHeistStruct(heistId, safeTake, KeepFullTakeWithoutCuts);

            if (heistId == 2 && IsScriptActive("rural_bank_heist"))
            {
                GlobalVariable.Get(GlobalPaletoHudTake).Write(safeTake);
                GlobalVariable.Get(GlobalActiveHeistTake).GetStructField(ActiveTakeArrayField).GetArrayItem(0, 1).Write(safeTake);
                _peakPaletoTake = safeTake;
            }
            else if (heistId == 0 && IsScriptActive("jewelry_heist"))
            {
                GlobalVariable.Get(GlobalActiveHeistTake).GetStructField(ActiveTakeArrayField).GetArrayItem(0, 1).Write(safeTake);
                _peakJewelTake = safeTake;
            }

            if (armCompletionLock)
            {
                LockedHeistId = heistId;
                LockedGrossTake = safeTake;
                LockTakeThroughCompletion = true;
            }
        }

        /// <summary>
        /// Recomputes protagonist shares on the target heist board entry when the user toggles KeepFullTakeWithoutCuts.
        /// </summary>
        public void ReapplyCutModeToTargetHeist(int heistId)
        {
            if (_isMultiplayerBlocked() || heistId < 0 || heistId > 4 || !StoryModeTrainer.IsSupportedScriptGlobalBuild())
            {
                return;
            }

            GlobalVariable entry = GetHeistBoardEntry(heistId);
            int actual = entry.GetStructField(1).Read<int>();
            int potential = entry.Read<int>();
            int gross = actual > 0 ? actual : potential;
            if (gross > 0 && (!_hasCustomShares || _customSharesHeistId != heistId))
            {
                ApplyTakeToHeistStruct(heistId, ClampTake(gross), KeepFullTakeWithoutCuts);
            }
        }

        public void SetCustomShare(int heistId, int charIndex, int amount, bool armCompletionLock = true)
        {
            if (_isMultiplayerBlocked() || heistId < 0 || heistId > 4 || charIndex < 0 || charIndex > 2 || !StoryModeTrainer.IsSupportedScriptGlobalBuild())
            {
                return;
            }

            int clamped = ClampTake(amount);
            GlobalVariable entry = GetHeistBoardEntry(heistId);
            if (!_hasCustomShares || _customSharesHeistId != heistId)
            {
                GlobalVariable existingShares = entry.GetStructField(2);
                _customShares[0] = ClampTake(existingShares.GetArrayItem(0, 1).Read<int>());
                _customShares[1] = ClampTake(existingShares.GetArrayItem(1, 1).Read<int>());
                _customShares[2] = ClampTake(existingShares.GetArrayItem(2, 1).Read<int>());
                _customSharesHeistId = heistId;
                _hasCustomShares = true;
            }

            _customShares[charIndex] = clamped;
            int shareSum = SafeAddClamped(_customShares[0], SafeAddClamped(_customShares[1], _customShares[2]));
            int currentActual = entry.GetStructField(1).Read<int>();
            int effectiveGross = ClampTake(Math.Max(currentActual, shareSum));
            if (effectiveGross > currentActual)
            {
                entry.Write(effectiveGross);
                entry.GetStructField(1).Write(effectiveGross);
            }
            WriteSharesAndPercentages(entry, _customShares[0], _customShares[1], _customShares[2], effectiveGross);

            if (armCompletionLock)
            {
                LockedHeistId = heistId;
                LockedGrossTake = effectiveGross;
                LockTakeThroughCompletion = true;
            }
        }

        public bool TryArmCompletionLock(int heistId, out string message)
        {
            if (_isMultiplayerBlocked())
            {
                LockTakeThroughCompletion = false;
                message = "Blocked outside offline Story Mode.";
                return false;
            }

            if (heistId < 0 || heistId > 4)
            {
                LockTakeThroughCompletion = false;
                message = "No valid heist selected to lock.";
                return false;
            }

            if (!StoryModeTrainer.IsSupportedScriptGlobalBuild())
            {
                LockTakeThroughCompletion = false;
                message = "Heist global lock requires GTA 5 Legacy build 1.0.3889.0.";
                return false;
            }

            GlobalVariable entry = GetHeistBoardEntry(heistId);
            int actual = entry.GetStructField(1).Read<int>();
            int potential = entry.Read<int>();
            int gross = actual > 0 ? actual : potential;

            if (gross <= 0)
            {
                LockTakeThroughCompletion = false;
                message = "Set a Heist Take first before locking " + GetHeistName(heistId) + ".";
                return false;
            }

            LockedHeistId = heistId;
            LockedGrossTake = ClampTake(gross);
            LockTakeThroughCompletion = true;
            message = "Locked " + GetHeistName(heistId) + " take at " + FormatCash(LockedGrossTake) + ".";
            return true;
        }

        public void ClearAllHeistPenalties()
        {
            if (_isMultiplayerBlocked() || !StoryModeTrainer.IsSupportedScriptGlobalBuild())
            {
                return;
            }

            for (int heistId = 0; heistId <= 4; heistId++)
            {
                ClearPenaltiesForHeist(heistId);
            }
        }

        /// <summary>
        /// Immediately pays out the configured protagonist net shares (.f_2[0..2]) into Michael, Franklin, and Trevor bank accounts.
        /// Useful both after editing a past/upcoming heist or if bypassing the delayed in-game text message transfer.
        /// </summary>
        public int PayOutHeistSharesNow(int heistId)
        {
            if (_isMultiplayerBlocked() || heistId < 0 || heistId > 4 || !StoryModeTrainer.IsSupportedScriptGlobalBuild())
            {
                return 0;
            }

            GlobalVariable entry = GetHeistBoardEntry(heistId);
            int michael = entry.GetStructField(2).GetArrayItem(0, 1).Read<int>();
            int franklin = entry.GetStructField(2).GetArrayItem(1, 1).Read<int>();
            int trevor = entry.GetStructField(2).GetArrayItem(2, 1).Read<int>();

            int paidTotal = 0;
            if (michael > 0)
            {
                AddCharacterCash(0, michael);
                paidTotal = SafeAddClamped(paidTotal, michael);
            }
            if (franklin > 0)
            {
                AddCharacterCash(1, franklin);
                paidTotal = SafeAddClamped(paidTotal, franklin);
            }
            if (trevor > 0)
            {
                AddCharacterCash(2, trevor);
                paidTotal = SafeAddClamped(paidTotal, trevor);
            }

            return paidTotal;
        }

        /// <summary>
        /// Adds cash safely to a Story Mode protagonist (-1 = currently active protagonist, 0 = Michael, 1 = Franklin, 2 = Trevor).
        /// Keeps both runtime Global_62237 and persistent sp0..2_total_cash stats synchronized.
        /// </summary>
        public int AddCharacterCash(int charIndex, int amount)
        {
            if (_isMultiplayerBlocked())
            {
                return -1;
            }

            int resolved = charIndex < 0 ? ResolveActiveProtagonistIndex() : charIndex;
            if (resolved < 0 || resolved > 2)
            {
                return -1;
            }

            int statHash = GetCachedStringHash(ProtagonistCashStats[resolved]);
            int current = 0;
            if (StoryModeTrainer.IsSupportedScriptGlobalBuild())
            {
                GlobalVariable cashGlobal = GlobalVariable.Get(GlobalProtagonistCashBase).GetArrayItem(resolved, 1);
                current = cashGlobal.Read<int>();
                if (current < 0)
                {
                    current = 0;
                }

                int updated = SafeAddClamped(current, amount);
                cashGlobal.Write(updated);
                Function.Call(Hash.STAT_SET_INT, statHash, updated, true);
                return updated;
            }

            OutputArgument outStat = new OutputArgument();
            if (Function.Call<bool>(Hash.STAT_GET_INT, statHash, outStat, -1))
            {
                current = Math.Max(0, outStat.GetResult<int>());
            }
            int fallbackUpdated = SafeAddClamped(current, amount);
            Function.Call(Hash.STAT_SET_INT, statHash, fallbackUpdated, true);
            return fallbackUpdated;
        }

        public string GetStatusSummary(int heistId)
        {
            if (heistId < 0 || heistId > 4)
            {
                return "Invalid heist ID.";
            }

            if (!StoryModeTrainer.IsSupportedScriptGlobalBuild())
            {
                return GetHeistName(heistId) + ": Script global inspection requires GTA 5 Legacy 1.0.3889.0.";
            }

            GlobalVariable entry = GetHeistBoardEntry(heistId);
            int potential = entry.Read<int>();
            int actual = entry.GetStructField(1).Read<int>();
            int michael = entry.GetStructField(2).GetArrayItem(0, 1).Read<int>();
            int franklin = entry.GetStructField(2).GetArrayItem(1, 1).Read<int>();
            int trevor = entry.GetStructField(2).GetArrayItem(2, 1).Read<int>();
            int penaltyTotal = GetPenaltyTotal(heistId);

            bool active = TryGetActiveHeistId(out int activeId);
            string activeTag = active ? "Live: " + GetHeistName(activeId) : "Free Roam / Board";

            return string.Format(
                "{0} | {1}~n~Actual: {2} (Pot: {3}) | Drop/Pen: {4}~n~M: {5} | F: {6} | T: {7}",
                GetHeistName(heistId),
                activeTag,
                FormatCash(actual),
                FormatCash(potential),
                FormatCash(penaltyTotal),
                FormatCash(michael),
                FormatCash(franklin),
                FormatCash(trevor));
        }

        public void Tick(Ped player, bool userGodMode, bool preservePlayerCombatProofs)
        {
            if (_isMultiplayerBlocked() || player == null || !player.Exists())
            {
                RestoreTakeProtectionEntities();
                RestorePoliceIgnoreIfApplied();
                IsHeistShieldedNow = false;
                _appliedPlayerCombatProofs = false;
                return;
            }

            bool activeHeist = TryGetActiveHeistId(out int activeHeistId);
            if (activeHeist)
            {
                _lastActiveHeistId = activeHeistId;
            }
            else
            {
                _peakPaletoTake = 0;
                _peakJewelTake = 0;
            }

            bool globalsSupported = StoryModeTrainer.IsSupportedScriptGlobalBuild();
            bool passPending = globalsSupported && GlobalVariable.Get(GlobalMissionPassPending).Read<int>() != 0;
            int passHeistId = globalsSupported ? GlobalVariable.Get(GlobalMissionPassHeistId).Read<int>() : -1;

            if (ProtectHeistTake && (activeHeist || passPending))
            {
                TickTakeProtection(player, activeHeist, activeHeistId, userGodMode, preservePlayerCombatProofs);
            }
            else
            {
                IsHeistShieldedNow = false;
                RestoreTakeProtectionEntities();
                RestorePoliceIgnoreIfApplied();
                if (_appliedPlayerCombatProofs)
                {
                    if (!userGodMode && !preservePlayerCombatProofs)
                    {
                        player.IsBulletProof = false;
                        player.IsExplosionProof = false;
                    }
                    _appliedPlayerCombatProofs = false;
                }
            }

            if (globalsSupported && LockTakeThroughCompletion && LockedHeistId >= 0 && LockedHeistId <= 4)
            {
                if ((activeHeist && activeHeistId == LockedHeistId) || (passPending && passHeistId == LockedHeistId))
                {
                    // Re-apply right across mission_stat_watcher.func_98 execution so Big Score/Bureau resets and cuts never overwrite the user's take.
                    ApplyTakeToHeistStruct(LockedHeistId, LockedGrossTake, KeepFullTakeWithoutCuts);
                }
            }
        }

        public void CleanupOnAbort(Ped player)
        {
            RestorePoliceIgnoreIfApplied();
            RestoreTakeProtectionEntities();
            IsHeistShieldedNow = false;

            if (_appliedPlayerCombatProofs && player != null && player.Exists())
            {
                player.IsBulletProof = false;
                player.IsExplosionProof = false;
            }
            _appliedPlayerCombatProofs = false;
        }

        private void TickTakeProtection(Ped player, bool activeHeist, int activeHeistId, bool userGodMode, bool preservePlayerCombatProofs)
        {
            ClearAllHeistPenalties();

            if (!activeHeist)
            {
                IsHeistShieldedNow = false;
                RestoreTakeProtectionEntities();
                RestorePoliceIgnoreIfApplied();
                if (_appliedPlayerCombatProofs)
                {
                    if (!userGodMode && !preservePlayerCombatProofs)
                    {
                        player.IsBulletProof = false;
                        player.IsExplosionProof = false;
                    }
                    _appliedPlayerCombatProofs = false;
                }
                return;
            }

            IsHeistShieldedNow = true;

            // Preserve peak loot displayed / stored on checkpoint globals for Paleto Score & Jewel Store.
            if (StoryModeTrainer.IsSupportedScriptGlobalBuild())
            {
                if (activeHeistId == 2)
                {
                    PreservePaletoTakeFloor();
                }
                else if (activeHeistId == 0)
                {
                    PreserveJewelTakeFloor();
                }
            }

            if (player.IsDead || Game.IsCutsceneActive)
            {
                RestoreTakeProtectionEntities();
                RestorePoliceIgnoreIfApplied();
                if (_appliedPlayerCombatProofs && !userGodMode && !preservePlayerCombatProofs)
                {
                    player.IsBulletProof = false;
                    player.IsExplosionProof = false;
                }
                _appliedPlayerCombatProofs = false;
                return;
            }

            // Shield player against bullet/explosion damage flags used during police shootouts.
            player.IsBulletProof = true;
            player.IsExplosionProof = true;
            _appliedPlayerCombatProofs = true;
            player.ClearLastWeaponDamage();
            Function.Call(Hash.CLEAR_ENTITY_LAST_DAMAGE_ENTITY, player.Handle);
            Vector3 pos = player.Position;
            Function.Call(Hash.STOP_FIRE_IN_RANGE, pos.X, pos.Y, pos.Z, 8.0f);

            if (PoliceIgnoreDuringHeists)
            {
                Wanted wanted = Game.Player?.Wanted;
                if (wanted != null)
                {
                    wanted.SetPoliceIgnorePlayer(true);
                    _appliedPoliceIgnoreFlag = true;
                }
            }
            else
            {
                RestorePoliceIgnoreIfApplied();
            }

            int now = Game.GameTime;
            if (now >= _nextPedSweepGameTime || (_nextPedSweepGameTime - now) > (PedSweepIntervalMs * 2))
            {
                _nextPedSweepGameTime = now + PedSweepIntervalMs;
                SweepAndProtectNearbyPeds(player);
            }
        }

        private void SweepAndProtectNearbyPeds(Ped player)
        {
            PruneDeadTrackedHandles();

            // Neutralize hostile cops' aim & rate so stray bullets cannot drain rural_bank_heist's IS_BULLET_IN_AREA bag check,
            // and keep accompanying crew protected from bullet/explosion bag drops.
            Ped[] nearbyPeds = World.GetNearbyPeds(player, PedSweepRadius);
            for (int i = 0; i < nearbyPeds.Length; i++)
            {
                Ped ped = nearbyPeds[i];
                if (ped == null || !ped.Exists() || ped.Handle == player.Handle || !ped.IsAlive)
                {
                    continue;
                }

                PedType pType = ped.PedType;
                bool isLawOrHostileType = pType == PedType.Cop
                    || pType == PedType.Swat
                    || pType == PedType.Army
                    || ped.IsEnemy
                    || ped.GetRelationshipWithPed(player) == Relationship.Hate;
                bool isAllyCrew = !isLawOrHostileType && (
                    ped.IsInGroup ||
                    pType == PedType.Player0 ||
                    pType == PedType.Player1 ||
                    pType == PedType.Player2 ||
                    ped.GetRelationshipWithPed(player) == Relationship.Companion);

                if (isAllyCrew)
                {
                    if (!_shieldedAllies.ContainsKey(ped.Handle))
                    {
                        _shieldedAllies[ped.Handle] = new AllyProtectionBaseline(ped);
                    }
                    ped.IsBulletProof = true;
                    ped.IsExplosionProof = true;
                    ped.IsInvincible = true;
                    ped.ClearLastWeaponDamage();
                    Function.Call(Hash.CLEAR_ENTITY_LAST_DAMAGE_ENTITY, ped.Handle);
                    continue;
                }

                if (isLawOrHostileType)
                {
                    if (!_suppressedHostileAccuracy.ContainsKey(ped.Handle))
                    {
                        _suppressedHostileAccuracy[ped.Handle] = ped.Accuracy;
                    }
                    ped.Accuracy = 0;
                    ped.ShootRate = 0;
                }
            }
        }

        private void PreservePaletoTakeFloor()
        {
            int hudTake = GlobalVariable.Get(GlobalPaletoHudTake).Read<int>();
            GlobalVariable activeVar = GlobalVariable.Get(GlobalActiveHeistTake).GetStructField(ActiveTakeArrayField).GetArrayItem(0, 1);
            int activeTake = activeVar.Read<int>();

            int observedMax = Math.Max(hudTake, activeTake);
            if (observedMax > _peakPaletoTake)
            {
                _peakPaletoTake = observedMax;
            }

            if (LockTakeThroughCompletion && LockedHeistId == 2 && LockedGrossTake > 0)
            {
                _peakPaletoTake = LockedGrossTake;
            }

            if (_peakPaletoTake > 0)
            {
                if (hudTake < _peakPaletoTake)
                {
                    GlobalVariable.Get(GlobalPaletoHudTake).Write(_peakPaletoTake);
                }
                if (activeTake < _peakPaletoTake)
                {
                    activeVar.Write(_peakPaletoTake);
                }
            }
        }

        private void PreserveJewelTakeFloor()
        {
            GlobalVariable activeVar = GlobalVariable.Get(GlobalActiveHeistTake).GetStructField(ActiveTakeArrayField).GetArrayItem(0, 1);
            int activeTake = activeVar.Read<int>();

            if (activeTake > _peakJewelTake)
            {
                _peakJewelTake = activeTake;
            }

            if (LockTakeThroughCompletion && LockedHeistId == 0 && LockedGrossTake > 0)
            {
                _peakJewelTake = LockedGrossTake;
            }

            if (_peakJewelTake > 0 && activeTake < _peakJewelTake)
            {
                activeVar.Write(_peakJewelTake);
            }
        }

        private void ApplyTakeToHeistStruct(int heistId, int grossTake, bool keepFullTake)
        {
            GlobalVariable entry = GetHeistBoardEntry(heistId);
            entry.Write(grossTake); // .f_0 Potential Take
            entry.GetStructField(1).Write(grossTake); // .f_1 Actual Take

            if (keepFullTake)
            {
                for (int slot = 0; slot < 5; slot++)
                {
                    entry.GetStructField(17).GetArrayItem(slot, 1).Write(0);
                }

                GlobalVariable watcherState = GlobalVariable.Get(GlobalMissionWatcherState);
                watcherState.GetStructField(WatcherLesterPctField).Write(0f);
                watcherState.GetStructField(WatcherLesterCutField).Write(0);
            }

            if (_hasCustomShares && _customSharesHeistId == heistId)
            {
                WriteSharesAndPercentages(entry, _customShares[0], _customShares[1], _customShares[2], grossTake);
            }
            else
            {
                ComputeDefaultProtagonistShares(entry, heistId, grossTake, keepFullTake);
            }
        }

        private static void ComputeDefaultProtagonistShares(GlobalVariable entry, int heistId, int actualTake, bool keepFullTake)
        {
            int mShare = 0;
            int fShare = 0;
            int tShare = 0;

            switch (heistId)
            {
                case 0: // Jewel Store: Michael + Franklin
                    if (keepFullTake)
                    {
                        fShare = (int)Math.Ceiling(actualTake * 0.25);
                        mShare = actualTake - fShare;
                    }
                    else
                    {
                        int lester = (int)Math.Ceiling(actualTake * 0.14);
                        fShare = (int)Math.Ceiling(actualTake * 0.12);
                        mShare = Math.Max(0, actualTake - lester - fShare);
                    }
                    break;

                case 1: // Merryweather: Canon awards $0 unless Keep Full Take override is used
                    if (keepFullTake)
                    {
                        int split = actualTake / 3;
                        mShare = split + (actualTake - (split * 3));
                        fShare = split;
                        tShare = split;
                    }
                    break;

                case 2: // Paleto Score: Michael, Franklin, Trevor
                    if (keepFullTake)
                    {
                        int split = actualTake / 3;
                        mShare = split + (actualTake - (split * 3));
                        fShare = split;
                        tShare = split;
                    }
                    else
                    {
                        int rem = actualTake - (int)Math.Floor(actualTake * 0.78);
                        rem = Math.Max(0, rem - (int)Math.Ceiling(actualTake * 0.12));
                        int split = rem / 3;
                        mShare = split;
                        fShare = split;
                        tShare = split;
                    }
                    break;

                case 3: // Bureau Raid: Franklin only
                    if (keepFullTake)
                    {
                        fShare = actualTake;
                    }
                    else
                    {
                        int lester = (int)Math.Ceiling(actualTake * 0.12);
                        fShare = Math.Max(0, actualTake - lester);
                    }
                    break;

                case 4: // The Big Score: Michael, Franklin, Trevor
                    if (keepFullTake)
                    {
                        int split = actualTake / 3;
                        mShare = split + (actualTake - (split * 3));
                        fShare = split;
                        tShare = split;
                    }
                    else
                    {
                        int rem = Math.Max(0, actualTake - (int)Math.Ceiling(actualTake * 0.12));
                        int split = rem / 3;
                        mShare = split;
                        fShare = split;
                        tShare = split;
                    }
                    break;
            }

            WriteSharesAndPercentages(entry, mShare, fShare, tShare, actualTake);
        }

        private static void WriteSharesAndPercentages(GlobalVariable entry, int mShare, int fShare, int tShare, int actualTake)
        {
            GlobalVariable shares = entry.GetStructField(2);
            shares.GetArrayItem(0, 1).Write(mShare);
            shares.GetArrayItem(1, 1).Write(fShare);
            shares.GetArrayItem(2, 1).Write(tShare);

            float mPct = actualTake > 0 && mShare > 0 ? (mShare / (float)actualTake) * 100f : (mShare == 0 ? 0f : -1f);
            float fPct = actualTake > 0 && fShare > 0 ? (fShare / (float)actualTake) * 100f : (fShare == 0 ? 0f : -1f);
            float tPct = actualTake > 0 && tShare > 0 ? (tShare / (float)actualTake) * 100f : (tShare == 0 ? 0f : -1f);

            GlobalVariable pcts = entry.GetStructField(6);
            pcts.GetArrayItem(0, 1).Write(mPct);
            pcts.GetArrayItem(1, 1).Write(fPct);
            pcts.GetArrayItem(2, 1).Write(tPct);
        }

        private void ClearPenaltiesForHeist(int heistId)
        {
            GlobalVariable pen = GlobalVariable.Get(GlobalHeistPenaltyArray).GetArrayItem(heistId, HeistPenaltyItemSize);
            int count = pen.GetStructField(PenaltyCountField).Read<int>();
            bool hasNonZero = count != 0;

            if (!hasNonZero)
            {
                for (int i = 0; i < 5; i++)
                {
                    if (pen.GetStructField(PenaltyAmountsField).GetArrayItem(i, 1).Read<int>() != 0)
                    {
                        hasNonZero = true;
                        break;
                    }
                }
            }

            if (!hasNonZero)
            {
                return;
            }

            pen.GetStructField(PenaltyCountField).Write(0);
            GlobalVariable types = pen.GetStructField(PenaltyTypesField);
            GlobalVariable amounts = pen.GetStructField(PenaltyAmountsField);
            for (int i = 0; i < 5; i++)
            {
                types.GetArrayItem(i, 1).Write(0);
                amounts.GetArrayItem(i, 1).Write(0);
            }
        }

        private static int GetPenaltyTotal(int heistId)
        {
            GlobalVariable pen = GlobalVariable.Get(GlobalHeistPenaltyArray).GetArrayItem(heistId, HeistPenaltyItemSize);
            int count = pen.GetStructField(PenaltyCountField).Read<int>();
            if (count <= 0)
            {
                return 0;
            }
            if (count > 5)
            {
                count = 5;
            }

            int total = 0;
            GlobalVariable amounts = pen.GetStructField(PenaltyAmountsField);
            for (int i = 0; i < count; i++)
            {
                int val = amounts.GetArrayItem(i, 1).Read<int>();
                if (val > 0)
                {
                    total = SafeAddClamped(total, val);
                }
            }

            return total;
        }

        private static GlobalVariable GetHeistBoardEntry(int heistId)
        {
            return GlobalVariable.Get(GlobalSavedBase)
                .GetStructField(GlobalSavedHeistField1)
                .GetStructField(GlobalSavedHeistBoardArrayField)
                .GetArrayItem(heistId, HeistBoardItemSize);
        }

        private bool IsScriptActive(string scriptName)
        {
            int hash = GetCachedStringHash(scriptName);
            return Function.Call<int>(Hash.GET_NUMBER_OF_THREADS_RUNNING_THE_SCRIPT_WITH_THIS_HASH, hash) > 0;
        }

        private int GetCachedStringHash(string input)
        {
            int hash;
            if (!_scriptHashCache.TryGetValue(input, out hash))
            {
                hash = unchecked((int)StringHash.AtStringHashUtf8(input, 0));
                _scriptHashCache[input] = hash;
            }

            return hash;
        }

        private void PruneDeadTrackedHandles()
        {
            if (_shieldedAllies.Count > 0)
            {
                _pruneHandlesBuffer.Clear();
                foreach (KeyValuePair<int, AllyProtectionBaseline> kvp in _shieldedAllies)
                {
                    Ped ally = Entity.FromHandle(kvp.Key) as Ped;
                    if (ally == null || !ally.Exists() || ally.IsDead)
                    {
                        _pruneHandlesBuffer.Add(kvp.Key);
                    }
                }
                for (int i = 0; i < _pruneHandlesBuffer.Count; i++)
                {
                    _shieldedAllies.Remove(_pruneHandlesBuffer[i]);
                }
            }

            if (_suppressedHostileAccuracy.Count > 0)
            {
                _pruneHandlesBuffer.Clear();
                foreach (KeyValuePair<int, int> kvp in _suppressedHostileAccuracy)
                {
                    Ped hostile = Entity.FromHandle(kvp.Key) as Ped;
                    if (hostile == null || !hostile.Exists() || hostile.IsDead)
                    {
                        _pruneHandlesBuffer.Add(kvp.Key);
                    }
                }
                for (int i = 0; i < _pruneHandlesBuffer.Count; i++)
                {
                    _suppressedHostileAccuracy.Remove(_pruneHandlesBuffer[i]);
                }
            }
        }

        private void RestoreTakeProtectionEntities()
        {
            if (_shieldedAllies.Count > 0)
            {
                foreach (KeyValuePair<int, AllyProtectionBaseline> kvp in _shieldedAllies)
                {
                    Ped ally = Entity.FromHandle(kvp.Key) as Ped;
                    if (ally != null && ally.Exists())
                    {
                        ally.IsBulletProof = kvp.Value.WasBulletProof;
                        ally.IsExplosionProof = kvp.Value.WasExplosionProof;
                        ally.IsInvincible = kvp.Value.WasInvincible;
                    }
                }
                _shieldedAllies.Clear();
            }

            if (_suppressedHostileAccuracy.Count > 0)
            {
                foreach (KeyValuePair<int, int> kvp in _suppressedHostileAccuracy)
                {
                    Ped hostile = Entity.FromHandle(kvp.Key) as Ped;
                    if (hostile != null && hostile.Exists())
                    {
                        hostile.Accuracy = kvp.Value;
                        hostile.ShootRate = DefaultPedShootRate;
                    }
                }
                _suppressedHostileAccuracy.Clear();
            }
        }

        private void RestorePoliceIgnoreIfApplied()
        {
            if (_appliedPoliceIgnoreFlag)
            {
                Wanted wanted = Game.Player?.Wanted;
                if (wanted != null)
                {
                    wanted.SetPoliceIgnorePlayer(false);
                }
                _appliedPoliceIgnoreFlag = false;
            }
        }

        private static int ResolveActiveProtagonistIndex()
        {
            Ped player = Game.Player?.Character;
            if (player != null && player.Exists())
            {
                int modelHash = player.Model.Hash;
                if (modelHash == unchecked((int)PedHash.Michael)) return 0;
                if (modelHash == unchecked((int)PedHash.Franklin)) return 1;
                if (modelHash == unchecked((int)PedHash.Trevor)) return 2;

                switch (player.PedType)
                {
                    case PedType.Player0: return 0;
                    case PedType.Player1: return 1;
                    case PedType.Player2: return 2;
                }
            }

            if (StoryModeTrainer.IsSupportedScriptGlobalBuild())
            {
                try
                {
                    int globalIdx = GlobalVariable.Get(GlobalCurrentProtagonistIndex).Read<int>();
                    if (globalIdx >= 0 && globalIdx <= 2)
                    {
                        return globalIdx;
                    }
                }
                catch
                {
                    // Fall through if global table is unavailable.
                }
            }

            return -1;
        }

        private static int ClampTake(int requested)
        {
            if (requested < 0) return 0;
            if (requested > MaxSafeTake) return MaxSafeTake;
            return requested;
        }

        private static int SafeAddClamped(int a, int b)
        {
            long sum = (long)a + b;
            if (sum >= int.MaxValue) return int.MaxValue;
            if (sum <= 0) return 0;
            return (int)sum;
        }
    }
}
