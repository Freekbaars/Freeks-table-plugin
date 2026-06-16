using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RhinoTable.Core.Models
{
    public enum HorizontalAlignment { Left, Center, Right }
    public enum VerticalAlignment   { Top, Middle, Bottom }

    public class TableCellData : INotifyPropertyChanged
    {
        private string _text = string.Empty;
        private string? _fontName;
        private double? _fontSize;
        private bool _bold;
        private bool _italic;
        private HorizontalAlignment _hAlign = HorizontalAlignment.Left;
        private VerticalAlignment   _vAlign = VerticalAlignment.Middle;
        private string? _backgroundColor;
        private int  _mergeRight;
        private int  _mergeDown;
        private bool _isMergedHidden;

        public string  Text             { get => _text;             set { _text           = value; Notify(); } }
        public string? FontName         { get => _fontName;         set { _fontName        = value; Notify(); } }
        public double? FontSize         { get => _fontSize;         set { _fontSize        = value; Notify(); } }
        public bool    Bold             { get => _bold;             set { _bold            = value; Notify(); } }
        public bool    Italic           { get => _italic;           set { _italic          = value; Notify(); } }
        public HorizontalAlignment HorizontalAlignment { get => _hAlign; set { _hAlign    = value; Notify(); } }
        public VerticalAlignment   VerticalAlignment   { get => _vAlign; set { _vAlign    = value; Notify(); } }
        public string? BackgroundColor  { get => _backgroundColor;  set { _backgroundColor = value; Notify(); } }
        public int     MergeRight       { get => _mergeRight;       set { _mergeRight      = value; Notify(); } }
        public int     MergeDown        { get => _mergeDown;        set { _mergeDown       = value; Notify(); } }
        public bool    IsMergedHidden   { get => _isMergedHidden;   set { _isMergedHidden  = value; Notify(); } }

        // Kleur & patroon
        private string? _textColor;
        private int     _fillPattern;

        public string? TextColor    { get => _textColor;    set { _textColor    = value; Notify(); } }
        // BackgroundColor = vulkleur (hex, bijv. "FF4444"), null = transparant
        // FillPattern: 0=Geen, 1=Effen, 2=Horizontaal, 3=Verticaal, 4=Diagonaal, 5=Kruis
        public int     FillPattern  { get => _fillPattern;  set { _fillPattern  = value; Notify(); } }

        // Aparte arceerkleur voor hatch-patronen (null = gebruik BackgroundColor)
        private string? _hatchColor;
        public string? HatchColor { get => _hatchColor; set { _hatchColor = value; Notify(); } }

        // Naam van Rhino-arceerpatroon (bijv. "Hatch1"). Null = geen Rhino-hatch.
        // Heeft voorrang boven FillPattern 2-5 (legacy).
        private string? _hatchPatternName;
        public string? HatchPatternName { get => _hatchPatternName; set { _hatchPatternName = value; Notify(); } }

        // Schaal van het Rhino arceerpatroon (standaard 1.0).
        private double _hatchScale = 1.0;
        public double HatchScale { get => _hatchScale; set { _hatchScale = value > 0 ? value : 1.0; Notify(); } }

        // Rotatie van het Rhino arceerpatroon in graden (standaard 0).
        private double _hatchRotation = 0.0;
        public double HatchRotation { get => _hatchRotation; set { _hatchRotation = value; Notify(); } }

        // Tekst terugloopt naar volgende regel als de breedte is bereikt
        private bool _wordWrap;
        public bool WordWrap { get => _wordWrap; set { _wordWrap = value; Notify(); } }

        // Celranden: dikte in mm per zijde (0 = geen aangepaste rand), kleur hex zonder #
        private float  _borderTop, _borderBottom, _borderLeft, _borderRight;
        private string? _borderColor;

        public float   BorderTop    { get => _borderTop;    set { _borderTop    = value; Notify(); } }
        public float   BorderBottom { get => _borderBottom; set { _borderBottom = value; Notify(); } }
        public float   BorderLeft   { get => _borderLeft;   set { _borderLeft   = value; Notify(); } }
        public float   BorderRight  { get => _borderRight;  set { _borderRight  = value; Notify(); } }
        public string? BorderColor  { get => _borderColor;  set { _borderColor  = value; Notify(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify([CallerMemberName] string? p = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }
}
