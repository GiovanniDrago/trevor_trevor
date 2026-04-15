using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;
using GTA.UI;
using GtaControl = GTA.Control;

public sealed class TrevorTrevor : Script
{
    private const int ClosedTickIntervalMs = 50;
    private const int MenuOpenHoldMs = 500;
    private const int MenuPageSize = 8;
    private const int CompanionRefreshMs = 1200;
    private const int VehicleTaskRetryMs = 1200;
    private const int VehicleBoardingFallbackMs = 4000;
    private const int MaxManagedVehicles = 2;
    private const int MaxRecentVehicles = 12;
    private const int MaxCrowCount = 30;
    private const int MaxGirlCount = 10;
    private const int MaxNpcCount = 20;
    private const float EngageDistance = 5000f;
    private const float NormalFollowSpeed = 28f;
    private const float AggressiveFollowSpeed = 38f;
    private const float AggressiveFollowMaxSpeed = 45f;
    private const int NormalDrivingStyle = 786468;
    private const int AggressiveDrivingStyle = 786603;
    private const float NormalNoRoadsDistance = 20f;
    private const float OffRoadNoRoadsDistance = 120f;
    private const int VehicleCombatAccuracy = 65;
    private const string GirlModelName = "s_f_y_hooker_01";
    private const string CrowModelName = "a_c_crow";
    private const string DefaultNpcFavoriteModelName = "igmax_custom";

    private static readonly string ScriptsDirectoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts");
    private static readonly string LogFilePath = Path.Combine(ScriptsDirectoryPath, "TrevorTrevor.log");
    private static readonly string VehicleFavoritesFilePath = Path.Combine(ScriptsDirectoryPath, "TrevorTrevor.favorites.txt");
    private static readonly string NpcFavoritesFilePath = Path.Combine(ScriptsDirectoryPath, "TrevorTrevor.npcfavorites.txt");
    private static readonly string SettingsFilePath = Path.Combine(ScriptsDirectoryPath, "TrevorTrevor.settings.txt");
    private static readonly Queue<int> SpawnedVehicles = new Queue<int>();
    private static readonly Dictionary<string, VehicleCatalog.VehicleEntry> VehicleCatalogByModel = CreateVehicleCatalogByModel();
    private static readonly Dictionary<string, PedCatalog.PedEntry> PedCatalogByModel = CreatePedCatalogByModel();
    private static readonly HashSet<int> WeaponizedVehicleModelHashes = CreateWeaponizedVehicleModelHashes();
    private static bool _notificationUnavailable;

    private readonly InstructionalButtonsRenderer _instructionalButtons = new InstructionalButtonsRenderer();
    private readonly List<VehicleCatalog.VehicleEntry> _recentVehicles = new List<VehicleCatalog.VehicleEntry>();
    private readonly List<VehicleCatalog.VehicleEntry> _favoriteVehicles = new List<VehicleCatalog.VehicleEntry>();
    private readonly List<PedCatalog.PedEntry> _favoriteNpcs = new List<PedCatalog.PedEntry>();
    private readonly List<int> _spawnedCrows = new List<int>();
    private readonly List<int> _girlCompanions = new List<int>();
    private readonly Dictionary<int, Blip> _girlBlips = new Dictionary<int, Blip>();
    private readonly Dictionary<int, VehicleAssignment> _girlVehicleAssignments = new Dictionary<int, VehicleAssignment>();
    private readonly List<ManagedNpc> _managedNpcs = new List<ManagedNpc>();

    private bool _menuOpen;
    private MenuLevel _menuLevel = MenuLevel.Root;
    private int _selectedRootIndex;
    private int _selectedVehicleCategoryIndex;
    private int _selectedVehicleIndex;
    private int _selectedRecentVehicleIndex;
    private int _selectedFavoriteVehicleIndex;
    private int _selectedNpcRootIndex;
    private int _selectedNpcCategoryIndex;
    private int _selectedNpcIndex;
    private int _selectedNpcFavoriteIndex;
    private int _selectedNpcSettingIndex;
    private int _rightHoldStartTick = -1;
    private bool _holdConsumed;
    private int _lastCompanionRefreshTick;
    private bool _girlsDanceMode;
    private readonly NpcSpawnSettings _npcSettings = new NpcSpawnSettings();

    private enum MenuLevel
    {
        Root,
        VehicleCategories,
        VehicleList,
        VehicleRecents,
        VehicleFavorites,
        NpcRoot,
        NpcCategories,
        NpcList,
        NpcFavorites,
        NpcSettings,
    }

    private enum RootEntry
    {
        Vehicles,
        RecentVehicles,
        Favorites,
        Crows,
        Npcs,
    }

    private enum NpcRootEntry
    {
        Spawns,
        Favorites,
        Settings,
    }

    private enum NpcSettingEntry
    {
        Armed,
        Disposition,
        VehicleMode,
        AggressiveDrive,
        OffRoad,
    }

    private enum NpcDisposition
    {
        Ally,
        Enemy,
    }

    private enum NpcVehicleMode
    {
        UsePlayerCar,
        DriveOwnCar,
    }

    private sealed class NpcSpawnSettings
    {
        internal bool Armed = true;
        internal NpcDisposition Disposition = NpcDisposition.Ally;
        internal NpcVehicleMode VehicleMode = NpcVehicleMode.UsePlayerCar;
        internal bool AggressiveDrive;
        internal bool UseOffRoad;
    }

    private sealed class ManagedNpc
    {
        internal int PedHandle;
        internal Blip AllyBlip;
        internal string ModelName;
        internal bool Armed;
        internal NpcDisposition Disposition;
        internal NpcVehicleMode VehicleMode;
        internal bool AggressiveDrive;
        internal bool UseOffRoad;
        internal int AssignedVehicle;
        internal int AssignedSeat = int.MinValue;
        internal int LastVehicleTaskTick;
        internal int AssignmentStartTick;
    }

    private sealed class VehicleAssignment
    {
        internal int VehicleHandle;
        internal int SeatIndex = int.MinValue;
        internal int LastTaskTick;
        internal int AssignmentStartTick;
    }

    public TrevorTrevor()
    {
        try
        {
            Directory.CreateDirectory(ScriptsDirectoryPath);
            LoadVehicleFavorites();
            LoadNpcFavorites();
            LoadSettings();
            Log("Script initialized.");
            SafeNotify("~b~TrevorTrevor loaded");
        }
        catch
        {
        }

        Tick += OnTick;
        KeyDown += OnKeyDown;
        Interval = ClosedTickIntervalMs;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F6 && !_menuOpen)
        {
            OpenMenu();
            Log("Menu opened with F6.");
        }
    }

    private VehicleCatalog.VehicleCategory CurrentVehicleCategory => VehicleCatalog.Categories[ClampIndex(_selectedVehicleCategoryIndex, VehicleCatalog.Categories.Length)];
    private PedCatalog.PedCategory CurrentNpcCategory => PedCatalog.Categories[ClampIndex(_selectedNpcCategoryIndex, PedCatalog.Categories.Length)];

    private void OnTick(object sender, EventArgs e)
    {
        try
        {
            RefreshManagedActors();

            if (!_menuOpen)
            {
                HandleMenuOpenHold();
                Interval = ClosedTickIntervalMs;
                return;
            }

            Interval = 0;
            DisableMenuControls();
            ClampSelections();
            HandleMenuNavigation();
            DrawMenu();
            DrawInstructionalButtons();
        }
        catch (Exception ex)
        {
            CloseMenu();
            Log($"Unhandled OnTick error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void RefreshManagedActors()
    {
        if (!HasElapsed(_lastCompanionRefreshTick, CompanionRefreshMs))
        {
            return;
        }

        _lastCompanionRefreshTick = Environment.TickCount;
        CleanupTrackedCrows();
        RefreshManagedNpcs();
    }

    private void HandleMenuOpenHold()
    {
        bool rightHeld = Game.IsControlPressed(GtaControl.FrontendRight) || Game.IsControlPressed(GtaControl.PhoneRight);
        if (!rightHeld)
        {
            _rightHoldStartTick = -1;
            _holdConsumed = false;
            return;
        }

        if (_holdConsumed)
        {
            return;
        }

        if (_rightHoldStartTick < 0)
        {
            _rightHoldStartTick = Environment.TickCount;
            return;
        }

        if (!HasElapsed(_rightHoldStartTick, MenuOpenHoldMs))
        {
            return;
        }

        _holdConsumed = true;
        OpenMenu();
    }

    private void OpenMenu()
    {
        _menuOpen = true;
        _menuLevel = MenuLevel.Root;
        ClampSelections();
        Interval = 0;
        Log("Menu opened.");
    }

    private void CloseMenu()
    {
        _menuOpen = false;
        _menuLevel = MenuLevel.Root;
        Interval = ClosedTickIntervalMs;
    }

    private void HandleMenuNavigation()
    {
        if (IsFavoriteTogglePressed())
        {
            HandleFavoriteToggle();
            return;
        }

        if (CanUseSecondaryAction() && IsMenuSecondaryActionPressed())
        {
            ExecuteMenuAction(keepMenuOpen: true);
            return;
        }

        if (IsPagePreviousPressed())
        {
            ChangeSelection(MenuPageSize * -1, wrap: false);
            return;
        }

        if (IsPageNextPressed())
        {
            ChangeSelection(MenuPageSize, wrap: false);
            return;
        }

        if (IsMenuUpPressed())
        {
            ChangeSelection(-1, wrap: true);
            return;
        }

        if (IsMenuDownPressed())
        {
            ChangeSelection(1, wrap: true);
            return;
        }

        if (IsMenuAcceptPressed())
        {
            ExecuteMenuAction(keepMenuOpen: false);
            return;
        }

        if (IsMenuCancelPressed())
        {
            HandleMenuBack();
        }
    }

    private bool CanUseSecondaryAction()
    {
        if (_menuLevel == MenuLevel.Root)
        {
            return (RootEntry)ClampIndex(_selectedRootIndex, GetRootItemCount()) == RootEntry.Crows;
        }

        return _menuLevel == MenuLevel.VehicleList
            || _menuLevel == MenuLevel.VehicleRecents
            || _menuLevel == MenuLevel.VehicleFavorites
            || _menuLevel == MenuLevel.NpcList
            || _menuLevel == MenuLevel.NpcFavorites;
    }

    private void HandleMenuBack()
    {
        switch (_menuLevel)
        {
            case MenuLevel.VehicleList:
                _menuLevel = MenuLevel.VehicleCategories;
                Log($"Returned to vehicle categories from {CurrentVehicleCategory.Name}.");
                break;
            case MenuLevel.VehicleCategories:
            case MenuLevel.VehicleRecents:
            case MenuLevel.VehicleFavorites:
            case MenuLevel.NpcRoot:
                _menuLevel = MenuLevel.Root;
                Log("Returned to root menu.");
                break;
            case MenuLevel.NpcList:
                _menuLevel = MenuLevel.NpcCategories;
                Log($"Returned to NPC categories from {CurrentNpcCategory.Name}.");
                break;
            case MenuLevel.NpcCategories:
            case MenuLevel.NpcFavorites:
            case MenuLevel.NpcSettings:
                _menuLevel = MenuLevel.NpcRoot;
                Log("Returned to NPC root menu.");
                break;
            default:
                CloseMenu();
                Log("Menu cancelled.");
                break;
        }
    }

    private void ExecuteMenuAction(bool keepMenuOpen)
    {
        switch (_menuLevel)
        {
            case MenuLevel.Root:
                ExecuteRootAction(keepMenuOpen);
                return;
            case MenuLevel.VehicleCategories:
                _menuLevel = MenuLevel.VehicleList;
                _selectedVehicleIndex = ClampIndex(_selectedVehicleIndex, CurrentVehicleCategory.Vehicles.Length);
                Log($"Vehicle category opened: {CurrentVehicleCategory.Name}");
                return;
            case MenuLevel.VehicleList:
            case MenuLevel.VehicleRecents:
            case MenuLevel.VehicleFavorites:
                ExecuteVehicleSpawnAction(keepMenuOpen);
                return;
            case MenuLevel.NpcRoot:
                ExecuteNpcRootAction(keepMenuOpen);
                return;
            case MenuLevel.NpcCategories:
                _menuLevel = MenuLevel.NpcList;
                _selectedNpcIndex = ClampIndex(_selectedNpcIndex, CurrentNpcCategory.Peds.Length);
                Log($"NPC category opened: {CurrentNpcCategory.Name}");
                return;
            case MenuLevel.NpcList:
            case MenuLevel.NpcFavorites:
                ExecuteNpcSpawnAction(keepMenuOpen);
                return;
            case MenuLevel.NpcSettings:
                CycleNpcSetting();
                return;
        }
    }

    private void ExecuteRootAction(bool keepMenuOpen)
    {
        switch ((RootEntry)ClampIndex(_selectedRootIndex, GetRootItemCount()))
        {
            case RootEntry.Vehicles:
                _menuLevel = MenuLevel.VehicleCategories;
                Log("Root action: Vehicles.");
                break;
            case RootEntry.RecentVehicles:
                _menuLevel = MenuLevel.VehicleRecents;
                Log("Root action: Recent Vehicles.");
                break;
            case RootEntry.Favorites:
                _menuLevel = MenuLevel.VehicleFavorites;
                Log("Root action: Favorites.");
                break;
            case RootEntry.Crows:
                if (!keepMenuOpen)
                {
                    CloseMenu();
                    _holdConsumed = true;
                }

                TrySpawnCrows();
                break;
            case RootEntry.Npcs:
                _menuLevel = MenuLevel.NpcRoot;
                Log("Root action: NPCs.");
                break;
        }
    }

    private void ExecuteVehicleSpawnAction(bool keepMenuOpen)
    {
        VehicleCatalog.VehicleEntry vehicle = GetCurrentVehicleEntry();
        if (vehicle == null)
        {
            return;
        }

        if (!keepMenuOpen)
        {
            CloseMenu();
            _holdConsumed = true;
        }

        Log($"Vehicle selected: {vehicle.DisplayName} ({vehicle.ModelName})");
        if (TrySpawnVehicle(vehicle))
        {
            AddRecentVehicle(vehicle);
        }
    }

    private void ExecuteNpcRootAction(bool keepMenuOpen)
    {
        switch ((NpcRootEntry)ClampIndex(_selectedNpcRootIndex, GetNpcRootItemCount()))
        {
            case NpcRootEntry.Spawns:
                _menuLevel = MenuLevel.NpcCategories;
                Log("NPC root action: Spawns.");
                break;
            case NpcRootEntry.Favorites:
                _menuLevel = MenuLevel.NpcFavorites;
                Log("NPC root action: Favorites.");
                break;
            case NpcRootEntry.Settings:
                _menuLevel = MenuLevel.NpcSettings;
                Log("NPC root action: Settings.");
                break;
        }
    }

    private void ExecuteNpcSpawnAction(bool keepMenuOpen)
    {
        PedCatalog.PedEntry ped = GetCurrentPedEntry();
        if (ped == null)
        {
            return;
        }

        if (!keepMenuOpen)
        {
            CloseMenu();
            _holdConsumed = true;
        }

        Log($"NPC selected: {ped.ModelName}");
        TrySpawnNpc(ped);
    }

    private void CycleNpcSetting()
    {
        switch ((NpcSettingEntry)ClampIndex(_selectedNpcSettingIndex, GetNpcSettingItemCount()))
        {
            case NpcSettingEntry.Armed:
                _npcSettings.Armed = !_npcSettings.Armed;
                break;
            case NpcSettingEntry.Disposition:
                _npcSettings.Disposition = _npcSettings.Disposition == NpcDisposition.Ally ? NpcDisposition.Enemy : NpcDisposition.Ally;
                break;
            case NpcSettingEntry.VehicleMode:
                _npcSettings.VehicleMode = _npcSettings.VehicleMode == NpcVehicleMode.UsePlayerCar ? NpcVehicleMode.DriveOwnCar : NpcVehicleMode.UsePlayerCar;
                break;
            case NpcSettingEntry.AggressiveDrive:
                _npcSettings.AggressiveDrive = !_npcSettings.AggressiveDrive;
                break;
            case NpcSettingEntry.OffRoad:
                _npcSettings.UseOffRoad = !_npcSettings.UseOffRoad;
                break;
        }

        SaveSettings();
        Log("NPC settings updated.");
    }

    private void HandleFavoriteToggle()
    {
        VehicleCatalog.VehicleEntry vehicle = GetCurrentVehicleEntry();
        if (vehicle != null)
        {
            ToggleVehicleFavorite(vehicle);
            return;
        }

        PedCatalog.PedEntry ped = GetCurrentPedEntry();
        if (ped != null)
        {
            ToggleNpcFavorite(ped);
        }
    }

    private void ToggleVehicleFavorite(VehicleCatalog.VehicleEntry vehicle)
    {
        int existingIndex = FindVehicleIndexByModel(_favoriteVehicles, vehicle.ModelName);
        if (existingIndex >= 0)
        {
            _favoriteVehicles.RemoveAt(existingIndex);
            SaveVehicleFavorites();
            _selectedFavoriteVehicleIndex = ClampIndex(_selectedFavoriteVehicleIndex, _favoriteVehicles.Count);
            SafeNotify($"~y~Removed favorite: {vehicle.DisplayName}");
            Log($"Vehicle favorite removed: {vehicle.ModelName}");
            return;
        }

        _favoriteVehicles.Insert(0, vehicle);
        SaveVehicleFavorites();
        _selectedFavoriteVehicleIndex = 0;
        SafeNotify($"~b~Added favorite: {vehicle.DisplayName}");
        Log($"Vehicle favorite added: {vehicle.ModelName}");
    }

    private void ToggleNpcFavorite(PedCatalog.PedEntry ped)
    {
        int existingIndex = FindPedIndexByModel(_favoriteNpcs, ped.ModelName);
        if (existingIndex >= 0)
        {
            _favoriteNpcs.RemoveAt(existingIndex);
            SaveNpcFavorites();
            _selectedNpcFavoriteIndex = ClampIndex(_selectedNpcFavoriteIndex, _favoriteNpcs.Count);
            SafeNotify($"~y~Removed favorite NPC: {ped.DisplayName}");
            Log($"NPC favorite removed: {ped.ModelName}");
            return;
        }

        _favoriteNpcs.Insert(0, ped);
        SaveNpcFavorites();
        _selectedNpcFavoriteIndex = 0;
        SafeNotify($"~b~Added favorite NPC: {ped.DisplayName}");
        Log($"NPC favorite added: {ped.ModelName}");
    }

    private void ChangeSelection(int delta, bool wrap)
    {
        int count = GetCurrentItemCount();
        if (count <= 0)
        {
            return;
        }

        int currentIndex = GetCurrentSelectionIndex();
        int newIndex;
        if (wrap)
        {
            newIndex = (currentIndex + delta) % count;
            if (newIndex < 0)
            {
                newIndex += count;
            }
        }
        else
        {
            newIndex = Math.Max(0, Math.Min(count - 1, currentIndex + delta));
        }

        SetCurrentSelectionIndex(newIndex);
    }

    private int GetCurrentItemCount()
    {
        switch (_menuLevel)
        {
            case MenuLevel.Root:
                return GetRootItemCount();
            case MenuLevel.VehicleCategories:
                return VehicleCatalog.Categories.Length;
            case MenuLevel.VehicleList:
                return CurrentVehicleCategory.Vehicles.Length;
            case MenuLevel.VehicleRecents:
                return _recentVehicles.Count;
            case MenuLevel.VehicleFavorites:
                return _favoriteVehicles.Count;
            case MenuLevel.NpcRoot:
                return GetNpcRootItemCount();
            case MenuLevel.NpcCategories:
                return PedCatalog.Categories.Length;
            case MenuLevel.NpcList:
                return CurrentNpcCategory.Peds.Length;
            case MenuLevel.NpcFavorites:
                return _favoriteNpcs.Count;
            case MenuLevel.NpcSettings:
                return GetNpcSettingItemCount();
            default:
                return 0;
        }
    }

    private static int GetRootItemCount()
    {
        return 5;
    }

    private static int GetNpcRootItemCount()
    {
        return 3;
    }

    private static int GetNpcSettingItemCount()
    {
        return 5;
    }

    private int GetCurrentSelectionIndex()
    {
        switch (_menuLevel)
        {
            case MenuLevel.Root:
                return _selectedRootIndex;
            case MenuLevel.VehicleCategories:
                return _selectedVehicleCategoryIndex;
            case MenuLevel.VehicleList:
                return _selectedVehicleIndex;
            case MenuLevel.VehicleRecents:
                return _selectedRecentVehicleIndex;
            case MenuLevel.VehicleFavorites:
                return _selectedFavoriteVehicleIndex;
            case MenuLevel.NpcRoot:
                return _selectedNpcRootIndex;
            case MenuLevel.NpcCategories:
                return _selectedNpcCategoryIndex;
            case MenuLevel.NpcList:
                return _selectedNpcIndex;
            case MenuLevel.NpcFavorites:
                return _selectedNpcFavoriteIndex;
            case MenuLevel.NpcSettings:
                return _selectedNpcSettingIndex;
            default:
                return 0;
        }
    }

    private void SetCurrentSelectionIndex(int value)
    {
        switch (_menuLevel)
        {
            case MenuLevel.Root:
                _selectedRootIndex = value;
                break;
            case MenuLevel.VehicleCategories:
                _selectedVehicleCategoryIndex = value;
                _selectedVehicleIndex = ClampIndex(_selectedVehicleIndex, CurrentVehicleCategory.Vehicles.Length);
                break;
            case MenuLevel.VehicleList:
                _selectedVehicleIndex = value;
                break;
            case MenuLevel.VehicleRecents:
                _selectedRecentVehicleIndex = value;
                break;
            case MenuLevel.VehicleFavorites:
                _selectedFavoriteVehicleIndex = value;
                break;
            case MenuLevel.NpcRoot:
                _selectedNpcRootIndex = value;
                break;
            case MenuLevel.NpcCategories:
                _selectedNpcCategoryIndex = value;
                _selectedNpcIndex = ClampIndex(_selectedNpcIndex, CurrentNpcCategory.Peds.Length);
                break;
            case MenuLevel.NpcList:
                _selectedNpcIndex = value;
                break;
            case MenuLevel.NpcFavorites:
                _selectedNpcFavoriteIndex = value;
                break;
            case MenuLevel.NpcSettings:
                _selectedNpcSettingIndex = value;
                break;
        }
    }

    private void ClampSelections()
    {
        _selectedRootIndex = ClampIndex(_selectedRootIndex, GetRootItemCount());
        _selectedVehicleCategoryIndex = ClampIndex(_selectedVehicleCategoryIndex, VehicleCatalog.Categories.Length);
        _selectedVehicleIndex = ClampIndex(_selectedVehicleIndex, CurrentVehicleCategory.Vehicles.Length);
        _selectedRecentVehicleIndex = ClampIndex(_selectedRecentVehicleIndex, _recentVehicles.Count);
        _selectedFavoriteVehicleIndex = ClampIndex(_selectedFavoriteVehicleIndex, _favoriteVehicles.Count);
        _selectedNpcRootIndex = ClampIndex(_selectedNpcRootIndex, GetNpcRootItemCount());
        _selectedNpcCategoryIndex = ClampIndex(_selectedNpcCategoryIndex, PedCatalog.Categories.Length);
        _selectedNpcIndex = ClampIndex(_selectedNpcIndex, CurrentNpcCategory.Peds.Length);
        _selectedNpcFavoriteIndex = ClampIndex(_selectedNpcFavoriteIndex, _favoriteNpcs.Count);
        _selectedNpcSettingIndex = ClampIndex(_selectedNpcSettingIndex, GetNpcSettingItemCount());
    }

    private static int ClampIndex(int index, int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        if (index < 0)
        {
            return 0;
        }

        if (index >= count)
        {
            return count - 1;
        }

        return index;
    }

    private static bool IsMenuUpPressed()
    {
        return Game.IsControlJustPressed(GtaControl.FrontendUp)
            || Game.IsControlJustPressed(GtaControl.PhoneUp);
    }

    private static bool IsMenuDownPressed()
    {
        return Game.IsControlJustPressed(GtaControl.FrontendDown)
            || Game.IsControlJustPressed(GtaControl.PhoneDown);
    }

    private static bool IsPagePreviousPressed()
    {
        return Game.IsControlJustPressed(GtaControl.FrontendLb);
    }

    private static bool IsPageNextPressed()
    {
        return Game.IsControlJustPressed(GtaControl.FrontendRb);
    }

    private static bool IsFavoriteTogglePressed()
    {
        return Game.IsControlJustPressed(GtaControl.FrontendX)
            || Game.IsControlJustPressed(GtaControl.PhoneExtraOption);
    }

    private static bool IsMenuSecondaryActionPressed()
    {
        return Game.IsControlJustPressed(GtaControl.FrontendY)
            || Game.IsControlJustPressed(GtaControl.PhoneOption);
    }

    private static bool IsMenuAcceptPressed()
    {
        return Game.IsControlJustPressed(GtaControl.FrontendAccept)
            || Game.IsControlJustPressed(GtaControl.PhoneSelect);
    }

    private static bool IsMenuCancelPressed()
    {
        return Game.IsControlJustPressed(GtaControl.FrontendCancel)
            || Game.IsControlJustPressed(GtaControl.PhoneCancel);
    }

    private static void DisableMenuControls()
    {
        Game.DisableControlThisFrame(GtaControl.FrontendRight);
        Game.DisableControlThisFrame(GtaControl.FrontendLeft);
        Game.DisableControlThisFrame(GtaControl.FrontendUp);
        Game.DisableControlThisFrame(GtaControl.FrontendDown);
        Game.DisableControlThisFrame(GtaControl.FrontendAccept);
        Game.DisableControlThisFrame(GtaControl.FrontendCancel);
        Game.DisableControlThisFrame(GtaControl.FrontendX);
        Game.DisableControlThisFrame(GtaControl.FrontendY);
        Game.DisableControlThisFrame(GtaControl.FrontendLb);
        Game.DisableControlThisFrame(GtaControl.FrontendRb);
        Game.DisableControlThisFrame(GtaControl.PhoneRight);
        Game.DisableControlThisFrame(GtaControl.PhoneLeft);
        Game.DisableControlThisFrame(GtaControl.PhoneUp);
        Game.DisableControlThisFrame(GtaControl.PhoneDown);
        Game.DisableControlThisFrame(GtaControl.PhoneSelect);
        Game.DisableControlThisFrame(GtaControl.PhoneCancel);
        Game.DisableControlThisFrame(GtaControl.PhoneExtraOption);
        Game.DisableControlThisFrame(GtaControl.PhoneOption);
    }

    private void DrawMenu()
    {
        int count = GetCurrentItemCount();
        int selectedIndex = count > 0 ? GetCurrentSelectionIndex() : 0;
        int pageStart = count > 0 ? (selectedIndex / MenuPageSize) * MenuPageSize : 0;
        int pageEnd = Math.Min(pageStart + MenuPageSize, count);

        Function.Call(Hash.DRAW_RECT, 0.19f, 0.29f, 0.36f, 0.50f, 0, 0, 0, 195);
        Function.Call(Hash.DRAW_RECT, 0.19f, 0.08f, 0.36f, 0.06f, 20, 20, 20, 230);
        DrawText(GetHeaderText(), 0.03f, 0.061f, 0.43f, 255, 255, 255, 255, false);

        if (count == 0)
        {
            DrawText(GetEmptyStateText(), 0.03f, 0.16f, 0.33f, 220, 220, 220, 255, false);
        }
        else
        {
            for (int i = pageStart; i < pageEnd; i++)
            {
                int visibleIndex = i - pageStart;
                float y = 0.12f + (visibleIndex * 0.045f);
                bool selected = i == selectedIndex;
                if (selected)
                {
                    Function.Call(Hash.DRAW_RECT, 0.19f, y + 0.015f, 0.33f, 0.038f, 45, 45, 45, 220);
                }

                string prefix = selected ? "> " : "  ";
                DrawText(prefix + GetItemLabel(i), 0.03f, y, 0.33f, 255, 255, 255, 255, false);
            }
        }

        DrawText(GetFooterLine1(pageStart, pageEnd, count), 0.03f, 0.49f, 0.28f, 200, 200, 200, 255, false);
        DrawText(GetFooterLine2(), 0.03f, 0.525f, 0.28f, 190, 190, 190, 255, false);
        DrawText(GetFooterLine3(), 0.03f, 0.56f, 0.28f, 190, 190, 190, 255, false);
    }

    private void DrawInstructionalButtons()
    {
        var buttons = new List<InstructionalButtonsRenderer.ButtonSpec>();
        if (_menuLevel == MenuLevel.Root)
        {
            buttons.Add(new InstructionalButtonsRenderer.ButtonSpec(30, "Select"));
            buttons.Add(new InstructionalButtonsRenderer.ButtonSpec(31, "Back"));
            if ((RootEntry)ClampIndex(_selectedRootIndex, GetRootItemCount()) == RootEntry.Crows)
            {
                buttons.Add(new InstructionalButtonsRenderer.ButtonSpec(33, "Spawn & Keep"));
            }

            buttons.Add(new InstructionalButtonsRenderer.ButtonSpec(45, "Browse"));
        }
        else if (_menuLevel == MenuLevel.NpcSettings)
        {
            buttons.Add(new InstructionalButtonsRenderer.ButtonSpec(30, "Change"));
            buttons.Add(new InstructionalButtonsRenderer.ButtonSpec(31, "Back"));
            buttons.Add(new InstructionalButtonsRenderer.ButtonSpec(45, "Browse"));
        }
        else if (_menuLevel == MenuLevel.VehicleList || _menuLevel == MenuLevel.VehicleRecents || _menuLevel == MenuLevel.VehicleFavorites || _menuLevel == MenuLevel.NpcList || _menuLevel == MenuLevel.NpcFavorites)
        {
            buttons.Add(new InstructionalButtonsRenderer.ButtonSpec(30, "Select"));
            buttons.Add(new InstructionalButtonsRenderer.ButtonSpec(33, "Spawn & Keep"));
            buttons.Add(new InstructionalButtonsRenderer.ButtonSpec(31, "Back"));
            buttons.Add(new InstructionalButtonsRenderer.ButtonSpec(32, "Favorite"));
            buttons.Add(new InstructionalButtonsRenderer.ButtonSpec(45, "Browse"));
            buttons.Add(new InstructionalButtonsRenderer.ButtonSpec(46, "Page"));
        }
        else
        {
            buttons.Add(new InstructionalButtonsRenderer.ButtonSpec(30, "Select"));
            buttons.Add(new InstructionalButtonsRenderer.ButtonSpec(31, "Back"));
            buttons.Add(new InstructionalButtonsRenderer.ButtonSpec(45, "Browse"));
            buttons.Add(new InstructionalButtonsRenderer.ButtonSpec(46, "Page"));
        }

        _instructionalButtons.Draw(buttons);
    }

    private string GetHeaderText()
    {
        switch (_menuLevel)
        {
            case MenuLevel.Root:
                return "Utility Menu";
            case MenuLevel.VehicleCategories:
                return "Utility Menu / Vehicles";
            case MenuLevel.VehicleList:
                return $"Utility Menu / Vehicles / {CurrentVehicleCategory.Name}";
            case MenuLevel.VehicleRecents:
                return "Utility Menu / Recent Vehicles";
            case MenuLevel.VehicleFavorites:
                return "Utility Menu / Vehicle Favorites";
            case MenuLevel.NpcRoot:
                return "Utility Menu / NPCs";
            case MenuLevel.NpcCategories:
                return "Utility Menu / NPCs / Spawns";
            case MenuLevel.NpcList:
                return $"Utility Menu / NPCs / {CurrentNpcCategory.Name}";
            case MenuLevel.NpcFavorites:
                return "Utility Menu / NPC Favorites";
            case MenuLevel.NpcSettings:
                return "Utility Menu / NPC Settings";
            default:
                return "Utility Menu";
        }
    }

    private string GetItemLabel(int index)
    {
        switch (_menuLevel)
        {
            case MenuLevel.Root:
                return GetRootItemLabel(index);
            case MenuLevel.VehicleCategories:
                return VehicleCatalog.Categories[index].Name;
            case MenuLevel.VehicleList:
                return FormatVehicleLabel(CurrentVehicleCategory.Vehicles[index]);
            case MenuLevel.VehicleRecents:
                return FormatVehicleLabel(_recentVehicles[index]);
            case MenuLevel.VehicleFavorites:
                return FormatVehicleLabel(_favoriteVehicles[index]);
            case MenuLevel.NpcRoot:
                return GetNpcRootLabel(index);
            case MenuLevel.NpcCategories:
                return PedCatalog.Categories[index].Name;
            case MenuLevel.NpcList:
                return FormatPedLabel(CurrentNpcCategory.Peds[index]);
            case MenuLevel.NpcFavorites:
                return FormatPedLabel(_favoriteNpcs[index]);
            case MenuLevel.NpcSettings:
                return GetNpcSettingLabel(index);
            default:
                return string.Empty;
        }
    }

    private string GetRootItemLabel(int index)
    {
        switch ((RootEntry)index)
        {
            case RootEntry.Vehicles:
                return "Vehicles";
            case RootEntry.RecentVehicles:
                return $"Recent Vehicles ({_recentVehicles.Count})";
            case RootEntry.Favorites:
                return $"Favorites ({_favoriteVehicles.Count})";
            case RootEntry.Crows:
                return "Crows";
            case RootEntry.Npcs:
                return "NPCs";
            default:
                return string.Empty;
        }
    }

    private string GetNpcRootLabel(int index)
    {
        switch ((NpcRootEntry)index)
        {
            case NpcRootEntry.Spawns:
                return "Spawns";
            case NpcRootEntry.Favorites:
                return $"Favorites ({_favoriteNpcs.Count})";
            case NpcRootEntry.Settings:
                return "Settings";
            default:
                return string.Empty;
        }
    }

    private string GetNpcSettingLabel(int index)
    {
        switch ((NpcSettingEntry)index)
        {
            case NpcSettingEntry.Armed:
                return "Armed: " + (_npcSettings.Armed ? "On" : "Off");
            case NpcSettingEntry.Disposition:
                return "Disposition: " + (_npcSettings.Disposition == NpcDisposition.Ally ? "Ally" : "Enemy");
            case NpcSettingEntry.VehicleMode:
                return "Vehicle Mode: " + (_npcSettings.VehicleMode == NpcVehicleMode.UsePlayerCar ? "Use Player Car" : "Drive Own Car");
            case NpcSettingEntry.AggressiveDrive:
                return "Aggressive Drive: " + (_npcSettings.AggressiveDrive ? "On" : "Off");
            case NpcSettingEntry.OffRoad:
                return "Off-Road: " + (_npcSettings.UseOffRoad ? "On" : "Off");
            default:
                return string.Empty;
        }
    }

    private string FormatVehicleLabel(VehicleCatalog.VehicleEntry vehicle)
    {
        return IsVehicleFavorite(vehicle.ModelName)
            ? vehicle.DisplayName + " [*]"
            : vehicle.DisplayName;
    }

    private string FormatPedLabel(PedCatalog.PedEntry ped)
    {
        return IsNpcFavorite(ped.ModelName)
            ? ped.DisplayName + " [*]"
            : ped.DisplayName;
    }

    private string GetFooterLine1(int pageStart, int pageEnd, int count)
    {
        switch (_menuLevel)
        {
            case MenuLevel.Root:
                return $"Root {FormatRange(pageStart, pageEnd, count)}";
            case MenuLevel.VehicleCategories:
                return $"Categories {FormatRange(pageStart, pageEnd, count)}";
            case MenuLevel.VehicleList:
                return $"{CurrentVehicleCategory.Name}: {FormatRange(pageStart, pageEnd, count)}";
            case MenuLevel.VehicleRecents:
                return $"Recent Vehicles: {FormatRange(pageStart, pageEnd, count)}";
            case MenuLevel.VehicleFavorites:
                return $"Favorites: {FormatRange(pageStart, pageEnd, count)}";
            case MenuLevel.NpcRoot:
                return $"NPC root: {FormatRange(pageStart, pageEnd, count)}";
            case MenuLevel.NpcCategories:
                return $"NPC categories: {FormatRange(pageStart, pageEnd, count)}";
            case MenuLevel.NpcList:
                return $"{CurrentNpcCategory.Name}: {FormatRange(pageStart, pageEnd, count)}";
            case MenuLevel.NpcFavorites:
                return $"NPC Favorites: {FormatRange(pageStart, pageEnd, count)}";
            case MenuLevel.NpcSettings:
                return $"NPC Settings: {FormatRange(pageStart, pageEnd, count)}";
            default:
                return string.Empty;
        }
    }

    private string GetFooterLine2()
    {
        VehicleCatalog.VehicleEntry vehicle = GetCurrentVehicleEntry();
        if (vehicle != null)
        {
            return "Model: " + vehicle.ModelName;
        }

        PedCatalog.PedEntry ped = GetCurrentPedEntry();
        if (ped != null)
        {
            return $"Model: {ped.ModelName}  Props: {ped.Props}  Components: {ped.Components}";
        }

        switch (_menuLevel)
        {
            case MenuLevel.Root:
                return (RootEntry)ClampIndex(_selectedRootIndex, GetRootItemCount()) == RootEntry.Crows
                    ? "A spawn  Y spawn & keep  B close"
                    : "A select  B close";
            case MenuLevel.NpcSettings:
                return "A change setting  B back";
            default:
                return string.Empty;
        }
    }

    private string GetFooterLine3()
    {
        switch (_menuLevel)
        {
            case MenuLevel.Root:
                return "Hold D-pad Right 0.5s or press F6 to open";
            case MenuLevel.NpcSettings:
                return "Settings persist between sessions";
            case MenuLevel.VehicleList:
            case MenuLevel.VehicleRecents:
            case MenuLevel.VehicleFavorites:
            case MenuLevel.NpcList:
            case MenuLevel.NpcFavorites:
                return "A spawn  Y spawn & keep  X favorite  B back";
            default:
                return "LB/RB page";
        }
    }

    private static string FormatRange(int pageStart, int pageEnd, int count)
    {
        if (count <= 0)
        {
            return "0/0";
        }

        return (pageStart + 1).ToString() + "-" + pageEnd.ToString() + "/" + count.ToString();
    }

    private string GetEmptyStateText()
    {
        switch (_menuLevel)
        {
            case MenuLevel.VehicleRecents:
                return "No recent vehicles yet.";
            case MenuLevel.VehicleFavorites:
                return "No vehicle favorites yet. Press X on a vehicle to add one.";
            case MenuLevel.NpcFavorites:
                return "No NPC favorites yet. Press X on a ped to add one.";
            default:
                return "Nothing to show here.";
        }
    }

    private VehicleCatalog.VehicleEntry GetCurrentVehicleEntry()
    {
        switch (_menuLevel)
        {
            case MenuLevel.VehicleList:
                return CurrentVehicleCategory.Vehicles.Length == 0 ? null : CurrentVehicleCategory.Vehicles[ClampIndex(_selectedVehicleIndex, CurrentVehicleCategory.Vehicles.Length)];
            case MenuLevel.VehicleRecents:
                return _recentVehicles.Count == 0 ? null : _recentVehicles[ClampIndex(_selectedRecentVehicleIndex, _recentVehicles.Count)];
            case MenuLevel.VehicleFavorites:
                return _favoriteVehicles.Count == 0 ? null : _favoriteVehicles[ClampIndex(_selectedFavoriteVehicleIndex, _favoriteVehicles.Count)];
            default:
                return null;
        }
    }

    private PedCatalog.PedEntry GetCurrentPedEntry()
    {
        switch (_menuLevel)
        {
            case MenuLevel.NpcList:
                return CurrentNpcCategory.Peds.Length == 0 ? null : CurrentNpcCategory.Peds[ClampIndex(_selectedNpcIndex, CurrentNpcCategory.Peds.Length)];
            case MenuLevel.NpcFavorites:
                return _favoriteNpcs.Count == 0 ? null : _favoriteNpcs[ClampIndex(_selectedNpcFavoriteIndex, _favoriteNpcs.Count)];
            default:
                return null;
        }
    }

    private bool IsVehicleFavorite(string modelName)
    {
        return FindVehicleIndexByModel(_favoriteVehicles, modelName) >= 0;
    }

    private bool IsNpcFavorite(string modelName)
    {
        return FindPedIndexByModel(_favoriteNpcs, modelName) >= 0;
    }

    private static int FindVehicleIndexByModel(List<VehicleCatalog.VehicleEntry> vehicles, string modelName)
    {
        for (int i = 0; i < vehicles.Count; i++)
        {
            if (string.Equals(vehicles[i].ModelName, modelName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindPedIndexByModel(List<PedCatalog.PedEntry> peds, string modelName)
    {
        for (int i = 0; i < peds.Count; i++)
        {
            if (string.Equals(peds[i].ModelName, modelName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private void AddRecentVehicle(VehicleCatalog.VehicleEntry vehicle)
    {
        int existingIndex = FindVehicleIndexByModel(_recentVehicles, vehicle.ModelName);
        if (existingIndex >= 0)
        {
            _recentVehicles.RemoveAt(existingIndex);
        }

        _recentVehicles.Insert(0, vehicle);
        while (_recentVehicles.Count > MaxRecentVehicles)
        {
            _recentVehicles.RemoveAt(_recentVehicles.Count - 1);
        }

        _selectedRecentVehicleIndex = ClampIndex(_selectedRecentVehicleIndex, _recentVehicles.Count);
    }

    private void LoadVehicleFavorites()
    {
        _favoriteVehicles.Clear();
        if (!File.Exists(VehicleFavoritesFilePath))
        {
            return;
        }

        foreach (string line in File.ReadAllLines(VehicleFavoritesFilePath))
        {
            string modelName = line.Trim();
            if (string.IsNullOrEmpty(modelName) || !VehicleCatalogByModel.TryGetValue(modelName, out VehicleCatalog.VehicleEntry vehicle))
            {
                continue;
            }

            if (FindVehicleIndexByModel(_favoriteVehicles, vehicle.ModelName) >= 0)
            {
                continue;
            }

            _favoriteVehicles.Add(vehicle);
        }
    }

    private void SaveVehicleFavorites()
    {
        try
        {
            string[] lines = new string[_favoriteVehicles.Count];
            for (int i = 0; i < _favoriteVehicles.Count; i++)
            {
                lines[i] = _favoriteVehicles[i].ModelName;
            }

            File.WriteAllLines(VehicleFavoritesFilePath, lines);
        }
        catch (Exception ex)
        {
            Log($"Save vehicle favorites failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void LoadNpcFavorites()
    {
        _favoriteNpcs.Clear();
        if (!File.Exists(NpcFavoritesFilePath))
        {
            if (PedCatalogByModel.TryGetValue(DefaultNpcFavoriteModelName, out PedCatalog.PedEntry defaultFavorite))
            {
                _favoriteNpcs.Add(defaultFavorite);
                SaveNpcFavorites();
            }

            return;
        }

        foreach (string line in File.ReadAllLines(NpcFavoritesFilePath))
        {
            string modelName = line.Trim();
            if (string.IsNullOrEmpty(modelName) || !PedCatalogByModel.TryGetValue(modelName, out PedCatalog.PedEntry ped))
            {
                continue;
            }

            if (FindPedIndexByModel(_favoriteNpcs, ped.ModelName) >= 0)
            {
                continue;
            }

            _favoriteNpcs.Add(ped);
        }
    }

    private void SaveNpcFavorites()
    {
        try
        {
            string[] lines = new string[_favoriteNpcs.Count];
            for (int i = 0; i < _favoriteNpcs.Count; i++)
            {
                lines[i] = _favoriteNpcs[i].ModelName;
            }

            File.WriteAllLines(NpcFavoritesFilePath, lines);
        }
        catch (Exception ex)
        {
            Log($"Save NPC favorites failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void LoadSettings()
    {
        if (!File.Exists(SettingsFilePath))
        {
            return;
        }

        foreach (string line in File.ReadAllLines(SettingsFilePath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string[] parts = line.Split(new[] { '=' }, 2);
            if (parts.Length != 2)
            {
                continue;
            }

            string key = parts[0].Trim();
            string value = parts[1].Trim();
            switch (key)
            {
                case "NpcArmed":
                    if (bool.TryParse(value, out bool armed))
                    {
                        _npcSettings.Armed = armed;
                    }
                    break;
                case "NpcDisposition":
                    if (string.Equals(value, "Enemy", StringComparison.OrdinalIgnoreCase))
                    {
                        _npcSettings.Disposition = NpcDisposition.Enemy;
                    }
                    else if (string.Equals(value, "Ally", StringComparison.OrdinalIgnoreCase))
                    {
                        _npcSettings.Disposition = NpcDisposition.Ally;
                    }
                    break;
                case "NpcVehicleMode":
                    if (string.Equals(value, "DriveOwnCar", StringComparison.OrdinalIgnoreCase))
                    {
                        _npcSettings.VehicleMode = NpcVehicleMode.DriveOwnCar;
                    }
                    else if (string.Equals(value, "UsePlayerCar", StringComparison.OrdinalIgnoreCase))
                    {
                        _npcSettings.VehicleMode = NpcVehicleMode.UsePlayerCar;
                    }
                    break;
                case "NpcAggressiveDrive":
                    if (bool.TryParse(value, out bool aggressiveDrive))
                    {
                        _npcSettings.AggressiveDrive = aggressiveDrive;
                    }
                    break;
                case "NpcUseOffRoad":
                    if (bool.TryParse(value, out bool useOffRoad))
                    {
                        _npcSettings.UseOffRoad = useOffRoad;
                    }
                    break;
            }
        }
    }

    private void SaveSettings()
    {
        try
        {
            string[] lines =
            {
                "NpcArmed=" + _npcSettings.Armed,
                "NpcDisposition=" + _npcSettings.Disposition,
                "NpcVehicleMode=" + _npcSettings.VehicleMode,
                "NpcAggressiveDrive=" + _npcSettings.AggressiveDrive,
                "NpcUseOffRoad=" + _npcSettings.UseOffRoad,
            };
            File.WriteAllLines(SettingsFilePath, lines);
        }
        catch (Exception ex)
        {
            Log($"Save settings failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void TrySpawnCrows()
    {
        int playerPed = Function.Call<int>(Hash.PLAYER_PED_ID);
        if (playerPed == 0 || !Function.Call<bool>(Hash.DOES_ENTITY_EXIST, playerPed) || Function.Call<bool>(Hash.IS_ENTITY_DEAD, playerPed))
        {
            SafeNotify("~r~Crows failed: player is not available.");
            Log("Crows failed: player not available.");
            return;
        }

        int modelHash = Game.GenerateHash(CrowModelName);
        if (!Function.Call<bool>(Hash.IS_MODEL_IN_CDIMAGE, modelHash) || !Function.Call<bool>(Hash.IS_MODEL_VALID, modelHash))
        {
            SafeNotify("~r~Crows failed: model not available.");
            Log("Crows failed: model unavailable.");
            return;
        }

        Function.Call(Hash.REQUEST_MODEL, modelHash);
        int requestStart = Environment.TickCount;
        while (!Function.Call<bool>(Hash.HAS_MODEL_LOADED, modelHash))
        {
            Script.Yield();
            if (HasElapsed(requestStart, 1000))
            {
                SafeNotify("~r~Crows failed: model timeout.");
                Log("Crows failed: model load timeout.");
                return;
            }
        }

        Vector3 playerPosition = Function.Call<Vector3>(Hash.GET_ENTITY_COORDS, playerPed, true);
        float playerHeading = Function.Call<float>(Hash.GET_ENTITY_HEADING, playerPed);
        int spawnedCount = 0;
        for (int i = 0; i < 10; i++)
        {
            float angle = (float)(i * (Math.PI * 2.0 / 10.0));
            float radius = 1.5f + (i % 3) * 0.7f;
            Vector3 spawnPosition = new Vector3(
                playerPosition.X + (float)Math.Cos(angle) * radius,
                playerPosition.Y + (float)Math.Sin(angle) * radius,
                playerPosition.Z + 0.2f);

            int crowPed = Function.Call<int>(Hash.CREATE_PED, 28, modelHash, spawnPosition.X, spawnPosition.Y, spawnPosition.Z, playerHeading, true, false);
            if (crowPed == 0)
            {
                continue;
            }

            Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, crowPed, true, false);
            Function.Call(Hash.TASK_WANDER_STANDARD, crowPed, 10f, 10);
            _spawnedCrows.Add(crowPed);
            spawnedCount++;
        }

        Function.Call(Hash.SET_MODEL_AS_NO_LONGER_NEEDED, modelHash);
        TrimCrowList();

        if (spawnedCount == 0)
        {
            SafeNotify("~r~Crows failed: could not create crows.");
            Log("Crows failed: no crows created.");
            return;
        }

        SafeNotify($"~g~Crows spawned: {spawnedCount}/10.");
        Log($"Crows spawned: {spawnedCount}/10.");
    }

    private void CleanupTrackedCrows()
    {
        for (int i = _spawnedCrows.Count - 1; i >= 0; i--)
        {
            int crowPed = _spawnedCrows[i];
            if (crowPed == 0 || !Function.Call<bool>(Hash.DOES_ENTITY_EXIST, crowPed) || Function.Call<bool>(Hash.IS_ENTITY_DEAD, crowPed))
            {
                _spawnedCrows.RemoveAt(i);
            }
        }
    }

    private void TrimCrowList()
    {
        while (_spawnedCrows.Count > MaxCrowCount)
        {
            int oldest = _spawnedCrows[0];
            _spawnedCrows.RemoveAt(0);
            if (oldest == 0 || !Function.Call<bool>(Hash.DOES_ENTITY_EXIST, oldest))
            {
                continue;
            }

            Function.Call(Hash.SET_ENTITY_AS_NO_LONGER_NEEDED, oldest);
        }
    }

    private void TrySpawnGirls(int requestedCount)
    {
        if (requestedCount <= 0)
        {
            return;
        }

        int playerPed = Function.Call<int>(Hash.PLAYER_PED_ID);
        if (playerPed == 0 || !Function.Call<bool>(Hash.DOES_ENTITY_EXIST, playerPed) || Function.Call<bool>(Hash.IS_ENTITY_DEAD, playerPed))
        {
            SafeNotify("~r~Girls failed: player is not available.");
            Log("Girls failed: player unavailable.");
            return;
        }

        int modelHash = Game.GenerateHash(GirlModelName);
        if (!Function.Call<bool>(Hash.IS_MODEL_IN_CDIMAGE, modelHash) || !Function.Call<bool>(Hash.IS_MODEL_VALID, modelHash))
        {
            SafeNotify("~r~Girls failed: model not available.");
            Log("Girls failed: model unavailable.");
            return;
        }

        Function.Call(Hash.REQUEST_MODEL, modelHash);
        int requestStart = Environment.TickCount;
        while (!Function.Call<bool>(Hash.HAS_MODEL_LOADED, modelHash))
        {
            Script.Yield();
            if (HasElapsed(requestStart, 1000))
            {
                SafeNotify("~r~Girls failed: model timeout.");
                Log("Girls failed: model load timeout.");
                return;
            }
        }

        int spawnedCount = 0;
        for (int i = 0; i < requestedCount; i++)
        {
            Vector3 playerPosition = Function.Call<Vector3>(Hash.GET_ENTITY_COORDS, playerPed, true);
            float playerHeading = Function.Call<float>(Hash.GET_ENTITY_HEADING, playerPed);
            Vector3 forward = HeadingToDirection(playerHeading);
            Vector3 right = new Vector3(forward.Y, -forward.X, 0f);
            int slot = _girlCompanions.Count + spawnedCount;
            float lane = (slot % 2 == 0) ? 2.0f : -2.0f;
            float step = 1.5f + (slot / 2) * 1.2f;
            Vector3 spawnPosition = playerPosition + right * lane + forward * step;

            int companionPed = Function.Call<int>(Hash.CREATE_PED, 26, modelHash, spawnPosition.X, spawnPosition.Y, spawnPosition.Z, playerHeading, true, false);
            if (companionPed == 0)
            {
                continue;
            }

            ConfigureGirlCompanion(companionPed);
            _girlCompanions.Add(companionPed);
            EnsureGirlBlip(companionPed);
            spawnedCount++;
        }

        Function.Call(Hash.SET_MODEL_AS_NO_LONGER_NEEDED, modelHash);
        EnforceGirlLimit();

        if (spawnedCount == 0)
        {
            SafeNotify("~r~Girls failed: no companions created.");
            Log("Girls failed: no companions created.");
            return;
        }

        SafeNotify($"~g~Girls spawned: {spawnedCount}/{requestedCount}.");
        Log($"Girls spawned: {spawnedCount}/{requestedCount}. Active: {_girlCompanions.Count}");
    }

    private void ConfigureGirlCompanion(int companionPed)
    {
        int playerPed = Function.Call<int>(Hash.PLAYER_PED_ID);
        int playerId = Function.Call<int>(Hash.PLAYER_ID);
        int playerGroup = Function.Call<int>(Hash.GET_PLAYER_GROUP, playerId);

        Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, companionPed, true, false);
        Function.Call(Hash.SET_PED_AS_GROUP_MEMBER, companionPed, playerGroup);
        Function.Call(Hash.SET_PED_NEVER_LEAVES_GROUP, companionPed, true);
        Function.Call(Hash.SET_PED_CAN_TELEPORT_TO_GROUP_LEADER, companionPed, playerGroup, true);
        Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, companionPed, 0, false);
        Function.Call(Hash.SET_PED_COMBAT_ABILITY, companionPed, 2);
        Function.Call(Hash.SET_PED_COMBAT_MOVEMENT, companionPed, 2);
        Function.Call(Hash.SET_PED_COMBAT_RANGE, companionPed, 2);
        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, companionPed, 1, true);
        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, companionPed, 2, true);
        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, companionPed, 3, false);
        Function.Call(Hash.SET_PED_ACCURACY, companionPed, VehicleCombatAccuracy);
        Function.Call(Hash.SET_PED_SEEING_RANGE, companionPed, EngageDistance);
        Function.Call(Hash.SET_PED_HEARING_RANGE, companionPed, EngageDistance);
        Function.Call(Hash.SET_PED_KEEP_TASK, companionPed, true);
        Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, companionPed, false);
        Function.Call(Hash.SET_PED_STAY_IN_VEHICLE_WHEN_JACKED, companionPed, true);
        Function.Call(Hash.SET_ENTITY_HEALTH, companionPed, 250);
        Function.Call(Hash.SET_PED_ARMOUR, companionPed, 100);
        Function.Call(Hash.GIVE_WEAPON_TO_PED, companionPed, (uint)WeaponHash.Pistol, 240, false, true);
        Function.Call(Hash.SET_CURRENT_PED_WEAPON, companionPed, (uint)WeaponHash.Pistol, true);
        Function.Call(Hash.TASK_FOLLOW_TO_OFFSET_OF_ENTITY, companionPed, playerPed, 0f, -1.3f, 0f, 3f, -1, 2f, true);
    }

    private void EnforceGirlLimit()
    {
        while (_girlCompanions.Count > MaxGirlCount)
        {
            int oldest = _girlCompanions[0];
            _girlCompanions.RemoveAt(0);
            ClearGirlVehicleAssignment(oldest);
            RemoveGirlBlip(oldest);
            if (oldest == 0 || !Function.Call<bool>(Hash.DOES_ENTITY_EXIST, oldest))
            {
                continue;
            }

            if (!Function.Call<bool>(Hash.IS_ENTITY_DEAD, oldest))
            {
                Function.Call(Hash.SET_ENTITY_HEALTH, oldest, 0);
            }
        }
    }

    private VehicleAssignment GetOrCreateGirlVehicleAssignment(int pedHandle)
    {
        if (!_girlVehicleAssignments.TryGetValue(pedHandle, out VehicleAssignment assignment))
        {
            assignment = new VehicleAssignment();
            _girlVehicleAssignments[pedHandle] = assignment;
        }

        return assignment;
    }

    private void ClearGirlVehicleAssignment(int pedHandle)
    {
        _girlVehicleAssignments.Remove(pedHandle);
    }

    private void EnsureGirlBlip(int pedHandle)
    {
        if (!IsPedAlive(pedHandle))
        {
            RemoveGirlBlip(pedHandle);
            return;
        }

        if (_girlBlips.TryGetValue(pedHandle, out Blip existingBlip) && existingBlip != null && existingBlip.Exists())
        {
            return;
        }

        int handle = Function.Call<int>(Hash.ADD_BLIP_FOR_ENTITY, pedHandle);
        if (!Function.Call<bool>(Hash.DOES_BLIP_EXIST, handle))
        {
            return;
        }

        var blip = new Blip(handle)
        {
            Sprite = BlipSprite.GTAOFriendly,
            Color = BlipColor.Blue,
            IsFriendly = true,
            IsShortRange = false,
            Name = "Girl Companion",
        };
        _girlBlips[pedHandle] = blip;
    }

    private void RemoveGirlBlip(int pedHandle)
    {
        if (!_girlBlips.TryGetValue(pedHandle, out Blip blip))
        {
            return;
        }

        if (blip != null && blip.Exists())
        {
            blip.Delete();
        }

        _girlBlips.Remove(pedHandle);
    }

    private void EnsureNpcBlip(ManagedNpc npc)
    {
        if (npc == null)
        {
            return;
        }

        if (npc.Disposition != NpcDisposition.Ally || !IsPedAlive(npc.PedHandle))
        {
            RemoveNpcBlip(npc);
            return;
        }

        if (npc.AllyBlip != null && npc.AllyBlip.Exists())
        {
            return;
        }

        int handle = Function.Call<int>(Hash.ADD_BLIP_FOR_ENTITY, npc.PedHandle);
        if (!Function.Call<bool>(Hash.DOES_BLIP_EXIST, handle))
        {
            return;
        }

        npc.AllyBlip = new Blip(handle)
        {
            Sprite = BlipSprite.GTAOFriendly,
            Color = BlipColor.Blue,
            IsFriendly = true,
            IsShortRange = false,
            Name = "Ally NPC",
        };
    }

    private static void RemoveNpcBlip(ManagedNpc npc)
    {
        if (npc?.AllyBlip != null && npc.AllyBlip.Exists())
        {
            npc.AllyBlip.Delete();
        }

        if (npc != null)
        {
            npc.AllyBlip = null;
        }
    }

    private static void ClearVehicleAssignment(VehicleAssignment assignment)
    {
        if (assignment == null)
        {
            return;
        }

        assignment.VehicleHandle = 0;
        assignment.SeatIndex = int.MinValue;
        assignment.LastTaskTick = 0;
        assignment.AssignmentStartTick = 0;
    }

    private static void ClearNpcVehicleAssignment(ManagedNpc npc)
    {
        if (npc == null)
        {
            return;
        }

        npc.AssignedVehicle = 0;
        npc.AssignedSeat = int.MinValue;
        npc.LastVehicleTaskTick = 0;
        npc.AssignmentStartTick = 0;
    }

    private void BuildPlayerVehicleSeatClaims(int playerVehicle, Dictionary<int, int> seatClaims)
    {
        if (playerVehicle == 0)
        {
            return;
        }

        ReserveOccupiedPassengerSeats(playerVehicle, seatClaims);

        for (int i = 0; i < _girlCompanions.Count; i++)
        {
            int pedHandle = _girlCompanions[i];
            if (!IsPedAlive(pedHandle))
            {
                continue;
            }

            if (_girlVehicleAssignments.TryGetValue(pedHandle, out VehicleAssignment assignment) && assignment.VehicleHandle == playerVehicle && assignment.SeatIndex >= 0 && !seatClaims.ContainsKey(assignment.SeatIndex))
            {
                seatClaims[assignment.SeatIndex] = pedHandle;
            }
        }

        for (int i = 0; i < _managedNpcs.Count; i++)
        {
            ManagedNpc npc = _managedNpcs[i];
            if (!IsPedAlive(npc.PedHandle))
            {
                continue;
            }

            if (npc.AssignedVehicle == playerVehicle && npc.AssignedSeat >= 0 && !seatClaims.ContainsKey(npc.AssignedSeat))
            {
                seatClaims[npc.AssignedSeat] = npc.PedHandle;
            }
        }
    }

    private static void ReserveOccupiedPassengerSeats(int vehicle, Dictionary<int, int> seatClaims)
    {
        int maxPassengers = Function.Call<int>(Hash.GET_VEHICLE_MAX_NUMBER_OF_PASSENGERS, vehicle);
        for (int seat = 0; seat < maxPassengers; seat++)
        {
            int pedInSeat = Function.Call<int>(Hash.GET_PED_IN_VEHICLE_SEAT, vehicle, seat, false);
            if (pedInSeat != 0)
            {
                seatClaims[seat] = pedInSeat;
            }
        }
    }

    private void BuildDriverVehicleClaims(int playerVehicle, Dictionary<int, int> driverClaims)
    {
        for (int i = 0; i < _girlCompanions.Count; i++)
        {
            int pedHandle = _girlCompanions[i];
            if (!IsPedAlive(pedHandle))
            {
                continue;
            }

            if (_girlVehicleAssignments.TryGetValue(pedHandle, out VehicleAssignment assignment) && assignment.VehicleHandle != 0 && assignment.VehicleHandle != playerVehicle && assignment.SeatIndex == -1 && !driverClaims.ContainsKey(assignment.VehicleHandle))
            {
                driverClaims[assignment.VehicleHandle] = pedHandle;
            }

            if (Function.Call<bool>(Hash.IS_PED_IN_ANY_VEHICLE, pedHandle, false))
            {
                int vehicle = Function.Call<int>(Hash.GET_VEHICLE_PED_IS_IN, pedHandle, false);
                if (vehicle != 0 && vehicle != playerVehicle && IsPedDriverOfVehicle(pedHandle, vehicle) && !driverClaims.ContainsKey(vehicle))
                {
                    driverClaims[vehicle] = pedHandle;
                }
            }
        }

        for (int i = 0; i < _managedNpcs.Count; i++)
        {
            ManagedNpc npc = _managedNpcs[i];
            if (!IsPedAlive(npc.PedHandle))
            {
                continue;
            }

            if (npc.AssignedVehicle != 0 && npc.AssignedVehicle != playerVehicle && npc.AssignedSeat == -1 && !driverClaims.ContainsKey(npc.AssignedVehicle))
            {
                driverClaims[npc.AssignedVehicle] = npc.PedHandle;
            }

            if (Function.Call<bool>(Hash.IS_PED_IN_ANY_VEHICLE, npc.PedHandle, false))
            {
                int vehicle = Function.Call<int>(Hash.GET_VEHICLE_PED_IS_IN, npc.PedHandle, false);
                if (vehicle != 0 && vehicle != playerVehicle && IsPedDriverOfVehicle(npc.PedHandle, vehicle) && !driverClaims.ContainsKey(vehicle))
                {
                    driverClaims[vehicle] = npc.PedHandle;
                }
            }
        }
    }

    private static bool CanReissueVehicleTask(int lastTaskTick)
    {
        return lastTaskTick == 0 || HasElapsed(lastTaskTick, VehicleTaskRetryMs);
    }

    private static bool ShouldForceDriverPlacement(int assignmentStartTick)
    {
        return assignmentStartTick != 0 && HasElapsed(assignmentStartTick, VehicleBoardingFallbackMs);
    }

    private static bool IsSeatClaimedBy(Dictionary<int, int> seatClaims, int seat, int pedHandle)
    {
        return seatClaims.TryGetValue(seat, out int ownerPed) && ownerPed == pedHandle;
    }

    private static bool IsSeatAvailableForPed(int vehicle, int seat, int pedHandle)
    {
        int pedInSeat = Function.Call<int>(Hash.GET_PED_IN_VEHICLE_SEAT, vehicle, seat, false);
        return pedInSeat == 0 || pedInSeat == pedHandle;
    }

    private static int FindFreePassengerSeat(int vehicle, Dictionary<int, int> seatClaims)
    {
        int maxPassengers = Function.Call<int>(Hash.GET_VEHICLE_MAX_NUMBER_OF_PASSENGERS, vehicle);
        for (int seat = 0; seat < maxPassengers; seat++)
        {
            if (seatClaims.ContainsKey(seat))
            {
                continue;
            }

            if (Function.Call<bool>(Hash.IS_VEHICLE_SEAT_FREE, vehicle, seat, false))
            {
                return seat;
            }
        }

        return -1;
    }

    private static void IssueEnterVehicleTask(int pedHandle, int vehicle, int seat, VehicleAssignment assignment)
    {
        Function.Call(Hash.TASK_ENTER_VEHICLE, pedHandle, vehicle, 10000, seat, 2.0f, 1, 0);
        assignment.LastTaskTick = Environment.TickCount;
    }

    private static void IssueEnterVehicleTask(int pedHandle, int vehicle, int seat, ManagedNpc npc)
    {
        Function.Call(Hash.TASK_ENTER_VEHICLE, pedHandle, vehicle, 10000, seat, 2.0f, 1, 0);
        npc.LastVehicleTaskTick = Environment.TickCount;
    }

    private bool TryAssignGirlToPlayerVehicle(int companion, VehicleAssignment assignment, int playerVehicle, Dictionary<int, int> seatClaims)
    {
        if (Function.Call<bool>(Hash.IS_PED_IN_VEHICLE, companion, playerVehicle, false))
        {
            int seat = GetPedSeatInVehicle(companion, playerVehicle);
            assignment.VehicleHandle = playerVehicle;
            assignment.SeatIndex = seat;
            if (seat >= 0)
            {
                seatClaims[seat] = companion;
            }

            return true;
        }

        bool companionInVehicle = Function.Call<bool>(Hash.IS_PED_IN_ANY_VEHICLE, companion, false);
        int companionVehicle = companionInVehicle ? Function.Call<int>(Hash.GET_VEHICLE_PED_IS_IN, companion, false) : 0;
        if (companionVehicle != 0 && companionVehicle != playerVehicle && assignment.VehicleHandle != companionVehicle)
        {
            if (CanReissueVehicleTask(assignment.LastTaskTick))
            {
                Function.Call(Hash.TASK_LEAVE_VEHICLE, companion, companionVehicle, 0);
                assignment.LastTaskTick = Environment.TickCount;
            }

            return true;
        }

        int targetSeat = -1;
        if (assignment.VehicleHandle == playerVehicle && assignment.SeatIndex >= 0)
        {
            if (IsSeatClaimedBy(seatClaims, assignment.SeatIndex, companion) && IsSeatAvailableForPed(playerVehicle, assignment.SeatIndex, companion))
            {
                targetSeat = assignment.SeatIndex;
            }
            else
            {
                ClearVehicleAssignment(assignment);
            }
        }

        if (targetSeat < 0)
        {
            targetSeat = FindFreePassengerSeat(playerVehicle, seatClaims);
            if (targetSeat < 0)
            {
                return false;
            }
        }

        assignment.VehicleHandle = playerVehicle;
        assignment.SeatIndex = targetSeat;
        seatClaims[targetSeat] = companion;
        if (companionVehicle != playerVehicle || CanReissueVehicleTask(assignment.LastTaskTick))
        {
            IssueEnterVehicleTask(companion, playerVehicle, targetSeat, assignment);
        }

        return true;
    }

    private bool TryAssignGirlToOwnVehicle(int companion, int playerPed, int playerVehicle, VehicleAssignment assignment, Dictionary<int, int> driverClaims)
    {
        bool companionInVehicle = Function.Call<bool>(Hash.IS_PED_IN_ANY_VEHICLE, companion, false);
        int companionVehicle = companionInVehicle ? Function.Call<int>(Hash.GET_VEHICLE_PED_IS_IN, companion, false) : 0;

        if (assignment.VehicleHandle != 0 && (!Function.Call<bool>(Hash.DOES_ENTITY_EXIST, assignment.VehicleHandle) || assignment.VehicleHandle == playerVehicle))
        {
            ClearVehicleAssignment(assignment);
        }

        if (companionVehicle != 0 && companionVehicle != playerVehicle && IsPedDriverOfVehicle(companion, companionVehicle))
        {
            assignment.VehicleHandle = companionVehicle;
            assignment.SeatIndex = -1;
            driverClaims[companionVehicle] = companion;
            if (CanReissueVehicleTask(assignment.LastTaskTick))
            {
                    TaskDriverFollow(companion, companionVehicle, playerVehicle, true, true);
                assignment.LastTaskTick = Environment.TickCount;
            }

            return true;
        }

        if (assignment.VehicleHandle != 0 && driverClaims.TryGetValue(assignment.VehicleHandle, out int driverOwner) && driverOwner != companion)
        {
            ClearVehicleAssignment(assignment);
        }

        if (assignment.VehicleHandle == 0)
        {
            int candidate = FindNearbyVehicleForPed(companion, playerVehicle, true, driverClaims, companion);
            if (candidate != 0)
            {
                assignment.VehicleHandle = candidate;
                assignment.SeatIndex = -1;
                assignment.LastTaskTick = 0;
                assignment.AssignmentStartTick = Environment.TickCount;
            }
        }

        if (assignment.VehicleHandle == 0)
        {
            return false;
        }

        driverClaims[assignment.VehicleHandle] = companion;

        if (companionVehicle == assignment.VehicleHandle)
        {
            if (IsPedDriverOfVehicle(companion, assignment.VehicleHandle))
            {
                if (CanReissueVehicleTask(assignment.LastTaskTick))
                {
                    TaskDriverFollow(companion, assignment.VehicleHandle, playerVehicle, true, true);
                    assignment.LastTaskTick = Environment.TickCount;
                }

                return true;
            }

            if (ShouldForceDriverPlacement(assignment.AssignmentStartTick))
            {
                Function.Call(Hash.SET_PED_INTO_VEHICLE, companion, assignment.VehicleHandle, -1);
                TaskDriverFollow(companion, assignment.VehicleHandle, playerVehicle, true, true);
                assignment.LastTaskTick = Environment.TickCount;
                return true;
            }

            if (CanReissueVehicleTask(assignment.LastTaskTick))
            {
                Function.Call(Hash.TASK_SHUFFLE_TO_NEXT_VEHICLE_SEAT, companion, assignment.VehicleHandle, 0);
                assignment.LastTaskTick = Environment.TickCount;
            }

            return true;
        }

        if (CanReissueVehicleTask(assignment.LastTaskTick))
        {
            if (ShouldForceDriverPlacement(assignment.AssignmentStartTick))
            {
                Function.Call(Hash.SET_PED_INTO_VEHICLE, companion, assignment.VehicleHandle, -1);
                TaskDriverFollow(companion, assignment.VehicleHandle, playerVehicle, true, true);
                assignment.LastTaskTick = Environment.TickCount;
                return true;
            }

            IssueEnterVehicleTask(companion, assignment.VehicleHandle, -1, assignment);
        }

        return true;
    }

    private bool TryAssignNpcToPlayerVehicle(ManagedNpc npc, int playerPed, int playerVehicle, Dictionary<int, int> seatClaims)
    {
        if (Function.Call<bool>(Hash.IS_PED_IN_VEHICLE, npc.PedHandle, playerVehicle, false))
        {
            int seat = GetPedSeatInVehicle(npc.PedHandle, playerVehicle);
            npc.AssignedVehicle = playerVehicle;
            npc.AssignedSeat = seat;
            if (seat >= 0)
            {
                seatClaims[seat] = npc.PedHandle;
            }

            return true;
        }

        bool pedInVehicle = Function.Call<bool>(Hash.IS_PED_IN_ANY_VEHICLE, npc.PedHandle, false);
        int pedVehicle = pedInVehicle ? Function.Call<int>(Hash.GET_VEHICLE_PED_IS_IN, npc.PedHandle, false) : 0;
        if (pedVehicle != 0 && pedVehicle != playerVehicle && npc.AssignedVehicle != pedVehicle)
        {
            if (CanReissueVehicleTask(npc.LastVehicleTaskTick))
            {
                Function.Call(Hash.TASK_LEAVE_VEHICLE, npc.PedHandle, pedVehicle, 0);
                npc.LastVehicleTaskTick = Environment.TickCount;
            }

            return true;
        }

        int targetSeat = -1;
        if (npc.AssignedVehicle == playerVehicle && npc.AssignedSeat >= 0)
        {
            if (IsSeatClaimedBy(seatClaims, npc.AssignedSeat, npc.PedHandle) && IsSeatAvailableForPed(playerVehicle, npc.AssignedSeat, npc.PedHandle))
            {
                targetSeat = npc.AssignedSeat;
            }
            else
            {
                ClearNpcVehicleAssignment(npc);
            }
        }

        if (targetSeat < 0)
        {
            targetSeat = FindFreePassengerSeat(playerVehicle, seatClaims);
            if (targetSeat < 0)
            {
                return false;
            }
        }

        npc.AssignedVehicle = playerVehicle;
        npc.AssignedSeat = targetSeat;
        seatClaims[targetSeat] = npc.PedHandle;
        if (pedVehicle != playerVehicle || CanReissueVehicleTask(npc.LastVehicleTaskTick))
        {
            IssueEnterVehicleTask(npc.PedHandle, playerVehicle, targetSeat, npc);
        }

        return true;
    }

    private bool TryAssignNpcToOwnVehicle(ManagedNpc npc, int playerPed, int playerVehicle, Dictionary<int, int> driverClaims)
    {
        bool pedInVehicle = Function.Call<bool>(Hash.IS_PED_IN_ANY_VEHICLE, npc.PedHandle, false);
        int pedVehicle = pedInVehicle ? Function.Call<int>(Hash.GET_VEHICLE_PED_IS_IN, npc.PedHandle, false) : 0;

        Function.Call(Hash.REMOVE_PED_FROM_GROUP, npc.PedHandle);
        Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, npc.PedHandle, true);
        Function.Call(Hash.SET_PED_KEEP_TASK, npc.PedHandle, true);

        if (npc.AssignedVehicle != 0 && (!Function.Call<bool>(Hash.DOES_ENTITY_EXIST, npc.AssignedVehicle) || npc.AssignedVehicle == playerVehicle))
        {
            ClearNpcVehicleAssignment(npc);
        }

        if (pedVehicle != 0 && pedVehicle != playerVehicle && IsPedDriverOfVehicle(npc.PedHandle, pedVehicle))
        {
            npc.AssignedVehicle = pedVehicle;
            npc.AssignedSeat = -1;
            driverClaims[pedVehicle] = npc.PedHandle;
            if (CanReissueVehicleTask(npc.LastVehicleTaskTick))
            {
                TaskDriverFollow(npc.PedHandle, pedVehicle, playerVehicle, npc.AggressiveDrive, npc.UseOffRoad);
                npc.LastVehicleTaskTick = Environment.TickCount;
            }

            return true;
        }

        if (npc.AssignedVehicle != 0 && driverClaims.TryGetValue(npc.AssignedVehicle, out int driverOwner) && driverOwner != npc.PedHandle)
        {
            ClearNpcVehicleAssignment(npc);
        }

        if (npc.AssignedVehicle == 0)
        {
            int candidate = FindNearbyVehicleForPed(npc.PedHandle, playerVehicle, true, driverClaims, npc.PedHandle);
            if (candidate != 0)
            {
                npc.AssignedVehicle = candidate;
                npc.AssignedSeat = -1;
                npc.LastVehicleTaskTick = 0;
                npc.AssignmentStartTick = Environment.TickCount;
            }
        }

        if (npc.AssignedVehicle == 0)
        {
            return false;
        }

        driverClaims[npc.AssignedVehicle] = npc.PedHandle;

        if (pedVehicle == npc.AssignedVehicle)
        {
            if (IsPedDriverOfVehicle(npc.PedHandle, npc.AssignedVehicle))
            {
                if (CanReissueVehicleTask(npc.LastVehicleTaskTick))
                {
                    TaskDriverFollow(npc.PedHandle, npc.AssignedVehicle, playerVehicle, npc.AggressiveDrive, npc.UseOffRoad);
                    npc.LastVehicleTaskTick = Environment.TickCount;
                }

                return true;
            }

            if (ShouldForceDriverPlacement(npc.AssignmentStartTick))
            {
                Function.Call(Hash.SET_PED_INTO_VEHICLE, npc.PedHandle, npc.AssignedVehicle, -1);
                TaskDriverFollow(npc.PedHandle, npc.AssignedVehicle, playerVehicle, npc.AggressiveDrive, npc.UseOffRoad);
                npc.LastVehicleTaskTick = Environment.TickCount;
                return true;
            }

            if (CanReissueVehicleTask(npc.LastVehicleTaskTick))
            {
                Function.Call(Hash.CLEAR_PED_TASKS, npc.PedHandle);
                Function.Call(Hash.TASK_SHUFFLE_TO_NEXT_VEHICLE_SEAT, npc.PedHandle, npc.AssignedVehicle, 0);
                npc.LastVehicleTaskTick = Environment.TickCount;
            }

            return true;
        }

        if (CanReissueVehicleTask(npc.LastVehicleTaskTick))
        {
            if (ShouldForceDriverPlacement(npc.AssignmentStartTick))
            {
                Function.Call(Hash.SET_PED_INTO_VEHICLE, npc.PedHandle, npc.AssignedVehicle, -1);
                TaskDriverFollow(npc.PedHandle, npc.AssignedVehicle, playerVehicle, npc.AggressiveDrive, npc.UseOffRoad);
                npc.LastVehicleTaskTick = Environment.TickCount;
                return true;
            }

            Function.Call(Hash.CLEAR_PED_TASKS, npc.PedHandle);
            IssueEnterVehicleTask(npc.PedHandle, npc.AssignedVehicle, -1, npc);
        }

        return true;
    }

    private void StartDanceForGirls()
    {
        _girlsDanceMode = true;
        int danced = 0;
        for (int i = _girlCompanions.Count - 1; i >= 0; i--)
        {
            int companion = _girlCompanions[i];
            if (!IsPedAlive(companion))
            {
                _girlCompanions.RemoveAt(i);
                continue;
            }

            Function.Call(Hash.CLEAR_PED_TASKS, companion);
            Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, companion, "WORLD_HUMAN_PARTYING", 0, true);
            danced++;
        }

        SafeNotify($"~p~Girls dancing: {danced}.");
        Log($"Girls dance action: {danced}");
    }

    private void KillAllGirls()
    {
        _girlsDanceMode = false;
        int killed = 0;
        for (int i = _girlCompanions.Count - 1; i >= 0; i--)
        {
            int companion = _girlCompanions[i];
            _girlCompanions.RemoveAt(i);
            ClearGirlVehicleAssignment(companion);
            RemoveGirlBlip(companion);
            if (companion == 0 || !Function.Call<bool>(Hash.DOES_ENTITY_EXIST, companion))
            {
                continue;
            }

            if (!Function.Call<bool>(Hash.IS_ENTITY_DEAD, companion))
            {
                Function.Call(Hash.SET_ENTITY_HEALTH, companion, 0);
                killed++;
            }
        }

        SafeNotify($"~p~Girls removed: {killed}.");
        Log($"Girls kill action: {killed}");
    }

    private void RefreshGirlCompanions()
    {
        int playerPed = Function.Call<int>(Hash.PLAYER_PED_ID);
        if (playerPed == 0 || !Function.Call<bool>(Hash.DOES_ENTITY_EXIST, playerPed))
        {
            return;
        }

        bool playerInVehicle = Function.Call<bool>(Hash.IS_PED_IN_ANY_VEHICLE, playerPed, false);
        int playerVehicle = playerInVehicle ? Function.Call<int>(Hash.GET_VEHICLE_PED_IS_IN, playerPed, false) : 0;
        bool playerUnderAttack = Function.Call<bool>(Hash.IS_PED_IN_COMBAT, playerPed, 0) || Function.Call<bool>(Hash.HAS_ENTITY_BEEN_DAMAGED_BY_ANY_PED, playerPed);
        if (Function.Call<bool>(Hash.HAS_ENTITY_BEEN_DAMAGED_BY_ANY_PED, playerPed))
        {
            Function.Call(Hash.CLEAR_ENTITY_LAST_DAMAGE_ENTITY, playerPed);
        }
        int combatTargetPed = playerUnderAttack ? FindCombatTargetPed(playerPed) : 0;

        var seatClaims = new Dictionary<int, int>();
        var driverClaims = new Dictionary<int, int>();
        if (playerInVehicle && playerVehicle != 0)
        {
            BuildPlayerVehicleSeatClaims(playerVehicle, seatClaims);
            BuildDriverVehicleClaims(playerVehicle, driverClaims);
        }

        for (int i = _girlCompanions.Count - 1; i >= 0; i--)
        {
            int companion = _girlCompanions[i];
            if (!IsPedAlive(companion))
            {
                _girlCompanions.RemoveAt(i);
                ClearGirlVehicleAssignment(companion);
                RemoveGirlBlip(companion);
                continue;
            }

            EnsureGirlBlip(companion);

            VehicleAssignment assignment = GetOrCreateGirlVehicleAssignment(companion);

            if (_girlsDanceMode)
            {
                ClearVehicleAssignment(assignment);
                bool inScenario = Function.Call<bool>(Hash.IS_PED_USING_ANY_SCENARIO, companion);
                if (!inScenario)
                {
                    Function.Call(Hash.CLEAR_PED_TASKS, companion);
                    Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, companion, "WORLD_HUMAN_PARTYING", 0, true);
                }

                continue;
            }

            if (playerInVehicle && playerVehicle != 0)
            {
                if (TryAssignGirlToPlayerVehicle(companion, assignment, playerVehicle, seatClaims))
                {
                    if (combatTargetPed != 0)
                    {
                        int companionVehicle = Function.Call<int>(Hash.GET_VEHICLE_PED_IS_IN, companion, false);
                        if (companionVehicle != 0)
                        {
                            TryVehicleCombat(companion, companionVehicle, IsPedDriverOfVehicle(companion, companionVehicle), combatTargetPed, true, true);
                        }
                    }

                    continue;
                }

                if (TryAssignGirlToOwnVehicle(companion, playerPed, playerVehicle, assignment, driverClaims))
                {
                    if (combatTargetPed != 0)
                    {
                        int companionVehicle = Function.Call<int>(Hash.GET_VEHICLE_PED_IS_IN, companion, false);
                        if (companionVehicle != 0)
                        {
                            TryVehicleCombat(companion, companionVehicle, IsPedDriverOfVehicle(companion, companionVehicle), combatTargetPed, true, true);
                        }
                    }

                    continue;
                }
            }
            else
            {
                ClearVehicleAssignment(assignment);
                if (Function.Call<bool>(Hash.IS_PED_IN_ANY_VEHICLE, companion, false))
                {
                    int companionVehicle = Function.Call<int>(Hash.GET_VEHICLE_PED_IS_IN, companion, false);
                    if (companionVehicle != 0 && CanReissueVehicleTask(assignment.LastTaskTick))
                    {
                        Function.Call(Hash.TASK_LEAVE_VEHICLE, companion, companionVehicle, 0);
                        assignment.LastTaskTick = Environment.TickCount;
                        continue;
                    }
                }
            }

            if (combatTargetPed != 0)
            {
                ClearVehicleAssignment(assignment);
                Function.Call(Hash.TASK_COMBAT_PED, companion, combatTargetPed, 0, 16);
                continue;
            }

            float lateral = (i % 2 == 0) ? 1.5f : -1.5f;
            float trailing = -1.5f - (i / 2) * 1.1f;
            Function.Call(Hash.TASK_FOLLOW_TO_OFFSET_OF_ENTITY, companion, playerPed, lateral, trailing, 0f, 3f, -1, 2f, true);
        }
    }

    private void TrySpawnNpc(PedCatalog.PedEntry ped)
    {
        int playerPed = Function.Call<int>(Hash.PLAYER_PED_ID);
        if (playerPed == 0 || !Function.Call<bool>(Hash.DOES_ENTITY_EXIST, playerPed) || Function.Call<bool>(Hash.IS_ENTITY_DEAD, playerPed))
        {
            SafeNotify("~r~NPC failed: player is not available.");
            Log("NPC failed: player unavailable.");
            return;
        }

        int modelHash = Game.GenerateHash(ped.ModelName);
        if (!Function.Call<bool>(Hash.IS_MODEL_IN_CDIMAGE, modelHash) || !Function.Call<bool>(Hash.IS_MODEL_VALID, modelHash))
        {
            SafeNotify($"~r~NPC failed: {ped.DisplayName} unavailable.");
            Log($"NPC failed: model unavailable ({ped.ModelName}).");
            return;
        }

        Function.Call(Hash.REQUEST_MODEL, modelHash);
        int requestStart = Environment.TickCount;
        while (!Function.Call<bool>(Hash.HAS_MODEL_LOADED, modelHash))
        {
            Script.Yield();
            if (HasElapsed(requestStart, 1000))
            {
                SafeNotify($"~r~NPC failed: {ped.DisplayName} load timeout.");
                Log($"NPC failed: model load timeout ({ped.ModelName}).");
                return;
            }
        }

        Vector3 playerPosition = Function.Call<Vector3>(Hash.GET_ENTITY_COORDS, playerPed, true);
        float playerHeading = Function.Call<float>(Hash.GET_ENTITY_HEADING, playerPed);
        Vector3 forward = HeadingToDirection(playerHeading);
        Vector3 right = new Vector3(forward.Y, -forward.X, 0f);
        float lane = ((_managedNpcs.Count % 2) == 0) ? 2.5f : -2.5f;
        float distance = 3.5f + (_managedNpcs.Count / 2) * 1.0f;
        Vector3 spawnPosition = playerPosition + forward * distance + right * lane;

        int pedHandle = Function.Call<int>(Hash.CREATE_PED, 26, modelHash, spawnPosition.X, spawnPosition.Y, spawnPosition.Z, playerHeading, true, false);
        Function.Call(Hash.SET_MODEL_AS_NO_LONGER_NEEDED, modelHash);
        if (pedHandle == 0)
        {
            SafeNotify($"~r~NPC failed: could not create {ped.DisplayName}.");
            Log($"NPC failed: create ped returned 0 ({ped.ModelName}).");
            return;
        }

        ConfigureNpc(pedHandle, ped);
        SafeNotify($"~g~NPC spawned: {ped.DisplayName}");
        Log($"NPC spawned: {ped.ModelName}");
    }

    private void ConfigureNpc(int pedHandle, PedCatalog.PedEntry ped)
    {
        int playerPed = Function.Call<int>(Hash.PLAYER_PED_ID);
        int playerId = Function.Call<int>(Hash.PLAYER_ID);
        int playerGroup = Function.Call<int>(Hash.GET_PLAYER_GROUP, playerId);

        Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, pedHandle, true, false);
        Function.Call(Hash.SET_PED_KEEP_TASK, pedHandle, true);
        Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, pedHandle, 0, false);
        Function.Call(Hash.SET_PED_COMBAT_ABILITY, pedHandle, 2);
        Function.Call(Hash.SET_PED_COMBAT_MOVEMENT, pedHandle, 2);
        Function.Call(Hash.SET_PED_COMBAT_RANGE, pedHandle, 2);
        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, pedHandle, 1, true);
        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, pedHandle, 2, true);
        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, pedHandle, 3, false);
        Function.Call(Hash.SET_PED_ACCURACY, pedHandle, VehicleCombatAccuracy);
        Function.Call(Hash.SET_PED_SEEING_RANGE, pedHandle, EngageDistance);
        Function.Call(Hash.SET_PED_HEARING_RANGE, pedHandle, EngageDistance);
        Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, pedHandle, false);
        Function.Call(Hash.SET_PED_STAY_IN_VEHICLE_WHEN_JACKED, pedHandle, true);

        if (_npcSettings.Armed)
        {
            Function.Call(Hash.GIVE_WEAPON_TO_PED, pedHandle, (uint)WeaponHash.Pistol, 240, false, true);
            Function.Call(Hash.SET_CURRENT_PED_WEAPON, pedHandle, (uint)WeaponHash.Pistol, true);
        }

        var managedNpc = new ManagedNpc
        {
            PedHandle = pedHandle,
            ModelName = ped.ModelName,
            Armed = _npcSettings.Armed,
            Disposition = _npcSettings.Disposition,
            VehicleMode = _npcSettings.VehicleMode,
            AggressiveDrive = _npcSettings.AggressiveDrive,
            UseOffRoad = _npcSettings.UseOffRoad,
        };

        if (_npcSettings.Disposition == NpcDisposition.Ally)
        {
            if (_npcSettings.VehicleMode == NpcVehicleMode.UsePlayerCar)
            {
                Function.Call(Hash.SET_PED_AS_GROUP_MEMBER, pedHandle, playerGroup);
                Function.Call(Hash.SET_PED_NEVER_LEAVES_GROUP, pedHandle, true);
                Function.Call(Hash.SET_PED_CAN_TELEPORT_TO_GROUP_LEADER, pedHandle, playerGroup, true);
                Function.Call(Hash.TASK_FOLLOW_TO_OFFSET_OF_ENTITY, pedHandle, playerPed, 0f, -1.8f, 0f, 3f, -1, 2f, true);
            }
            else
            {
                Function.Call(Hash.REMOVE_PED_FROM_GROUP, pedHandle);
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, pedHandle, true);
            }
        }
        else
        {
            Function.Call(Hash.TASK_COMBAT_PED, pedHandle, playerPed, 0, 16);
        }

        _managedNpcs.Add(managedNpc);
        EnsureNpcBlip(managedNpc);
        EnforceNpcLimit();
    }

    private void EnforceNpcLimit()
    {
        while (_managedNpcs.Count > MaxNpcCount)
        {
            ManagedNpc oldest = _managedNpcs[0];
            _managedNpcs.RemoveAt(0);
            RemoveNpcBlip(oldest);
            if (oldest.PedHandle == 0 || !Function.Call<bool>(Hash.DOES_ENTITY_EXIST, oldest.PedHandle))
            {
                continue;
            }

            if (!Function.Call<bool>(Hash.IS_ENTITY_DEAD, oldest.PedHandle))
            {
                Function.Call(Hash.SET_ENTITY_HEALTH, oldest.PedHandle, 0);
            }
        }
    }

    private void RefreshManagedNpcs()
    {
        int playerPed = Function.Call<int>(Hash.PLAYER_PED_ID);
        if (playerPed == 0 || !Function.Call<bool>(Hash.DOES_ENTITY_EXIST, playerPed))
        {
            return;
        }

        bool playerInVehicle = Function.Call<bool>(Hash.IS_PED_IN_ANY_VEHICLE, playerPed, false);
        int playerVehicle = playerInVehicle ? Function.Call<int>(Hash.GET_VEHICLE_PED_IS_IN, playerPed, false) : 0;
        bool playerUnderAttack = Function.Call<bool>(Hash.IS_PED_IN_COMBAT, playerPed, 0) || Function.Call<bool>(Hash.HAS_ENTITY_BEEN_DAMAGED_BY_ANY_PED, playerPed);
        if (Function.Call<bool>(Hash.HAS_ENTITY_BEEN_DAMAGED_BY_ANY_PED, playerPed))
        {
            Function.Call(Hash.CLEAR_ENTITY_LAST_DAMAGE_ENTITY, playerPed);
        }
        int combatTargetPed = playerUnderAttack ? FindCombatTargetPed(playerPed) : 0;

        var seatClaims = new Dictionary<int, int>();
        var driverClaims = new Dictionary<int, int>();
        if (playerInVehicle && playerVehicle != 0)
        {
            BuildPlayerVehicleSeatClaims(playerVehicle, seatClaims);
            BuildDriverVehicleClaims(playerVehicle, driverClaims);
        }

        for (int i = _managedNpcs.Count - 1; i >= 0; i--)
        {
            ManagedNpc npc = _managedNpcs[i];
            if (!IsPedAlive(npc.PedHandle))
            {
                RemoveNpcBlip(npc);
                _managedNpcs.RemoveAt(i);
                ClearNpcVehicleAssignment(npc);
                continue;
            }

            EnsureNpcBlip(npc);

            if (playerInVehicle && playerVehicle != 0 && npc.VehicleMode == NpcVehicleMode.DriveOwnCar)
            {
                Function.Call(Hash.REMOVE_PED_FROM_GROUP, npc.PedHandle);
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, npc.PedHandle, true);
                if (TryAssignNpcToOwnVehicle(npc, playerPed, playerVehicle, driverClaims))
                {
                    if (combatTargetPed != 0 && npc.Armed)
                    {
                        int npcVehicle = Function.Call<int>(Hash.GET_VEHICLE_PED_IS_IN, npc.PedHandle, false);
                        if (npcVehicle != 0)
                        {
                            TryVehicleCombat(npc.PedHandle, npcVehicle, IsPedDriverOfVehicle(npc.PedHandle, npcVehicle), combatTargetPed, npc.AggressiveDrive, npc.UseOffRoad);
                        }
                    }

                    continue;
                }
            }

            if (npc.Disposition == NpcDisposition.Enemy)
            {
                RemoveNpcBlip(npc);
                ClearNpcVehicleAssignment(npc);
                Function.Call(Hash.TASK_COMBAT_PED, npc.PedHandle, playerPed, 0, 16);
                continue;
            }

            if (playerInVehicle && playerVehicle != 0 && npc.VehicleMode == NpcVehicleMode.UsePlayerCar)
            {
                if (TryAssignNpcToPlayerVehicle(npc, playerPed, playerVehicle, seatClaims))
                {
                    if (combatTargetPed != 0 && npc.Armed)
                    {
                        int npcVehicle = Function.Call<int>(Hash.GET_VEHICLE_PED_IS_IN, npc.PedHandle, false);
                        if (npcVehicle != 0)
                        {
                            TryVehicleCombat(npc.PedHandle, npcVehicle, IsPedDriverOfVehicle(npc.PedHandle, npcVehicle), combatTargetPed, npc.AggressiveDrive, npc.UseOffRoad);
                        }
                    }

                    continue;
                }
            }

            bool npcInVehicle = Function.Call<bool>(Hash.IS_PED_IN_ANY_VEHICLE, npc.PedHandle, false);
            if (npcInVehicle)
            {
                int npcVehicle = Function.Call<int>(Hash.GET_VEHICLE_PED_IS_IN, npc.PedHandle, false);
                if (npcVehicle != 0)
                {
                    if (CanReissueVehicleTask(npc.LastVehicleTaskTick))
                    {
                        Function.Call(Hash.TASK_LEAVE_VEHICLE, npc.PedHandle, npcVehicle, 0);
                        npc.LastVehicleTaskTick = Environment.TickCount;
                        ClearNpcVehicleAssignment(npc);
                        continue;
                    }
                }
            }

            ClearNpcVehicleAssignment(npc);

            if (combatTargetPed != 0 && npc.Armed)
            {
                Function.Call(Hash.TASK_COMBAT_PED, npc.PedHandle, combatTargetPed, 0, 16);
                continue;
            }

            if (npc.VehicleMode == NpcVehicleMode.DriveOwnCar)
            {
                Function.Call(Hash.REMOVE_PED_FROM_GROUP, npc.PedHandle);
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, npc.PedHandle, false);
            }

            int followerIndex = i + _girlCompanions.Count;
            float lateral = (followerIndex % 2 == 0) ? 1.8f : -1.8f;
            float trailing = -2.0f - (followerIndex / 2) * 1.0f;
            Function.Call(Hash.TASK_FOLLOW_TO_OFFSET_OF_ENTITY, npc.PedHandle, playerPed, lateral, trailing, 0f, 3f, -1, 2f, true);
        }
    }

    private void HandleNpcPlayerVehicleFollow(int pedHandle, int playerPed, int playerVehicle, HashSet<int> reservedPassengerSeats)
    {
        if (Function.Call<bool>(Hash.IS_PED_IN_VEHICLE, pedHandle, playerVehicle, false))
        {
            int seat = GetPedSeatInVehicle(pedHandle, playerVehicle);
            if (seat >= 0)
            {
                reservedPassengerSeats.Add(seat);
            }

            return;
        }

        bool pedInVehicle = Function.Call<bool>(Hash.IS_PED_IN_ANY_VEHICLE, pedHandle, false);
        if (pedInVehicle)
        {
            int pedVehicle = Function.Call<int>(Hash.GET_VEHICLE_PED_IS_IN, pedHandle, false);
            if (pedVehicle != playerVehicle)
            {
                Function.Call(Hash.TASK_LEAVE_VEHICLE, pedHandle, pedVehicle, 0);
                return;
            }
        }

        int freeSeat = GetFreePassengerSeat(playerVehicle, reservedPassengerSeats);
        if (freeSeat >= 0)
        {
            reservedPassengerSeats.Add(freeSeat);
            Function.Call(Hash.TASK_ENTER_VEHICLE, pedHandle, playerVehicle, 10000, freeSeat, 2.0f, 1, 0);
            return;
        }

        Function.Call(Hash.TASK_FOLLOW_TO_OFFSET_OF_ENTITY, pedHandle, playerPed, 0f, -2.0f, 0f, 3f, -1, 2f, true);
    }

    private void HandleNpcDriveOwnCar(ManagedNpc npc, int playerPed, int playerVehicle)
    {
        bool npcInVehicle = Function.Call<bool>(Hash.IS_PED_IN_ANY_VEHICLE, npc.PedHandle, false);
        int npcVehicle = npcInVehicle ? Function.Call<int>(Hash.GET_VEHICLE_PED_IS_IN, npc.PedHandle, false) : 0;

        if (npc.AssignedVehicle != 0 && !Function.Call<bool>(Hash.DOES_ENTITY_EXIST, npc.AssignedVehicle))
        {
            npc.AssignedVehicle = 0;
        }

        if (npcVehicle != 0 && npcVehicle != playerVehicle && IsPedDriverOfVehicle(npc.PedHandle, npcVehicle))
        {
            TaskDriverFollow(npc.PedHandle, npcVehicle, playerVehicle, npc.AggressiveDrive, npc.UseOffRoad);
            npc.AssignedVehicle = npcVehicle;
            return;
        }

        if (npc.AssignedVehicle == 0)
        {
            npc.AssignedVehicle = FindNearbyVehicleForPed(npc.PedHandle, playerVehicle, true);
        }

        if (npc.AssignedVehicle != 0)
        {
            if (npcVehicle == npc.AssignedVehicle && IsPedDriverOfVehicle(npc.PedHandle, npc.AssignedVehicle))
            {
                TaskDriverFollow(npc.PedHandle, npc.AssignedVehicle, playerVehicle, npc.AggressiveDrive, npc.UseOffRoad);
                return;
            }

            Function.Call(Hash.TASK_ENTER_VEHICLE, npc.PedHandle, npc.AssignedVehicle, 10000, -1, 2.0f, 1, 0);
            return;
        }

        Function.Call(Hash.TASK_FOLLOW_TO_OFFSET_OF_ENTITY, npc.PedHandle, playerPed, 0f, -2.0f, 0f, 3f, -1, 2f, true);
    }

    private static void TaskDriverFollow(int driverPed, int supportVehicle, int playerVehicle)
    {
        TaskDriverFollow(driverPed, supportVehicle, playerVehicle, false, false);
    }

    private static void TaskDriverFollow(int driverPed, int supportVehicle, int playerVehicle, bool aggressiveDrive, bool useOffRoad)
    {
        if (playerVehicle == 0 || supportVehicle == 0)
        {
            return;
        }

        float cruiseSpeed = aggressiveDrive ? AggressiveFollowSpeed : NormalFollowSpeed;
        float maxCruiseSpeed = aggressiveDrive ? AggressiveFollowMaxSpeed : NormalFollowSpeed;
        int drivingStyle = aggressiveDrive ? AggressiveDrivingStyle : NormalDrivingStyle;
        float noRoadsDistance = useOffRoad ? OffRoadNoRoadsDistance : NormalNoRoadsDistance;

        Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, driverPed, true);
        Function.Call(Hash.SET_DRIVER_ABILITY, driverPed, 1.0f);
        Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, driverPed, aggressiveDrive ? 0.9f : 0.55f);
        Function.Call(Hash.TASK_VEHICLE_ESCORT, driverPed, supportVehicle, playerVehicle, -1, cruiseSpeed, drivingStyle, 12f, 0, noRoadsDistance);
        Function.Call(Hash.SET_DRIVE_TASK_DRIVING_STYLE, driverPed, drivingStyle);
        Function.Call(Hash.SET_DRIVE_TASK_CRUISE_SPEED, driverPed, cruiseSpeed);
        Function.Call(Hash.SET_DRIVE_TASK_MAX_CRUISE_SPEED, driverPed, maxCruiseSpeed, true);
    }

    private static void TaskDriverChase(int driverPed, int supportVehicle, int targetPed, bool aggressiveDrive, bool useOffRoad)
    {
        if (targetPed == 0 || supportVehicle == 0)
        {
            return;
        }

        float cruiseSpeed = aggressiveDrive ? AggressiveFollowSpeed : NormalFollowSpeed;
        int drivingStyle = aggressiveDrive ? AggressiveDrivingStyle : NormalDrivingStyle;
        Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, driverPed, true);
        Function.Call(Hash.SET_DRIVER_ABILITY, driverPed, 1.0f);
        Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, driverPed, aggressiveDrive ? 1.0f : 0.7f);
        Function.Call(Hash.TASK_VEHICLE_MISSION_PED_TARGET, driverPed, supportVehicle, targetPed, 6, cruiseSpeed, drivingStyle, 10f, useOffRoad ? 80f : 20f, useOffRoad);
        Function.Call(Hash.SET_DRIVE_TASK_DRIVING_STYLE, driverPed, drivingStyle);
        Function.Call(Hash.SET_DRIVE_TASK_CRUISE_SPEED, driverPed, cruiseSpeed);
    }

    private static void TryVehicleCombat(int pedHandle, int vehicleHandle, bool isDriver, int targetPed, bool aggressiveDrive, bool useOffRoad)
    {
        if (pedHandle == 0 || vehicleHandle == 0 || targetPed == 0)
        {
            return;
        }

        if (isDriver && IsWeaponizedVehicle(vehicleHandle))
        {
            Function.Call(Hash.TASK_VEHICLE_SHOOT_AT_PED, pedHandle, targetPed, 20f);
            return;
        }

        if (isDriver)
        {
            TaskDriverChase(pedHandle, vehicleHandle, targetPed, aggressiveDrive, useOffRoad);
        }

        Function.Call(Hash.TASK_DRIVE_BY, pedHandle, targetPed, 0, 0f, 0f, 0f, 250f, VehicleCombatAccuracy, true, (uint)Game.GenerateHash("FIRING_PATTERN_FULL_AUTO"));
    }

    private int FindCombatTargetPed(int playerPed)
    {
        Entity targetedEntity = Game.Player.TargetedEntity;
        if (targetedEntity is Ped targetedPed && IsValidCombatTarget(targetedPed.Handle, playerPed))
        {
            return targetedPed.Handle;
        }

        Ped playerCharacter = Game.Player.Character;
        if (playerCharacter == null || !playerCharacter.Exists())
        {
            return 0;
        }

        Ped[] nearbyPeds = World.GetNearbyPeds(playerCharacter, 80f);
        float bestDistance = float.MaxValue;
        int bestTarget = 0;
        for (int i = 0; i < nearbyPeds.Length; i++)
        {
            Ped nearbyPed = nearbyPeds[i];
            if (!IsValidCombatTarget(nearbyPed.Handle, playerPed))
            {
                continue;
            }

            bool hostile = Function.Call<bool>(Hash.IS_PED_IN_COMBAT, nearbyPed.Handle, playerPed)
                || Function.Call<bool>(Hash.HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY, playerPed, nearbyPed.Handle, true);
            if (!hostile)
            {
                continue;
            }

            float distance = nearbyPed.Position.DistanceToSquared(playerCharacter.Position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestTarget = nearbyPed.Handle;
            }
        }

        return bestTarget;
    }

    private bool IsValidCombatTarget(int pedHandle, int playerPed)
    {
        if (!IsPedAlive(pedHandle) || pedHandle == playerPed)
        {
            return false;
        }

        if (Function.Call<bool>(Hash.IS_PED_A_PLAYER, pedHandle))
        {
            return false;
        }

        if (_girlCompanions.Contains(pedHandle))
        {
            return false;
        }

        for (int i = 0; i < _managedNpcs.Count; i++)
        {
            ManagedNpc npc = _managedNpcs[i];
            if (npc.PedHandle == pedHandle && npc.Disposition == NpcDisposition.Ally)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsWeaponizedVehicle(int vehicleHandle)
    {
        if (vehicleHandle == 0)
        {
            return false;
        }

        int modelHash = Function.Call<int>(Hash.GET_ENTITY_MODEL, vehicleHandle);
        return WeaponizedVehicleModelHashes.Contains(modelHash);
    }

    private static HashSet<int> CreateWeaponizedVehicleModelHashes()
    {
        return new HashSet<int>
        {
            Game.GenerateHash("rhino"),
            Game.GenerateHash("khanjali"),
            Game.GenerateHash("apc"),
            Game.GenerateHash("halftrack"),
            Game.GenerateHash("barrage"),
            Game.GenerateHash("chernobog"),
            Game.GenerateHash("technical"),
            Game.GenerateHash("technical2"),
            Game.GenerateHash("technical3"),
            Game.GenerateHash("insurgent3"),
            Game.GenerateHash("scarab"),
            Game.GenerateHash("scarab2"),
            Game.GenerateHash("scarab3"),
        };
    }

    private static int FindNearbyVehicleForPed(int ped, int excludedVehicle, bool requireFreeDriverSeat)
    {
        return FindNearbyVehicleForPed(ped, excludedVehicle, requireFreeDriverSeat, null, 0);
    }

    private static int FindNearbyVehicleForPed(int ped, int excludedVehicle, bool requireFreeDriverSeat, Dictionary<int, int> driverClaims, int claimingPed)
    {
        Vector3 pedPosition = Function.Call<Vector3>(Hash.GET_ENTITY_COORDS, ped, true);
        for (int i = 0; i < 8; i++)
        {
            int candidate = Function.Call<int>(Hash.GET_RANDOM_VEHICLE_IN_SPHERE, pedPosition.X, pedPosition.Y, pedPosition.Z, 45f, 0, 70);
            if (candidate == 0 || candidate == excludedVehicle)
            {
                continue;
            }

            if (!Function.Call<bool>(Hash.DOES_ENTITY_EXIST, candidate) || Function.Call<bool>(Hash.IS_ENTITY_DEAD, candidate))
            {
                continue;
            }

            if (driverClaims != null && driverClaims.TryGetValue(candidate, out int ownerPed) && ownerPed != 0 && ownerPed != claimingPed)
            {
                continue;
            }

            if (requireFreeDriverSeat && !Function.Call<bool>(Hash.IS_VEHICLE_SEAT_FREE, candidate, -1, false))
            {
                continue;
            }

            return candidate;
        }

        return 0;
    }

    private static int GetAnyFreePassengerSeat(int vehicle)
    {
        int maxPassengers = Function.Call<int>(Hash.GET_VEHICLE_MAX_NUMBER_OF_PASSENGERS, vehicle);
        for (int seat = 0; seat < maxPassengers; seat++)
        {
            if (Function.Call<bool>(Hash.IS_VEHICLE_SEAT_FREE, vehicle, seat, false))
            {
                return seat;
            }
        }

        return -1;
    }

    private static int GetFreePassengerSeat(int vehicle, HashSet<int> reservedPassengerSeats)
    {
        int maxPassengers = Function.Call<int>(Hash.GET_VEHICLE_MAX_NUMBER_OF_PASSENGERS, vehicle);
        for (int seat = 0; seat < maxPassengers; seat++)
        {
            if (reservedPassengerSeats.Contains(seat))
            {
                continue;
            }

            if (Function.Call<bool>(Hash.IS_VEHICLE_SEAT_FREE, vehicle, seat, false))
            {
                return seat;
            }
        }

        return -1;
    }

    private static int GetPedSeatInVehicle(int ped, int vehicle)
    {
        int maxPassengers = Function.Call<int>(Hash.GET_VEHICLE_MAX_NUMBER_OF_PASSENGERS, vehicle);
        for (int seat = -1; seat < maxPassengers; seat++)
        {
            int pedInSeat = Function.Call<int>(Hash.GET_PED_IN_VEHICLE_SEAT, vehicle, seat, false);
            if (pedInSeat == ped)
            {
                return seat;
            }
        }

        return int.MinValue;
    }

    private static bool IsPedDriverOfVehicle(int ped, int vehicle)
    {
        int driver = Function.Call<int>(Hash.GET_PED_IN_VEHICLE_SEAT, vehicle, -1, false);
        return driver == ped;
    }

    private static bool IsPedAlive(int ped)
    {
        return ped != 0 && Function.Call<bool>(Hash.DOES_ENTITY_EXIST, ped) && !Function.Call<bool>(Hash.IS_ENTITY_DEAD, ped);
    }

    private static bool TrySpawnVehicle(VehicleCatalog.VehicleEntry vehicle)
    {
        int playerPed = Function.Call<int>(Hash.PLAYER_PED_ID);
        if (playerPed == 0 || !Function.Call<bool>(Hash.DOES_ENTITY_EXIST, playerPed) || Function.Call<bool>(Hash.IS_ENTITY_DEAD, playerPed))
        {
            SafeNotify($"~r~{vehicle.DisplayName} failed: player is not available.");
            Log("Spawn failed: player is not available.");
            return false;
        }

        int modelHash = Game.GenerateHash(vehicle.ModelName);
        if (!Function.Call<bool>(Hash.IS_MODEL_IN_CDIMAGE, modelHash) || !Function.Call<bool>(Hash.IS_MODEL_A_VEHICLE, modelHash))
        {
            SafeNotify($"~r~{vehicle.DisplayName} failed: model not available.");
            Log($"Spawn failed: model not available ({vehicle.ModelName}).");
            return false;
        }

        Function.Call(Hash.REQUEST_MODEL, modelHash);
        int modelRequestTime = Environment.TickCount;
        while (!Function.Call<bool>(Hash.HAS_MODEL_LOADED, modelHash))
        {
            Script.Yield();
            if (HasElapsed(modelRequestTime, 1000))
            {
                SafeNotify($"~r~{vehicle.DisplayName} failed: model loading timeout.");
                Log($"Spawn failed: model loading timeout ({vehicle.ModelName}).");
                return false;
            }
        }

        Vector3 playerPosition = Function.Call<Vector3>(Hash.GET_ENTITY_COORDS, playerPed, true);
        float playerHeading = Function.Call<float>(Hash.GET_ENTITY_HEADING, playerPed);
        Vector3 forward = HeadingToDirection(playerHeading);
        Vector3 right = new Vector3(forward.Y, -forward.X, 0f);
        Vector3 basePosition = playerPosition + forward * 8f;
        float[] offsets = { 0f, 4f, -4f, 7f, -7f };
        int spawnedVehicle = 0;

        foreach (float offset in offsets)
        {
            Vector3 spawnPosition = basePosition + right * offset;
            bool occupied = Function.Call<bool>(Hash.IS_POSITION_OCCUPIED, spawnPosition.X, spawnPosition.Y, spawnPosition.Z, 3f, false, true, true, false, false, 0, false);
            if (occupied)
            {
                continue;
            }

            spawnedVehicle = Function.Call<int>(Hash.CREATE_VEHICLE, modelHash, spawnPosition.X, spawnPosition.Y, spawnPosition.Z, playerHeading, true, false);
            if (spawnedVehicle == 0)
            {
                continue;
            }

            Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, spawnedVehicle);
            Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, spawnedVehicle, true, false);
            break;
        }

        Function.Call(Hash.SET_MODEL_AS_NO_LONGER_NEEDED, modelHash);

        if (spawnedVehicle == 0)
        {
            SafeNotify($"~r~{vehicle.DisplayName} failed: no free space found.");
            Log($"Spawn failed: no free space found ({vehicle.ModelName}).");
            return false;
        }

        TrackSpawnedVehicle(spawnedVehicle);
        SafeNotify($"~g~{vehicle.DisplayName} spawned.");
        Log($"Spawn success: {vehicle.ModelName}");
        return true;
    }

    private static void TrackSpawnedVehicle(int vehicle)
    {
        if (vehicle == 0)
        {
            return;
        }

        SpawnedVehicles.Enqueue(vehicle);
        while (SpawnedVehicles.Count > MaxManagedVehicles)
        {
            int oldestVehicle = SpawnedVehicles.Dequeue();
            if (oldestVehicle == 0 || !Function.Call<bool>(Hash.DOES_ENTITY_EXIST, oldestVehicle))
            {
                continue;
            }

            Function.Call(Hash.SET_ENTITY_AS_NO_LONGER_NEEDED, oldestVehicle);
        }
    }

    private static Dictionary<string, VehicleCatalog.VehicleEntry> CreateVehicleCatalogByModel()
    {
        var catalog = new Dictionary<string, VehicleCatalog.VehicleEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (VehicleCatalog.VehicleCategory category in VehicleCatalog.Categories)
        {
            foreach (VehicleCatalog.VehicleEntry vehicle in category.Vehicles)
            {
                if (!catalog.ContainsKey(vehicle.ModelName))
                {
                    catalog.Add(vehicle.ModelName, vehicle);
                }
            }
        }

        return catalog;
    }

    private static Dictionary<string, PedCatalog.PedEntry> CreatePedCatalogByModel()
    {
        var catalog = new Dictionary<string, PedCatalog.PedEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (PedCatalog.PedCategory category in PedCatalog.Categories)
        {
            foreach (PedCatalog.PedEntry ped in category.Peds)
            {
                if (!catalog.ContainsKey(ped.ModelName))
                {
                    catalog.Add(ped.ModelName, ped);
                }
            }
        }

        return catalog;
    }

    private static bool HasElapsed(int startTick, int durationMs)
    {
        return unchecked(Environment.TickCount - startTick) > durationMs;
    }

    private static Vector3 HeadingToDirection(float heading)
    {
        float headingRadians = heading * (float)Math.PI / 180f;
        float x = -(float)Math.Sin(headingRadians);
        float y = (float)Math.Cos(headingRadians);
        return new Vector3(x, y, 0f);
    }

    private static void DrawText(string value, float x, float y, float scale, int r, int g, int b, int a, bool centered)
    {
        Function.Call(Hash.SET_TEXT_FONT, 0);
        Function.Call(Hash.SET_TEXT_SCALE, 1.0f, scale);
        Function.Call(Hash.SET_TEXT_COLOUR, r, g, b, a);
        Function.Call(Hash.SET_TEXT_CENTRE, centered);
        Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING");
        Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, value);
        Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT, x, y, 0);
    }

    private static void SafeNotify(string message)
    {
        if (_notificationUnavailable)
        {
            return;
        }

        try
        {
            Notification.Show(message);
        }
        catch (Exception ex)
        {
            if (ex is TypeInitializationException)
            {
                _notificationUnavailable = true;
            }

            Log($"Notification failure ({ex.GetType().Name}): {ex.Message}");
        }
    }

    private static void Log(string message)
    {
        try
        {
            File.AppendAllText(LogFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}
