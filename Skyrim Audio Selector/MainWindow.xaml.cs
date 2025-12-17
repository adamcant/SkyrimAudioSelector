using System.Collections.ObjectModel;
using System.Media;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Skyrim_Audio_Selector
{
    public partial class MainWindow : Window
    {
        private MenuItem _miOpenConflictInExplorer;
        private MenuItem _miOpenVariantInExplorer;

        private bool _isDarkMode = true;

        private SoundVariant _currentlyPlayingVariant;
        private SoundPlayer _soundPlayer;
        private DispatcherTimer _playbackTimer;

        private readonly Dictionary<string, string> _bsaExtractCache =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _transcodedCache =
            new(StringComparer.OrdinalIgnoreCase);

        private Dictionary<string, List<SoundVariant>> _conflicts =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SoundVariant> _winners =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly ObservableCollection<SoundVariant> _currentVariants = [];
        private string _currentConflictKey;

        private CancellationTokenSource _durationCts;

        private static readonly string FfmpegPath = ResolveFfmpegPath();

        private const string PatchFolderName = "SkyrimAudioSelector_Patch";
        private const string PatchModDisplayName = "SkyrimAudioSelector_Patch (Generated Patch)";

        private static readonly Regex DurationRegex = GenerateDurationRegex();

        public MainWindow()
        {
            InitializeComponent();
            VariantsDataGrid.ItemsSource = _currentVariants;

            SetTheme(true);
            SetupExplorerContextMenus();
        }

        [GeneratedRegex(@"Duration:\s(?<h>\d+):(?<m>\d+):(?<s>\d+(\.\d+)?)", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-SE")]
        private static partial Regex GenerateDurationRegex();
    }
}
