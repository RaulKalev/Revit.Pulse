using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Pulse.Core.Settings;

namespace Pulse.UI.ViewModels
{
    /// <summary>
    /// Editable ViewModel wrapper around a single ParameterMapping.
    /// Exposed as rows in the Settings dialog.
    /// </summary>
    public class ParameterMappingViewModel : ViewModelBase
    {
        /// <summary>Logical key used internally by the module (e.g. "Panel", "Address").</summary>
        public string LogicalName { get; }

        /// <summary>Whether this mapping is essential for the module to function.</summary>
        public bool IsRequired { get; }

        /// <summary>The factory default Revit parameter name — used by Reset to Defaults.</summary>
        public string DefaultRevitParameterName { get; }

        private string _revitParameterName;
        /// <summary>The actual Revit parameter name in the user's model (editable).</summary>
        public string RevitParameterName
        {
            get => _revitParameterName;
            set => SetField(ref _revitParameterName, value);
        }

        public ParameterMappingViewModel(ParameterMapping mapping)
        {
            if (mapping == null) throw new ArgumentNullException(nameof(mapping));
            LogicalName = mapping.LogicalName;
            IsRequired = mapping.IsRequired;
            RevitParameterName = mapping.RevitParameterName;
            DefaultRevitParameterName = mapping.DefaultRevitParameterName ?? mapping.RevitParameterName;
        }

        /// <summary>Convert back to the model type for saving.</summary>
        public ParameterMapping ToModel() =>
            new ParameterMapping(LogicalName, RevitParameterName, IsRequired)
            {
                DefaultRevitParameterName = DefaultRevitParameterName
            };
    }

    /// <summary>
    /// ViewModel for the Settings dialog.
    /// Lets users configure the Revit category and parameter name mappings for a module.
    /// </summary>
    public class SettingsViewModel : ViewModelBase
    {
        private readonly ModuleSettings _defaults;

        // ─── Display ──────────────────────────────────────────────────────────
        public string ModuleName { get; }
        public string ModuleDescription { get; }

        // ─── Editable fields ─────────────────────────────────────────────────
        private string _revitCategory;
        /// <summary>The Revit category name the module collects elements from.</summary>
        public string RevitCategory
        {
            get => _revitCategory;
            set => SetField(ref _revitCategory, value);
        }

        /// <summary>Observable list of parameter mappings shown as editable rows.</summary>
        public ObservableCollection<ParameterMappingViewModel> Mappings { get; }
            = new ObservableCollection<ParameterMappingViewModel>();

        // ─── Commands ────────────────────────────────────────────────────────
        public ICommand SaveCommand { get; }
        public ICommand ResetDefaultsCommand { get; }
        public ICommand CancelCommand { get; }

        // ─── Events ──────────────────────────────────────────────────────────
        /// <summary>Raised with the validated new settings when the user clicks Save.</summary>
        public event Action<ModuleSettings> SettingsSaved;

        /// <summary>Raised when the user clicks Cancel (no changes committed).</summary>
        public event Action Cancelled;

        public SettingsViewModel(ModuleSettings current, ModuleSettings defaults,
                                 string moduleName, string moduleDescription)
        {
            _defaults = defaults ?? throw new ArgumentNullException(nameof(defaults));
            ModuleName = moduleName ?? string.Empty;
            ModuleDescription = moduleDescription ?? string.Empty;

            // Seed from current settings (fall back to defaults if current is null)
            var source = current ?? defaults;
            RevitCategory = source.Categories?.Count > 0 ? source.Categories[0] : string.Empty;
            foreach (var m in source.ParameterMappings ?? defaults.ParameterMappings)
                Mappings.Add(new ParameterMappingViewModel(m));

            SaveCommand = new RelayCommand(ExecuteSave);
            ResetDefaultsCommand = new RelayCommand(ExecuteResetDefaults);
            CancelCommand = new RelayCommand(_ => Cancelled?.Invoke());
        }

        // ─── Helpers ─────────────────────────────────────────────────────────

        private void ExecuteSave() => SettingsSaved?.Invoke(BuildSettings());

        private void ExecuteResetDefaults()
        {
            RevitCategory = _defaults.Categories?.Count > 0 ? _defaults.Categories[0] : string.Empty;
            Mappings.Clear();
            foreach (var m in _defaults.ParameterMappings)
                Mappings.Add(new ParameterMappingViewModel(m));
        }

        private ModuleSettings BuildSettings()
        {
            var settings = new ModuleSettings
            {
                ModuleId = _defaults.ModuleId,
                SchemaVersion = _defaults.SchemaVersion,
                Categories = new List<string>(),
                ParameterMappings = new List<ParameterMapping>()
            };

            if (!string.IsNullOrWhiteSpace(RevitCategory))
                settings.Categories.Add(RevitCategory.Trim());

            foreach (var vm in Mappings)
                settings.ParameterMappings.Add(vm.ToModel());

            return settings;
        }
    }
}
