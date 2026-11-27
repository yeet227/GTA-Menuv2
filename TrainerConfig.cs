using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace StoryModeTrainerMod
{
    /// <summary>
    /// Persistent INI configuration loaded alongside the compiled trainer assembly.
    /// </summary>
    internal sealed class TrainerConfig
    {
        private static readonly char[] CommentDelimiters = new[] { ';', '#' };

        public Keys MenuToggleKey { get; set; } = Keys.F3;
        public Keys NoClipToggleKey { get; set; } = Keys.F9;
        public bool DespawnPreviousVehicle { get; set; } = true;
        public bool SpawnMaxUpgraded { get; set; } = false;
        public bool AutoRepairVehicle { get; set; } = false;
        public float NoClipBaseSpeed { get; set; } = 18.0f;
        public bool NoClipExitFallShield { get; set; } = true;
        public bool NoClipSnapToGroundOnExit { get; set; } = false;
        public bool HeistProtectTakeByDefault { get; set; } = false;
        public bool HeistKeepFullTakeWithoutCuts { get; set; } = true;
        public bool PlayerGodMode { get; set; } = false;
        public bool PlayerVehicleGodMode { get; set; } = false;
        public bool PlayerNeverWanted { get; set; } = false;
        public bool PlayerInfiniteAmmo { get; set; } = false;
        public bool PlayerInfiniteClip { get; set; } = false;
        public bool PlayerInfiniteSprint { get; set; } = false;
        public bool PlayerInfiniteSpecialAbility { get; set; } = false;
        public bool PlayerCharacterWheelSwap { get; set; } = true;
        public bool OnlineKeepSpawnedVehiclesPersistent { get; set; } = true;
        public bool OnlineAutoUnlockMpMaps { get; set; } = false;

        /// <summary>
        /// Reads StoryModeTrainer.ini if present, falling back to safe defaults on missing or invalid entries.
        /// </summary>
        public static TrainerConfig Load(string iniPath)
        {
            TrainerConfig config = new TrainerConfig();
            if (string.IsNullOrEmpty(iniPath) || !File.Exists(iniPath))
            {
                return config;
            }

            try
            {
                string section = string.Empty;
                string[] lines = File.ReadAllLines(iniPath);
                for (int i = 0; i < lines.Length; i++)
                {
                    string raw = StripInlineComment(lines[i]).Trim();
                    if (raw.Length == 0)
                    {
                        continue;
                    }

                    if (raw[0] == '[' && raw[raw.Length - 1] == ']')
                    {
                        section = raw.Substring(1, raw.Length - 2).Trim();
                        continue;
                    }

                    int eq = raw.IndexOf('=');
                    if (eq <= 0)
                    {
                        continue;
                    }

                    string key = raw.Substring(0, eq).Trim();
                    string value = raw.Substring(eq + 1).Trim();
                    ApplySetting(config, section, key, value);
                }
            }
            catch
            {
                // Ignore malformed INI reads so trainer initialization never stalls script startup.
            }

            if (config.MenuToggleKey == config.NoClipToggleKey)
            {
                config.NoClipToggleKey = config.MenuToggleKey == Keys.F9 ? Keys.F3 : Keys.F9;
            }

            return config;
        }

        /// <summary>
        /// Persists the current trainer configuration to StoryModeTrainer.ini cleanly.
        /// </summary>
        public bool Save(string iniPath)
        {
            if (string.IsNullOrWhiteSpace(iniPath))
            {
                return false;
            }

            try
            {
                string dir = Path.GetDirectoryName(iniPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                float clampedSpeed = ClampNoClipBaseSpeed(NoClipBaseSpeed);
                NoClipBaseSpeed = clampedSpeed;

                if (MenuToggleKey == Keys.None)
                {
                    MenuToggleKey = Keys.F3;
                }

                if (NoClipToggleKey == Keys.None || NoClipToggleKey == MenuToggleKey)
                {
                    NoClipToggleKey = MenuToggleKey == Keys.F9 ? Keys.F3 : Keys.F9;
                }

                StringBuilder sb = new StringBuilder(1024);
                sb.AppendLine("; StoryModeTrainer configuration (placed in GTA V\\scripts\\StoryModeTrainer.ini)");
                sb.AppendLine();
                sb.AppendLine("[Controls]");
                sb.AppendLine("; Any System.Windows.Forms.Keys enum name (e.g. F3, F5, Insert, F9, NumPad0).");
                sb.AppendLine("; F3 and F9 avoid colliding with SHVDN ConsoleKeyBinding (F4) and GTA character switch keys (F5-F8).");
                sb.AppendLine("MenuToggleKey=" + MenuToggleKey);
                sb.AppendLine("NoClipToggleKey=" + NoClipToggleKey);
                sb.AppendLine();
                sb.AppendLine("[Spawner]");
                sb.AppendLine("; Automatically remove the previously trainer-spawned vehicle when spawning a new one");
                sb.AppendLine("DespawnPreviousVehicle=" + FormatBool(DespawnPreviousVehicle));
                sb.AppendLine("; Automatically install max performance upgrades and turbo on newly spawned vehicles");
                sb.AppendLine("SpawnMaxUpgraded=" + FormatBool(SpawnMaxUpgraded));
                sb.AppendLine("; Automatically repair the currently driven vehicle when damaged");
                sb.AppendLine("AutoRepairVehicle=" + FormatBool(AutoRepairVehicle));
                sb.AppendLine();
                sb.AppendLine("[NoClip]");
                sb.AppendLine("; Base movement speed in meters/second before menu preset & Shift boost (2..200)");
                sb.AppendLine("BaseSpeed=" + clampedSpeed.ToString("0.##", CultureInfo.InvariantCulture));
                sb.AppendLine("; Grant a 3-second impact/ragdoll shield when turning No-Clip off in mid-air");
                sb.AppendLine("ExitFallShield=" + FormatBool(NoClipExitFallShield));
                sb.AppendLine("; Snap directly onto ground below when exiting No-Clip (false lets you stop in interiors/ledges)");
                sb.AppendLine("SnapToGroundOnExit=" + FormatBool(NoClipSnapToGroundOnExit));
                sb.AppendLine();
                sb.AppendLine("[Heists]");
                sb.AppendLine("; Automatically block police bullet/explosion/damage money loss during Story Mode heists on startup");
                sb.AppendLine("ProtectTakeByDefault=" + FormatBool(HeistProtectTakeByDefault));
                sb.AppendLine("; When editing heist take, keep 100% split among protagonists without deducting crew or FBI/Lester cuts");
                sb.AppendLine("KeepFullTakeWithoutCuts=" + FormatBool(HeistKeepFullTakeWithoutCuts));
                sb.AppendLine();
                sb.AppendLine("[Player]");
                sb.AppendLine("; Optional startup defaults for offline Single-Player protections, weapons, stamina, and Character Wheel");
                sb.AppendLine("GodMode=" + FormatBool(PlayerGodMode));
                sb.AppendLine("VehicleGodMode=" + FormatBool(PlayerVehicleGodMode));
                sb.AppendLine("NeverWanted=" + FormatBool(PlayerNeverWanted));
                sb.AppendLine("InfiniteAmmo=" + FormatBool(PlayerInfiniteAmmo));
                sb.AppendLine("InfiniteClip=" + FormatBool(PlayerInfiniteClip));
                sb.AppendLine("InfiniteSprint=" + FormatBool(PlayerInfiniteSprint));
                sb.AppendLine("InfiniteSpecialAbility=" + FormatBool(PlayerInfiniteSpecialAbility));
                sb.AppendLine("CharacterWheelSwap=" + FormatBool(PlayerCharacterWheelSwap));
                sb.AppendLine();
                sb.AppendLine("[OnlineAssets]");
                sb.AppendLine("; Mark trainer-spawned vehicles (including GTA Online DLC models) as mission-persistent offline");
                sb.AppendLine("KeepSpawnedVehiclesPersistent=" + FormatBool(OnlineKeepSpawnedVehiclesPersistent));
                sb.AppendLine("; Enable Single-Player DLC map changeset priority on startup (false keeps default until a DLC map is loaded)");
                sb.AppendLine("AutoUnlockMpMaps=" + FormatBool(OnlineAutoUnlockMpMaps));

                string tempPath = iniPath + ".tmp";
                File.WriteAllText(tempPath, sb.ToString(), new UTF8Encoding(false));
                if (File.Exists(iniPath))
                {
                    File.Replace(tempPath, iniPath, null);
                }
                else
                {
                    File.Move(tempPath, iniPath);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string FormatBool(bool value)
        {
            return value ? "true" : "false";
        }

        private static float ClampNoClipBaseSpeed(float speed)
        {
            if (float.IsNaN(speed) || float.IsInfinity(speed))
            {
                return 18.0f;
            }

            if (speed < 2.0f)
            {
                return 2.0f;
            }

            if (speed > 200.0f)
            {
                return 200.0f;
            }

            return speed;
        }

        private static void ApplySetting(TrainerConfig config, string section, string key, string value)
        {
            if (string.Equals(section, "Controls", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(key, "MenuToggleKey", StringComparison.OrdinalIgnoreCase) && TryParseKey(value, out Keys menuKey))
                {
                    config.MenuToggleKey = menuKey;
                }
                else if (string.Equals(key, "NoClipToggleKey", StringComparison.OrdinalIgnoreCase) && TryParseKey(value, out Keys noClipKey))
                {
                    config.NoClipToggleKey = noClipKey;
                }
                return;
            }

            if (string.Equals(section, "Spawner", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(key, "DespawnPreviousVehicle", StringComparison.OrdinalIgnoreCase) && TryParseBool(value, out bool despawn))
                {
                    config.DespawnPreviousVehicle = despawn;
                }
                else if (string.Equals(key, "SpawnMaxUpgraded", StringComparison.OrdinalIgnoreCase) && TryParseBool(value, out bool maxUpgraded))
                {
                    config.SpawnMaxUpgraded = maxUpgraded;
                }
                else if (string.Equals(key, "AutoRepairVehicle", StringComparison.OrdinalIgnoreCase) && TryParseBool(value, out bool autoRepair))
                {
                    config.AutoRepairVehicle = autoRepair;
                }
                return;
            }

            if (string.Equals(section, "NoClip", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(key, "BaseSpeed", StringComparison.OrdinalIgnoreCase)
                    && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float speed)
                    && !float.IsNaN(speed)
                    && !float.IsInfinity(speed))
                {
                    config.NoClipBaseSpeed = ClampNoClipBaseSpeed(speed);
                }
                else if (string.Equals(key, "ExitFallShield", StringComparison.OrdinalIgnoreCase) && TryParseBool(value, out bool shield))
                {
                    config.NoClipExitFallShield = shield;
                }
                else if (string.Equals(key, "SnapToGroundOnExit", StringComparison.OrdinalIgnoreCase) && TryParseBool(value, out bool snap))
                {
                    config.NoClipSnapToGroundOnExit = snap;
                }
                return;
            }

            if (string.Equals(section, "Heists", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(key, "ProtectTakeByDefault", StringComparison.OrdinalIgnoreCase) && TryParseBool(value, out bool protect))
                {
                    config.HeistProtectTakeByDefault = protect;
                }
                else if (string.Equals(key, "KeepFullTakeWithoutCuts", StringComparison.OrdinalIgnoreCase) && TryParseBool(value, out bool fullTake))
                {
                    config.HeistKeepFullTakeWithoutCuts = fullTake;
                }
                return;
            }

            if (string.Equals(section, "Player", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(key, "GodMode", StringComparison.OrdinalIgnoreCase) && TryParseBool(value, out bool godMode))
                {
                    config.PlayerGodMode = godMode;
                }
                else if (string.Equals(key, "VehicleGodMode", StringComparison.OrdinalIgnoreCase) && TryParseBool(value, out bool vehGodMode))
                {
                    config.PlayerVehicleGodMode = vehGodMode;
                }
                else if (string.Equals(key, "NeverWanted", StringComparison.OrdinalIgnoreCase) && TryParseBool(value, out bool neverWanted))
                {
                    config.PlayerNeverWanted = neverWanted;
                }
                else if (string.Equals(key, "InfiniteAmmo", StringComparison.OrdinalIgnoreCase) && TryParseBool(value, out bool infAmmo))
                {
                    config.PlayerInfiniteAmmo = infAmmo;
                }
                else if (string.Equals(key, "InfiniteClip", StringComparison.OrdinalIgnoreCase) && TryParseBool(value, out bool infClip))
                {
                    config.PlayerInfiniteClip = infClip;
                }
                else if (string.Equals(key, "InfiniteSprint", StringComparison.OrdinalIgnoreCase) && TryParseBool(value, out bool infSprint))
                {
                    config.PlayerInfiniteSprint = infSprint;
                }
                else if (string.Equals(key, "InfiniteSpecialAbility", StringComparison.OrdinalIgnoreCase) && TryParseBool(value, out bool infSpecial))
                {
                    config.PlayerInfiniteSpecialAbility = infSpecial;
                }
                else if (string.Equals(key, "CharacterWheelSwap", StringComparison.OrdinalIgnoreCase) && TryParseBool(value, out bool wheelSwap))
                {
                    config.PlayerCharacterWheelSwap = wheelSwap;
                }
                return;
            }

            if (string.Equals(section, "OnlineAssets", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(key, "KeepSpawnedVehiclesPersistent", StringComparison.OrdinalIgnoreCase) && TryParseBool(value, out bool persistent))
                {
                    config.OnlineKeepSpawnedVehiclesPersistent = persistent;
                }
                else if (string.Equals(key, "AutoUnlockMpMaps", StringComparison.OrdinalIgnoreCase) && TryParseBool(value, out bool autoMpMaps))
                {
                    config.OnlineAutoUnlockMpMaps = autoMpMaps;
                }
            }
        }

        private static bool TryParseBool(string value, out bool result)
        {
            if (bool.TryParse(value, out result))
            {
                return true;
            }

            string trimmed = value != null ? value.Trim() : string.Empty;
            if (trimmed == "1" || string.Equals(trimmed, "yes", StringComparison.OrdinalIgnoreCase) || string.Equals(trimmed, "on", StringComparison.OrdinalIgnoreCase))
            {
                result = true;
                return true;
            }
            if (trimmed == "0" || string.Equals(trimmed, "no", StringComparison.OrdinalIgnoreCase) || string.Equals(trimmed, "off", StringComparison.OrdinalIgnoreCase))
            {
                result = false;
                return true;
            }

            result = false;
            return false;
        }

        private static string StripInlineComment(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return string.Empty;
            }

            int commentIndex = line.IndexOfAny(CommentDelimiters);
            return commentIndex >= 0 ? line.Substring(0, commentIndex) : line;
        }

        private static bool TryParseKey(string value, out Keys parsed)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                parsed = Keys.None;
                return false;
            }

            string trimmed = value.Trim();
            if (trimmed.IndexOf(',') >= 0 || int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                parsed = Keys.None;
                return false;
            }

            if (Enum.TryParse(trimmed, true, out parsed) && Enum.IsDefined(typeof(Keys), parsed))
            {
                Keys keyCode = parsed & Keys.KeyCode;
                if (keyCode != Keys.None
                    && (parsed & Keys.Modifiers) == Keys.None
                    && keyCode != Keys.LButton
                    && keyCode != Keys.RButton
                    && keyCode != Keys.MButton
                    && keyCode != Keys.XButton1
                    && keyCode != Keys.XButton2
                    && keyCode != Keys.Escape
                    && keyCode != Keys.Return
                    && keyCode != Keys.ShiftKey
                    && keyCode != Keys.ControlKey
                    && keyCode != Keys.Menu
                    && keyCode != Keys.LShiftKey
                    && keyCode != Keys.RShiftKey
                    && keyCode != Keys.LControlKey
                    && keyCode != Keys.RControlKey
                    && keyCode != Keys.LMenu
                    && keyCode != Keys.RMenu
                    && keyCode != Keys.LWin
                    && keyCode != Keys.RWin)
                {
                    return true;
                }
            }

            parsed = Keys.None;
            return false;
        }
    }
}
