// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Fkgcc
{
    internal static class DesktopProgram
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                if (args.Length == 2 && args[0] == "--render")
                {
                    if (!Path.IsPathRooted(args[1]))
                        throw new ArgumentException("预览图片必须使用绝对路径。");
                    using (LightingForm preview = new LightingForm(true))
                        preview.RenderPreview(args[1]);
                    return 0;
                }
                if (args.Length != 0)
                    throw new ArgumentException("用法：FKGCC.Light.exe，或 --render <绝对路径.png>。");
                Application.Run(new LightingForm(false));
                return 0;
            }
            catch (Exception exception)
            {
                if (args.Length > 0)
                    Console.Error.WriteLine(exception.GetBaseException().Message);
                else
                    MessageBox.Show(exception.GetBaseException().Message, "FKGCC Light", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }

    public sealed class LightingForm : Form
    {
        private readonly bool preview;
        private readonly Dictionary<string, HeaderOption> headers = new Dictionary<string, HeaderOption>(StringComparer.OrdinalIgnoreCase);
        private readonly ToolTip tips = new ToolTip();
        private TableLayoutPanel root;
        private RoundedPanel connectionCard;
        private Panel editorPanel;
        private Label statusTitle;
        private Label statusDetail;
        private Label selectionCount;
        private Label feedbackLabel;
        private Label validationLabel;
        private Label brightnessValue;
        private Label speedValue;
        private ComboBox modeInput;
        private TextBox colorInput;
        private TrackBar brightnessInput;
        private TrackBar speedInput;
        private ColorSwatch colorSwatch;
        private AccentButton colorButton;
        private AccentButton applyButton;
        private AccentButton offButton;
        private AccentButton probeButton;
        private AccentButton hardwareButton;
        private AccentButton loadButton;
        private AccentButton saveButton;
        private FlowLayoutPanel presets;
        private bool busy;
        private bool suppressChanges;
        private bool probeStarted;
        private bool supported;

        public LightingForm(bool preview)
        {
            this.preview = preview;
            Text = "FKGCC Light";
            Name = "FKGCCLight";
            Font = Theme.BodyFont;
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1120, 810);
            MinimumSize = new Size(1000, 780);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            DoubleBuffered = true;
            BuildInterface();
            SetEditorDefaults();
            if (preview)
            {
                SetConnection("界面预览 · 不连接硬件", BoardLayout.BoardName + "  ·  可编辑配置，所有设备操作均已禁用。", Theme.Purple);
                SetFeedback("预览模式：不会检测设备，也不会发送任何灯光指令。", Theme.Muted);
            }
            else
            {
                SetConnection("准备检测灯光控制器", "启动时仅检测连接；点击“应用灯光”才会更改所选接口。", Theme.Cyan);
                SetFeedback("当前参数尚未应用。", Theme.Muted);
            }
            Shown += async delegate
            {
                if (!this.preview && !probeStarted)
                {
                    probeStarted = true;
                    await ProbeAsync();
                }
            };
            KeyDown += HandleShortcut;
        }

        internal void RenderPreview(string path)
        {
            if (!preview) throw new InvalidOperationException("只能导出不访问硬件的预览。");
            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
            // Native ComboBox controls need their shown lifecycle before
            // WM_PRINT can draw selected text. This is a non-activating,
            // off-screen preview, and its Shown handler never probes a device.
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-20000, -20000);
            try
            {
                Show();
                Application.DoEvents();
                CreateControlTree(this);
                PerformLayout();
                root.PerformLayout();
                using (Bitmap bitmap = new Bitmap(root.Width, root.Height, PixelFormat.Format32bppArgb))
                {
                    root.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    bitmap.Save(path, ImageFormat.Png);
                }
            }
            finally { Hide(); }
        }

        protected override bool ShowWithoutActivation { get { return preview; } }

        private static void CreateControlTree(Control control)
        {
            IntPtr handle = control.Handle;
            control.PerformLayout();
            foreach (Control child in control.Controls) CreateControlTree(child);
        }

        private void BuildInterface()
        {
            root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.BackColor = Theme.Background;
            root.Padding = new Padding(28, 20, 28, 16);
            root.ColumnCount = 1;
            root.RowCount = 4;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 123));
            Controls.Add(root);
            root.Controls.Add(BuildTitle(), 0, 0);
            root.Controls.Add(BuildConnection(), 0, 1);
            root.Controls.Add(BuildEditor(), 0, 2);
            root.Controls.Add(BuildFooter(), 0, 3);
        }

        private Control BuildTitle()
        {
            TableLayoutPanel title = Grid(2, 1, Theme.Background);
            title.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            title.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 178));
            Panel words = new Panel();
            words.Dock = DockStyle.Fill;
            words.BackColor = Theme.Background;
            Label name = LabelOf("FKGCC Light", 27, Theme.Text);
            name.Font = new Font("Segoe UI", 27, FontStyle.Bold);
            name.Location = new Point(0, 0);
            name.Size = new Size(360, 44);
            words.Controls.Add(name);
            Label subtitle = LabelOf("独立主板灯光客户端", 9.5F, Theme.Muted);
            subtitle.Location = new Point(2, 47);
            subtitle.Size = new Size(460, 23);
            words.Controls.Add(subtitle);
            title.Controls.Add(words, 0, 0);
            Label badge = LabelOf(preview ? "●  界面预览" : "●  本地 · 手动应用", 9, Theme.Cyan);
            badge.TextAlign = ContentAlignment.MiddleRight;
            badge.Dock = DockStyle.Fill;
            title.Controls.Add(badge, 1, 0);
            return title;
        }

        private Control BuildConnection()
        {
            connectionCard = new RoundedPanel();
            connectionCard.Dock = DockStyle.Fill;
            connectionCard.Margin = new Padding(0, 4, 0, 12);
            connectionCard.Padding = new Padding(18, 10, 14, 10);
            TableLayoutPanel content = Grid(2, 1, Theme.Surface);
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126));
            TableLayoutPanel messages = Grid(1, 2, Theme.Surface);
            messages.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
            messages.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            statusTitle = LabelOf("", 11F, Theme.Text);
            statusTitle.Font = new Font(Theme.FontFamily, 11, FontStyle.Bold);
            statusTitle.Dock = DockStyle.Fill;
            statusDetail = LabelOf("", 8.8F, Theme.Muted);
            statusDetail.Dock = DockStyle.Fill;
            messages.Controls.Add(statusTitle, 0, 0);
            messages.Controls.Add(statusDetail, 0, 1);
            content.Controls.Add(messages, 0, 0);
            probeButton = ButtonOf("重新检测", "probeDevice", false);
            probeButton.Dock = DockStyle.Fill;
            probeButton.Margin = new Padding(12, 9, 0, 9);
            probeButton.Click += async delegate { await ProbeAsync(); };
            tips.SetToolTip(probeButton, "重新读取设备状态，不改变灯光。快捷键 F5。");
            content.Controls.Add(probeButton, 1, 0);
            connectionCard.Controls.Add(content);
            return connectionCard;
        }

        private Control BuildEditor()
        {
            editorPanel = new Panel();
            editorPanel.Dock = DockStyle.Fill;
            editorPanel.Margin = Padding.Empty;
            editorPanel.AutoScroll = true;
            editorPanel.AutoScrollMinSize = new Size(0, 450);
            TableLayoutPanel columns = Grid(2, 1, Theme.Background);
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));
            RoundedPanel channels = new RoundedPanel();
            channels.Dock = DockStyle.Fill;
            channels.Margin = new Padding(0, 0, 12, 0);
            channels.Padding = new Padding(18);
            channels.Controls.Add(BuildHeaders());
            columns.Controls.Add(channels, 0, 0);
            RoundedPanel settings = new RoundedPanel();
            settings.Dock = DockStyle.Fill;
            settings.Margin = Padding.Empty;
            settings.Padding = new Padding(22, 18, 22, 16);
            settings.Controls.Add(BuildSettings());
            columns.Controls.Add(settings, 1, 0);
            editorPanel.Controls.Add(columns);
            return editorPanel;
        }

        private Control BuildHeaders()
        {
            HeaderDefinition[] definitions = BoardLayout.Headers;
            TableLayoutPanel section = Grid(1, definitions.Length + 4, Theme.Surface);
            section.RowStyles.Add(new RowStyle(SizeType.Absolute, 33));
            section.RowStyles.Add(new RowStyle(SizeType.Absolute, 49));
            for (int index = 0; index < definitions.Length; index++)
                section.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            section.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            section.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            TableLayoutPanel heading = Grid(2, 1, Theme.Surface);
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            Label label = LabelOf("选择接口", 13, Theme.Text);
            label.Font = new Font(Theme.FontFamily, 13, FontStyle.Bold);
            label.Dock = DockStyle.Fill;
            heading.Controls.Add(label, 0, 0);
            selectionCount = LabelOf("", 8.5F, Theme.Cyan);
            selectionCount.Dock = DockStyle.Fill;
            selectionCount.TextAlign = ContentAlignment.MiddleRight;
            heading.Controls.Add(selectionCount, 1, 0);
            section.Controls.Add(heading, 0, 0);
            Label board = LabelOf(BoardLayout.BoardName, 9, Theme.Muted);
            board.Dock = DockStyle.Fill;
            board.Padding = new Padding(0, 4, 0, 0);
            section.Controls.Add(board, 0, 1);
            int row = 2;
            foreach (HeaderDefinition definition in definitions)
            {
                HeaderOption option = new HeaderOption(definition);
                option.Name = "header_" + definition.Key;
                option.Dock = DockStyle.Fill;
                option.Margin = new Padding(0, 0, 0, 7);
                option.Checked = true;
                option.CheckedChanged += EditorChanged;
                headers.Add(definition.Key, option);
                tips.SetToolTip(option, definition.Label + "\n仅向选中的接口发送当前灯效。");
                section.Controls.Add(option, 0, row++);
            }
            Label note = LabelOf("仅修改勾选的接口。\nRGB 与 ARGB 可一起设置常亮等灯效。", 8.5F, Theme.Muted);
            note.Dock = DockStyle.Fill;
            note.Padding = new Padding(1, 5, 0, 0);
            section.Controls.Add(note, 0, definitions.Length + 3);
            return section;
        }

        private Control BuildSettings()
        {
            TableLayoutPanel section = Grid(1, 6, Theme.Surface);
            section.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            section.RowStyles.Add(new RowStyle(SizeType.Absolute, 71));
            section.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
            section.RowStyles.Add(new RowStyle(SizeType.Absolute, 77));
            section.RowStyles.Add(new RowStyle(SizeType.Absolute, 77));
            section.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Label title = LabelOf("编辑灯效", 13, Theme.Text);
            title.Font = new Font(Theme.FontFamily, 13, FontStyle.Bold);
            title.Dock = DockStyle.Fill;
            section.Controls.Add(title, 0, 0);

            TableLayoutPanel mode = Grid(1, 2, Theme.Surface);
            mode.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            mode.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Label modeLabel = LabelOf("灯效模式", 9, Theme.Muted);
            modeLabel.Dock = DockStyle.Fill;
            mode.Controls.Add(modeLabel, 0, 0);
            modeInput = new ComboBox();
            modeInput.Name = "lightingMode";
            modeInput.AccessibleName = "灯效模式";
            modeInput.Dock = DockStyle.Top;
            modeInput.DropDownStyle = ComboBoxStyle.DropDownList;
            modeInput.DrawMode = DrawMode.OwnerDrawFixed;
            modeInput.ItemHeight = 26;
            modeInput.FlatStyle = FlatStyle.Flat;
            modeInput.BackColor = Theme.Input;
            modeInput.ForeColor = Theme.Text;
            modeInput.Font = new Font(Theme.FontFamily, 11);
            modeInput.Margin = new Padding(0, 0, 0, 8);
            modeInput.Items.Add(new ModeChoice("常亮  ·  Static", LightingMode.Static));
            modeInput.Items.Add(new ModeChoice("关灯  ·  Off", LightingMode.Off));
            modeInput.Items.Add(new ModeChoice("呼吸  ·  Breathing", LightingMode.Breathing));
            modeInput.Items.Add(new ModeChoice("闪烁  ·  Blink", LightingMode.Blink));
            modeInput.Items.Add(new ModeChoice("色彩循环  ·  Cycle", LightingMode.Cycle));
            modeInput.SelectedIndexChanged += EditorChanged;
            modeInput.DrawItem += DrawModeItem;
            mode.Controls.Add(modeInput, 0, 1);
            section.Controls.Add(mode, 0, 1);
            section.Controls.Add(BuildColorEditor(), 0, 2);
            section.Controls.Add(BuildSlider(true), 0, 3);
            section.Controls.Add(BuildSlider(false), 0, 4);
            validationLabel = LabelOf("", 8.8F, Theme.Muted);
            validationLabel.Dock = DockStyle.Fill;
            validationLabel.Padding = new Padding(0, 3, 0, 0);
            section.Controls.Add(validationLabel, 0, 5);
            return section;
        }

        private Control BuildColorEditor()
        {
            TableLayoutPanel row = Grid(2, 1, Theme.Surface);
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 103));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            colorSwatch = new ColorSwatch();
            colorSwatch.Dock = DockStyle.Fill;
            colorSwatch.Margin = new Padding(0, 0, 14, 8);
            row.Controls.Add(colorSwatch, 0, 0);
            TableLayoutPanel inputs = Grid(2, 3, Theme.Surface);
            inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            inputs.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
            inputs.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            inputs.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Label label = LabelOf("单色 / HEX", 9, Theme.Muted);
            label.Dock = DockStyle.Fill;
            inputs.Controls.Add(label, 0, 0);
            inputs.SetColumnSpan(label, 2);
            colorInput = new TextBox();
            colorInput.Name = "colorHex";
            colorInput.AccessibleName = "颜色十六进制代码";
            colorInput.MaxLength = 7;
            colorInput.CharacterCasing = CharacterCasing.Upper;
            colorInput.BackColor = Theme.Input;
            colorInput.ForeColor = Theme.Text;
            colorInput.BorderStyle = BorderStyle.FixedSingle;
            colorInput.Font = new Font("Consolas", 13);
            colorInput.Dock = DockStyle.Top;
            colorInput.Margin = new Padding(0, 0, 10, 0);
            colorInput.TextChanged += EditorChanged;
            inputs.Controls.Add(colorInput, 0, 1);
            colorButton = ButtonOf("选择颜色", "chooseColor", false);
            colorButton.Dock = DockStyle.Fill;
            colorButton.Margin = new Padding(0, 0, 0, 5);
            colorButton.Click += ChooseColor;
            inputs.Controls.Add(colorButton, 1, 1);
            presets = new FlowLayoutPanel();
            presets.Dock = DockStyle.Fill;
            presets.Margin = Padding.Empty;
            presets.BackColor = Theme.Surface;
            presets.WrapContents = false;
            string[] colors = new string[] { "#A78BFA", "#59C8FF", "#43E0BC", "#FFB267", "#FFFFFF" };
            foreach (string hex in colors)
            {
                Color parsed;
                ParseColor(hex, out parsed);
                AccentButton preset = ButtonOf("", "preset" + hex.Substring(1), false);
                preset.Size = new Size(28, 26);
                preset.Margin = new Padding(0, 4, 9, 0);
                preset.CustomFill = parsed;
                preset.AccessibleName = "使用颜色 " + hex;
                preset.Tag = hex;
                preset.Click += delegate(object sender, EventArgs eventArgs) { colorInput.Text = (string)((Control)sender).Tag; };
                tips.SetToolTip(preset, hex);
                presets.Controls.Add(preset);
            }
            inputs.Controls.Add(presets, 0, 2);
            inputs.SetColumnSpan(presets, 2);
            row.Controls.Add(inputs, 1, 0);
            return row;
        }

        private Control BuildSlider(bool brightness)
        {
            TableLayoutPanel group = Grid(2, 2, Theme.Surface);
            group.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            group.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            group.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            group.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Label caption = LabelOf(brightness ? "亮度" : "速度", 9, Theme.Muted);
            caption.Dock = DockStyle.Fill;
            group.Controls.Add(caption, 0, 0);
            Label value = LabelOf("", 10, Theme.Text);
            value.Dock = DockStyle.Fill;
            value.TextAlign = ContentAlignment.TopRight;
            group.Controls.Add(value, 1, 0);
            TrackBar slider = new TrackBar();
            slider.Name = brightness ? "brightness" : "speed";
            slider.AccessibleName = brightness ? "亮度百分比" : "灯效速度，从慢到快";
            slider.Dock = DockStyle.Fill;
            slider.Margin = new Padding(0, 0, 0, 8);
            slider.Minimum = brightness ? 0 : 1;
            slider.Maximum = brightness ? 100 : 10;
            slider.LargeChange = brightness ? 10 : 1;
            slider.SmallChange = 1;
            slider.TickStyle = TickStyle.None;
            slider.BackColor = Theme.Surface;
            slider.ValueChanged += EditorChanged;
            group.Controls.Add(slider, 0, 1);
            group.SetColumnSpan(slider, 2);
            if (brightness) { brightnessInput = slider; brightnessValue = value; }
            else { speedInput = slider; speedValue = value; }
            return group;
        }

        private Control BuildFooter()
        {
            TableLayoutPanel footer = Grid(1, 3, Theme.Background);
            footer.Padding = new Padding(0, 16, 0, 0);
            footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 37));
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            TableLayoutPanel actions = Grid(6, 1, Theme.Background);
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 106));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 106));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 146));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 154));
            loadButton = ButtonOf("载入配置", "loadProfile", false);
            saveButton = ButtonOf("保存配置", "saveProfile", false);
            hardwareButton = ButtonOf("启用硬件灯效", "takeControl", false);
            offButton = ButtonOf("关闭所选灯光", "turnOffLighting", false);
            applyButton = ButtonOf("应用灯光", "applyLighting", true);
            AccentButton[] buttons = new AccentButton[] { loadButton, saveButton, hardwareButton, offButton, applyButton };
            foreach (AccentButton button in buttons)
            {
                button.Dock = DockStyle.Fill;
                button.Margin = new Padding(0, 0, 9, 0);
            }
            applyButton.Margin = Padding.Empty;
            loadButton.Click += LoadProfile;
            saveButton.Click += SaveProfile;
            hardwareButton.Click += async delegate { await TakeControlAsync(); };
            offButton.Click += async delegate { await ApplyAsync(true); };
            applyButton.Click += async delegate { await ApplyAsync(false); };
            actions.Controls.Add(loadButton, 0, 0);
            actions.Controls.Add(saveButton, 1, 0);
            actions.Controls.Add(hardwareButton, 2, 0);
            actions.Controls.Add(offButton, 4, 0);
            actions.Controls.Add(applyButton, 5, 0);
            tips.SetToolTip(loadButton, "从本地载入参数，不会自动应用。Ctrl+O");
            tips.SetToolTip(saveButton, "仅保存当前参数到本地，不会自动应用。Ctrl+S");
            tips.SetToolTip(applyButton, "向所选接口发送当前灯效。Ctrl+Enter");
            tips.SetToolTip(offButton, "仅关闭勾选的接口，保留编辑器中的灯效参数。");
            tips.SetToolTip(hardwareButton, "如果之前使用其他 RGB 软件且应用后无反应，点击此按钮。\n它会将两条 ARGB 切到硬件灯效，并关闭音频同步。\n此操作影响两条 ARGB；完成后请再点击“应用灯光”。");
            footer.Controls.Add(actions, 0, 0);
            feedbackLabel = LabelOf("", 9, Theme.Muted);
            feedbackLabel.Name = "operationFeedback";
            feedbackLabel.Dock = DockStyle.Fill;
            feedbackLabel.Padding = new Padding(0, 7, 0, 0);
            footer.Controls.Add(feedbackLabel, 0, 1);
            Label transparency = LabelOf("建议先退出 GCC / RGB Fusion / 其他 RGB 软件，避免互相覆盖。关闭本窗口即退出；不在后台运行。", 8.3F, Theme.Muted);
            transparency.Dock = DockStyle.Fill;
            footer.Controls.Add(transparency, 0, 2);
            return footer;
        }

        private void SetEditorDefaults()
        {
            suppressChanges = true;
            modeInput.SelectedIndex = 0;
            LightingSetting defaults = new LightingSetting();
            colorInput.Text = defaults.ColorHex;
            brightnessInput.Value = defaults.BrightnessPercent;
            speedInput.Value = defaults.Speed;
            suppressChanges = false;
            RefreshEditor();
        }

        private void EditorChanged(object sender, EventArgs args)
        {
            if (suppressChanges) return;
            RefreshEditor();
            if (!busy && !preview)
                SetFeedback("参数已修改，尚未应用。", Theme.Muted);
        }

        private void RefreshEditor()
        {
            if (modeInput == null || colorInput == null || brightnessInput == null || speedInput == null || applyButton == null) return;
            LightingMode mode = CurrentMode;
            int selected = 0;
            foreach (HeaderOption header in headers.Values) if (header.Checked) selected++;
            selectionCount.Text = selected.ToString(CultureInfo.InvariantCulture) + " / " + headers.Count.ToString(CultureInfo.InvariantCulture) + " 已选择";
            brightnessValue.Text = brightnessInput.Value.ToString(CultureInfo.InvariantCulture) + "%";
            speedValue.Text = speedInput.Value.ToString(CultureInfo.InvariantCulture) + " / 10";
            Color color;
            bool validColor = ParseColor(colorInput.Text, out color);
            if (validColor) colorSwatch.SwatchColor = color;
            colorInput.ForeColor = validColor ? Theme.Text : Theme.Error;
            bool singleColor = mode == LightingMode.Static || mode == LightingMode.Breathing || mode == LightingMode.Blink;
            colorInput.Enabled = singleColor;
            colorButton.Enabled = singleColor;
            presets.Enabled = singleColor;
            brightnessInput.Enabled = mode != LightingMode.Off;
            speedInput.Enabled = mode != LightingMode.Off && mode != LightingMode.Static;
            string validation = ValidateEditor();
            validationLabel.ForeColor = validation.Length == 0 ? Theme.Muted : Theme.Warning;
            validationLabel.Text = validation.Length == 0 ? ModeDescription(mode) : validation;
            bool canWrite = !preview && supported && !busy;
            applyButton.Enabled = canWrite && validation.Length == 0;
            offButton.Enabled = canWrite && selected > 0;
            hardwareButton.Enabled = canWrite;
            probeButton.Enabled = !preview && !busy;
            loadButton.Enabled = !busy;
            saveButton.Enabled = !busy;
        }

        private LightingMode CurrentMode
        {
            get { return modeInput.SelectedItem is ModeChoice ? ((ModeChoice)modeInput.SelectedItem).Mode : LightingMode.Static; }
        }

        private void DrawModeItem(object sender, DrawItemEventArgs args)
        {
            if (args.Bounds.Width <= 0 || args.Bounds.Height <= 0) return;
            bool selected = (args.State & DrawItemState.Selected) != 0;
            Color background = selected ? Color.FromArgb(47, 54, 82) : Theme.Input;
            using (SolidBrush brush = new SolidBrush(background)) args.Graphics.FillRectangle(brush, args.Bounds);
            string text = args.Index >= 0 && args.Index < modeInput.Items.Count ? modeInput.Items[args.Index].ToString() : modeInput.Text;
            Rectangle bounds = args.Bounds;
            bounds.X += 7;
            bounds.Width -= 9;
            TextRenderer.DrawText(args.Graphics, text, modeInput.Font, bounds, modeInput.Enabled ? Theme.Text : Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if ((args.State & DrawItemState.Focus) != 0) args.DrawFocusRectangle();
        }

        private string ValidateEditor()
        {
            bool any = false;
            foreach (HeaderOption header in headers.Values)
            {
                if (!header.Checked) continue;
                any = true;
            }
            if (!any) return "请先选择至少一个接口。";
            Color unused;
            bool needsColor = CurrentMode == LightingMode.Static || CurrentMode == LightingMode.Breathing || CurrentMode == LightingMode.Blink;
            if (needsColor && !ParseColor(colorInput.Text, out unused)) return "请输入 6 位 HEX 颜色，例如 #A78BFA。";
            return "";
        }

        private static string ModeDescription(LightingMode mode)
        {
            switch (mode)
            {
                case LightingMode.Off: return "将关闭所选接口；其他接口不受影响。";
                case LightingMode.Static: return "保持一种颜色。调整参数后，点击“应用灯光”。";
                case LightingMode.Breathing: return "所选颜色逐渐变亮、变暗。速度从 1（慢）到 10（快）。";
                case LightingMode.Blink: return "所选颜色反复闪烁。速度从 1（慢）到 10（快）。";
                case LightingMode.Cycle: return "自动切换多种颜色；此模式不使用单色设置。";
                default: return "调整参数后，点击“应用灯光”。";
            }
        }

        private LightingSetting CaptureSetting(bool off)
        {
            if (!off)
            {
                string validation = ValidateEditor();
                if (validation.Length > 0) throw new InvalidOperationException(validation);
            }
            List<string> selected = new List<string>();
            foreach (HeaderDefinition definition in BoardLayout.Headers)
                if (headers[definition.Key].Checked) selected.Add(definition.Key);
            Color color;
            if (!ParseColor(colorInput.Text, out color)) color = Color.White;
            LightingSetting setting = new LightingSetting();
            setting.Headers = selected.ToArray();
            setting.Mode = off ? LightingMode.Off : CurrentMode;
            setting.ColorHex = HexOf(color);
            setting.BrightnessPercent = brightnessInput.Value;
            setting.Speed = speedInput.Value;
            setting.Validate();
            return setting;
        }

        private async Task ProbeAsync()
        {
            if (preview || busy) return;
            supported = false;
            SetBusy(true);
            SetConnection("正在检测灯光控制器…", "仅读取连接信息，不更改灯光。", Theme.Cyan);
            SetFeedback("正在检测，请稍候。", Theme.Muted);
            try
            {
                ProbeResult result = await Task.Run(delegate { return LightingClient.Probe(); });
                if (IsDisposed) return;
                supported = result != null && result.IsSupported;
                if (result == null) throw new InvalidOperationException("设备没有返回可用的检测结果。");
                string detail = result.Status;
                if (String.IsNullOrWhiteSpace(detail)) detail = supported ? result.BoardName : "未检测到此主板受支持的灯光控制器。";
                SetConnection(supported ? "控制器已就绪" : "暂时无法控制灯光", detail, supported ? Theme.Cyan : Theme.Warning);
                string info = (result.BoardName ?? "") + "\n" + (result.ControllerProduct ?? "") + "\n固件：" + (result.FirmwareVersion ?? "未知");
                tips.SetToolTip(statusTitle, info);
                tips.SetToolTip(statusDetail, detail);
                if (result.Conflicts != null && result.Conflicts.Length > 0)
                    SetFeedback("检测到其他灯光软件：" + String.Join("、", result.Conflicts) + "。建议先退出，避免互相覆盖。", Theme.Warning);
                else
                    SetFeedback(supported ? "检测完成；当前参数尚未应用。" : "未发送灯光指令。检查设备状态后可重新检测。", Theme.Muted);
            }
            catch (Exception exception)
            {
                if (IsDisposed) return;
                supported = false;
                SetConnection("设备状态未确认", exception.GetBaseException().Message, Theme.Warning);
                SetFeedback("检测未完成；未发送灯光设置。可点击“重新检测”。", Theme.Warning);
            }
            finally { if (!IsDisposed) SetBusy(false); }
        }

        private async Task ApplyAsync(bool off)
        {
            if (preview || !supported || busy) return;
            LightingSetting setting;
            try { setting = CaptureSetting(off); }
            catch (Exception exception) { SetFeedback(exception.GetBaseException().Message, Theme.Warning); return; }
            SetBusy(true);
            SetFeedback(off ? "正在关闭所选接口的灯光…" : "正在发送所选接口的灯效…", Theme.Cyan);
            try
            {
                string result = await Task.Run(delegate { return LightingClient.Apply(setting); });
                if (IsDisposed) return;
                SetFeedback("指令已发送至 " + setting.Headers.Length.ToString(CultureInfo.InvariantCulture) + " 个接口。请查看实际灯光，确认效果。", Theme.Cyan);
                tips.SetToolTip(feedbackLabel, result ?? "指令已发送，尚未目视确认。" );
            }
            catch (Exception exception)
            {
                if (IsDisposed) return;
                supported = false;
                SetConnection("应用结果未确认", "请重新检测设备；程序不会自动重试。", Theme.Warning);
                SetFeedback("未确认应用成功：" + exception.GetBaseException().Message, Theme.Error);
            }
            finally { if (!IsDisposed) SetBusy(false); }
        }

        private async Task TakeControlAsync()
        {
            if (preview || !supported || busy) return;
            SetBusy(true);
            SetFeedback("正在将两条 ARGB 切到硬件灯效，并关闭音频同步…", Theme.Cyan);
            try
            {
                string result = await Task.Run(delegate { return LightingClient.TakeControl(); });
                if (IsDisposed) return;
                SetFeedback("硬件灯效指令已发送；请再点击“应用灯光”发送当前参数。", Theme.Cyan);
                tips.SetToolTip(feedbackLabel, result ?? "尚未目视确认灯效。" );
            }
            catch (Exception exception)
            {
                if (IsDisposed) return;
                supported = false;
                SetConnection("硬件灯效状态未确认", "请重新检测设备；程序不会自动重试。", Theme.Warning);
                SetFeedback("未确认操作成功：" + exception.GetBaseException().Message, Theme.Error);
            }
            finally { if (!IsDisposed) SetBusy(false); }
        }

        private void SetBusy(bool value)
        {
            busy = value;
            editorPanel.Enabled = !value;
            UseWaitCursor = value;
            applyButton.Text = value ? "请稍候…" : "应用灯光";
            RefreshEditor();
        }

        private void SetConnection(string title, string detail, Color color)
        {
            statusTitle.Text = "●  " + title;
            statusTitle.ForeColor = color;
            statusDetail.Text = detail;
            connectionCard.BorderColor = Color.FromArgb(255, (color.R + Theme.Surface.R * 3) / 4, (color.G + Theme.Surface.G * 3) / 4, (color.B + Theme.Surface.B * 3) / 4);
            connectionCard.Invalidate();
        }

        private void SetFeedback(string message, Color color)
        {
            feedbackLabel.Text = message;
            feedbackLabel.ForeColor = color;
            tips.SetToolTip(feedbackLabel, message);
        }

        private void ChooseColor(object sender, EventArgs args)
        {
            using (ColorDialog dialog = new ColorDialog())
            {
                Color color;
                if (ParseColor(colorInput.Text, out color)) dialog.Color = color;
                dialog.FullOpen = true;
                if (dialog.ShowDialog(this) == DialogResult.OK) colorInput.Text = HexOf(dialog.Color);
            }
        }

        private void SaveProfile(object sender, EventArgs args)
        {
            if (busy) return;
            try
            {
                LightingSetting setting = CaptureSetting(false);
                using (SaveFileDialog dialog = new SaveFileDialog())
                {
                    dialog.Title = "保存灯光配置（不会应用）";
                    dialog.Filter = "灯光配置 (*.xml)|*.xml|所有文件 (*.*)|*.*";
                    dialog.DefaultExt = "xml";
                    dialog.FileName = "FKGCC-Light.xml";
                    dialog.RestoreDirectory = true;
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    ProfileStore.Save(dialog.FileName, setting);
                    SetFeedback("已保存配置。灯光状态没有因此改变。", Theme.Cyan);
                }
            }
            catch (Exception exception) { SetFeedback("保存失败：" + exception.GetBaseException().Message, Theme.Error); }
        }

        private void LoadProfile(object sender, EventArgs args)
        {
            if (busy) return;
            try
            {
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Title = "载入灯光配置（不会应用）";
                    dialog.Filter = "灯光配置 (*.xml)|*.xml|所有文件 (*.*)|*.*";
                    dialog.RestoreDirectory = true;
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    LightingSetting setting = ProfileStore.Load(dialog.FileName);
                    setting.Validate();
                    HashSet<string> selected = new HashSet<string>(setting.Headers, StringComparer.OrdinalIgnoreCase);
                    suppressChanges = true;
                    try
                    {
                        foreach (HeaderOption header in headers.Values) header.Checked = selected.Contains(header.Definition.Key);
                        for (int index = 0; index < modeInput.Items.Count; index++)
                            if (((ModeChoice)modeInput.Items[index]).Mode == setting.Mode) modeInput.SelectedIndex = index;
                        colorInput.Text = setting.ColorHex;
                        brightnessInput.Value = setting.BrightnessPercent;
                        speedInput.Value = setting.Speed;
                    }
                    finally { suppressChanges = false; RefreshEditor(); }
                    SetFeedback("配置已载入，尚未应用。确认参数后，点击“应用灯光”。", Theme.Cyan);
                }
            }
            catch (Exception exception) { SetFeedback("载入失败：" + exception.GetBaseException().Message, Theme.Error); }
        }

        private void HandleShortcut(object sender, KeyEventArgs args)
        {
            if (args.Control && args.KeyCode == Keys.S) saveButton.PerformClick();
            else if (args.Control && args.KeyCode == Keys.O) loadButton.PerformClick();
            else if (args.Control && args.KeyCode == Keys.Enter) applyButton.PerformClick();
            else if (args.KeyCode == Keys.F5) probeButton.PerformClick();
            else return;
            args.Handled = true;
            args.SuppressKeyPress = true;
        }

        private static bool ParseColor(string input, out Color color)
        {
            color = Color.White;
            string value = (input ?? "").Trim();
            if (value.StartsWith("#", StringComparison.Ordinal)) value = value.Substring(1);
            int rgb;
            if (value.Length != 6 || !Int32.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb)) return false;
            color = Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
            return true;
        }

        private static string HexOf(Color color)
        {
            return "#" + color.R.ToString("X2", CultureInfo.InvariantCulture) + color.G.ToString("X2", CultureInfo.InvariantCulture) + color.B.ToString("X2", CultureInfo.InvariantCulture);
        }

        private static TableLayoutPanel Grid(int columns, int rows, Color background)
        {
            TableLayoutPanel layout = new TableLayoutPanel();
            layout.ColumnCount = columns;
            layout.RowCount = rows;
            if (columns == 1) layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            if (rows == 1) layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Dock = DockStyle.Fill;
            layout.Margin = Padding.Empty;
            layout.Padding = Padding.Empty;
            layout.BackColor = background;
            return layout;
        }

        private static Label LabelOf(string text, float size, Color color)
        {
            Label label = new Label();
            label.Text = text;
            label.Font = new Font(Theme.FontFamily, size, FontStyle.Regular);
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            label.Margin = Padding.Empty;
            label.AutoEllipsis = true;
            return label;
        }

        private static AccentButton ButtonOf(string text, string name, bool primary)
        {
            AccentButton button = new AccentButton();
            button.Text = text;
            button.Name = name;
            button.AccessibleName = text;
            button.Primary = primary;
            button.Font = new Font(Theme.FontFamily, 9F, primary ? FontStyle.Bold : FontStyle.Regular);
            return button;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tips.Dispose();
            base.Dispose(disposing);
        }

        private sealed class ModeChoice
        {
            internal readonly string Text;
            internal readonly LightingMode Mode;
            internal ModeChoice(string text, LightingMode mode) { Text = text; Mode = mode; }
            public override string ToString() { return Text; }
        }
    }

    internal static class Theme
    {
        internal const string FontFamily = "Microsoft YaHei UI";
        internal static readonly Font BodyFont = new Font(FontFamily, 9F);
        internal static readonly Color Background = Color.FromArgb(12, 16, 26);
        internal static readonly Color Surface = Color.FromArgb(21, 27, 41);
        internal static readonly Color Input = Color.FromArgb(29, 37, 54);
        internal static readonly Color Border = Color.FromArgb(43, 53, 73);
        internal static readonly Color Text = Color.FromArgb(235, 239, 250);
        internal static readonly Color Muted = Color.FromArgb(148, 163, 187);
        internal static readonly Color Cyan = Color.FromArgb(99, 211, 239);
        internal static readonly Color Purple = Color.FromArgb(174, 151, 250);
        internal static readonly Color Warning = Color.FromArgb(246, 193, 115);
        internal static readonly Color Error = Color.FromArgb(255, 147, 159);

        internal static GraphicsPath Rounded(RectangleF rectangle, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float diameter = radius * 2;
            path.AddArc(rectangle.X, rectangle.Y, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Y, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rectangle.X, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class RoundedPanel : Panel
    {
        internal Color BorderColor = Theme.Border;
        internal RoundedPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }
        protected override void OnPaint(PaintEventArgs args)
        {
            args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Theme.Rounded(new RectangleF(0.5F, 0.5F, Width - 1, Height - 1), 12))
            using (SolidBrush fill = new SolidBrush(Theme.Surface))
            using (Pen border = new Pen(BorderColor))
            {
                args.Graphics.FillPath(fill, path);
                args.Graphics.DrawPath(border, path);
            }
        }
    }

    internal sealed class AccentButton : Button
    {
        internal bool Primary;
        internal Color? CustomFill;
        private bool hover;
        private bool pressed;
        internal AccentButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Color.Transparent;
            ForeColor = Theme.Text;
            Cursor = Cursors.Hand;
            UseVisualStyleBackColor = false;
        }
        protected override void OnMouseEnter(EventArgs args) { hover = true; Invalidate(); base.OnMouseEnter(args); }
        protected override void OnMouseLeave(EventArgs args) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(args); }
        protected override void OnMouseDown(MouseEventArgs args) { pressed = true; Invalidate(); base.OnMouseDown(args); }
        protected override void OnMouseUp(MouseEventArgs args) { pressed = false; Invalidate(); base.OnMouseUp(args); }
        protected override void OnPaint(PaintEventArgs args)
        {
            args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = Primary ? Color.FromArgb(118, 105, 222) : Theme.Input;
            if (CustomFill.HasValue) fill = CustomFill.Value;
            if (!Enabled) fill = Color.FromArgb(34, 41, 58);
            else if (pressed) fill = ControlPaint.Dark(fill, 0.12F);
            else if (hover) fill = ControlPaint.Light(fill, 0.12F);
            using (GraphicsPath path = Theme.Rounded(new RectangleF(0.5F, 0.5F, Width - 1, Height - 1), 7))
            using (SolidBrush brush = new SolidBrush(fill))
            using (Pen pen = new Pen(Primary && Enabled ? Theme.Purple : Theme.Border))
            {
                args.Graphics.FillPath(brush, path);
                args.Graphics.DrawPath(pen, path);
            }
            TextRenderer.DrawText(args.Graphics, Text, Font, ClientRectangle, Enabled ? Theme.Text : Color.FromArgb(111, 124, 147), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(args.Graphics, Rectangle.Inflate(ClientRectangle, -5, -5), Theme.Cyan, fill);
        }
    }

    internal sealed class HeaderOption : CheckBox
    {
        internal readonly HeaderDefinition Definition;
        private bool hover;
        internal HeaderOption(HeaderDefinition definition)
        {
            Definition = definition;
            Text = definition.Key;
            AccessibleName = definition.Key + (definition.Addressable ? "，5V ARGB 接口" : "，12V RGB 接口");
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            AutoSize = false;
        }
        protected override void OnMouseEnter(EventArgs args) { hover = true; Invalidate(); base.OnMouseEnter(args); }
        protected override void OnMouseLeave(EventArgs args) { hover = false; Invalidate(); base.OnMouseLeave(args); }
        protected override void OnCheckedChanged(EventArgs args) { Invalidate(); base.OnCheckedChanged(args); }
        protected override void OnPaint(PaintEventArgs args)
        {
            args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = args.Graphics.DpiX / 96F;
            Color background = Checked ? Color.FromArgb(32, 42, 63) : Theme.Surface;
            if (hover && Enabled) background = Color.FromArgb(38, 49, 72);
            using (GraphicsPath path = Theme.Rounded(new RectangleF(0.5F, 0.5F, Width - 1, Height - 1), 8))
            using (SolidBrush brush = new SolidBrush(background))
            using (Pen border = new Pen(Checked ? Color.FromArgb(63, 81, 110) : Theme.Border))
            {
                args.Graphics.FillPath(brush, path);
                args.Graphics.DrawPath(border, path);
            }
            int boxSize = (int)Math.Round(17 * scale);
            Rectangle box = new Rectangle((int)Math.Round(13 * scale), (Height - boxSize) / 2, boxSize, boxSize);
            using (SolidBrush brush = new SolidBrush(Checked ? Theme.Cyan : Theme.Input)) args.Graphics.FillRectangle(brush, box);
            using (Pen pen = new Pen(Checked ? Theme.Cyan : Theme.Muted)) args.Graphics.DrawRectangle(pen, box);
            if (Checked)
            {
                using (Pen check = new Pen(Theme.Background, 2F * scale))
                    args.Graphics.DrawLines(check, new PointF[] { new PointF(box.X + 4 * scale, box.Y + 8 * scale), new PointF(box.X + 7 * scale, box.Y + 11 * scale), new PointF(box.X + 13 * scale, box.Y + 5 * scale) });
            }
            Rectangle title = new Rectangle((int)(43 * scale), (int)(6 * scale), Width - (int)(51 * scale), (int)(22 * scale));
            Rectangle description = new Rectangle((int)(43 * scale), (int)(27 * scale), Width - (int)(51 * scale), (int)(18 * scale));
            using (Font name = new Font("Segoe UI", 10F, FontStyle.Bold))
            using (Font sub = new Font(Theme.FontFamily, 8F))
            {
                TextRenderer.DrawText(args.Graphics, Definition.Key, name, title, Enabled ? Theme.Text : Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(args.Graphics, Definition.Addressable ? "5V · 可寻址 ARGB" : "12V · RGB", sub, description, Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(args.Graphics, Rectangle.Inflate(ClientRectangle, -4, -4), Theme.Cyan, background);
        }
    }

    internal sealed class ColorSwatch : Control
    {
        private Color color = Theme.Purple;
        internal Color SwatchColor { get { return color; } set { color = value; Invalidate(); } }
        internal ColorSwatch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            BackColor = Theme.Surface;
            AccessibleName = "当前颜色预览";
            TabStop = false;
        }
        protected override void OnPaint(PaintEventArgs args)
        {
            args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = args.Graphics.DpiX / 96F;
            int diameter = Math.Min(Width - (int)(12 * scale), Height - (int)(28 * scale));
            int x = (Width - diameter) / 2;
            int y = 1;
            for (int ring = 6; ring >= 1; ring--)
            {
                int inset = (int)((6 - ring) * 2 * scale);
                using (SolidBrush glow = new SolidBrush(Color.FromArgb(10 + ring * 2, color)))
                    args.Graphics.FillEllipse(glow, x + inset, y + inset, diameter - inset * 2, diameter - inset * 2);
            }
            using (SolidBrush brush = new SolidBrush(color)) args.Graphics.FillEllipse(brush, x + 13 * scale, y + 13 * scale, diameter - 26 * scale, diameter - 26 * scale);
            TextRenderer.DrawText(args.Graphics, "颜色预览", Theme.BodyFont, new Rectangle(0, Height - (int)(22 * scale), Width, (int)(20 * scale)), Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
