using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeathAdderManager.Core.Domain.Enums;
using DeathAdderManager.Core.Domain.Models;
using DeathAdderManager.Core.Interfaces;

namespace DeathAdderManager.ViewModels;

public sealed partial class DpiStageViewModel : ObservableObject
{
    [ObservableProperty] private int    _stageIndex;
    [ObservableProperty] private int    _dpi;
    [ObservableProperty] private bool   _enabled;
    [ObservableProperty] private string _label = string.Empty;
    [ObservableProperty] private bool   _isActive;

    public DpiStageViewModel(DpiStage stage) => LoadFrom(stage);

    public void LoadFrom(DpiStage stage)
    {
        StageIndex = stage.Index;
        Dpi        = stage.Dpi;
        Enabled    = stage.Enabled;
        Label      = stage.Label;
    }

    public DpiStage ToDomainModel() => new(StageIndex, Dpi, Enabled) { Label = Label };
}

public sealed partial class PerformanceViewModel : ObservableObject
{
    private readonly IMouseService _mouseService;
    private CancellationTokenSource? _liveDpiDebounceCts;
    private bool _isLoadingProfile;

    [ObservableProperty] private int         _selectedPollingRate = 1000;
    [ObservableProperty] private bool        _hasUnsavedChanges   = false;
    [ObservableProperty] private bool        _sensitivityStagesEnabled = false;

    public ObservableCollection<DpiStageViewModel> DpiStages { get; } = new();
    public IEnumerable<DpiStageViewModel> VisibleDpiStages => SensitivityStagesEnabled ? DpiStages : DpiStages.Take(1);

    public List<int> PollingRateOptions { get; } = new() { 125, 500, 1000 };

    public PerformanceViewModel(IMouseService mouseService)
    {
        _mouseService = mouseService;

        // Populate default 5 stages
        foreach (var s in DpiStage.CreateDefaults())
            DpiStages.Add(new DpiStageViewModel(s));

        SelectedStage = DpiStages[0];
        DpiStages[0].IsActive = true;

        // Attach change tracking
        foreach (var vm in DpiStages)
            AttachStage(vm);
    }

    [ObservableProperty] private DpiStageViewModel? _selectedStage;

    public void LoadFromProfile(MouseProfile profile)
    {
        _isLoadingProfile = true;
        DpiStages.Clear();
        foreach (var s in profile.DpiStages)
        {
            var vm = new DpiStageViewModel(s);
            AttachStage(vm);
            DpiStages.Add(vm);
        }
        var activeIndex = Math.Clamp(profile.ActiveDpiStage, 0, Math.Max(0, DpiStages.Count - 1));
        var active = DpiStages[activeIndex];
        active.IsActive = true;
        SelectedStage = active;
        SelectedPollingRate = (int)profile.PollingRate;
        SensitivityStagesEnabled = profile.DpiStages.Count(stage => stage.Enabled) > 1;
        HasUnsavedChanges   = false;
        OnPropertyChanged(nameof(VisibleDpiStages));
        _isLoadingProfile = false;
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        var profile = _mouseService.ActiveProfile;
        if (profile == null) return;

        if (!SensitivityStagesEnabled)
        {
            for (var i = 0; i < DpiStages.Count; i++)
            {
                DpiStages[i].Enabled = i == 0;
                DpiStages[i].IsActive = i == 0;
            }
            SelectedStage = DpiStages[0];
        }

        profile.DpiStages = DpiStages.Select(vm =>
        {
            var stage = vm.ToDomainModel();
            stage.Enabled = SensitivityStagesEnabled ? vm.Enabled : vm.StageIndex == 0;
            return stage;
        }).ToList();
        profile.PollingRate = SelectedPollingRate switch
        {
            500  => PollingRate.Hz500,
            125  => PollingRate.Hz125,
            _    => PollingRate.Hz1000,
        };
        profile.ActiveDpiStage = SensitivityStagesEnabled
            ? DpiStages.IndexOf(DpiStages.First(s => s.IsActive))
            : 0;

        await _mouseService.SaveAndApplyProfileAsync(profile);
        HasUnsavedChanges = false;
    }

    [RelayCommand]
    private async Task SetActiveStageAsync(DpiStageViewModel stage)
    {
        if (!SensitivityStagesEnabled && DpiStages.IndexOf(stage) != 0)
            return;

        foreach (var s in DpiStages) s.IsActive = false;
        stage.IsActive    = true;
        SelectedStage     = stage;
        HasUnsavedChanges = true;

        var device = _mouseService.ActiveDevice;
        if (device?.IsConnected == true)
        {
            var dpiStages = DpiStages
                .OrderBy(s => s.StageIndex)
                .Select(s => SensitivityStagesEnabled || s.StageIndex == 0 ? s.Dpi : 0)
                .ToList();

            var activeStageIndex = DpiStages
                .OrderBy(s => s.StageIndex)
                .ToList()
                .FindIndex(s => s.IsActive);
            if (activeStageIndex < 0) activeStageIndex = 0;

            // Send full multi-stage DPI write so hardware applies the correct DPI instantly.
            await device.SetDpiStagesAsync(dpiStages, activeStageIndex, CancellationToken.None);
        }
    }

    partial void OnSelectedPollingRateChanged(int value)
    {
        HasUnsavedChanges = true;

        var device = _mouseService.ActiveDevice;
        if (device?.IsConnected == true)
        {
            PollingRate rate = value switch
            {
                500 => PollingRate.Hz500,
                125 => PollingRate.Hz125,
                _ => PollingRate.Hz1000,
            };
            // Fire and forget, no UI blocking
            _ = device.SetPollingRateAsync(rate, CancellationToken.None);
        }
    }

    partial void OnSensitivityStagesEnabledChanged(bool value)
    {
        if (!value && DpiStages.Count > 0)
        {
            foreach (var stage in DpiStages)
            {
                stage.IsActive = false;
                stage.Enabled = stage.StageIndex == 0;
            }

            DpiStages[0].IsActive = true;
            SelectedStage = DpiStages[0];
        }
        else if (value)
        {
            foreach (var stage in DpiStages)
                stage.Enabled = true;

            if (SelectedStage == null && DpiStages.Count > 0)
            {
                DpiStages[0].IsActive = true;
                SelectedStage = DpiStages[0];
            }
        }

        HasUnsavedChanges = true;
        OnPropertyChanged(nameof(VisibleDpiStages));
    }

    private void AttachStage(DpiStageViewModel vm)
    {
        vm.PropertyChanged += OnStagePropertyChanged;
    }

    private void OnStagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not DpiStageViewModel stage) return;
        if (_isLoadingProfile) return;

        HasUnsavedChanges = true;

        if (e.PropertyName == nameof(DpiStageViewModel.Dpi) && stage.IsActive)
        {
            _ = QueueLiveDpiUpdateAsync(stage);
        }
    }

    private async Task QueueLiveDpiUpdateAsync(DpiStageViewModel stage)
    {
        _liveDpiDebounceCts?.Cancel();
        _liveDpiDebounceCts?.Dispose();
        _liveDpiDebounceCts = new CancellationTokenSource();
        var ct = _liveDpiDebounceCts.Token;

        try
        {
            await Task.Delay(120, ct);
            if (ct.IsCancellationRequested) return;

            var device = _mouseService.ActiveDevice;
            if (device == null || !device.IsConnected) return;

            // Use full multi-stage DPI write (0x04/0x06 size 0x26) + apply,
            // matching the captured Synapse transaction shape.
            var dpiStages = DpiStages
                .OrderBy(s => s.StageIndex)
                .Select(s => SensitivityStagesEnabled || s.StageIndex == 0 ? s.Dpi : 0)
                .ToList();

            var activeStageIndex = DpiStages
                .OrderBy(s => s.StageIndex)
                .ToList()
                .FindIndex(s => s.IsActive);

            if (activeStageIndex < 0) activeStageIndex = 0;

            await device.SetDpiStagesAsync(dpiStages, activeStageIndex, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            // Newer slider value superseded this write.
        }
    }
}
