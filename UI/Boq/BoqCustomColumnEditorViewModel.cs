using System.Collections.ObjectModel;
using System.Windows.Input;
using Pulse.Core.Boq;
using Pulse.UI.ViewModels;

namespace Pulse.UI.Boq
{
    /// <summary>
    /// ViewModel for the Add/Edit custom column dialog embedded in the BOQ settings panel.
    /// Supports all three <see cref="CustomColumnKind"/> values via a UI picker.
    /// </summary>
    public class BoqCustomColumnEditorViewModel : ViewModelBase
    {
        // ── Identity ─────────────────────────────────────────────────────────

        private string _columnKey = string.Empty;
        /// <summary>Internal unique key.  Auto-generated when creating; shown in edit mode.</summary>
        public string ColumnKey
        {
            get => _columnKey;
            set => SetField(ref _columnKey, value);
        }

        private string _header = string.Empty;
        public string Header
        {
            get => _header;
            set => SetField(ref _header, value);
        }

        // ── Kind ─────────────────────────────────────────────────────────────

        private CustomColumnKind _kind = CustomColumnKind.Concat;
        public CustomColumnKind Kind
        {
            get => _kind;
            set => SetField(ref _kind, value);
        }

        // ── Sources ──────────────────────────────────────────────────────────

        /// <summary>All field keys available as sources (populated by the owner VM).</summary>
        public ObservableCollection<string> AvailableSourceKeys { get; } = new ObservableCollection<string>();

        /// <summary>Selected source keys for this custom column.</summary>
        public ObservableCollection<string> SelectedSourceKeys { get; } = new ObservableCollection<string>();

        // ── Delimiter ────────────────────────────────────────────────────────

        private string _delimiter = " ";
        public string Delimiter
        {
            get => _delimiter;
            set => SetField(ref _delimiter, value);
        }

        // ── Validation ───────────────────────────────────────────────────────

        public bool IsValid =>
            !string.IsNullOrWhiteSpace(Header)
            && SelectedSourceKeys.Count > 0;

        // ── Factories ────────────────────────────────────────────────────────

        /// <summary>Populate this VM from an existing definition (edit mode).</summary>
        public void LoadFrom(BoqCustomColumnDefinition def)
        {
            ColumnKey = def.ColumnKey;
            Header    = def.Header;
            Kind      = def.Kind;
            Delimiter = def.Delimiter ?? " ";

            SelectedSourceKeys.Clear();
            foreach (var k in def.SourceKeys)
                SelectedSourceKeys.Add(k);
        }

        /// <summary>Build a <see cref="BoqCustomColumnDefinition"/> from the current state.</summary>
        public BoqCustomColumnDefinition ToDefinition()
        {
            var def = new BoqCustomColumnDefinition
            {
                ColumnKey = string.IsNullOrWhiteSpace(ColumnKey)
                    ? "Custom_" + Header.Replace(" ", "_")
                    : ColumnKey,
                Header    = Header,
                Kind      = Kind,
                Delimiter = Delimiter,
            };
            def.SourceKeys.AddRange(SelectedSourceKeys);
            return def;
        }
    }
}
