using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Gigasoft.ProEssentials;
using Gigasoft.ProEssentials.Enums;
using System.Windows.Media;

namespace AudioWaveform
{
    /// <summary>
    /// ProEssentials WPF Audio Waveform Oscilloscope
    ///
    /// Displays dual-channel audio waveforms as a live oscilloscope using PesgoWpf.
    ///
    /// Features:
    ///   - 5-song playlist (AI-generated songs about ProEssentials)
    ///   - Dual channel (Left + Right) on separate synchronized axes
    ///   - UseDataAtLocation — zero-copy, chart reads app memory directly
    ///   - mciSendString (Win32 MCI) for lightweight audio playback
    ///   - Vertical line annotation tracks playback position in real time
    ///   - Smart viewport tracking — follows playhead when zoomed in
    ///   - Custom X axis: time-formatted labels (M:SS.mmm)
    ///   - ZoomWindow overview at bottom shows full waveform + position
    ///   - Synchronized lyrics overlay (graph annotations) — all 5 songs
    ///   - Lyrics on/off toggle (button + right-click menu)
    ///   - Overlap Axes mode: L+R on same plot, amber vs cyan colors
    ///   - Auto-play next song when current song ends
    ///   - Background pre-load: next song data ready in memory before needed
    ///   - Full options menu on right-click chart (ProEssentials custom menus,
    ///     per Example 127 — CustomMenuText / CustomMenuState / PeCustomMenu)
    ///
    /// Audio technology note for repo README:
    ///   This demo uses mciSendString (Win32 MCI / winmm.dll) — the lightest
    ///   possible playback mechanism, pure Win32, no NuGet packages.
    ///   Alternatives: NAudio (full-featured, NuGet), WPF MediaPlayer (COM/WMP
    ///   wrapper), MediaFoundation (modern Win32, more setup code).
    ///   mciSendString wins for zero-dependency demo goals.
    /// </summary>
    public partial class MainWindow : Window
    {
        // -----------------------------------------------------------------------
        // Win32 MCI
        // -----------------------------------------------------------------------
        [DllImport("winmm.dll", CharSet = CharSet.Auto)]
        private static extern int mciSendString(string command,
            StringBuilder returnValue, int returnLength, IntPtr callback);

        private const string MCI_ALIAS = "ProEssentialsSong";

        // -----------------------------------------------------------------------
        // Song definitions
        // -----------------------------------------------------------------------
        private readonly string[] SongFiles = {
            "What-I-Already-Done-GigaSoft.wav",
            "ItAllComesOutInTheWash-GigaSoft.wav",
            "BabyYouCanRender-Gigasoft.wav",
            "NobodyRendersLikeWeDo-Gigasoft.wav",
            "PourMeSomeProEssentials-GigaSoft.wav",
        };

        private readonly string[] SongTitles = {
            "What I Already Done",
            "It All Comes Out In The Wash",
            "Baby You Can Render",
            "Nobody Renders Like We Do",
            "Pour Me Some Pro-Essentials",
        };

        // -----------------------------------------------------------------------
        // Audio data — current song
        // UseDataAtLocation passes pointers — no copy, arrays stay on LOH.
        // Y layout: [0..SongSize-1] = left channel, [SongSize..2*SongSize-1] = right
        // -----------------------------------------------------------------------
        private float[] tmpSongXData;
        private float[] tmpSongYData;
        private int     SongSize;
        private int     SampleRate = 16000;

        // -----------------------------------------------------------------------
        // Pre-loaded next song
        // Background Task reads the next WAV; volatile bool signals completion.
        // -----------------------------------------------------------------------
        private float[]       _preloadXData;
        private float[]       _preloadYData;
        private int           _preloadSize;
        private int           _preloadRate;
        private int           _preloadSongIndex  = -1;
        private volatile bool _preloadReady      = false;

        // -----------------------------------------------------------------------
        // Playback / UI state
        // -----------------------------------------------------------------------
        private bool   bPlayingSong      = false;
        private int    m_nCurrentSong    = 0;
        private int    m_nLastPositionMs = 0;
        private bool   m_bOScopeMode     = false;
        private bool   m_bDbMode         = false;
        private bool   m_bShowLyrics     = true;
        private bool   m_bOverlapAxes    = false;
        private bool   m_bAutoPlayNext   = true;
        private double m_dOScopeHalfRange = 300;  // default 37 ms at 16000 Hz

        private DispatcherTimer _timer;

        // -----------------------------------------------------------------------
        // Overlap-mode colors
        // Amber (L) vs cyan (R) — high contrast pair on dark background.
        // -----------------------------------------------------------------------
        private static readonly Color OverlapColorLeft  = Color.FromArgb(255, 255, 140,   0);
        private static readonly Color OverlapColorRight = Color.FromArgb(255,   0, 255, 255);
        private static readonly Color NormalColorLeft   = Color.FromArgb(255,  40, 225, 225);
        private static readonly Color NormalColorRight  = Color.FromArgb(255,   0, 255, 255);

        // -----------------------------------------------------------------------
        // Custom menu indices (right-click chart, per Example 127)
        //
        //  0  "|"                            separator
        //  1  "▶ Play / Pause"               simple toggle
        //  2  "■ Stop|Keep Position|Rewind"  popup submenu
        //  3  "↺ Reset|Full Reset|Zoom Only|Position Only"  popup submenu
        //  4  "|"                            separator
        //  5  "⊙ O-Scope|Tight 37ms|Medium 100ms|Wide 250ms|Exit Full View"  popup
        //  6  "≋ dBFS Scale"                 checkable toggle
        //  7  "|"                            separator
        //  8  "🎵 Lyrics"                    checkable toggle  (default: Checked)
        //  9  "⊡ Overlap Axes"               checkable toggle  (default: UnChecked)
        // 10  "|"                            separator
        // 11  "⏭ Next Song"                  simple
        // 12  "⏮ Previous Song"              simple
        // 13  "🔁 Auto-Play Next"             checkable toggle  (default: Checked)
        // -----------------------------------------------------------------------
        private const int MENU_PLAY        = 1;
        private const int MENU_STOP        = 2;
        private const int MENU_RESET       = 3;
        private const int MENU_OSCOPE      = 5;
        private const int MENU_DBFS        = 6;
        private const int MENU_LYRICS      = 8;
        private const int MENU_OVERLAP     = 9;
        private const int MENU_NEXT        = 11;
        private const int MENU_PREV        = 12;
        private const int MENU_AUTOPLAY    = 13;

        // -----------------------------------------------------------------------
        // Constructor
        // -----------------------------------------------------------------------
        public MainWindow()
        {
            InitializeComponent();
            Pesgo1.PeCustomGridNumber += new PesgoWpf.CustomGridNumberEventHandler(Pesgo1_PeCustomGridNumber);
            Pesgo1.PeCustomMenu       += new PesgoWpf.CustomMenuEventHandler(Pesgo1_PeCustomMenu);
            Pesgo1.MouseDown          += new MouseButtonEventHandler(Pesgo1_MouseDown);
        }

        // -----------------------------------------------------------------------
        // Window initialized — populate playlist
        // -----------------------------------------------------------------------
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            foreach (string title in SongTitles)
                SongList.Items.Add(title);

            SongList.SelectedIndex = 0;

            _timer          = new DispatcherTimer(DispatcherPriority.Render);
            _timer.Interval = TimeSpan.FromMilliseconds(25);
            _timer.Tick    += Timer_Tick;
        }

        void Pesgo1_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSong(0);
        }

        // -----------------------------------------------------------------------
        // LoadSong — load WAV, configure chart, kick off pre-load of next song
        // -----------------------------------------------------------------------
        void LoadSong(int songIndex)
        {
            bool wasPlaying = bPlayingSong;
            if (bPlayingSong) StopPlayback();

            m_nCurrentSong    = songIndex;
            m_nLastPositionMs = 0;

            // Use pre-loaded data if available — instant, no file I/O
            if (_preloadReady && _preloadSongIndex == songIndex)
            {
                tmpSongXData      = _preloadXData;
                tmpSongYData      = _preloadYData;
                SongSize          = _preloadSize;
                SampleRate        = _preloadRate;
                _preloadXData     = null;
                _preloadYData     = null;
                _preloadReady     = false;
                _preloadSongIndex = -1;
            }
            else
            {
                string filePath = SongFiles[songIndex];
                FileStream inStream = null;
                try { inStream = File.OpenRead(filePath); }
                catch
                {
                    MessageBox.Show(
                        $"Song file not found:\n{filePath}\n\nMake sure all WAV files are in the same folder as the executable.",
                        "File Not Found", MessageBoxButton.OK);
                    return;
                }

                WaveReader wr = new WaveReader(inStream);
                inStream.Close();

                SampleRate   = wr.SampleRate;
                SongSize     = wr.Frames;
                tmpSongXData = new float[SongSize];
                tmpSongYData = new float[SongSize * 2];

                for (int k = 0; k < SongSize; k++)
                {
                    tmpSongXData[k]            = k + 1;
                    tmpSongYData[k]            = (float)wr.Data[0][k] / 327.680F;
                    tmpSongYData[k + SongSize] = (float)wr.Data[1][k] / 327.680F;
                }
            }

            InitChart();

            if (wasPlaying) StartPlayback();

            // Pre-load next song in background
            PreloadSongAsync((m_nCurrentSong + 1) % SongFiles.Length);
        }

        // -----------------------------------------------------------------------
        // PreloadSongAsync — read next WAV on a background thread
        // -----------------------------------------------------------------------
        void PreloadSongAsync(int songIndex)
        {
            _preloadReady     = false;
            _preloadSongIndex = -1;

            Task.Run(() =>
            {
                FileStream inStream = null;
                try { inStream = File.OpenRead(SongFiles[songIndex]); }
                catch { return; }

                WaveReader wr = new WaveReader(inStream);
                inStream.Close();

                int    size  = wr.Frames;
                var    xData = new float[size];
                var    yData = new float[size * 2];

                for (int k = 0; k < size; k++)
                {
                    xData[k]        = k + 1;
                    yData[k]        = (float)wr.Data[0][k] / 327.680F;
                    yData[k + size] = (float)wr.Data[1][k] / 327.680F;
                }

                _preloadXData     = xData;
                _preloadYData     = yData;
                _preloadSize      = size;
                _preloadRate      = wr.SampleRate;
                _preloadSongIndex = songIndex;
                _preloadReady     = true;    // volatile write — visible to dispatcher
            });
        }

        // -----------------------------------------------------------------------
        // InitChart — configure PesgoWpf for the loaded song
        // -----------------------------------------------------------------------
        void InitChart()
        {
            Pesgo1.PeFunction.Reset();
            Pesgo1.PeUserInterface.Dialog.ModelessAutoClose = true;
            Pesgo1.PeFunction.Reinitialize();

            // =======================================================================
            // Data — UseDataAtLocation, zero-copy
            // =======================================================================
            Pesgo1.PeData.Subsets        = 2;
            Pesgo1.PeData.Points         = SongSize;
            Pesgo1.PeData.DuplicateDataX = DuplicateData.PointIncrement;
            Pesgo1.PeData.X.UseDataAtLocation(tmpSongXData, SongSize);
            Pesgo1.PeData.Y.UseDataAtLocation(tmpSongYData, SongSize * 2);

            // =======================================================================
            // Multi-axes
            // =======================================================================
            Pesgo1.PeGrid.MultiAxesSubsets[0] = 1;
            Pesgo1.PeGrid.MultiAxesSubsets[1] = 1;

            if (m_bOverlapAxes)
                Pesgo1.PeGrid.OverlapMultiAxes[0] = 2;

            Pesgo1.PeGrid.WorkingAxis = 0;
            Pesgo1.PeString.YAxisLabel = "Left";
            Pesgo1.PeGrid.WorkingAxis = 1;
            Pesgo1.PeString.YAxisLabel = "Right";
            Pesgo1.PeGrid.WorkingAxis = 0;

            // =======================================================================
            // Plot method and fixed Y scale ±70
            // =======================================================================
            Pesgo1.PePlot.Method              = SGraphPlottingMethod.Line;
            Pesgo1.PePlot.Option.NullDataGaps = false;
            Pesgo1.PeData.NullDataValue       = -999;
            Pesgo1.PeData.AutoScaleData       = false;

            Pesgo1.PeGrid.WorkingAxis = 0;
            Pesgo1.PeGrid.Configure.ManualScaleControlY = ManualScaleControl.MinMax;
            Pesgo1.PeGrid.Configure.ManualMinY          = -70.0F;
            Pesgo1.PeGrid.Configure.ManualMaxY          =  70.0F;
            Pesgo1.PeGrid.Option.ShowYAxis              = ShowAxis.Empty;

            Pesgo1.PeGrid.WorkingAxis = 1;
            Pesgo1.PeGrid.Configure.ManualScaleControlY = ManualScaleControl.MinMax;
            Pesgo1.PeGrid.Configure.ManualMinY          = -70.0F;
            Pesgo1.PeGrid.Configure.ManualMaxY          =  70.0F;
            Pesgo1.PeGrid.Option.ShowYAxis              = ShowAxis.Empty;

            Pesgo1.PeGrid.WorkingAxis = 0;

            Pesgo1.PeFont.LineAnnotationTextSize = 60;
            SetYAxisAnnotations();

            // =======================================================================
            // Subset colors
            // =======================================================================
            ApplySubsetColors();

            Pesgo1.PeColor.SubsetShades[0]    = Color.FromArgb(255, 128, 128, 128);
            Pesgo1.PeColor.SubsetShades[1]    = Color.FromArgb(255, 128, 128, 128);
            Pesgo1.PePlot.SubsetLineTypes[0]  = LineType.ThinSolid;
            Pesgo1.PePlot.SubsetLineTypes[1]  = LineType.ThinSolid;
            Pesgo1.PePlot.Option.LineShadows  = false;
            Pesgo1.PePlot.DataShadows         = DataShadows.None;

            Pesgo1.PeString.SubsetLabels[0] = "Left";
            Pesgo1.PeString.SubsetLabels[1] = "Right";
            Pesgo1.PeLegend.Show            = m_bOverlapAxes;
            Pesgo1.PeLegend.SimpleLine      = true;
            Pesgo1.PeLegend.Style           = LegendStyle.OneLineInsideOverlap;

            // =======================================================================
            // Styling
            // =======================================================================
            Pesgo1.PeColor.BitmapGradientMode         = false;
            Pesgo1.PeColor.QuickStyle                 = QuickStyle.DarkNoBorder;
            Pesgo1.PeGrid.Configure.AutoMinMaxPadding = 1;

            // =======================================================================
            // Zoom and navigation
            // =======================================================================
            Pesgo1.PePlot.ZoomWindow.Show                       = true;
            Pesgo1.PePlot.ZoomWindow.ShowAnnotations            = true;
            Pesgo1.PePlot.ZoomWindow.ShowXAxis                  = false;
            Pesgo1.PeUserInterface.Allow.Zooming                = AllowZooming.Horizontal;
            Pesgo1.PeUserInterface.Allow.ZoomStyle              = ZoomStyle.Ro2Not;
            Pesgo1.PeUserInterface.Scrollbar.MouseWheelFunction = MouseWheelFunction.HorizontalZoom;
            Pesgo1.PeUserInterface.Scrollbar.ScrollingHorzZoom  = true;
            Pesgo1.PeUserInterface.Scrollbar.MouseDraggingX     = true;

            // =======================================================================
            // Playhead annotation
            // =======================================================================
            Pesgo1.PeAnnotation.Show                    = true;
            Pesgo1.PeAnnotation.Line.XAxisShow          = true;
            Pesgo1.PeAnnotation.Line.XAxis[0]           = 1;
            Pesgo1.PeAnnotation.Line.XAxisType[0]       = LineAnnotationType.MediumThinSolid;
            Pesgo1.PeAnnotation.Line.XAxisColor[0]      = Color.FromArgb(255, 255, 50, 50);
            Pesgo1.PeAnnotation.Line.XAxisZoomWindow[0] = AnnotationZoomWindow.GraphAndZoomWindow;
            Pesgo1.PeAnnotation.Line.TextSize            = 100;

            // =======================================================================
            // X axis — custom time format
            // =======================================================================
            Pesgo1.PeGrid.Option.ShowXAxis          = ShowAxis.GridNumbers;
            Pesgo1.PeGrid.Option.CustomGridNumbersX = true;

            // =======================================================================
            // Menu and dialog simplification
            // =======================================================================
            Pesgo1.PeUserInterface.Menu.Help                = MenuControl.Hide;
            Pesgo1.PeUserInterface.Menu.IncludeDataLabels   = MenuControl.Hide;
            Pesgo1.PeUserInterface.Menu.LegendLocation      = MenuControl.Hide;
            Pesgo1.PeUserInterface.Menu.MarkDataPoints      = MenuControl.Hide;
            Pesgo1.PeUserInterface.Menu.DataShadow          = MenuControl.Hide;
            Pesgo1.PeUserInterface.Menu.DataPrecision       = MenuControl.Hide;
            Pesgo1.PeUserInterface.Menu.BitmapGradient      = MenuControl.Hide;
            Pesgo1.PeUserInterface.Menu.GridInFront         = MenuControl.Hide;
            Pesgo1.PeUserInterface.Menu.ViewingStyle        = MenuControl.Hide;
            Pesgo1.PeUserInterface.Menu.GridBands           = MenuControl.Hide;

            Pesgo1.PeUserInterface.Dialog.RandomPointsToExport = false;
            Pesgo1.PeUserInterface.Allow.FocalRect             = false;
            Pesgo1.PePlot.Allow.Bar                            = false;
            Pesgo1.PeUserInterface.Dialog.PlotCustomization    = false;
            Pesgo1.PeUserInterface.Dialog.Page2                = false;
            Pesgo1.PeUserInterface.Dialog.Axis                 = false;
            Pesgo1.PeUserInterface.Dialog.Subsets              = false;
            Pesgo1.PeUserInterface.Allow.TextExport            = false;
            Pesgo1.PeUserInterface.Dialog.AllowEmfExport       = false;
            Pesgo1.PeUserInterface.Dialog.AllowWmfExport       = false;

            // =======================================================================
            // Custom right-click menu items (Example 127 pattern)
            // =======================================================================
            SetupCustomMenus();

            // =======================================================================
            // Titles
            // =======================================================================
            Pesgo1.PeString.MainTitle    = SongTitles[m_nCurrentSong];
            Pesgo1.PeString.SubTitle     = "||Mouse-Wheel-Zoom  /  R-Click for menu";
            Pesgo1.PeFont.MainTitle.Font = "Arial";
            Pesgo1.PeFont.SubTitle.Font  = "Arial";

            // =======================================================================
            // Lyrics graph annotation
            // =======================================================================
            Pesgo1.PeAnnotation.Graph.Show    = m_bShowLyrics;
            Pesgo1.PeAnnotation.Graph.TextSize = 400;

            // =======================================================================
            // Cursor — disabled for performance at high zoom
            // =======================================================================
            Pesgo1.PeUserInterface.Cursor.PromptTracking      = false;
            Pesgo1.PeUserInterface.Cursor.PromptStyle         = CursorPromptStyle.None;
            Pesgo1.PeUserInterface.Cursor.HourGlassThreshold  = 1000000000;

            // =======================================================================
            // Rendering
            // =======================================================================
            Pesgo1.PeConfigure.Composite2D3D         = Composite2D3D.Foreground;
            Pesgo1.PeConfigure.RenderEngine          = RenderEngine.Direct3D;
            Pesgo1.PeSpecial.AutoImageReset          = false;
            Pesgo1.PeFont.SizeGlobalCntl             = 0.6F;
            Pesgo1.PeFunction.Force3dxNewColors      = true;
            Pesgo1.PeFunction.Force3dxVerticeRebuild = true;

            Pesgo1.PeFunction.ReinitializeResetImage();
            Pesgo1.Invalidate();
            Pesgo1.UpdateLayout();
        }

        // -----------------------------------------------------------------------
        // SetupCustomMenus — configure the PE right-click popup menu
        //
        // Pattern from Example 127:
        //   CustomMenuText[i] = "Item Text"          simple item
        //   CustomMenuText[i] = "|"                  separator line
        //   CustomMenuText[i] = "Parent|Sub1|Sub2"   popup with sub-items
        //   CustomMenuLocation[i] = Bottom           append below built-in items
        //   CustomMenuState[i, 0] = Checked          checkmark on non-popup item
        //   CustomMenuState[i, n] = Checked          checkmark on nth sub-item (1-based)
        //
        // Indices are defined as MENU_* constants at the top of this file.
        // The PeCustomMenu event fires with e.MenuIndex / e.SubmenuIndex.
        // -----------------------------------------------------------------------
        void SetupCustomMenus()
        {
            // Separator
            Pesgo1.PeUserInterface.Menu.CustomMenuText[0]     = "|";
            Pesgo1.PeUserInterface.Menu.CustomMenuLocation[0] = CustomMenuLocation.Bottom;

            // Play / Pause
            Pesgo1.PeUserInterface.Menu.CustomMenuText[MENU_PLAY]     = "▶  Play / Pause";
            Pesgo1.PeUserInterface.Menu.CustomMenuLocation[MENU_PLAY] = CustomMenuLocation.Bottom;

            // Stop submenu: sub-item 1 = Keep Position, 2 = Rewind to Start
            Pesgo1.PeUserInterface.Menu.CustomMenuText[MENU_STOP]     = "■  Stop|Keep Position|Rewind to Start";
            Pesgo1.PeUserInterface.Menu.CustomMenuLocation[MENU_STOP] = CustomMenuLocation.Bottom;

            // Reset submenu
            Pesgo1.PeUserInterface.Menu.CustomMenuText[MENU_RESET]     = "↺  Reset|Full Reset|Zoom Only|Position Only";
            Pesgo1.PeUserInterface.Menu.CustomMenuLocation[MENU_RESET] = CustomMenuLocation.Bottom;

            // Separator
            Pesgo1.PeUserInterface.Menu.CustomMenuText[4]     = "|";
            Pesgo1.PeUserInterface.Menu.CustomMenuLocation[4] = CustomMenuLocation.Bottom;

            // O-Scope submenu: sub 1=Tight, 2=Medium, 3=Wide, 4=Exit
            Pesgo1.PeUserInterface.Menu.CustomMenuText[MENU_OSCOPE]     = "⊙  O-Scope|Tight  37ms|Medium  100ms|Wide  250ms|Exit / Full View";
            Pesgo1.PeUserInterface.Menu.CustomMenuLocation[MENU_OSCOPE] = CustomMenuLocation.Bottom;

            // dBFS toggle — starts unchecked (linear mode is default)
            Pesgo1.PeUserInterface.Menu.CustomMenuText[MENU_DBFS]     = "≋  dBFS Scale";
            Pesgo1.PeUserInterface.Menu.CustomMenuLocation[MENU_DBFS] = CustomMenuLocation.Bottom;
            Pesgo1.PeUserInterface.Menu.CustomMenuState[MENU_DBFS, 0] = m_bDbMode
                ? CustomMenuState.Checked : CustomMenuState.UnChecked;

            // Separator
            Pesgo1.PeUserInterface.Menu.CustomMenuText[7]     = "|";
            Pesgo1.PeUserInterface.Menu.CustomMenuLocation[7] = CustomMenuLocation.Bottom;

            // Lyrics toggle — starts checked
            Pesgo1.PeUserInterface.Menu.CustomMenuText[MENU_LYRICS]     = "🎵  Lyrics Overlay";
            Pesgo1.PeUserInterface.Menu.CustomMenuLocation[MENU_LYRICS] = CustomMenuLocation.Bottom;
            Pesgo1.PeUserInterface.Menu.CustomMenuState[MENU_LYRICS, 0] = m_bShowLyrics
                ? CustomMenuState.Checked : CustomMenuState.UnChecked;

            // Overlap axes toggle — starts unchecked
            Pesgo1.PeUserInterface.Menu.CustomMenuText[MENU_OVERLAP]     = "⊡  Overlap Axes";
            Pesgo1.PeUserInterface.Menu.CustomMenuLocation[MENU_OVERLAP] = CustomMenuLocation.Bottom;
            Pesgo1.PeUserInterface.Menu.CustomMenuState[MENU_OVERLAP, 0] = m_bOverlapAxes
                ? CustomMenuState.Checked : CustomMenuState.UnChecked;

            // Separator
            Pesgo1.PeUserInterface.Menu.CustomMenuText[10]     = "|";
            Pesgo1.PeUserInterface.Menu.CustomMenuLocation[10] = CustomMenuLocation.Bottom;

            // Next / Prev song
            Pesgo1.PeUserInterface.Menu.CustomMenuText[MENU_NEXT]     = "⏭  Next Song";
            Pesgo1.PeUserInterface.Menu.CustomMenuLocation[MENU_NEXT] = CustomMenuLocation.Bottom;

            Pesgo1.PeUserInterface.Menu.CustomMenuText[MENU_PREV]     = "⏮  Previous Song";
            Pesgo1.PeUserInterface.Menu.CustomMenuLocation[MENU_PREV] = CustomMenuLocation.Bottom;

            // Auto-play toggle — starts checked
            Pesgo1.PeUserInterface.Menu.CustomMenuText[MENU_AUTOPLAY]     = "🔁  Auto-Play Next Song";
            Pesgo1.PeUserInterface.Menu.CustomMenuLocation[MENU_AUTOPLAY] = CustomMenuLocation.Bottom;
            Pesgo1.PeUserInterface.Menu.CustomMenuState[MENU_AUTOPLAY, 0] = m_bAutoPlayNext
                ? CustomMenuState.Checked : CustomMenuState.UnChecked;
        }

        // -----------------------------------------------------------------------
        // Pesgo1_PeCustomMenu — handle right-click menu selections
        //
        // e.MenuIndex    = top-level custom menu index
        // e.SubmenuIndex = sub-item index for popup menus (1-based)
        // -----------------------------------------------------------------------
        void Pesgo1_PeCustomMenu(object sender, Gigasoft.ProEssentials.EventArg.CustomMenuEventArgs e)
        {
            switch (e.MenuIndex)
            {
                case MENU_PLAY:
                    BtnPlay_Click(null, null);
                    break;

                case MENU_STOP:
                    if (e.SubmenuIndex == 1) StopPlayback();                     // Keep Position
                    else if (e.SubmenuIndex == 2)                                // Rewind to Start
                    {
                        StopPlayback();
                        m_nLastPositionMs = 0;
                        Pesgo1.PeAnnotation.Line.XAxis[0] = 1;
                        TimeDisplay.Text = "0:00.000";
                        Pesgo1.Invalidate();
                    }
                    break;

                case MENU_RESET:
                    if      (e.SubmenuIndex == 1) DoFullReset();
                    else if (e.SubmenuIndex == 2)
                    {
                        if (m_bOScopeMode) ExitOScopeMode();
                        Pesgo1.PeGrid.Zoom.Mode = false;
                        Pesgo1.PeFunction.ReinitializeResetImage();
                        Pesgo1.Invalidate();
                    }
                    else if (e.SubmenuIndex == 3)
                    {
                        if (bPlayingSong) StopPlayback();
                        m_nLastPositionMs = 0;
                        Pesgo1.PeAnnotation.Line.XAxis[0] = 1;
                        TimeDisplay.Text = "0:00.000";
                        Pesgo1.Invalidate();
                    }
                    break;

                case MENU_OSCOPE:
                    if      (e.SubmenuIndex == 1) { m_dOScopeHalfRange = 300;  EnterOScopeMode(m_dOScopeHalfRange); }
                    else if (e.SubmenuIndex == 2) { m_dOScopeHalfRange = 800;  EnterOScopeMode(m_dOScopeHalfRange); }
                    else if (e.SubmenuIndex == 3) { m_dOScopeHalfRange = 2000; EnterOScopeMode(m_dOScopeHalfRange); }
                    else if (e.SubmenuIndex == 4) ExitOScopeMode();
                    break;

                case MENU_DBFS:
                    ToggleDbMode();
                    Pesgo1.PeUserInterface.Menu.CustomMenuState[MENU_DBFS, 0] = m_bDbMode
                        ? CustomMenuState.Checked : CustomMenuState.UnChecked;
                    break;

                case MENU_LYRICS:
                    ToggleLyrics();
                    Pesgo1.PeUserInterface.Menu.CustomMenuState[MENU_LYRICS, 0] = m_bShowLyrics
                        ? CustomMenuState.Checked : CustomMenuState.UnChecked;
                    break;

                case MENU_OVERLAP:
                    m_bOverlapAxes = !m_bOverlapAxes;
                    ApplyOverlapMode();
                    Pesgo1.PeUserInterface.Menu.CustomMenuState[MENU_OVERLAP, 0] = m_bOverlapAxes
                        ? CustomMenuState.Checked : CustomMenuState.UnChecked;
                    break;

                case MENU_NEXT:
                    SongList.SelectedIndex = (m_nCurrentSong + 1) % SongFiles.Length;
                    break;

                case MENU_PREV:
                    SongList.SelectedIndex = (m_nCurrentSong - 1 + SongFiles.Length) % SongFiles.Length;
                    break;

                case MENU_AUTOPLAY:
                    m_bAutoPlayNext = !m_bAutoPlayNext;
                    Pesgo1.PeUserInterface.Menu.CustomMenuState[MENU_AUTOPLAY, 0] = m_bAutoPlayNext
                        ? CustomMenuState.Checked : CustomMenuState.UnChecked;
                    break;
            }
        }

        // -----------------------------------------------------------------------
        // ApplySubsetColors — set colors per current mode
        // -----------------------------------------------------------------------
        void ApplySubsetColors()
        {
            if (m_bOverlapAxes)
            {
                Pesgo1.PeColor.SubsetColors[0] = OverlapColorLeft;
                Pesgo1.PeColor.SubsetColors[1] = OverlapColorRight;
                Pesgo1.PeGrid.WorkingAxis = 0;
                Pesgo1.PeColor.YAxis      = OverlapColorLeft;
                Pesgo1.PeGrid.WorkingAxis = 1;
                Pesgo1.PeColor.YAxis      = OverlapColorRight;
                Pesgo1.PeGrid.WorkingAxis = 0;
            }
            else
            {
                Pesgo1.PeColor.SubsetColors[0] = NormalColorLeft;
                Pesgo1.PeColor.SubsetColors[1] = NormalColorRight;
            }
        }

        // -----------------------------------------------------------------------
        // ApplyOverlapMode — toggle overlap axes on/off
        // -----------------------------------------------------------------------
        void ApplyOverlapMode()
        {
            BtnOverlap.Content = m_bOverlapAxes ? "⊡ Separate" : "⊡ Overlap";
            BtnOverlap.Style   = m_bOverlapAxes
                ? (Style)FindResource("DarkButtonActiveStyle")
                : (Style)FindResource("DarkButtonStyle");

            if (m_bOverlapAxes)
                Pesgo1.PeGrid.OverlapMultiAxes[0] = 2;
            else
                Pesgo1.PeGrid.OverlapMultiAxes.Clear();

            ApplySubsetColors();
            SetYAxisAnnotations();
            Pesgo1.PeLegend.Show = m_bOverlapAxes;

            // Force D3D engine to discard cached subset color data.
            // Vertices don't change on a color-only update.
            Pesgo1.PeFunction.Force3dxNewColors = true;

            Pesgo1.PeFunction.ReinitializeResetImage();
            Pesgo1.Invalidate();
        }

        // -----------------------------------------------------------------------
        // MCI Playback helpers
        // -----------------------------------------------------------------------
        void StartPlayback()
        {
            string filePath = Path.GetFullPath(SongFiles[m_nCurrentSong]);
            mciSendString($"close {MCI_ALIAS}", null, 0, IntPtr.Zero);
            mciSendString($"open \"{filePath}\" type waveaudio alias {MCI_ALIAS}", null, 0, IntPtr.Zero);

            if (m_nLastPositionMs > 0)
                mciSendString($"play {MCI_ALIAS} from {m_nLastPositionMs}", null, 0, IntPtr.Zero);
            else
                mciSendString($"play {MCI_ALIAS}", null, 0, IntPtr.Zero);

            bPlayingSong    = true;
            BtnPlay.Content = "❚❚ Pause";
            Pesgo1.PeUserInterface.Allow.Zooming = AllowZooming.None;
            _timer.Start();
        }

        void StopPlayback()
        {
            _timer.Stop();
            m_nLastPositionMs = GetMciPositionMs();
            mciSendString($"stop {MCI_ALIAS}",  null, 0, IntPtr.Zero);
            mciSendString($"close {MCI_ALIAS}", null, 0, IntPtr.Zero);
            bPlayingSong    = false;
            BtnPlay.Content = "▶  Play";
            Pesgo1.PeUserInterface.Allow.Zooming = AllowZooming.Horizontal;
        }

        int GetMciPositionMs()
        {
            var sb = new StringBuilder(128);
            mciSendString($"status {MCI_ALIAS} position", sb, 128, IntPtr.Zero);
            return int.TryParse(sb.ToString().Trim(), out int ms) ? ms : 0;
        }

        // -----------------------------------------------------------------------
        // Timer_Tick — sync playhead, viewport tracking, lyrics, auto-advance
        // -----------------------------------------------------------------------
        void Timer_Tick(object sender, EventArgs e)
        {
            int ms = GetMciPositionMs();

            // Detect natural end of song
            var sbState = new StringBuilder(128);
            mciSendString($"status {MCI_ALIAS} mode", sbState, 128, IntPtr.Zero);
            if (sbState.ToString().Trim().ToLower() == "stopped")
            {
                StopPlayback();
                m_nLastPositionMs = 0;
                Pesgo1.PeAnnotation.Line.XAxis[0] = 1;
                TimeDisplay.Text = "0:00.000";
                Pesgo1.Invalidate();

                if (m_bAutoPlayNext)
                {
                    int nextSong = (m_nCurrentSong + 1) % SongFiles.Length;
                    SongList.SelectedIndex = nextSong;
                    if (!bPlayingSong) StartPlayback();
                }
                return;
            }

            // Convert ms → sample index (80ms latency compensation)
            double dPos = Math.Min(((ms + 80) / 1000.0) * SampleRate, SongSize);
            Pesgo1.PeAnnotation.Line.XAxis[0] = dPos;

            // Time display
            double totalSec = ms / 1000.0;
            int    mins     = (int)(totalSec / 60);
            double secs     = totalSec - (mins * 60);
            TimeDisplay.Text = $"{mins}:{secs:00.000}";

            // Smart viewport tracking
            if (Pesgo1.PeGrid.Zoom.Mode && Mouse.LeftButton == MouseButtonState.Released)
            {
                double dRange = Pesgo1.PeGrid.Zoom.MaxX - Pesgo1.PeGrid.Zoom.MinX;

                if (dPos > Pesgo1.PeGrid.Zoom.MaxX)
                {
                    if (dRange < SampleRate)
                    { Pesgo1.PeGrid.Zoom.MaxX = dPos; Pesgo1.PeGrid.Zoom.MinX = dPos - dRange; }
                    else
                    { Pesgo1.PeGrid.Zoom.MaxX = dPos + dRange; Pesgo1.PeGrid.Zoom.MinX = dPos; }
                }

                if (dRange < SampleRate)
                {
                    Pesgo1.PeGrid.Option.ShowXAxis         = ShowAxis.Empty;
                    Pesgo1.PeAnnotation.Line.XAxisType[0]  = LineAnnotationType.NoLine;
                    Pesgo1.PeAnnotation.Line.XAxis[1]      = Pesgo1.PeGrid.Zoom.MinX;
                    Pesgo1.PeAnnotation.Line.XAxisType[1]  = LineAnnotationType.GridTick;
                    Pesgo1.PeAnnotation.Line.XAxisText[1]  = "|h" + (Pesgo1.PeGrid.Zoom.MinX / SampleRate).ToString("F6");
                    Pesgo1.PeAnnotation.Line.XAxis[2]      = dPos;
                    Pesgo1.PeAnnotation.Line.XAxisType[2]  = LineAnnotationType.GridTick;
                    Pesgo1.PeAnnotation.Line.XAxisText[2]  = "|h" + (dPos / SampleRate).ToString("F6");
                    Pesgo1.PeAnnotation.Line.BottomMargin  = "XX";
                }
                else
                {
                    Pesgo1.PeGrid.Option.ShowXAxis         = ShowAxis.All;
                    Pesgo1.PeAnnotation.Line.XAxis.Clear(1);
                    Pesgo1.PeAnnotation.Line.XAxisType.Clear(1);
                    Pesgo1.PeAnnotation.Line.XAxisText.Clear(1);
                    Pesgo1.PeAnnotation.Line.XAxisType[0]  = LineAnnotationType.MediumThinSolid;
                    Pesgo1.PeAnnotation.Line.BottomMargin  = "";
                }
            }

            UpdateLyricsAnnotation(ms);
            Pesgo1.Invalidate();
        }

        // -----------------------------------------------------------------------
        // UpdateLyricsAnnotation — place current lyric line at viewport center
        // -----------------------------------------------------------------------
        void UpdateLyricsAnnotation(int ms)
        {
            if (!m_bShowLyrics)
            {
                Pesgo1.PeAnnotation.Graph.Text[3] = "";
                return;
            }

            string lyricText = null;
            if      (m_nCurrentSong == 0) { var l = LyricsWhatIAlreadyDone.GetLine(ms);       if (l.HasValue) lyricText = l.Value.Text; }
            else if (m_nCurrentSong == 1) { var l = LyricsItAllComesOutInTheWash.GetLine(ms);  if (l.HasValue) lyricText = l.Value.Text; }
            else if (m_nCurrentSong == 2) { var l = LyricsBabyYouCanRender.GetLine(ms);        if (l.HasValue) lyricText = l.Value.Text; }
            else if (m_nCurrentSong == 3) { var l = LyricsNobodyRendersLikeWeDo.GetLine(ms);   if (l.HasValue) lyricText = l.Value.Text; }
            else if (m_nCurrentSong == 4) { var l = LyricsPourMeSomeProEssentials.GetLine(ms); if (l.HasValue) lyricText = l.Value.Text; }

            double viewMid = Pesgo1.PeGrid.Zoom.Mode
                ? (Pesgo1.PeGrid.Zoom.MinX + Pesgo1.PeGrid.Zoom.MaxX) / 2.0
                : SongSize / 2.0;

            if (lyricText != null)
            {
                Pesgo1.PeAnnotation.Graph.X[3]    = viewMid;
                Pesgo1.PeAnnotation.Graph.Y[3]     = 55;
                Pesgo1.PeAnnotation.Graph.Type[3]  = (int)GraphAnnotationType.NoSymbol;
                Pesgo1.PeAnnotation.Graph.Text[3]  = WrapLyric(lyricText, 36);
                Pesgo1.PeAnnotation.Graph.Color[3] = Color.FromArgb(255, 255, 220, 50);
            }
            else
            {
                Pesgo1.PeAnnotation.Graph.Text[3] = "";
            }
        }

        // -----------------------------------------------------------------------
        // Button click handlers
        // -----------------------------------------------------------------------
        void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            if (!bPlayingSong)
            {
                if (Pesgo1.PeGrid.Zoom.Mode)
                {
                    double pos = Pesgo1.PeAnnotation.Line.XAxis[0];
                    double min = Pesgo1.PeGrid.Zoom.MinX, max = Pesgo1.PeGrid.Zoom.MaxX;
                    double rng = max - min;
                    if (pos < min || pos > max)
                    { Pesgo1.PeGrid.Zoom.MinX = pos - rng / 2; Pesgo1.PeGrid.Zoom.MaxX = pos + rng / 2; }
                }
                StartPlayback();
            }
            else StopPlayback();
        }

        void BtnStop_Click(object sender, RoutedEventArgs e)  => StopPlayback();
        void BtnReset_Click(object sender, RoutedEventArgs e) => DoFullReset();

        void BtnOScope_Click(object sender, RoutedEventArgs e)
        {
            if (!m_bOScopeMode) EnterOScopeMode(m_dOScopeHalfRange);
            else                ExitOScopeMode();
        }

        void BtnDb_Click(object sender, RoutedEventArgs e)
        {
            ToggleDbMode();
            // Keep right-click menu check state in sync
            Pesgo1.PeUserInterface.Menu.CustomMenuState[MENU_DBFS, 0] = m_bDbMode
                ? CustomMenuState.Checked : CustomMenuState.UnChecked;
        }

        void BtnLyrics_Click(object sender, RoutedEventArgs e)
        {
            ToggleLyrics();
            Pesgo1.PeUserInterface.Menu.CustomMenuState[MENU_LYRICS, 0] = m_bShowLyrics
                ? CustomMenuState.Checked : CustomMenuState.UnChecked;
        }

        void BtnOverlap_Click(object sender, RoutedEventArgs e)
        {
            m_bOverlapAxes = !m_bOverlapAxes;
            ApplyOverlapMode();
            Pesgo1.PeUserInterface.Menu.CustomMenuState[MENU_OVERLAP, 0] = m_bOverlapAxes
                ? CustomMenuState.Checked : CustomMenuState.UnChecked;
        }

        // -----------------------------------------------------------------------
        // Toggle helpers — shared by buttons and menu handlers
        // -----------------------------------------------------------------------
        void ToggleDbMode()
        {
            m_bDbMode     = !m_bDbMode;
            BtnDb.Content = m_bDbMode ? "≋  Linear" : "≋  dBFS";
            SetYAxisAnnotations();
            Pesgo1.PeFunction.ReinitializeResetImage();
            Pesgo1.Invalidate();
        }

        void ToggleLyrics()
        {
            m_bShowLyrics = !m_bShowLyrics;
            Pesgo1.PeAnnotation.Graph.Show = m_bShowLyrics;
            BtnLyrics.Content = m_bShowLyrics ? "🎵 Lyrics" : "🔇 Lyrics";
            BtnLyrics.Style   = m_bShowLyrics
                ? (Style)FindResource("DarkButtonStyle")
                : (Style)FindResource("DarkButtonActiveStyle");
            if (!m_bShowLyrics) Pesgo1.PeAnnotation.Graph.Text[3] = "";
            Pesgo1.PeFunction.ReinitializeResetImage();
            Pesgo1.Invalidate();
        }

        void DoFullReset()
        {
            if (bPlayingSong) StopPlayback();
            m_nLastPositionMs = 0;
            if (m_bOScopeMode) ExitOScopeMode();
            Pesgo1.PeGrid.Zoom.Mode = false;
            Pesgo1.PeAnnotation.Line.XAxis[0] = 1;
            TimeDisplay.Text = "0:00.000";
            Pesgo1.PeFunction.ReinitializeResetImage();
            Pesgo1.Invalidate();
        }

        // -----------------------------------------------------------------------
        // EnterOScopeMode / ExitOScopeMode
        // -----------------------------------------------------------------------
        void EnterOScopeMode(double halfRange)
        {
            m_bOScopeMode     = true;
            m_dOScopeHalfRange = halfRange;
            BtnOScope.Content  = "⊙  Full View";
            BtnOScope.Style    = (Style)FindResource("DarkButtonActiveStyle");

            double center = Pesgo1.PeAnnotation.Line.XAxis[0];
            if (center <= 1) center = halfRange;

            Pesgo1.PeGrid.Zoom.MinX = center - halfRange;
            Pesgo1.PeGrid.Zoom.MaxX = center + halfRange;
            Pesgo1.PeGrid.WorkingAxis = 0;
            Pesgo1.PeGrid.Zoom.MinY = -70.0; Pesgo1.PeGrid.Zoom.MaxY = 70.0;
            Pesgo1.PeGrid.WorkingAxis = 1;
            Pesgo1.PeGrid.Zoom.MinY = -70.0; Pesgo1.PeGrid.Zoom.MaxY = 70.0;
            Pesgo1.PeGrid.WorkingAxis = 0;
            Pesgo1.PeGrid.Zoom.Mode = true;

            Pesgo1.PeFunction.ReinitializeResetImage();
            Pesgo1.Invalidate();
        }

        void ExitOScopeMode()
        {
            m_bOScopeMode     = false;
            BtnOScope.Content  = "⊙  O-Scope";
            BtnOScope.Style    = (Style)FindResource("DarkButtonStyle");

            Pesgo1.PeGrid.Zoom.Mode = false;
            Pesgo1.PeGrid.Option.ShowXAxis  = ShowAxis.All;
            Pesgo1.PeAnnotation.Line.XAxis.Clear(1);
            Pesgo1.PeAnnotation.Line.XAxisType.Clear(1);
            Pesgo1.PeAnnotation.Line.XAxisText.Clear(1);
            Pesgo1.PeAnnotation.Line.XAxisType[0] = LineAnnotationType.MediumThinSolid;
            Pesgo1.PeAnnotation.Line.BottomMargin  = "";

            SetYAxisAnnotations();
            Pesgo1.PeFunction.ReinitializeResetImage();
            Pesgo1.Invalidate();
        }

        // -----------------------------------------------------------------------
        // SeekTo — seek to a specific ms position
        // -----------------------------------------------------------------------
        void SeekTo(int ms)
        {
            ms = Math.Max(0, Math.Min(ms, (int)(SongSize / (double)SampleRate * 1000)));
            m_nLastPositionMs = ms;

            double samplePos = (ms / 1000.0) * SampleRate;
            Pesgo1.PeAnnotation.Line.XAxis[0] = samplePos;

            double totalSec = ms / 1000.0;
            int    mins     = (int)(totalSec / 60);
            double secs     = totalSec - (mins * 60);
            TimeDisplay.Text = $"{mins}:{secs:00.000}";

            if (bPlayingSong)
            {
                _timer.Stop();
                mciSendString($"seek {MCI_ALIAS} to {ms}", null, 0, IntPtr.Zero);
                mciSendString($"play {MCI_ALIAS}", null, 0, IntPtr.Zero);
                _timer.Start();
            }
            Pesgo1.Invalidate();
        }

        // -----------------------------------------------------------------------
        // Chart left-click — seek to clicked position
        // -----------------------------------------------------------------------
        void Pesgo1_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;

            System.Windows.Point pt   = e.GetPosition(Pesgo1);
            System.Windows.Rect  rect = Pesgo1.PeFunction.GetRectGraph();
            if (!rect.Contains(pt)) return;

            double fX = 0, fY = 0;
            int nA = 0, nX = (int)pt.X, nY = (int)pt.Y;
            Pesgo1.PeFunction.ConvPixelToGraph(ref nA, ref nX, ref nY, ref fX, ref fY, false, false, false);

            SeekTo((int)(fX / SampleRate * 1000.0));
        }

        // -----------------------------------------------------------------------
        // SongList_SelectionChanged — switch to selected song
        // -----------------------------------------------------------------------
        void SongList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            int idx = SongList.SelectedIndex;
            if (idx < 0 || idx == m_nCurrentSong) return;
            if (m_bOScopeMode) ExitOScopeMode();
            LoadSong(idx);
            if (!bPlayingSong) StartPlayback();
        }

        // -----------------------------------------------------------------------
        // SetYAxisAnnotations — horizontal reference lines on Y axis
        //
        // In overlap mode only emits axis 0 entries — both axes share the same
        // physical graph area so axis 1 entries would duplicate the grid lines.
        // -----------------------------------------------------------------------
        void SetYAxisAnnotations()
        {
            for (int i = 10; i < 50; i++)
            {
                Pesgo1.PeAnnotation.Line.YAxis.Clear(i);
                Pesgo1.PeAnnotation.Line.YAxisType.Clear(i);
                Pesgo1.PeAnnotation.Line.YAxisText.Clear(i);
                Pesgo1.PeAnnotation.Line.YAxisColor.Clear(i);
                Pesgo1.PeAnnotation.Line.YAxisAxis.Clear(i);
            }

            Color tickColor  = Color.FromArgb(255, 140, 140, 140);
            int   axisCount  = m_bOverlapAxes ? 1 : 2;

            if (!m_bDbMode)
            {
                float[] linTicks = { 70, 35, 0, -35, -70 };
                int idx = 10;
                for (int axis = 0; axis < axisCount; axis++)
                    foreach (float tick in linTicks)
                    {
                        Pesgo1.PeAnnotation.Line.YAxis[idx]      = tick;
                        Pesgo1.PeAnnotation.Line.YAxisType[idx]  = LineAnnotationType.GridLine;
                        Pesgo1.PeAnnotation.Line.YAxisColor[idx] = tickColor;
                        Pesgo1.PeAnnotation.Line.YAxisText[idx]  = "|L" + ((int)tick).ToString();
                        Pesgo1.PeAnnotation.Line.YAxisAxis[idx]  = axis;
                        idx++;
                    }
                Pesgo1.PeAnnotation.Line.LeftMargin = "-70 ";
            }
            else
            {
                var dbTicks = new (string label, string shortLabel, float linear)[]
                {
                    ("-3 dB", "-3",  49.6f), ("-6",  "-6",  35.1f),
                    ("-9",    "-9",  24.8f), ("-12", "-12", 17.6f),
                    ("-15",   "-15", 12.5f), ("-21", "-21",  6.2f),
                    ("-∞",    "-∞",   0.0f),
                };

                var allTicks = new System.Collections.Generic.List<(string text, float val)>();
                for (int i = 0; i < dbTicks.Length; i++)
                {
                    string display = (i == 0) ? dbTicks[i].label : dbTicks[i].shortLabel;
                    allTicks.Add((display, dbTicks[i].linear));
                    if (dbTicks[i].linear > 0.01f) allTicks.Add((display, -dbTicks[i].linear));
                }

                int idx = 10;
                for (int axis = 0; axis < axisCount; axis++)
                    foreach (var (text, val) in allTicks)
                    {
                        Pesgo1.PeAnnotation.Line.YAxis[idx]      = val;
                        Pesgo1.PeAnnotation.Line.YAxisType[idx]  = LineAnnotationType.GridLine;
                        Pesgo1.PeAnnotation.Line.YAxisColor[idx] = tickColor;
                        Pesgo1.PeAnnotation.Line.YAxisText[idx]  = "|L" + text;
                        Pesgo1.PeAnnotation.Line.YAxisAxis[idx]  = axis;
                        idx++;
                    }
                Pesgo1.PeAnnotation.Line.LeftMargin = "-3 dB ";
            }
        }

        // -----------------------------------------------------------------------
        // WrapLyric — soft-wrap long lines near midpoint
        // -----------------------------------------------------------------------
        string WrapLyric(string text, int maxChars = 36)
        {
            if (text.Length <= maxChars) return text;
            int mid = text.Length / 2, best = -1;
            for (int d = 0; d <= mid; d++)
            {
                if (mid - d >= 0 && text[mid - d] == ' ') { best = mid - d; break; }
                if (mid + d < text.Length && text[mid + d] == ' ') { best = mid + d; break; }
            }
            return best < 0 ? text : text.Substring(0, best) + "\n" + text.Substring(best + 1);
        }

        // -----------------------------------------------------------------------
        // PeCustomGridNumber — format X axis labels as M:SS.mmm
        // -----------------------------------------------------------------------
        void Pesgo1_PeCustomGridNumber(object sender, Gigasoft.ProEssentials.EventArg.CustomGridNumberEventArgs e)
        {
            if (e.AxisType == 2 || e.AxisType == 7)
            {
                double totalSec = e.NumberValue / SampleRate;
                int    mins     = (int)(totalSec / 60);
                double rem      = totalSec - (mins * 60);
                e.NumberString  = $"{mins}:{rem:00.000}";
            }
        }

        // -----------------------------------------------------------------------
        // Window_Closing — release MCI
        // -----------------------------------------------------------------------
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _timer?.Stop();
            mciSendString($"stop {MCI_ALIAS}",  null, 0, IntPtr.Zero);
            mciSendString($"close {MCI_ALIAS}", null, 0, IntPtr.Zero);
        }
    }
}
