using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;

namespace StoryModeTrainerMod
{
    /// <summary>
    /// Safely manages GTA Online DLC assets and Single-Player IPL maps (Online vehicles persistence,
    /// Cayo Perico, North Yankton, Diamond Casino &amp; Penthouse, USS Luxington Carrier, Superyacht,
    /// Gunrunning Bunker, Doomsday Facility, Port Cargo Ship, and Story Mission Interiors) strictly
    /// inside offline Story Mode with full mesh &amp; static/interior collision integrity, without
    /// triggering Online transitions, loading screens, network APIs, or MP* stat writes.
    /// </summary>
    internal sealed class OnlineAssetsController
    {
        public static readonly string[] OnlineVehiclePresets =
        {
            "OPPRESSOR2",
            "DELUXO",
            "TOREADOR",
            "SCRAMJET",
            "VIGILANTE",
            "KHANJALI",
            "RAIJU",
            "STRIKEFORCE",
            "AKULA",
            "HYDRA",
            "IGNUS",
            "KRIEGER",
            "EMERUS",
            "DEVESTE",
            "BUFFALO5",
            "TORERO2",
            "SHINOBI",
            "SHOTARO",
            "HAKUCHOU2",
            "ALKONOST",
            "KOSATKA",
            "KURUMA2",
            "INSURGENT"
        };

        public static readonly Vector3 SafeLosSantosReturnCoords = new Vector3(215.76f, -855.25f, 30.73f);
        public static readonly Vector3 DiamondCasinoEntranceCoords = new Vector3(911.0f, 48.0f, 81.1f);
        public static readonly Vector3 DiamondCasinoMainInteriorCoords = new Vector3(1089.76f, 206.01f, -49.00f);
        public static readonly Vector3 DiamondCasinoPenthouseCoords = new Vector3(976.64f, 70.29f, 115.16f);
        public static readonly Vector3 CayoPericoAirstripCoords = new Vector3(4435.0f, -4450.0f, 4.2f);
        public static readonly Vector3 CayoPericoCompoundCoords = new Vector3(4998.0f, -5744.0f, 15.8f);
        public static readonly Vector3 CayoPericoBeachPartyCoords = new Vector3(4920.5f, -4907.2f, 3.4f);
        public static readonly Vector3 AircraftCarrierDeckCoords = new Vector3(3084.73f, -4770.71f, 15.26f);
        public static readonly Vector3 SuperyachtDeckCoords = new Vector3(-2027.95f, -1036.70f, 6.71f);
        public static readonly Vector3 NorthYanktonCoords = new Vector3(5321.20f, -5189.10f, 83.52f);
        public static readonly Vector3 NorthYanktonPrologueRoadCoords = new Vector3(3360.19f, -4849.67f, 111.80f);
        public static readonly Vector3 NorthYanktonBankInteriorCoords = new Vector3(5308.50f, -5222.00f, 83.52f);
        public static readonly Vector3 GunrunningBunkerCoords = new Vector3(892.63f, -3245.87f, -98.26f);
        public static readonly Vector3 DoomsdayFacilityCoords = new Vector3(483.20f, 4810.50f, -58.92f);
        public static readonly Vector3 CargoShipDeckCoords = new Vector3(-90.00f, -2365.80f, 14.30f);
        public static readonly Vector3 FibLobbyCoords = new Vector3(110.40f, -744.20f, 45.75f);
        public static readonly Vector3 CoronerMorgueCoords = new Vector3(240.90f, -1379.20f, 33.74f);
        public static readonly Vector3 LifeinvaderOfficeCoords = new Vector3(-1047.90f, -233.00f, 39.02f);
        public static readonly Vector3 OneilRanchCoords = new Vector3(2441.20f, 4968.50f, 51.70f);
        public static readonly Vector3 CluckinBellFactoryCoords = new Vector3(-72.60f, 6253.70f, 31.09f);

        private static readonly string[] DiamondCasinoIpls =
        {
            "vw_casino_main",
            "vw_casino_penthouse",
            "vw_dlc_casino_apart",
            "vw_casino_carpark",
            "vw_casino_garage",
            "hei_dlc_windows_casino",
            "hei_dlc_windows_casino_lod",
            "vw_dlc_casino_door",
            "vw_dlc_casino_door_lod",
            "hei_dlc_casino_aircon",
            "hei_dlc_casino_aircon_lod",
            "hei_dlc_vw_roofdoors_locked",
            "vw_ch3_additions",
            "vw_ch3_additions_long_0",
            "vw_ch3_additions_strm_0",
            "vw_casino_billboard",
            "vw_casino_billboard_lod",
            "vw_int_placement_vw"
        };

        private static readonly string[] DiamondCasinoConflictingDoorIpls =
        {
            "hei_dlc_casino_door",
            "hei_dlc_casino_door_lod"
        };

        private static readonly string[] PenthouseInteriorSets =
        {
            "set_pent_tint_shell",
            "set_pent_Pattern_01",
            "set_pent_spa_bar_open",
            "set_pent_media_bar_open",
            "set_pent_dealer",
            "set_pent_arcade_retro",
            "set_pent_bar_clutter",
            "set_pent_clutter_01",
            "set_pent_bar_light_01"
        };

        private static readonly string[] AircraftCarrierIpls =
        {
            "hei_carrier",
            "hei_carrier_DistantLights",
            "hei_Carrier_int1",
            "hei_Carrier_int2",
            "hei_Carrier_int3",
            "hei_Carrier_int4",
            "hei_Carrier_int5",
            "hei_Carrier_int6",
            "hei_carrier_LODLights",
            "hei_carrier_slod"
        };

        // Complete Dignity / Heist Yacht IPLs including smboat hull + static deck collision (.ybn) and room LODs.
        private static readonly string[] SuperyachtExteriorIpls =
        {
            "smboat",
            "smboat_lod",
            "smboat_distantlights",
            "smboat_lodlights",
            "hei_yacht_heist",
            "hei_yacht_heist_lod",
            "hei_yacht_heist_slod",
            "hei_yacht_heist_DistantLights",
            "hei_yacht_heist_LODLights",
            "hei_yacht_heist_enginrm",
            "hei_yacht_heist_enginrm_lod",
            "hei_yacht_heist_Lounge",
            "hei_yacht_heist_Lounge_lod",
            "hei_yacht_heist_Bridge",
            "hei_yacht_heist_Bridge_lod",
            "hei_yacht_heist_Bar",
            "hei_yacht_heist_Bar_lod",
            "hei_yacht_heist_Bedrm",
            "hei_yacht_heist_Bedrm_lod"
        };

        private static readonly string[] CayoPericoConflictingIpls =
        {
            "h4_islandairstrip_doorsclosed",
            "h4_islandairstrip_doorsclosed_lod",
            "h4_mansion_gate_broken",
            "h4_mansion_gate_broken_lod",
            "h4_underwater_gate_open"
        };

        private static readonly string[] CayoPericoIpls =
        {
            "h4_islandx",
            "h4_islandx_lod",
            "h4_islandx_props",
            "h4_islandx_props_lod",
            "h4_mph4_island",
            "h4_mph4_island_lod",
            "h4_mph4_island_long_0",
            "h4_mph4_island_strm_0",
            "h4_mph4_island_ne_placement",
            "h4_mph4_island_nw_placement",
            "h4_mph4_island_se_placement",
            "h4_mph4_island_sw_placement",
            "h4_islandx_placement_01",
            "h4_islandx_placement_02",
            "h4_islandx_placement_03",
            "h4_islandx_placement_04",
            "h4_islandx_placement_05",
            "h4_islandx_placement_06",
            "h4_islandx_placement_07",
            "h4_islandx_placement_08",
            "h4_islandx_placement_09",
            "h4_islandx_placement_10",
            "h4_mph4_terrain_01",
            "h4_mph4_terrain_01_long_0",
            "h4_mph4_terrain_02",
            "h4_mph4_terrain_02_strm_0",
            "h4_mph4_terrain_03",
            "h4_mph4_terrain_03_strm_0",
            "h4_mph4_terrain_04",
            "h4_mph4_terrain_04_strm_0",
            "h4_mph4_terrain_05",
            "h4_mph4_terrain_05_strm_0",
            "h4_mph4_terrain_06",
            "h4_mph4_terrain_06_strm_0",
            "h4_mph4_terrain_lod",
            "h4_mph4_terrain_occ_00",
            "h4_mph4_terrain_occ_01",
            "h4_mph4_terrain_occ_02",
            "h4_mph4_terrain_occ_03",
            "h4_mph4_terrain_occ_04",
            "h4_mph4_terrain_occ_05",
            "h4_mph4_terrain_occ_06",
            "h4_mph4_terrain_occ_07",
            "h4_mph4_terrain_occ_08",
            "h4_mph4_terrain_occ_09",
            "h4_mph4_airstrip",
            "h4_mph4_airstrip_interior_0_airstrip_hanger",
            "h4_islandairstrip",
            "h4_islandairstrip_lod",
            "h4_islandairstrip_slod",
            "h4_islandairstrip_props",
            "h4_islandairstrip_props_lod",
            "h4_islandairstrip_props_slod",
            "h4_islandairstrip_propsb",
            "h4_islandairstrip_propsb_lod",
            "h4_islandairstrip_propsb_slod",
            "h4_islandairstrip_hangar_props",
            "h4_islandairstrip_hangar_props_lod",
            "h4_islandairstrip_hangar_props_slod",
            "h4_islandairstrip_doorsopen",
            "h4_islandairstrip_doorsopen_lod",
            "h4_mph4_mansion",
            "h4_mph4_mansion_strm_0",
            "h4_mph4_mansion_b",
            "h4_mph4_mansion_b_strm_0",
            "h4_islandx_mansion",
            "h4_islandx_mansion_lod",
            "h4_islandx_mansion_slod",
            "h4_islandx_mansion_props",
            "h4_islandx_mansion_props_lod",
            "h4_islandx_mansion_props_slod",
            "h4_mph4_dock",
            "h4_islandxdock",
            "h4_islandxdock_lod",
            "h4_islandxdock_slod",
            "h4_islandxdock_props",
            "h4_islandxdock_props_lod",
            "h4_islandxdock_props_slod",
            "h4_islandxdock_props_2",
            "h4_islandxdock_props_2_lod",
            "h4_islandxdock_props_2_slod",
            "h4_islandxdock_water_hatch",
            "h4_islandx_maindock",
            "h4_islandx_maindock_lod",
            "h4_islandx_maindock_slod",
            "h4_islandx_maindock_props",
            "h4_islandx_maindock_props_lod",
            "h4_islandx_maindock_props_slod",
            "h4_islandx_maindock_props_2",
            "h4_islandx_maindock_props_2_lod",
            "h4_islandx_maindock_props_2_slod",
            "h4_islandxtower",
            "h4_islandxtower_lod",
            "h4_islandxtower_slod",
            "h4_islandxtower_veg",
            "h4_islandxtower_veg_lod",
            "h4_islandxtower_veg_slod",
            "h4_islandx_barrack_props",
            "h4_islandx_barrack_props_lod",
            "h4_islandx_barrack_props_slod",
            "h4_islandx_barrack_hatch",
            "h4_islandx_checkpoint",
            "h4_islandx_checkpoint_lod",
            "h4_islandx_checkpoint_props",
            "h4_islandx_checkpoint_props_lod",
            "h4_islandx_checkpoint_props_slod",
            "h4_islandxcanal_props",
            "h4_islandxcanal_props_lod",
            "h4_islandxcanal_props_slod",
            "h4_mph4_beach",
            "h4_beach",
            "h4_beach_lod",
            "h4_beach_slod",
            "h4_beach_props",
            "h4_beach_props_lod",
            "h4_beach_props_slod",
            "h4_beach_bar_props",
            "h4_beach_party",
            "h4_beach_party_lod",
            "h4_beach_props_party",
            "h4_islandx_Mansion_Office",
            "h4_islandx_Mansion_Office_lod",
            "h4_islandx_Mansion_LockUp_01",
            "h4_islandx_Mansion_LockUp_01_lod",
            "h4_islandx_Mansion_LockUp_02",
            "h4_islandx_Mansion_LockUp_02_lod",
            "h4_islandx_Mansion_LockUp_03",
            "h4_islandx_Mansion_LockUp_03_lod",
            "h4_IslandX_Mansion_Vault",
            "h4_IslandX_Mansion_Vault_lod",
            "h4_IslandX_Mansion_B",
            "h4_IslandX_Mansion_B_lod",
            "h4_IslandX_Mansion_B_slod",
            "h4_IslandX_Mansion_GuardFence",
            "h4_IslandX_Mansion_Entrance_Fence",
            "h4_IslandX_Mansion_B_Side_Fence",
            "h4_IslandX_Mansion_Lights",
            "h4_mansion_gate_closed",
            "h4_mansion_remains_cage",
            "h4_Underwater_Gate_Closed",
            "h4_aa_guns",
            "h4_aa_guns_lod",
            "h4_islandx_terrain_01", "h4_islandx_terrain_01_lod", "h4_islandx_terrain_01_slod",
            "h4_islandx_terrain_02", "h4_islandx_terrain_02_lod", "h4_islandx_terrain_02_slod",
            "h4_islandx_terrain_03", "h4_islandx_terrain_03_lod", "h4_islandx_terrain_03_slod",
            "h4_islandx_terrain_04", "h4_islandx_terrain_04_lod", "h4_islandx_terrain_04_slod",
            "h4_islandx_terrain_05", "h4_islandx_terrain_05_lod", "h4_islandx_terrain_05_slod",
            "h4_islandx_terrain_06", "h4_islandx_terrain_06_lod", "h4_islandx_terrain_06_slod",
            "h4_islandX_Terrain_props_05_a", "h4_islandX_Terrain_props_05_a_lod", "h4_islandX_Terrain_props_05_a_slod",
            "h4_islandX_Terrain_props_05_b", "h4_islandX_Terrain_props_05_b_lod", "h4_islandX_Terrain_props_05_b_slod",
            "h4_islandX_Terrain_props_05_c", "h4_islandX_Terrain_props_05_c_lod", "h4_islandX_Terrain_props_05_c_slod",
            "h4_islandX_Terrain_props_05_d", "h4_islandX_Terrain_props_05_d_lod", "h4_islandX_Terrain_props_05_d_slod",
            "h4_islandX_Terrain_props_05_e", "h4_islandX_Terrain_props_05_e_lod", "h4_islandX_Terrain_props_05_e_slod",
            "h4_islandX_Terrain_props_05_f", "h4_islandX_Terrain_props_05_f_lod", "h4_islandX_Terrain_props_05_f_slod",
            "h4_islandX_Terrain_props_06_a", "h4_islandX_Terrain_props_06_a_lod", "h4_islandX_Terrain_props_06_a_slod",
            "h4_islandX_Terrain_props_06_b", "h4_islandX_Terrain_props_06_b_lod", "h4_islandX_Terrain_props_06_b_slod",
            "h4_islandX_Terrain_props_06_c", "h4_islandX_Terrain_props_06_c_lod", "h4_islandX_Terrain_props_06_c_slod",
            "h4_mph4_terrain_01_grass_0", "h4_mph4_terrain_01_grass_1",
            "h4_mph4_terrain_02_grass_0", "h4_mph4_terrain_02_grass_1",
            "h4_mph4_terrain_02_grass_2", "h4_mph4_terrain_02_grass_3",
            "h4_mph4_terrain_04_grass_0", "h4_mph4_terrain_04_grass_1",
            "h4_mph4_terrain_05_grass_0", "h4_mph4_terrain_06_grass_0",
            "h4_mph4_wtowers",
            "h4_nw_ipl_00", "h4_nw_ipl_01", "h4_nw_ipl_02", "h4_nw_ipl_03",
            "h4_nw_ipl_04", "h4_nw_ipl_05", "h4_nw_ipl_06", "h4_nw_ipl_07",
            "h4_nw_ipl_08", "h4_nw_ipl_09",
            "h4_ne_ipl_00", "h4_ne_ipl_01", "h4_ne_ipl_02", "h4_ne_ipl_03",
            "h4_ne_ipl_04", "h4_ne_ipl_05", "h4_ne_ipl_06", "h4_ne_ipl_07",
            "h4_ne_ipl_08", "h4_ne_ipl_09",
            "h4_sw_ipl_00", "h4_sw_ipl_01", "h4_sw_ipl_02", "h4_sw_ipl_03",
            "h4_sw_ipl_04", "h4_sw_ipl_05", "h4_sw_ipl_06", "h4_sw_ipl_07",
            "h4_sw_ipl_08", "h4_sw_ipl_09",
            "h4_se_ipl_00", "h4_se_ipl_01", "h4_se_ipl_02", "h4_se_ipl_03",
            "h4_se_ipl_04", "h4_se_ipl_05", "h4_se_ipl_06", "h4_se_ipl_07",
            "h4_se_ipl_08", "h4_se_ipl_09"
        };

        private static readonly string[] NorthYanktonConflictingIpls =
        {
            "prologue03_grv_dug",
            "prologue03_grv_dug_lod",
            "prologue03_grv_fun",
            "prologue03_grv_fun_lod"
        };

        private static readonly string[] NorthYanktonIpls =
        {
            "plg_01",
            "prologue01", "prologue01_lod",
            "prologue01c", "prologue01c_lod",
            "prologue01d", "prologue01d_lod",
            "prologue01e", "prologue01e_lod",
            "prologue01f", "prologue01f_lod",
            "prologue01g", "prologue01g_lod",
            "prologue01h", "prologue01h_lod",
            "prologue01i", "prologue01i_lod",
            "prologue01j", "prologue01j_lod",
            "prologue01k", "prologue01k_lod",
            "prologue01z", "prologue01z_lod",
            "plg_02",
            "prologue02", "prologue02_lod",
            "plg_03",
            "prologue03", "prologue03_lod",
            "prologue03b", "prologue03b_lod",
            "prologue03_grv_cov", "prologue03_grv_cov_lod",
            "prologue_grv_torch",
            "plg_04",
            "prologue04", "prologue04_lod",
            "prologue04b", "prologue04b_lod",
            "prologue04_cover", "prologue04_cover_lod",
            "plg_05",
            "prologue05", "prologue05_lod",
            "prologue05b", "prologue05b_lod",
            "plg_06",
            "prologue06", "prologue06_lod",
            "prologue06b", "prologue06b_lod",
            "prologue06_int", "prologue06_int_lod",
            "prologue06_pannel", "prologue06_pannel_lod",
            "prologue_occl", "plg_occl_00",
            "prologuerd", "prologuerd_lod",
            "prologuerdb",
            "prologue_DistantLights", "prologue_LODLights",
            "prologue_m2_door", "prologue_m2_door_lod",
            "des_protree_start", "des_protree_end"
        };

        private static readonly string[] GunrunningBunkerIpls =
        {
            "gr_entrance_placement",
            "gr_grdlc_interior_placement",
            "gr_grdlc_interior_placement_interior_0_grdlc_int_01_milo_",
            "gr_grdlc_interior_placement_interior_1_grdlc_int_02_milo_"
        };

        private static readonly string[] GunrunningBunkerEntitySets =
        {
            "bunker_style_a",
            "upgrade_bunker_set",
            "security_upgrade",
            "Office_Upgrade_set",
            "gun_range_lights",
            "gun_locker_upgrade",
            "gun_schematic_set"
        };

        private static readonly string[] DoomsdayFacilityIpls =
        {
            "xm_x17dlc_int_placement",
            "xm_x17dlc_int_placement_interior_4_x17dlc_int_facility_milo_",
            "xm_x17dlc_int_placement_interior_33_x17dlc_int_02_milo_"
        };

        private static readonly string[] DoomsdayFacilityEntitySets =
        {
            "set_int_02_shell",
            "set_int_02_lounge1",
            "set_int_02_decal_01",
            "set_int_02_cannon",
            "set_int_02_clutter1",
            "set_int_02_crewemblem",
            "set_int_02_security",
            "set_int_02_sleep"
        };

        private static readonly string[] CargoShipIpls =
        {
            "cargoship"
        };

        private static readonly string[] CargoShipConflictingIpls =
        {
            "sunkcargoship"
        };

        private static readonly string[] StoryInteriorsIpls =
        {
            "FIBlobby",
            "coronertrash",
            "Coroner_Int_on",
            "facelobby",
            "CS1_02_cf_onmission1",
            "CS1_02_cf_onmission2",
            "CS1_02_cf_onmission3",
            "CS1_02_cf_onmission4",
            "farm",
            "farm_props",
            "farmint"
        };

        private static readonly string[] StoryInteriorsConflictingIpls =
        {
            "FIBlobbyfake",
            "Coroner_Int_off",
            "facelobbyfake",
            "CS1_02_cf_offmission",
            "farm_burnt",
            "farm_burnt_lod",
            "farm_burnt_props",
            "farmint_cap",
            "farmint_cap_lod"
        };

        private static readonly string[] StoryInteriorsDefaultProxies =
        {
            "FIBlobbyfake",
            "Coroner_Int_off",
            "facelobbyfake",
            "CS1_02_cf_offmission"
        };

        private static readonly string[] BurntFarmIpls =
        {
            "farm_burnt",
            "farm_burnt_lod",
            "farm_burnt_props",
            "farmint_cap",
            "farmint_cap_lod"
        };

        private static readonly string[] IntactFarmIpls =
        {
            "farm",
            "farm_props",
            "farmint"
        };

        private const int ShopControllerCheckIntervalMs = 1500;
        private const int MaintenanceCheckIntervalMs = 1000;
        private const int CayoFakeIslandRadarHash = unchecked((int)0xC0A90510);
        private const int InstanceHintDriving = 2;
        private static readonly Vector2 CayoPericoCenter2D = new Vector2(5031.4f, -5150.9f);
        private const float CayoPathNodeResetSafeRadiusSq = 2500.0f * 2500.0f;

        private readonly Func<bool> _isMultiplayerBlocked;
        private readonly List<int> _pinnedInteriors = new List<int>();
        private int _casinoInteriorId;
        private int _penthouseInteriorId;
        private int _yachtInteriorId;
        private int _carrierInteriorId;
        private int _yanktonBankInteriorId;
        private int _bunkerInteriorId;
        private int _facilityInteriorId;
        private int _fibLobbyInteriorId;
        private int _coronerInteriorId;
        private int _lifeinvaderInteriorId;
        private int _ranchInteriorId;
        private int _shopControllerScriptHash;
        private int _nextShopCheckGameTime;
        private int _nextMaintenanceGameTime;
        private bool _pendingStartupMpMapsUnlock;
        private bool _cayoZoneEnvironmentActive;
        private bool _cayoPathNodesActive;
        private bool _northYanktonZoneEnvironmentActive;
        private bool _worldBoundaryExtended;
        private bool _wasSunkCargoShipActiveBeforeLoad;
        private bool _wasFarmBurntBeforeLoad;

        public OnlineAssetsController(
            bool keepVehiclesPersistent,
            bool autoUnlockMpMaps,
            Func<bool> isMultiplayerBlocked)
        {
            KeepSpawnedVehiclesPersistent = keepVehiclesPersistent;
            _isMultiplayerBlocked = isMultiplayerBlocked ?? (() => false);
            ExplicitMpMapsPreference = autoUnlockMpMaps;
            _pendingStartupMpMapsUnlock = autoUnlockMpMaps;
        }

        public bool KeepSpawnedVehiclesPersistent { get; set; }
        public bool SuspendShopControllerDespawn { get; set; }
        public bool MpMapsModeEnabled { get; private set; }
        public bool ExplicitMpMapsPreference { get; private set; }
        public bool DiamondCasinoLoaded { get; private set; }
        public bool AircraftCarrierLoaded { get; private set; }
        public bool SuperyachtLoaded { get; private set; }
        public bool CayoPericoLoaded { get; private set; }
        public bool NorthYanktonLoaded { get; private set; }
        public bool GunrunningBunkerLoaded { get; private set; }
        public bool DoomsdayFacilityLoaded { get; private set; }
        public bool CargoShipLoaded { get; private set; }
        public bool StoryInteriorsLoaded { get; private set; }

        public bool HasAnyMpChangesetMapLoaded =>
            DiamondCasinoLoaded
            || AircraftCarrierLoaded
            || SuperyachtLoaded
            || GunrunningBunkerLoaded
            || DoomsdayFacilityLoaded;

        public bool HasAnyOffMapAssetLoaded =>
            CayoPericoLoaded
            || NorthYanktonLoaded
            || AircraftCarrierLoaded;

        public bool HasAnyMapLoaded =>
            HasAnyMpChangesetMapLoaded
            || CayoPericoLoaded
            || NorthYanktonLoaded
            || CargoShipLoaded
            || StoryInteriorsLoaded;

        /// <summary>
        /// Hysteresis-aware Cayo Perico zone check cleanly separated east of USS Luxington (X = 3084.73)
        /// so Carrier and Los Santos meshes/collisions are never affected outside Cayo Perico waters.
        /// </summary>
        public static bool IsCoordInCayoPericoZone(Vector3 p, bool useKeepHysteresis = false)
        {
            if (useKeepHysteresis)
            {
                return p.X >= 3240.0f && p.X <= 6450.0f && p.Y <= -3600.0f && p.Y >= -6850.0f;
            }

            return p.X >= 3380.0f && p.X <= 6250.0f && p.Y <= -3800.0f && p.Y >= -6650.0f;
        }

        public static bool IsCoordInNorthYanktonZone(Vector3 p)
        {
            return p.X >= 2500.0f && p.X <= 6500.0f && p.Y <= -3600.0f && p.Y >= -6800.0f && p.Z > 30.0f;
        }

        public bool IsPlayerInCayoPericoZone(Ped player)
        {
            if (player == null || !player.Exists()) return false;
            return IsCoordInCayoPericoZone(player.Position, useKeepHysteresis: _cayoZoneEnvironmentActive);
        }

        public bool IsPlayerInNorthYanktonZone(Ped player)
        {
            if (player == null || !player.Exists()) return false;
            return IsCoordInNorthYanktonZone(player.Position);
        }

        public bool IsPlayerInAircraftCarrierZone(Ped player)
        {
            if (player == null || !player.Exists()) return false;
            Vector3 p = player.Position;
            return (p - AircraftCarrierDeckCoords).LengthSquared() < 160000.0f && p.Z < 60.0f;
        }

        public bool IsPlayerOnSuperyacht(Ped player)
        {
            if (player == null || !player.Exists()) return false;
            Vector3 p = player.Position;
            return (p - SuperyachtDeckCoords).LengthSquared() < 22500.0f;
        }

        public bool IsPlayerOnCargoShip(Ped player)
        {
            if (player == null || !player.Exists()) return false;
            Vector3 p = player.Position;
            return (p - CargoShipDeckCoords).LengthSquared() < 40000.0f;
        }

        public bool IsPlayerInsideLoadedCasino(Ped player)
        {
            if (player == null || !player.Exists()) return false;
            Vector3 p = player.Position;
            return (p - DiamondCasinoMainInteriorCoords).LengthSquared() < 62500.0f
                || ((p - DiamondCasinoPenthouseCoords).LengthSquared() < 10000.0f && p.Z > 105.0f);
        }

        public bool IsPlayerInsideLoadedBunker(Ped player)
        {
            if (player == null || !player.Exists()) return false;
            Vector3 p = player.Position;
            return (p - GunrunningBunkerCoords).LengthSquared() < 40000.0f && p.Z < -70.0f;
        }

        public bool IsPlayerInsideLoadedFacility(Ped player)
        {
            if (player == null || !player.Exists()) return false;
            Vector3 p = player.Position;
            return (p - DoomsdayFacilityCoords).LengthSquared() < 62500.0f && p.Z < -35.0f;
        }

        public bool IsPlayerInsideLoadedStoryInterior(Ped player)
        {
            if (player == null || !player.Exists()) return false;
            Vector3 p = player.Position;
            return (p - FibLobbyCoords).LengthSquared() < 3600.0f
                || (p - CoronerMorgueCoords).LengthSquared() < 3600.0f
                || (p - LifeinvaderOfficeCoords).LengthSquared() < 3600.0f
                || (p - OneilRanchCoords).LengthSquared() < 2500.0f
                || (p - CluckinBellFactoryCoords).LengthSquared() < 6400.0f;
        }

        public bool IsPlayerInAnyLoadedDlcMap(Ped player)
        {
            if (player == null || !player.Exists()) return false;
            return (CayoPericoLoaded && IsPlayerInCayoPericoZone(player))
                || (NorthYanktonLoaded && IsPlayerInNorthYanktonZone(player))
                || (AircraftCarrierLoaded && IsPlayerInAircraftCarrierZone(player))
                || (DiamondCasinoLoaded && IsPlayerInsideLoadedCasino(player))
                || (SuperyachtLoaded && IsPlayerOnSuperyacht(player))
                || (GunrunningBunkerLoaded && IsPlayerInsideLoadedBunker(player))
                || (DoomsdayFacilityLoaded && IsPlayerInsideLoadedFacility(player))
                || (CargoShipLoaded && IsPlayerOnCargoShip(player))
                || (StoryInteriorsLoaded && IsPlayerInsideLoadedStoryInterior(player));
        }

        /// <summary>
        /// Synchronizes Cayo Perico (HeistIsland static collision hopper + water + path nodes + IPLs)
        /// and North Yankton (prologue nodes + minimap) to match a target teleport destination BEFORE
        /// starting a scene load. Handles both entering off-map islands AND returning to Los Santos
        /// (immediately releasing HeistIsland while the entity is frozen so Los Santos .ybn collisions stream).
        /// Returns true if any zone hopper state changed and the caller should yield frames before probing.
        /// </summary>
        public bool PrepareZoneEnvironmentForCoords(Vector3 destination)
        {
            if (_isMultiplayerBlocked()) return false;

            bool changed = false;

            if (CayoPericoLoaded)
            {
                bool destInCayo = IsCoordInCayoPericoZone(destination, useKeepHysteresis: false);
                if (destInCayo)
                {
                    SyncWorldBoundaryState();
                    if (!_cayoZoneEnvironmentActive) changed = true;
                    SyncCayoPericoZoneEnvironment(true, force: true);
                }
                else if (_cayoZoneEnvironmentActive)
                {
                    changed = true;
                    SyncCayoPericoZoneEnvironment(false, force: true);
                }
            }

            if (NorthYanktonLoaded)
            {
                bool destInYankton = IsCoordInNorthYanktonZone(destination);
                if (destInYankton)
                {
                    SyncWorldBoundaryState();
                    if (!_northYanktonZoneEnvironmentActive) changed = true;
                    SyncNorthYanktonZoneEnvironment(true, force: true);
                }
                else if (_northYanktonZoneEnvironmentActive)
                {
                    changed = true;
                    SyncNorthYanktonZoneEnvironment(false, force: true);
                }
            }

            if (AircraftCarrierLoaded && (destination - AircraftCarrierDeckCoords).LengthSquared() < 160000.0f)
            {
                SyncWorldBoundaryState();
            }

            return changed;
        }

        /// <summary>
        /// Mounts or unmounts the local DLC map changeset group (ON_ENTER_MP + SET_INSTANCE_PRIORITY_HINT)
        /// in Single-Player without flipping the engine loading-screen mode flag to GTA Online or forcing
        /// a blocking map-reload screen.
        /// </summary>
        public bool SetMpMapsMode(bool enable, bool explicitUserToggle = false)
        {
            if (_isMultiplayerBlocked()) return false;

            if (explicitUserToggle)
            {
                ExplicitMpMapsPreference = enable;
                if (!enable)
                {
                    _pendingStartupMpMapsUnlock = false;
                }
            }

            if (enable)
            {
                if (!MpMapsModeEnabled)
                {
                    Function.Call(Hash.ON_ENTER_MP);
                    Function.Call(Hash.SET_INSTANCE_PRIORITY_HINT, InstanceHintDriving);
                    MpMapsModeEnabled = true;
                }
                return true;
            }

            if (MpMapsModeEnabled)
            {
                Function.Call(Hash.SET_INSTANCE_PRIORITY_HINT, 0);
                Function.Call(Hash.ON_ENTER_SP);
                MpMapsModeEnabled = false;
            }
            return true;
        }

        public void SetDiamondCasinoEnabled(bool enable)
        {
            if (_isMultiplayerBlocked()) return;

            if (enable)
            {
                SetMpMapsMode(true);
                RemoveIplBatch(DiamondCasinoConflictingDoorIpls);
                RequestIplBatch(DiamondCasinoIpls);
                DiamondCasinoLoaded = true;
                ConfigureLoadedMapInteriors(forceRefresh: true);
            }
            else
            {
                UnpinInteriorId(ref _casinoInteriorId);
                UnpinInteriorId(ref _penthouseInteriorId);
                RemoveIplBatch(DiamondCasinoIpls);
                RemoveIplBatch(DiamondCasinoConflictingDoorIpls);
                DiamondCasinoLoaded = false;
                ResetMpMapsModeIfNoMpMapsLoaded();
            }
        }

        public void SetAircraftCarrierEnabled(bool enable)
        {
            if (_isMultiplayerBlocked()) return;

            if (enable)
            {
                AircraftCarrierLoaded = true;
                if (NorthYanktonLoaded)
                {
                    SetNorthYanktonEnabled(false);
                }
                SetMpMapsMode(true);
                RequestIplBatch(AircraftCarrierIpls);
                SyncWorldBoundaryState();
                ConfigureLoadedMapInteriors(forceRefresh: true);
            }
            else
            {
                UnpinInteriorId(ref _carrierInteriorId);
                RemoveIplBatch(AircraftCarrierIpls);
                AircraftCarrierLoaded = false;
                SyncWorldBoundaryState();
                ResetMpMapsModeIfNoMpMapsLoaded();
            }
        }

        public void SetSuperyachtEnabled(bool enable)
        {
            if (_isMultiplayerBlocked()) return;

            if (enable)
            {
                SetMpMapsMode(true);
                RequestIplBatch(SuperyachtExteriorIpls);
                SuperyachtLoaded = true;
                ConfigureLoadedMapInteriors(forceRefresh: true);
            }
            else
            {
                UnpinInteriorId(ref _yachtInteriorId);
                RemoveIplBatch(SuperyachtExteriorIpls);
                SuperyachtLoaded = false;
                ResetMpMapsModeIfNoMpMapsLoaded();
            }
        }

        public void SetCayoPericoEnabled(bool enable)
        {
            if (_isMultiplayerBlocked()) return;

            if (enable)
            {
                CayoPericoLoaded = true;
                if (NorthYanktonLoaded)
                {
                    SetNorthYanktonEnabled(false);
                }

                // Cayo Perico streams via CIslandHopper (SET_ISLAND_ENABLED) + REQUEST_IPL strictly offline
                // without calling ON_ENTER_MP, keeping Los Santos intact out of zone while mounting full
                // CCS_MPHEIST4_ISLAND static .ybn collision bounds on arrival.
                SyncWorldBoundaryState();
                bool inZoneNow = IsPlayerInCayoPericoZone(Game.Player?.Character);
                SyncCayoPericoZoneEnvironment(inZoneNow, force: true);
            }
            else
            {
                SyncCayoPericoZoneEnvironment(false, force: true);
                RemoveIplBatch(CayoPericoIpls);
                RemoveIplBatch(CayoPericoConflictingIpls);
                CayoPericoLoaded = false;
                SyncWorldBoundaryState();
            }
        }

        public void SetNorthYanktonEnabled(bool enable)
        {
            if (_isMultiplayerBlocked()) return;

            if (enable)
            {
                NorthYanktonLoaded = true;
                if (CayoPericoLoaded)
                {
                    SetCayoPericoEnabled(false);
                }
                if (AircraftCarrierLoaded)
                {
                    SetAircraftCarrierEnabled(false);
                }

                RemoveIplBatch(NorthYanktonConflictingIpls);
                RequestIplBatch(NorthYanktonIpls);
                SyncWorldBoundaryState();
                SyncNorthYanktonZoneEnvironment(IsPlayerInNorthYanktonZone(Game.Player?.Character), force: true);
                ConfigureLoadedMapInteriors(forceRefresh: true);
            }
            else
            {
                UnpinInteriorId(ref _yanktonBankInteriorId);
                SyncNorthYanktonZoneEnvironment(false, force: true);
                RemoveIplBatch(NorthYanktonIpls);
                RemoveIplBatch(NorthYanktonConflictingIpls);
                NorthYanktonLoaded = false;
                SyncWorldBoundaryState();
            }
        }

        public void SetGunrunningBunkerEnabled(bool enable)
        {
            if (_isMultiplayerBlocked()) return;

            if (enable)
            {
                SetMpMapsMode(true);
                RequestIplBatch(GunrunningBunkerIpls);
                GunrunningBunkerLoaded = true;
                ConfigureLoadedMapInteriors(forceRefresh: true);
            }
            else
            {
                UnpinInteriorId(ref _bunkerInteriorId);
                RemoveIplBatch(GunrunningBunkerIpls);
                GunrunningBunkerLoaded = false;
                ResetMpMapsModeIfNoMpMapsLoaded();
            }
        }

        public void SetDoomsdayFacilityEnabled(bool enable)
        {
            if (_isMultiplayerBlocked()) return;

            if (enable)
            {
                SetMpMapsMode(true);
                RequestIplBatch(DoomsdayFacilityIpls);
                DoomsdayFacilityLoaded = true;
                ConfigureLoadedMapInteriors(forceRefresh: true);
            }
            else
            {
                UnpinInteriorId(ref _facilityInteriorId);
                RemoveIplBatch(DoomsdayFacilityIpls);
                DoomsdayFacilityLoaded = false;
                ResetMpMapsModeIfNoMpMapsLoaded();
            }
        }

        public void SetCargoShipEnabled(bool enable)
        {
            if (_isMultiplayerBlocked()) return;

            if (enable)
            {
                if (!CargoShipLoaded)
                {
                    _wasSunkCargoShipActiveBeforeLoad = Function.Call<bool>(Hash.IS_IPL_ACTIVE, "sunkcargoship");
                }
                RemoveIplBatch(CargoShipConflictingIpls);
                RequestIplBatch(CargoShipIpls);
                CargoShipLoaded = true;
            }
            else
            {
                RemoveIplBatch(CargoShipIpls);
                if (_wasSunkCargoShipActiveBeforeLoad)
                {
                    RequestIplBatch(CargoShipConflictingIpls);
                    _wasSunkCargoShipActiveBeforeLoad = false;
                }
                CargoShipLoaded = false;
            }
        }

        public void SetStoryInteriorsEnabled(bool enable)
        {
            if (_isMultiplayerBlocked()) return;

            if (enable)
            {
                if (!StoryInteriorsLoaded)
                {
                    _wasFarmBurntBeforeLoad = Function.Call<bool>(Hash.IS_IPL_ACTIVE, "farm_burnt");
                }
                RemoveIplBatch(StoryInteriorsConflictingIpls);
                RequestIplBatch(StoryInteriorsIpls);
                StoryInteriorsLoaded = true;
                ConfigureLoadedMapInteriors(forceRefresh: true);
            }
            else
            {
                UnpinInteriorId(ref _fibLobbyInteriorId);
                UnpinInteriorId(ref _coronerInteriorId);
                UnpinInteriorId(ref _lifeinvaderInteriorId);
                UnpinInteriorId(ref _ranchInteriorId);

                RemoveIplBatch(StoryInteriorsIpls);
                RequestIplBatch(StoryInteriorsDefaultProxies);

                if (_wasFarmBurntBeforeLoad)
                {
                    RequestIplBatch(BurntFarmIpls);
                    _wasFarmBurntBeforeLoad = false;
                }
                else
                {
                    RequestIplBatch(IntactFarmIpls);
                }

                StoryInteriorsLoaded = false;
            }
        }

        /// <summary>
        /// Applies safe offline entity persistence flags to trainer-spawned vehicles so Online DLC vehicles
        /// remain usable in Story Mode without altering disk files or writing Online stats.
        /// </summary>
        public void ApplyOfflineVehiclePersistence(Vehicle vehicle)
        {
            if (_isMultiplayerBlocked() || !KeepSpawnedVehiclesPersistent || vehicle == null || !vehicle.Exists())
            {
                return;
            }

            vehicle.IsPersistent = true;
            Function.Call(Hash.SET_VEHICLE_HAS_BEEN_OWNED_BY_PLAYER, vehicle.Handle, true);
            Function.Call(Hash.SET_VEHICLE_IS_STOLEN, vehicle.Handle, false);

            if (SuspendShopControllerDespawn)
            {
                TrySuspendShopControllerDespawn();
            }
        }

        public void Tick()
        {
            if (_isMultiplayerBlocked()) return;

            Ped player = Game.Player?.Character;
            bool playerReady = player != null && player.Exists();

            bool loadingScreenActive = Function.Call<bool>(Hash.GET_IS_LOADING_SCREEN_ACTIVE);

            if (_pendingStartupMpMapsUnlock && !loadingScreenActive && playerReady && !Game.IsMissionActive)
            {
                _pendingStartupMpMapsUnlock = false;
                SetMpMapsMode(true);
            }

            if (CayoPericoLoaded)
            {
                bool inCayoZone = IsPlayerInCayoPericoZone(player);
                SyncCayoPericoZoneEnvironment(inCayoZone, force: false);
                if (inCayoZone && playerReady)
                {
                    // Assert SET_USE_ISLAND_MAP every frame while on Cayo Perico so the island radar stays active.
                    Function.Call(Hash.SET_USE_ISLAND_MAP, true);

                    // Render exterior island radar only when not inside an actual compound/hangar MLO interior.
                    if (Function.Call<int>(Hash.GET_INTERIOR_FROM_ENTITY, player.Handle) == 0)
                    {
                        Function.Call(Hash.SET_RADAR_AS_EXTERIOR_THIS_FRAME);
                        Function.Call(Hash.SET_RADAR_AS_INTERIOR_THIS_FRAME, CayoFakeIslandRadarHash, 4700.0f, -5145.0f, 0, 0);
                    }
                }
            }

            if (_cayoPathNodesActive && !_cayoZoneEnvironmentActive && IsPlayerSafelyAwayFromCayoPerico(player))
            {
                Function.Call(Hash.SET_ALLOW_STREAM_HEIST_ISLAND_NODES, 0);
                _cayoPathNodesActive = false;
            }

            if (NorthYanktonLoaded)
            {
                bool inYanktonZone = IsPlayerInNorthYanktonZone(player);
                SyncNorthYanktonZoneEnvironment(inYanktonZone, force: false);
            }

            if (_worldBoundaryExtended && HasAnyOffMapAssetLoaded)
            {
                ExtendOffMapBoundariesThisFrame();
            }

            int now = Game.GameTime;
            if (!loadingScreenActive && (HasAnyMapLoaded || MpMapsModeEnabled))
            {
                if (now >= _nextMaintenanceGameTime || (_nextMaintenanceGameTime - now) > (MaintenanceCheckIntervalMs * 2))
                {
                    _nextMaintenanceGameTime = now + MaintenanceCheckIntervalMs;
                    MaintainLoadedMapsAndInteriors();
                }
            }

            if (SuspendShopControllerDespawn)
            {
                if (now >= _nextShopCheckGameTime || (_nextShopCheckGameTime - now) > (ShopControllerCheckIntervalMs * 2))
                {
                    _nextShopCheckGameTime = now + ShopControllerCheckIntervalMs;
                    TrySuspendShopControllerDespawn();
                }
            }
        }

        /// <summary>
        /// Periodically verifies that Story Mode save reloads or death/hospital respawns have not
        /// unloaded active IPLs, re-capped interiors, or stripped interior entity sets.
        /// </summary>
        private void MaintainLoadedMapsAndInteriors()
        {
            if (HasAnyMpChangesetMapLoaded && !MpMapsModeEnabled)
            {
                SetMpMapsMode(true);
            }

            if (DiamondCasinoLoaded && !Function.Call<bool>(Hash.IS_IPL_ACTIVE, "vw_casino_main"))
            {
                Function.Call(Hash.ON_ENTER_MP);
                Function.Call(Hash.SET_INSTANCE_PRIORITY_HINT, InstanceHintDriving);
                RemoveIplBatch(DiamondCasinoConflictingDoorIpls);
                RequestIplBatch(DiamondCasinoIpls);
                _casinoInteriorId = 0;
                _penthouseInteriorId = 0;
            }

            if (AircraftCarrierLoaded && !Function.Call<bool>(Hash.IS_IPL_ACTIVE, "hei_carrier"))
            {
                Function.Call(Hash.ON_ENTER_MP);
                RequestIplBatch(AircraftCarrierIpls);
                _carrierInteriorId = 0;
            }

            if (SuperyachtLoaded && (!Function.Call<bool>(Hash.IS_IPL_ACTIVE, "smboat") || !Function.Call<bool>(Hash.IS_IPL_ACTIVE, "hei_yacht_heist")))
            {
                Function.Call(Hash.ON_ENTER_MP);
                RequestIplBatch(SuperyachtExteriorIpls);
                _yachtInteriorId = 0;
            }

            if (CayoPericoLoaded && _cayoZoneEnvironmentActive)
            {
                if (!Function.Call<bool>(Hash.IS_IPL_ACTIVE, "h4_islandx")
                    || !Function.Call<bool>(Hash.IS_IPL_ACTIVE, "h4_islandairstrip_doorsopen")
                    || Function.Call<bool>(Hash.IS_IPL_ACTIVE, "h4_islandairstrip_doorsclosed")
                    || Function.Call<bool>(Hash.IS_IPL_ACTIVE, "h4_mansion_gate_broken"))
                {
                    SyncCayoPericoZoneEnvironment(true, force: true);
                }
            }

            if (NorthYanktonLoaded && !Function.Call<bool>(Hash.IS_IPL_ACTIVE, "prologue01"))
            {
                RemoveIplBatch(NorthYanktonConflictingIpls);
                RequestIplBatch(NorthYanktonIpls);
                _yanktonBankInteriorId = 0;
            }

            if (GunrunningBunkerLoaded && !Function.Call<bool>(Hash.IS_IPL_ACTIVE, "gr_grdlc_interior_placement"))
            {
                Function.Call(Hash.ON_ENTER_MP);
                RequestIplBatch(GunrunningBunkerIpls);
                _bunkerInteriorId = 0;
            }

            if (DoomsdayFacilityLoaded && !Function.Call<bool>(Hash.IS_IPL_ACTIVE, "xm_x17dlc_int_placement"))
            {
                Function.Call(Hash.ON_ENTER_MP);
                RequestIplBatch(DoomsdayFacilityIpls);
                _facilityInteriorId = 0;
            }

            if (CargoShipLoaded && !Function.Call<bool>(Hash.IS_IPL_ACTIVE, "cargoship"))
            {
                RemoveIplBatch(CargoShipConflictingIpls);
                RequestIplBatch(CargoShipIpls);
            }

            if (StoryInteriorsLoaded && !Function.Call<bool>(Hash.IS_IPL_ACTIVE, "FIBlobby"))
            {
                RemoveIplBatch(StoryInteriorsConflictingIpls);
                RequestIplBatch(StoryInteriorsIpls);
                _fibLobbyInteriorId = 0;
                _coronerInteriorId = 0;
                _lifeinvaderInteriorId = 0;
                _ranchInteriorId = 0;
            }

            ConfigureLoadedMapInteriors(forceRefresh: false);
        }

        private void ConfigureLoadedMapInteriors(bool forceRefresh)
        {
            if (DiamondCasinoLoaded)
            {
                EnsureSimpleInteriorHealthy(DiamondCasinoMainInteriorCoords, ref _casinoInteriorId, forceRefresh);
                EnsureEntitySetInteriorHealthy(
                    DiamondCasinoPenthouseCoords,
                    ref _penthouseInteriorId,
                    PenthouseInteriorSets,
                    "set_pent_tint_shell",
                    forceRefresh,
                    pentId =>
                    {
                        Function.Call(Hash.SET_INTERIOR_ENTITY_SET_TINT_INDEX, pentId, "set_pent_tint_shell", 1);
                        Function.Call(Hash.SET_INTERIOR_ENTITY_SET_TINT_INDEX, pentId, "set_pent_Pattern_01", 1);
                    });
            }

            if (SuperyachtLoaded)
            {
                EnsureSimpleInteriorHealthy(SuperyachtDeckCoords, ref _yachtInteriorId, forceRefresh);
            }

            if (AircraftCarrierLoaded)
            {
                EnsureSimpleInteriorHealthy(AircraftCarrierDeckCoords, ref _carrierInteriorId, forceRefresh);
            }

            if (NorthYanktonLoaded)
            {
                EnsureSimpleInteriorHealthy(NorthYanktonBankInteriorCoords, ref _yanktonBankInteriorId, forceRefresh);
            }

            if (GunrunningBunkerLoaded)
            {
                EnsureEntitySetInteriorHealthy(
                    GunrunningBunkerCoords,
                    ref _bunkerInteriorId,
                    GunrunningBunkerEntitySets,
                    "bunker_style_a",
                    forceRefresh,
                    null);
            }

            if (DoomsdayFacilityLoaded)
            {
                EnsureEntitySetInteriorHealthy(
                    DoomsdayFacilityCoords,
                    ref _facilityInteriorId,
                    DoomsdayFacilityEntitySets,
                    "set_int_02_shell",
                    forceRefresh,
                    facId =>
                    {
                        Function.Call(Hash.SET_INTERIOR_ENTITY_SET_TINT_INDEX, facId, "set_int_02_shell", 1);
                        Function.Call(Hash.SET_INTERIOR_ENTITY_SET_TINT_INDEX, facId, "set_int_02_decal_01", 1);
                    });
            }

            if (StoryInteriorsLoaded)
            {
                EnsureSimpleInteriorHealthy(FibLobbyCoords, ref _fibLobbyInteriorId, forceRefresh);
                EnsureSimpleInteriorHealthy(CoronerMorgueCoords, ref _coronerInteriorId, forceRefresh);
                EnsureSimpleInteriorHealthy(LifeinvaderOfficeCoords, ref _lifeinvaderInteriorId, forceRefresh);
                EnsureSimpleInteriorHealthy(OneilRanchCoords, ref _ranchInteriorId, forceRefresh);
            }
        }

        private void EnsureSimpleInteriorHealthy(Vector3 coords, ref int cachedId, bool forceRefresh)
        {
            if (!forceRefresh && cachedId != 0 && IsInteriorPinnedAndUncapped(cachedId))
            {
                return;
            }

            int id = PinInteriorAt(coords, forceRefresh);
            if (id != 0)
            {
                cachedId = id;
            }
        }

        private void EnsureEntitySetInteriorHealthy(
            Vector3 coords,
            ref int cachedId,
            string[] entitySets,
            string sentinelSet,
            bool forceRefresh,
            Action<int> applyTints)
        {
            bool needsConfig = forceRefresh
                || cachedId == 0
                || !IsInteriorPinnedAndUncapped(cachedId)
                || (!string.IsNullOrEmpty(sentinelSet) && !Function.Call<bool>(Hash.IS_INTERIOR_ENTITY_SET_ACTIVE, cachedId, sentinelSet));

            if (!needsConfig)
            {
                return;
            }

            int id = PinInteriorAt(coords, refreshImmediately: false);
            if (id == 0)
            {
                return;
            }

            cachedId = id;
            for (int i = 0; i < entitySets.Length; i++)
            {
                if (!Function.Call<bool>(Hash.IS_INTERIOR_ENTITY_SET_ACTIVE, id, entitySets[i]))
                {
                    Function.Call(Hash.ACTIVATE_INTERIOR_ENTITY_SET, id, entitySets[i]);
                }
            }

            applyTints?.Invoke(id);
            Function.Call(Hash.REFRESH_INTERIOR, id);
        }

        private static bool IsInteriorPinnedAndUncapped(int interiorId)
        {
            return interiorId != 0
                && Function.Call<bool>(Hash.IS_VALID_INTERIOR, interiorId)
                && !Function.Call<bool>(Hash.IS_INTERIOR_DISABLED, interiorId)
                && !Function.Call<bool>(Hash.IS_INTERIOR_CAPPED, interiorId);
        }

        private void SyncCayoPericoZoneEnvironment(bool inCayoZone, bool force)
        {
            if (!force && _cayoZoneEnvironmentActive == inCayoZone)
            {
                return;
            }

            _cayoZoneEnvironmentActive = inCayoZone;
            if (inCayoZone)
            {
                // Enable HeistIsland hopper in-zone so CCS_MPHEIST4_ISLAND static .ybn terrain/compound
                // collision bounds stream in cleanly while keeping Los Santos untouched when out of zone.
                Function.Call(Hash.SET_ISLAND_ENABLED, "HeistIsland", true);
                RemoveIplBatch(CayoPericoConflictingIpls);
                RequestIplBatch(CayoPericoIpls);
                Function.Call(Hash.SET_USE_ISLAND_MAP, true);
                Function.Call(Hash.SET_ALLOW_STREAM_HEIST_ISLAND_NODES, 1);
                _cayoPathNodesActive = true;
                Function.Call(Hash.LOAD_GLOBAL_WATER_FILE, 1);
                Function.Call(Hash.SET_SCENARIO_GROUP_ENABLED, "Heist_Island_Peds", true);
                Function.Call(Hash.SET_AUDIO_FLAG, "PlayerOnDLCHeist4Island", true);
                Function.Call(Hash.SET_AMBIENT_ZONE_LIST_STATE_PERSISTENT, "AZL_DLC_Hei4_Island_Zones", true, true);
                Function.Call(Hash.SET_AMBIENT_ZONE_LIST_STATE_PERSISTENT, "AZL_DLC_Hei4_Island_Disabled_Zones", false, true);
            }
            else
            {
                Function.Call(Hash.SET_AMBIENT_ZONE_LIST_STATE_PERSISTENT, "AZL_DLC_Hei4_Island_Zones", false, false);
                Function.Call(Hash.SET_AMBIENT_ZONE_LIST_STATE_PERSISTENT, "AZL_DLC_Hei4_Island_Disabled_Zones", true, false);
                Function.Call(Hash.SET_SCENARIO_GROUP_ENABLED, "Heist_Island_Peds", false);
                Function.Call(Hash.SET_AUDIO_FLAG, "PlayerOnDLCHeist4Island", false);
                Function.Call(Hash.LOAD_GLOBAL_WATER_FILE, 0);
                if (_cayoPathNodesActive && IsPlayerSafelyAwayFromCayoPerico(Game.Player?.Character))
                {
                    Function.Call(Hash.SET_ALLOW_STREAM_HEIST_ISLAND_NODES, 0);
                    _cayoPathNodesActive = false;
                }
                Function.Call(Hash.SET_USE_ISLAND_MAP, false);
                RemoveIplBatch(CayoPericoIpls);
                RemoveIplBatch(CayoPericoConflictingIpls);
                Function.Call(Hash.SET_ISLAND_ENABLED, "HeistIsland", false);
            }
        }

        private static bool IsPlayerSafelyAwayFromCayoPerico(Ped player)
        {
            if (player == null || !player.Exists())
            {
                return false;
            }

            Vector3 pos = player.Position;
            float dx = pos.X - CayoPericoCenter2D.X;
            float dy = pos.Y - CayoPericoCenter2D.Y;
            return (dx * dx + dy * dy) > CayoPathNodeResetSafeRadiusSq;
        }

        private void SyncNorthYanktonZoneEnvironment(bool inYanktonZone, bool force)
        {
            if (!force && _northYanktonZoneEnvironmentActive == inYanktonZone)
            {
                return;
            }

            _northYanktonZoneEnvironmentActive = inYanktonZone;
            Function.Call(Hash.SET_ALLOW_STREAM_PROLOGUE_NODES, inYanktonZone);
            Function.Call(Hash.SET_MINIMAP_IN_PROLOGUE, inYanktonZone);
        }

        private void SyncWorldBoundaryState()
        {
            if (HasAnyOffMapAssetLoaded)
            {
                _worldBoundaryExtended = true;
                ExtendOffMapBoundariesThisFrame();
            }
            else if (_worldBoundaryExtended)
            {
                _worldBoundaryExtended = false;
                Function.Call(Hash.RESET_WORLD_BOUNDARY_FOR_PLAYER);
            }
        }

        private static void ExtendOffMapBoundariesThisFrame()
        {
            Function.Call(Hash.EXTEND_WORLD_BOUNDARY_FOR_PLAYER, -12000.0f, -12000.0f, 30.0f);
            Function.Call(Hash.EXTEND_WORLD_BOUNDARY_FOR_PLAYER, 12000.0f, 12000.0f, 30.0f);
        }

        /// <summary>
        /// Safely unloads all loaded MP IPLs, Cayo Perico, North Yankton, and Story interiors, and reverts ON_ENTER_SP.
        /// Caller should warp the player back to Los Santos first if standing off-map or inside an unloaded interior.
        /// </summary>
        public void RevertAllToStoryWorld()
        {
            _pendingStartupMpMapsUnlock = false;
            ExplicitMpMapsPreference = false;

            if (CayoPericoLoaded) SetCayoPericoEnabled(false);
            if (NorthYanktonLoaded) SetNorthYanktonEnabled(false);
            if (DiamondCasinoLoaded) SetDiamondCasinoEnabled(false);
            if (AircraftCarrierLoaded) SetAircraftCarrierEnabled(false);
            if (SuperyachtLoaded) SetSuperyachtEnabled(false);
            if (GunrunningBunkerLoaded) SetGunrunningBunkerEnabled(false);
            if (DoomsdayFacilityLoaded) SetDoomsdayFacilityEnabled(false);
            if (CargoShipLoaded) SetCargoShipEnabled(false);
            if (StoryInteriorsLoaded) SetStoryInteriorsEnabled(false);

            for (int i = 0; i < _pinnedInteriors.Count; i++)
            {
                int id = _pinnedInteriors[i];
                if (Function.Call<bool>(Hash.IS_VALID_INTERIOR, id))
                {
                    Function.Call(Hash.UNPIN_INTERIOR, id);
                }
            }
            _pinnedInteriors.Clear();

            if (_worldBoundaryExtended)
            {
                _worldBoundaryExtended = false;
                Function.Call(Hash.RESET_WORLD_BOUNDARY_FOR_PLAYER);
            }

            if (_cayoPathNodesActive && IsPlayerSafelyAwayFromCayoPerico(Game.Player?.Character))
            {
                Function.Call(Hash.SET_ALLOW_STREAM_HEIST_ISLAND_NODES, 0);
                _cayoPathNodesActive = false;
            }

            if (MpMapsModeEnabled)
            {
                Function.Call(Hash.SET_INSTANCE_PRIORITY_HINT, 0);
                Function.Call(Hash.ON_ENTER_SP);
                MpMapsModeEnabled = false;
            }

            SuspendShopControllerDespawn = false;
        }

        public void CleanupOnAbort()
        {
            RevertAllToStoryWorld();
        }

        private void ResetMpMapsModeIfNoMpMapsLoaded()
        {
            if (!HasAnyMpChangesetMapLoaded && !ExplicitMpMapsPreference && MpMapsModeEnabled)
            {
                SetMpMapsMode(false);
            }
        }

        private void TrySuspendShopControllerDespawn()
        {
            if (_shopControllerScriptHash == 0)
            {
                _shopControllerScriptHash = unchecked((int)AtHashValue.ComputeHash("shop_controller"));
            }

            if (Function.Call<int>(Hash.GET_NUMBER_OF_THREADS_RUNNING_THE_SCRIPT_WITH_THIS_HASH, _shopControllerScriptHash) > 0)
            {
                Function.Call(Hash.TERMINATE_ALL_SCRIPTS_WITH_THIS_NAME, "shop_controller");
            }
        }

        public int EnsureInteriorPinnedAt(Vector3 coords)
        {
            if (_isMultiplayerBlocked()) return 0;
            int id = PinInteriorAt(coords, refreshImmediately: true);
            ConfigureLoadedMapInteriors(forceRefresh: false);
            return id;
        }

        public bool IsInteriorReadyAt(Vector3 coords)
        {
            if (_isMultiplayerBlocked()) return false;
            int id = EnsureInteriorPinnedAt(coords);
            return id != 0
                && Function.Call<bool>(Hash.IS_VALID_INTERIOR, id)
                && Function.Call<bool>(Hash.IS_INTERIOR_READY, id);
        }

        private int PinInteriorAt(Vector3 coords, bool refreshImmediately)
        {
            int interiorId = Function.Call<int>(Hash.GET_INTERIOR_AT_COORDS, coords.X, coords.Y, coords.Z);
            if (Function.Call<bool>(Hash.IS_VALID_INTERIOR, interiorId))
            {
                if (Function.Call<bool>(Hash.IS_INTERIOR_DISABLED, interiorId))
                {
                    Function.Call(Hash.DISABLE_INTERIOR, interiorId, false);
                }
                if (Function.Call<bool>(Hash.IS_INTERIOR_CAPPED, interiorId))
                {
                    Function.Call(Hash.CAP_INTERIOR, interiorId, false);
                }
                Function.Call(Hash.PIN_INTERIOR_IN_MEMORY, interiorId);
                if (refreshImmediately)
                {
                    Function.Call(Hash.REFRESH_INTERIOR, interiorId);
                }
                if (!_pinnedInteriors.Contains(interiorId))
                {
                    _pinnedInteriors.Add(interiorId);
                }
                return interiorId;
            }
            return 0;
        }

        private void UnpinInteriorId(ref int interiorId)
        {
            if (interiorId == 0) return;
            if (Function.Call<bool>(Hash.IS_VALID_INTERIOR, interiorId))
            {
                Function.Call(Hash.UNPIN_INTERIOR, interiorId);
            }
            _pinnedInteriors.Remove(interiorId);
            interiorId = 0;
        }

        private static void RequestIplBatch(string[] ipls)
        {
            for (int i = 0; i < ipls.Length; i++)
            {
                string ipl = ipls[i];
                if (!Function.Call<bool>(Hash.IS_IPL_ACTIVE, ipl))
                {
                    Function.Call(Hash.REQUEST_IPL, ipl);
                }
            }
        }

        private static void RemoveIplBatch(string[] ipls)
        {
            for (int i = 0; i < ipls.Length; i++)
            {
                string ipl = ipls[i];
                if (Function.Call<bool>(Hash.IS_IPL_ACTIVE, ipl))
                {
                    Function.Call(Hash.REMOVE_IPL, ipl);
                }
            }
        }
    }
}
