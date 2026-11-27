using System;
using GTA;
using GTA.Input;
using GTA.Math;
using GTA.Native;

namespace StoryModeTrainerMod
{
    /// <summary>
    /// Camera-relative 3D flight and phase-through-walls controller for both peds and vehicles.
    /// Guarantees zero ragdoll, collision, fall, or impact consequences while flying and during exit.
    /// </summary>
    internal sealed class NoClipController
    {
        public static readonly string[] SpeedLabels =
        {
            "Slow (0.3x)",
            "Normal (1x)",
            "Fast (3x)",
            "Turbo (8x)",
            "Hyper (20x)"
        };

        private static readonly float[] SpeedMultipliers =
        {
            0.3f,
            1.0f,
            3.0f,
            8.0f,
            20.0f
        };

        private static readonly ControlAction[] SuppressedControls =
        {
            ControlAction.MoveUpOnly,
            ControlAction.MoveDownOnly,
            ControlAction.MoveLeftOnly,
            ControlAction.MoveRightOnly,
            ControlAction.MoveUpDown,
            ControlAction.MoveLeftRight,
            ControlAction.Jump,
            ControlAction.Duck,
            ControlAction.Sprint,
            ControlAction.VehicleAccelerate,
            ControlAction.VehicleBrake,
            ControlAction.VehicleMoveUpDown,
            ControlAction.VehicleMoveLeftRight,
            ControlAction.VehicleHandbrake,
            ControlAction.VehicleDuck,
            ControlAction.VehicleFlyThrottleUp,
            ControlAction.VehicleFlyThrottleDown,
            ControlAction.VehicleFlyRollLeftRight,
            ControlAction.VehicleFlyPitchUpDown
        };

        private const int FallShieldDurationMs = 3000;
        private const int MaxClockSkewToleranceMs = 5000;

        private readonly float _baseSpeed;
        private readonly Func<bool> _isMultiplayerBlocked;
        private Entity _controlledEntity;
        private Vehicle _fallShieldVehicle;
        private int _fallShieldUntilGameTime;
        private int _speedPresetIndex = 1;

        public NoClipController(float baseSpeed, bool exitFallShield, bool snapToGroundOnExit, Func<bool> isMultiplayerBlocked = null)
        {
            if (float.IsNaN(baseSpeed) || float.IsInfinity(baseSpeed) || baseSpeed < 2.0f || baseSpeed > 200.0f)
            {
                _baseSpeed = 18.0f;
            }
            else
            {
                _baseSpeed = baseSpeed;
            }
            _isMultiplayerBlocked = isMultiplayerBlocked ?? (() => false);
            ExitFallShieldEnabled = exitFallShield;
            SnapToGroundOnExit = snapToGroundOnExit;
            AlignHeadingWithCamera = true;
        }

        public bool IsActive { get; private set; }
        public bool ExitFallShieldEnabled { get; set; }
        public bool SnapToGroundOnExit { get; set; }
        public bool AlignHeadingWithCamera { get; set; }

        public bool IsFallShieldActive
        {
            get
            {
                if (_fallShieldUntilGameTime <= 0)
                {
                    return false;
                }
                int now = Game.GameTime;
                int remaining = _fallShieldUntilGameTime - now;
                return remaining > 0 && remaining <= FallShieldDurationMs + MaxClockSkewToleranceMs;
            }
        }

        public bool IsControllingEntity(Entity entity)
        {
            return IsActive && _controlledEntity != null && entity != null && _controlledEntity.Handle == entity.Handle;
        }

        public int SpeedPresetIndex
        {
            get { return _speedPresetIndex; }
            set
            {
                if (value < 0) value = 0;
                if (value >= SpeedMultipliers.Length) value = SpeedMultipliers.Length - 1;
                _speedPresetIndex = value;
            }
        }

        public bool Toggle(Ped player, bool userGodMode, bool userVehicleGodMode, bool preserveCombatProofs = false)
        {
            if (IsActive)
            {
                Disable(player, userGodMode, userVehicleGodMode, preserveCombatProofs);
            }
            else
            {
                Enable(player);
            }

            return IsActive;
        }

        public void Enable(Ped player)
        {
            if (_isMultiplayerBlocked() || !CanOperateSafely(player))
            {
                return;
            }

            _fallShieldUntilGameTime = 0;
            _fallShieldVehicle = null;
            IsActive = true;

            Entity target = ResolveTargetEntity(player);
            _controlledEntity = target;
            ApplyNoClipEntityState(player, target);
        }

        public void Disable(Ped player, bool userGodMode, bool userVehicleGodMode, bool preserveCombatProofs = false)
        {
            if (!IsActive)
            {
                return;
            }

            IsActive = false;

            Entity releaseTarget = _controlledEntity;
            _controlledEntity = null;

            bool playerValid = player != null && player.Exists();
            Vehicle currentVeh = playerValid && player.IsInVehicle() ? player.CurrentVehicle : null;
            bool inCurrentVeh = currentVeh != null && currentVeh.Exists();

            if (releaseTarget != null && releaseTarget.Exists())
            {
                ReleaseEntityPhysics(releaseTarget);
                if (releaseTarget is Vehicle releasedVeh && (!inCurrentVeh || releasedVeh.Handle != currentVeh.Handle))
                {
                    RestoreVehicleDefaults(releasedVeh, false, keepCollisionProof: false);
                }
            }

            if (playerValid)
            {
                if (!inCurrentVeh)
                {
                    ReleaseEntityPhysics(player);
                }
                else
                {
                    player.IsPositionFrozen = false;
                    player.HasGravity = true;
                    ReleaseEntityPhysics(currentVeh);
                }

                bool landed = false;
                if (SnapToGroundOnExit && player.IsAlive && CanOperateSafely(player))
                {
                    landed = LandOnSurfaceBelow(player, currentVeh);
                }

                if (!landed && ExitFallShieldEnabled && player.IsAlive)
                {
                    // Brief grace shield so disabling No-Clip in mid-air or after a failed ground probe never causes fatal impact or ragdoll.
                    _fallShieldUntilGameTime = Game.GameTime + FallShieldDurationMs;
                    _fallShieldVehicle = inCurrentVeh ? currentVeh : null;
                    ApplyFallShieldState(player, _fallShieldVehicle, preserveCombatProofs);
                }
                else
                {
                    _fallShieldUntilGameTime = 0;
                    _fallShieldVehicle = null;
                    RestorePlayerDefaults(player, userGodMode, preserveCombatProofs);
                    if (inCurrentVeh)
                    {
                        RestoreVehicleDefaults(currentVeh, userVehicleGodMode, keepCollisionProof: userVehicleGodMode);
                    }
                }
            }
            else if (inCurrentVeh)
            {
                RestoreVehicleDefaults(currentVeh, userVehicleGodMode, keepCollisionProof: userVehicleGodMode);
            }
        }

        public void Tick(Ped player, bool userGodMode, bool userVehicleGodMode, bool preserveCombatProofs = false)
        {
            if (_isMultiplayerBlocked())
            {
                if (IsActive)
                {
                    ForceCleanupOnAbort(player);
                }
                return;
            }

            if (player == null || !player.Exists())
            {
                if (IsActive)
                {
                    Disable(null, userGodMode, userVehicleGodMode, preserveCombatProofs);
                }
                return;
            }

            if (!IsActive)
            {
                TickFallShieldCleanup(player, userGodMode, userVehicleGodMode, preserveCombatProofs);
                return;
            }

            if (!CanOperateSafely(player))
            {
                Disable(player, userGodMode, userVehicleGodMode, preserveCombatProofs);
                return;
            }

            Entity target = ResolveTargetEntity(player);
            if (_controlledEntity != null && _controlledEntity.Handle != target.Handle)
            {
                if (_controlledEntity.Exists())
                {
                    ReleaseEntityPhysics(_controlledEntity);
                    if (_controlledEntity is Vehicle oldVeh)
                    {
                        RestoreVehicleDefaults(oldVeh, false, keepCollisionProof: false);
                    }
                }
                _controlledEntity = target;
            }
            else if (_controlledEntity == null)
            {
                _controlledEntity = target;
            }

            ApplyNoClipEntityState(player, target);
            DisableInterferingControls();
            MoveTargetAlongCamera(target);
        }

        /// <summary>
        /// Safely restores gravity, collision, freeze, proofs, and ragdoll state when unloading or reloading scripts.
        /// </summary>
        public void ForceCleanupOnAbort(Ped player)
        {
            IsActive = false;
            _fallShieldUntilGameTime = 0;

            if (_controlledEntity != null && _controlledEntity.Exists())
            {
                ReleaseEntityPhysics(_controlledEntity);
                if (_controlledEntity is Vehicle ctrlVeh)
                {
                    RestoreVehicleDefaults(ctrlVeh, false, keepCollisionProof: false);
                }
            }
            _controlledEntity = null;

            if (_fallShieldVehicle != null && _fallShieldVehicle.Exists())
            {
                ReleaseEntityPhysics(_fallShieldVehicle);
                RestoreVehicleDefaults(_fallShieldVehicle, false, keepCollisionProof: false);
            }
            _fallShieldVehicle = null;

            if (player != null && player.Exists())
            {
                Vehicle currentVeh = player.IsInVehicle() ? player.CurrentVehicle : null;
                if (currentVeh != null && currentVeh.Exists())
                {
                    player.IsPositionFrozen = false;
                    player.HasGravity = true;
                    ReleaseEntityPhysics(currentVeh);
                    RestoreVehicleDefaults(currentVeh, false, keepCollisionProof: false);
                }
                else
                {
                    ReleaseEntityPhysics(player);
                }

                RestorePlayerDefaults(player, false, preserveCombatProofs: false);
            }
        }

        public bool TryLandOnSurfaceBelow(Ped player)
        {
            if (player == null || !player.Exists() || player.IsDead)
            {
                return false;
            }

            Vehicle currentVeh = player.IsInVehicle() ? player.CurrentVehicle : null;
            return LandOnSurfaceBelow(player, currentVeh);
        }

        private void TickFallShieldCleanup(Ped player, bool userGodMode, bool userVehicleGodMode, bool preserveCombatProofs)
        {
            if (_fallShieldUntilGameTime <= 0)
            {
                return;
            }

            int remaining = _fallShieldUntilGameTime - Game.GameTime;
            if (remaining > 0 && remaining <= FallShieldDurationMs + MaxClockSkewToleranceMs && player.IsAlive)
            {
                ApplyFallShieldState(player, _fallShieldVehicle, preserveCombatProofs);
                return;
            }

            _fallShieldUntilGameTime = 0;
            RestorePlayerDefaults(player, userGodMode, preserveCombatProofs);

            if (_fallShieldVehicle != null && _fallShieldVehicle.Exists())
            {
                bool stillInShieldVehicle = player.IsInVehicle(_fallShieldVehicle);
                bool keepVehGod = userVehicleGodMode && stillInShieldVehicle;
                RestoreVehicleDefaults(_fallShieldVehicle, keepVehGod, keepCollisionProof: keepVehGod);
            }
            _fallShieldVehicle = null;
        }

        private static bool CanOperateSafely(Ped player)
        {
            if (player == null || !player.Exists() || player.IsDead || Game.IsCutsceneActive)
            {
                return false;
            }

            return !Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS);
        }

        private static Entity ResolveTargetEntity(Ped player)
        {
            Vehicle currentVeh = player.CurrentVehicle;
            if (player.IsInVehicle() && currentVeh != null && currentVeh.Exists())
            {
                return currentVeh;
            }

            return player;
        }

        private static void ApplyNoClipEntityState(Ped player, Entity target)
        {
            target.IsPositionFrozen = true;
            target.IsCollisionEnabled = false;
            target.HasGravity = false;
            target.Velocity = Vector3.Zero;
            target.IsCollisionProof = true;
            target.IsInvincible = true;
            target.IsExplosionProof = true;
            target.IsFireProof = true;
            target.IsBulletProof = true;
            target.IsMeleeProof = true;

            player.IsCollisionProof = true;
            player.IsFireProof = true;
            player.IsExplosionProof = true;
            player.IsBulletProof = true;
            player.IsMeleeProof = true;
            player.IsInvincible = true;
            player.CanRagdoll = false;
            player.DrownsInWater = false;
            player.DiesInstantlyInWater = false;
            player.DrownsInSinkingVehicle = false;
            if (player.IsRagdoll)
            {
                player.CancelRagdoll();
            }

            player.ClearLastWeaponDamage();
            Function.Call(Hash.CLEAR_ENTITY_LAST_DAMAGE_ENTITY, player.Handle);

            if (target.Handle != player.Handle)
            {
                Function.Call(Hash.CLEAR_ENTITY_LAST_DAMAGE_ENTITY, target.Handle);
            }
        }

        private static void ApplyFallShieldState(Ped player, Vehicle fallVehicle, bool preserveCombatProofs)
        {
            player.CanRagdoll = false;
            if (player.IsRagdoll)
            {
                player.CancelRagdoll();
            }
            player.IsCollisionProof = true;
            player.IsFireProof = true;
            player.IsExplosionProof = true;
            player.IsBulletProof = preserveCombatProofs;
            player.IsMeleeProof = false;
            player.IsInvincible = true;
            player.ClearLastWeaponDamage();
            Function.Call(Hash.CLEAR_ENTITY_LAST_DAMAGE_ENTITY, player.Handle);
            if (fallVehicle != null && fallVehicle.Exists())
            {
                fallVehicle.IsCollisionProof = true;
                fallVehicle.IsExplosionProof = true;
                fallVehicle.IsInvincible = true;
            }
        }

        private static void RestorePlayerDefaults(Ped player, bool userGodMode, bool preserveCombatProofs)
        {
            player.IsCollisionProof = userGodMode;
            player.IsFireProof = userGodMode;
            player.IsMeleeProof = userGodMode;
            player.IsBulletProof = userGodMode || preserveCombatProofs;
            player.IsExplosionProof = userGodMode || preserveCombatProofs;
            player.CanRagdoll = !userGodMode;
            player.DrownsInWater = !userGodMode;
            player.DiesInstantlyInWater = false;
            player.DrownsInSinkingVehicle = !userGodMode;
            player.IsInvincible = userGodMode;
        }

        private static void RestoreVehicleDefaults(Vehicle vehicle, bool userVehicleGodMode, bool keepCollisionProof)
        {
            vehicle.IsCollisionProof = keepCollisionProof || userVehicleGodMode;
            vehicle.IsFireProof = userVehicleGodMode;
            vehicle.IsExplosionProof = userVehicleGodMode;
            vehicle.IsBulletProof = userVehicleGodMode;
            vehicle.IsMeleeProof = userVehicleGodMode;
            vehicle.IsInvincible = userVehicleGodMode;
        }

        private static void ReleaseEntityPhysics(Entity entity)
        {
            entity.Velocity = Vector3.Zero;
            entity.IsCollisionEnabled = true;
            entity.HasGravity = true;
            entity.IsPositionFrozen = false;
        }

        private static void DisableInterferingControls()
        {
            for (int i = 0; i < SuppressedControls.Length; i++)
            {
                Controls.DisableControlActionThisFrame(ControlType.PlayerControl, SuppressedControls[i], true);
            }
        }

        private void MoveTargetAlongCamera(Entity target)
        {
            float kbdForward = Controls.GetDisabledControlNormal(ControlType.PlayerControl, ControlAction.MoveUpOnly)
                - Controls.GetDisabledControlNormal(ControlType.PlayerControl, ControlAction.MoveDownOnly);
            float stickForward = -Controls.GetDisabledControlNormal(ControlType.PlayerControl, ControlAction.MoveUpDown);
            float forwardInput = Math.Abs(kbdForward) >= Math.Abs(stickForward) ? kbdForward : stickForward;

            float kbdRight = Controls.GetDisabledControlNormal(ControlType.PlayerControl, ControlAction.MoveRightOnly)
                - Controls.GetDisabledControlNormal(ControlType.PlayerControl, ControlAction.MoveLeftOnly);
            float stickRight = Controls.GetDisabledControlNormal(ControlType.PlayerControl, ControlAction.MoveLeftRight);
            float rightInput = Math.Abs(kbdRight) >= Math.Abs(stickRight) ? kbdRight : stickRight;

            float upInput = 0f;
            if (Controls.IsDisabledControlPressed(ControlType.PlayerControl, ControlAction.Jump)
                || Controls.IsDisabledControlPressed(ControlType.PlayerControl, ControlAction.VehicleHandbrake))
            {
                upInput += 1f;
            }
            if (Controls.IsDisabledControlPressed(ControlType.PlayerControl, ControlAction.Duck)
                || Controls.IsDisabledControlPressed(ControlType.PlayerControl, ControlAction.VehicleDuck))
            {
                upInput -= 1f;
            }

            if (AlignHeadingWithCamera)
            {
                float yaw = GameplayCamera.Rotation.Z;
                if (target is Vehicle)
                {
                    target.Rotation = new Vector3(0f, 0f, yaw);
                }
                else
                {
                    target.Heading = yaw;
                }
            }

            Vector3 forward = GameplayCamera.Direction;
            Vector3 right = GameplayCamera.RightVector;
            Vector3 moveDir = (forward * forwardInput) + (right * rightInput) + (Vector3.WorldUp * upInput);

            if (moveDir.LengthSquared() < 0.0001f)
            {
                return;
            }

            if (moveDir.LengthSquared() > 1.0f)
            {
                moveDir.Normalize();
            }

            float boost = Controls.IsDisabledControlPressed(ControlType.PlayerControl, ControlAction.Sprint) ? 3.0f : 1.0f;
            float frameTime = Math.Max(0.0f, Math.Min(0.1f, Game.LastFrameTime));
            float distance = _baseSpeed * SpeedMultipliers[_speedPresetIndex] * boost * frameTime;
            target.PositionNoOffset = target.Position + (moveDir * distance);
        }

        private static bool LandOnSurfaceBelow(Ped player, Vehicle currentVeh)
        {
            Entity target = currentVeh != null && currentVeh.Exists() ? (Entity)currentVeh : player;
            Vector3 pos = target.Position;
            Function.Call(Hash.REQUEST_COLLISION_AT_COORD, pos.X, pos.Y, pos.Z);

            float groundZ;
            if (!TryProbeGroundHeight(pos, target, out groundZ))
            {
                return false;
            }

            float rawBottom = Math.Abs(target.Model.Dimensions.rearBottomLeft.Z);
            float zOffset = target is Vehicle
                ? Math.Max(0.5f, Math.Min(5.0f, rawBottom))
                : 1.0f;
            target.PositionNoOffset = new Vector3(pos.X, pos.Y, groundZ + zOffset);
            target.Velocity = Vector3.Zero;

            if (target is Vehicle landedVeh && !landedVeh.IsInWater && !landedVeh.Model.IsBoat && !landedVeh.Model.IsSubmarine)
            {
                landedVeh.PlaceOnGround();
            }

            return true;
        }

        private static bool TryProbeGroundHeight(Vector3 pos, Entity ignoreEntity, out float groundZ)
        {
            // First probe local physical collision (Map | Objects) directly below current Z so underground
            // interiors (Casino, Bunker, Facility) never warp upward to street level and IPL decks
            // (USS Luxington, Superyacht, Cargo Ship) hit solid deck geometry rather than ocean water.
            RaycastResult localRay = World.Raycast(
                new Vector3(pos.X, pos.Y, pos.Z + 1.2f),
                new Vector3(pos.X, pos.Y, pos.Z - 90.0f),
                IntersectFlags.Map | IntersectFlags.Objects,
                ignoreEntity
            );
            if (localRay.DidHit)
            {
                groundZ = localRay.HitPosition.Z;
                return true;
            }

            if (World.GetGroundHeight(new Vector3(pos.X, pos.Y, pos.Z + 1.5f), out groundZ, GetGroundHeightMode.Normal))
            {
                return true;
            }

            // Avoid probing from sky altitudes if currently underground inside a sub-zero interior cell.
            if (pos.Z < -15.0f && Function.Call<int>(Hash.GET_INTERIOR_AT_COORDS, pos.X, pos.Y, pos.Z) != 0)
            {
                groundZ = 0f;
                return false;
            }

            float[] probeHeights = { pos.Z + 25.0f, pos.Z * 0.5f, 300.0f, 150.0f, 60.0f, 20.0f, pos.Z + 250.0f, 1000.0f };
            for (int i = 0; i < probeHeights.Length; i++)
            {
                float probeZ = probeHeights[i];
                Function.Call(Hash.REQUEST_COLLISION_AT_COORD, pos.X, pos.Y, probeZ);

                RaycastResult elevatedRay = World.Raycast(
                    new Vector3(pos.X, pos.Y, probeZ),
                    new Vector3(pos.X, pos.Y, -50.0f),
                    IntersectFlags.Map | IntersectFlags.Objects,
                    ignoreEntity
                );
                if (elevatedRay.DidHit)
                {
                    groundZ = elevatedRay.HitPosition.Z;
                    return true;
                }

                OutputArgument outArg = new OutputArgument();
                float queryZ = Math.Max(probeZ, pos.Z + 5.0f);
                if (Function.Call<bool>(Hash.GET_GROUND_Z_FOR_3D_COORD, pos.X, pos.Y, queryZ, outArg, false, false)
                    || Function.Call<bool>(Hash.GET_GROUND_Z_FOR_3D_COORD, pos.X, pos.Y, probeZ, outArg, false, false))
                {
                    groundZ = outArg.GetResult<float>();
                    return true;
                }
            }

            if (World.GetGroundHeight(new Vector3(pos.X, pos.Y, pos.Z + 1.5f), out groundZ, GetGroundHeightMode.ConsiderWaterAsGround)
                || World.GetGroundHeight(new Vector3(pos.X, pos.Y, 1000.0f), out groundZ, GetGroundHeightMode.ConsiderWaterAsGround))
            {
                return true;
            }

            groundZ = 0f;
            return false;
        }
    }
}
