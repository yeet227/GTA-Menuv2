using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;

namespace StoryModeTrainerMod
{
    /// <summary>
    /// Dynamically tunes acceleration, torque, top speed, braking, and traction multipliers for the
    /// currently occupied vehicle, restoring exact original vehicle and handling baselines on exit or unload.
    /// </summary>
    internal sealed class VehiclePhysicsController
    {
        private sealed class HandlingBaseline
        {
            public HandlingData Handling { get; }
            public float InitialDriveForce { get; }
            public float BrakeForce { get; }
            public float HandBrakeForce { get; }
            public float TractionCurveMax { get; }
            public float TractionCurveMin { get; }
            public float LowSpeedTractionLossMultiplier { get; }
            public float DownForceModifier { get; }

            public HandlingBaseline(HandlingData hd)
            {
                Handling = hd;
                InitialDriveForce = hd.InitialDriveForce;
                BrakeForce = hd.BrakeForce;
                HandBrakeForce = hd.HandBrakeForce;
                TractionCurveMax = hd.TractionCurveMax;
                TractionCurveMin = hd.TractionCurveMin;
                LowSpeedTractionLossMultiplier = hd.LowSpeedTractionLossMultiplier;
                DownForceModifier = hd.DownForceModifier;
            }

            public void Restore()
            {
                if (Handling == null || !Handling.IsValid) return;

                Handling.InitialDriveForce = InitialDriveForce;
                Handling.BrakeForce = BrakeForce;
                Handling.HandBrakeForce = HandBrakeForce;
                Handling.TractionCurveMax = TractionCurveMax;
                Handling.TractionCurveMin = TractionCurveMin;
                Handling.LowSpeedTractionLossMultiplier = LowSpeedTractionLossMultiplier;
                Handling.DownForceModifier = DownForceModifier;
            }
        }

        public static readonly string[] PowerLabels =
        {
            "1.0x (Stock)",
            "1.5x (Sport)",
            "2.0x (Super)",
            "3.0x (Hyper)",
            "5.0x (Rocket)",
            "8.0x (Extreme)",
            "12.0x (Warp)"
        };

        private static readonly float[] PowerMultipliers =
        {
            1.0f,
            1.5f,
            2.0f,
            3.0f,
            5.0f,
            8.0f,
            12.0f
        };

        public static readonly string[] TopSpeedLabels =
        {
            "+0% (Stock)",
            "+25% Speed",
            "+50% Speed",
            "+100% (2x Speed)",
            "+200% (3x Speed)",
            "+400% (5x Speed)"
        };

        private static readonly float[] TopSpeedBoostPercents =
        {
            0.0f,
            25.0f,
            50.0f,
            100.0f,
            200.0f,
            400.0f
        };

        public static readonly string[] BrakeLabels =
        {
            "1.0x (Stock)",
            "1.5x (Sport)",
            "2.0x (Race)",
            "3.0x (Instant)",
            "5.0x (Anchor)"
        };

        private static readonly float[] BrakeMultipliers =
        {
            1.0f,
            1.5f,
            2.0f,
            3.0f,
            5.0f
        };

        public static readonly string[] TractionLabels =
        {
            "1.0x (Stock)",
            "1.3x (Sport)",
            "1.75x (Track)",
            "2.5x (Downforce)",
            "4.0x (Rails)"
        };

        private static readonly float[] TractionMultipliers =
        {
            1.0f,
            1.3f,
            1.75f,
            2.5f,
            4.0f
        };

        private readonly Dictionary<long, HandlingBaseline> _handlingBaselines = new Dictionary<long, HandlingBaseline>();
        private readonly Func<bool> _isMultiplayerBlocked;

        private int _powerIndex;
        private int _topSpeedIndex;
        private int _brakeIndex;
        private int _tractionIndex;
        private Vehicle _activeVehicle;
        private float _appliedTopSpeedPct;
        private bool _appliedNativePower;
        private bool _handlingModified;

        public VehiclePhysicsController(Func<bool> isMultiplayerBlocked = null)
        {
            _isMultiplayerBlocked = isMultiplayerBlocked ?? (() => false);
        }

        public bool AutoRepairCurrentVehicle { get; set; }

        public int PowerIndex
        {
            get { return _powerIndex; }
            set { _powerIndex = ClampIndex(value, PowerMultipliers.Length); }
        }

        public int TopSpeedIndex
        {
            get { return _topSpeedIndex; }
            set { _topSpeedIndex = ClampIndex(value, TopSpeedBoostPercents.Length); }
        }

        public int BrakeIndex
        {
            get { return _brakeIndex; }
            set { _brakeIndex = ClampIndex(value, BrakeMultipliers.Length); }
        }

        public int TractionIndex
        {
            get { return _tractionIndex; }
            set { _tractionIndex = ClampIndex(value, TractionMultipliers.Length); }
        }

        public bool HasAnyMultiplierActive =>
            _powerIndex > 0 || _topSpeedIndex > 0 || _brakeIndex > 0 || _tractionIndex > 0;

        /// <summary>
        /// Signals that the active vehicle was repaired or upgraded so MODIFY_VEHICLE_TOP_SPEED is re-applied next tick.
        /// </summary>
        public void NotifyVehicleRepairedOrUpgraded()
        {
            _appliedTopSpeedPct = -1.0f;
        }

        /// <summary>
        /// Rights an overturned or stuck vehicle while preserving its current heading.
        /// </summary>
        public static bool FlipVehicleUpright(Vehicle vehicle)
        {
            if (vehicle == null || !vehicle.Exists())
            {
                return false;
            }

            float heading = vehicle.Heading;
            Vector3 vel = vehicle.Velocity;
            vehicle.Rotation = new Vector3(0.0f, 0.0f, heading);
            if (!vehicle.IsInWater && !vehicle.Model.IsBoat && !vehicle.Model.IsSubmarine)
            {
                vehicle.PlaceOnGround();
            }
            vehicle.Velocity = new Vector3(vel.X, vel.Y, 0.0f);
            return true;
        }

        /// <summary>
        /// Resets all physics sliders to stock (1.0x / +0%) and immediately restores current & cached vehicles.
        /// </summary>
        public void ResetToStock(Vehicle currentVehicle)
        {
            _powerIndex = 0;
            _topSpeedIndex = 0;
            _brakeIndex = 0;
            _tractionIndex = 0;
            AutoRepairCurrentVehicle = false;

            if (_activeVehicle != null && _activeVehicle.Exists())
            {
                ResetVehicleInstance(_activeVehicle);
            }

            if (currentVehicle != null && currentVehicle.Exists() && (_activeVehicle == null || currentVehicle.Handle != _activeVehicle.Handle))
            {
                ResetVehicleInstance(currentVehicle);
            }

            RestoreAllHandlingBaselines();
            _activeVehicle = null;
            _appliedTopSpeedPct = 0.0f;
            _appliedNativePower = false;
            _handlingModified = false;
        }

        /// <summary>
        /// Applies configured multipliers to the player's current vehicle and cleans up previous vehicles on exit.
        /// </summary>
        public void Tick(Ped player)
        {
            if (_isMultiplayerBlocked())
            {
                ClearActiveVehicleOverrides();
                return;
            }

            Vehicle current = (player != null && player.Exists() && player.IsInVehicle()) ? player.CurrentVehicle : null;
            bool inCurrent = current != null && current.Exists();
            bool isDriver = inCurrent && current.Driver != null && current.Driver.Handle == player.Handle;

            if (!inCurrent)
            {
                ClearActiveVehicleOverrides();
                return;
            }

            if (AutoRepairCurrentVehicle
                && (current.BodyHealth < 999f
                    || current.EngineHealth < 999f
                    || current.PetrolTankHealth < 999f
                    || Function.Call<bool>(Hash.GET_DOES_VEHICLE_HAVE_DAMAGE_DECALS, current.Handle)))
            {
                current.Repair();
                _appliedTopSpeedPct = -1.0f; // Repair can clear per-entity top speed overrides cleanly.
            }

            if (!isDriver)
            {
                if (_activeVehicle != null && (_activeVehicle.Handle != current.Handle || _appliedNativePower || _handlingModified || _appliedTopSpeedPct > 0.01f))
                {
                    ClearActiveVehicleOverrides();
                }
                return;
            }

            if (_activeVehicle != null && _activeVehicle.Handle != current.Handle)
            {
                ClearActiveVehicleOverrides();
            }

            _activeVehicle = current;

            float powerMult = PowerMultipliers[_powerIndex];
            if (powerMult > 1.001f)
            {
                Function.Call(Hash.SET_VEHICLE_CHEAT_POWER_INCREASE, current.Handle, powerMult);
                current.EngineTorqueMultiplier = powerMult;
                _appliedNativePower = true;
            }
            else if (_appliedNativePower)
            {
                Function.Call(Hash.SET_VEHICLE_CHEAT_POWER_INCREASE, current.Handle, 1.0f);
                current.EngineTorqueMultiplier = 1.0f;
                _appliedNativePower = false;
            }

            float desiredTopSpeedPct = TopSpeedBoostPercents[_topSpeedIndex];
            if (Math.Abs(_appliedTopSpeedPct - desiredTopSpeedPct) > 0.01f)
            {
                Function.Call(Hash.MODIFY_VEHICLE_TOP_SPEED, current.Handle, desiredTopSpeedPct);
                _appliedTopSpeedPct = desiredTopSpeedPct;
            }

            ApplyHandlingMultipliers(current, powerMult, BrakeMultipliers[_brakeIndex], TractionMultipliers[_tractionIndex]);
        }

        private void ClearActiveVehicleOverrides()
        {
            if (_activeVehicle != null)
            {
                if (_activeVehicle.Exists())
                {
                    ResetVehicleInstance(_activeVehicle);
                }
                RestoreAllHandlingBaselines();
                _activeVehicle = null;
            }
            else if (_handlingModified)
            {
                RestoreAllHandlingBaselines();
            }

            _appliedTopSpeedPct = 0.0f;
            _appliedNativePower = false;
            _handlingModified = false;
        }

        public void CleanupOnAbort(Ped player)
        {
            Vehicle current = (player != null && player.Exists() && player.IsInVehicle()) ? player.CurrentVehicle : null;
            ResetToStock(current);
        }

        private void ApplyHandlingMultipliers(Vehicle vehicle, float powerMult, float brakeMult, float tractionMult)
        {
            bool wantHandlingOverride = powerMult > 1.001f || brakeMult > 1.001f || tractionMult > 1.001f;
            if (!wantHandlingOverride)
            {
                if (_handlingModified)
                {
                    RestoreAllHandlingBaselines();
                    _handlingModified = false;
                }
                return;
            }

            HandlingData hd = vehicle.HandlingData;
            if (hd == null || !hd.IsValid)
            {
                return;
            }

            long key = hd.MemoryAddress.ToInt64();
            HandlingBaseline baseline;
            if (!_handlingBaselines.TryGetValue(key, out baseline))
            {
                baseline = new HandlingBaseline(hd);
                _handlingBaselines[key] = baseline;
            }

            // Combine moderate handling drive-force scaling with native torque/cheat power so even heavy/EV models launch smoothly.
            float accelHandlingMult = 1.0f + ((powerMult - 1.0f) * 0.25f);
            hd.InitialDriveForce = baseline.InitialDriveForce * accelHandlingMult;
            hd.BrakeForce = baseline.BrakeForce * brakeMult;
            hd.HandBrakeForce = baseline.HandBrakeForce * Math.Max(1.0f, brakeMult * 0.85f);
            hd.TractionCurveMax = baseline.TractionCurveMax * tractionMult;
            hd.TractionCurveMin = baseline.TractionCurveMin * Math.Min(3.0f, 1.0f + ((tractionMult - 1.0f) * 0.7f));
            hd.DownForceModifier = Math.Max(baseline.DownForceModifier, baseline.DownForceModifier + ((tractionMult - 1.0f) * 1.5f));
            if (tractionMult > 1.001f)
            {
                hd.LowSpeedTractionLossMultiplier = baseline.LowSpeedTractionLossMultiplier / tractionMult;
            }
            else
            {
                hd.LowSpeedTractionLossMultiplier = baseline.LowSpeedTractionLossMultiplier;
            }

            _handlingModified = true;
        }

        private void ResetVehicleInstance(Vehicle vehicle)
        {
            Function.Call(Hash.SET_VEHICLE_CHEAT_POWER_INCREASE, vehicle.Handle, 1.0f);
            vehicle.EngineTorqueMultiplier = 1.0f;
            Function.Call(Hash.MODIFY_VEHICLE_TOP_SPEED, vehicle.Handle, 0.0f);
        }

        private void RestoreAllHandlingBaselines()
        {
            if (_handlingBaselines.Count == 0) return;

            foreach (KeyValuePair<long, HandlingBaseline> kvp in _handlingBaselines)
            {
                kvp.Value.Restore();
            }
            _handlingBaselines.Clear();
        }

        private static int ClampIndex(int index, int length)
        {
            if (index < 0) return 0;
            if (index >= length) return length - 1;
            return index;
        }
    }
}
