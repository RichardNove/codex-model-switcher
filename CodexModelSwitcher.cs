using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CodexModelSwitcher
{
    internal static class Program
    {
        [DllImport("shcore.dll")]
        private static extern int SetProcessDpiAwareness(int awareness);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length >= 2 && args[0] == "--print-secret")
            {
                try
                {
                    Console.Out.Write(SecretStore.Load(args[1]));
                    return 0;
                }
                catch
                {
                    return 2;
                }
            }

            if (args.Length == 1 && args[0] == "--read-codex-limits")
            {
                try
                {
                    Console.Out.WriteLine(CodexAppServerUsage.Read().ToDisplayString());
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex.Message);
                    return 3;
                }
            }

            if (args.Length == 1 && args[0] == "--self-test")
            {
                try
                {
                    SelfTest.Run();
                    Console.Out.WriteLine("SELF_TEST_OK");
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex.ToString());
                    return 1;
                }
            }

            EnableDpiAwareness();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length >= 2 && (args[0] == "--render-preview" || args[0] == "--render-manager-preview" || args[0] == "--render-usage-preview"))
            {
                Form preview = args[0] == "--render-manager-preview" ? (Form)new ModelManagerForm(new Switcher())
                    : args[0] == "--render-usage-preview" ? (Form)new UsageForm()
                    : new MainForm();
                using (preview)
                {
                    int width, height;
                    if (args.Length >= 4 && int.TryParse(args[2], out width) && int.TryParse(args[3], out height))
                        preview.ClientSize = new Size(Math.Max(preview.MinimumSize.Width, width), Math.Max(preview.MinimumSize.Height, height));
                    preview.StartPosition = FormStartPosition.Manual;
                    preview.Location = new Point(-5000, -5000);
                    preview.ShowInTaskbar = false;
                    preview.Show();
                    Application.DoEvents();
                    if (args[0] == "--render-usage-preview")
                    {
                        DateTime waitUntil = DateTime.UtcNow.AddSeconds(10);
                        while (DateTime.UtcNow < waitUntil) { Application.DoEvents(); Thread.Sleep(100); }
                    }
                    using (Bitmap bitmap = new Bitmap(preview.Width, preview.Height))
                    {
                        preview.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                        string target = Path.GetFullPath(args[1]);
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        bitmap.Save(target, System.Drawing.Imaging.ImageFormat.Png);
                    }
                    preview.Close();
                }
                return 0;
            }
            Application.Run(new MainForm());
            return 0;
        }

        private static void EnableDpiAwareness()
        {
            try
            {
                // Per-monitor DPI awareness when the source is compiled without the bundled manifest.
                SetProcessDpiAwareness(2);
            }
            catch
            {
                try { SetProcessDPIAware(); } catch { }
            }
        }
    }

    internal sealed class MainForm : Form
    {
        private static readonly Color Ink = Color.FromArgb(29, 29, 31);
        private static readonly Color Muted = Color.FromArgb(110, 110, 115);
        private static readonly Color Canvas = Color.FromArgb(245, 245, 247);
        private static readonly Color Blue = Color.FromArgb(0, 113, 227);
        private static readonly Color Teal = Color.FromArgb(0, 145, 130);
        private readonly TextBox keyBox;
        private readonly Label statusLabel;
        private readonly Label keyStatusLabel;
        private readonly Switcher switcher;
        private readonly Panel contentPanel;
        private float contentScale = 1F;
        private bool positioningContent;

        public MainForm()
        {
            Text = "Codex 模型启动器";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1080, 750);
            MinimumSize = new Size(900, 700);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            AutoScroll = true;
            BackColor = Canvas;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            DoubleBuffered = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { Icon = SystemIcons.Application; }

            switcher = new Switcher();

            contentPanel = new Panel();
            contentPanel.Size = new Size(1080, 720);
            contentPanel.BackColor = Canvas;
            Controls.Add(contentPanel);
            AutoScrollMinSize = contentPanel.Size;
            Resize += delegate { PositionContent(); };

            BrandMark brand = new BrandMark();
            brand.Location = new Point(40, 34);
            brand.Size = new Size(56, 56);
            contentPanel.Controls.Add(brand);

            Label title = NewLabel("Codex 模型启动器", 24F, FontStyle.Bold, Ink);
            title.Location = new Point(116, 26);
            title.Size = new Size(520, 48);
            title.TextAlign = ContentAlignment.MiddleLeft;
            contentPanel.Controls.Add(title);

            Label subtitle = NewLabel("选择模型，一键写入安全配置并启动 Codex", 9.5F, FontStyle.Regular, Muted);
            subtitle.Location = new Point(118, 73);
            subtitle.Size = new Size(520, 30);
            subtitle.TextAlign = ContentAlignment.MiddleLeft;
            contentPanel.Controls.Add(subtitle);

            Label safeTag = NewLabel("●  本地配置 · 密钥加密", 9F, FontStyle.Bold, Color.FromArgb(0, 128, 112));
            safeTag.Location = new Point(590, 49);
            safeTag.Size = new Size(205, 32);
            safeTag.TextAlign = ContentAlignment.MiddleRight;
            contentPanel.Controls.Add(safeTag);

            RoundedButton usageButton = SmallButton("用量概览", Color.FromArgb(99, 99, 102), 100);
            usageButton.Location = new Point(810, 43);
            usageButton.Click += delegate { using (UsageForm form = new UsageForm()) form.ShowDialog(this); };
            contentPanel.Controls.Add(usageButton);

            RoundedButton importButton = SmallButton("导入模型  +", Ink, 120);
            importButton.Location = new Point(920, 43);
            importButton.Click += delegate { using (ModelManagerForm form = new ModelManagerForm(switcher)) form.ShowDialog(this); };
            contentPanel.Controls.Add(importButton);

            FlowLayoutPanel cards = new FlowLayoutPanel();
            cards.Location = new Point(40, 126);
            cards.Size = new Size(1000, 232);
            cards.BackColor = Canvas;
            cards.WrapContents = false;
            cards.Padding = new Padding(0);
            cards.Margin = new Padding(0);
            contentPanel.Controls.Add(cards);

            cards.Controls.Add(CreateCard("G", "GPT / OpenAI", "使用现有 ChatGPT 账号\n恢复原来的 Codex 配置", Blue, delegate { ActivateOpenAI(); }));
            cards.Controls.Add(CreateCard("D", "DeepSeek Flash", "支持图片输入，速度更快\n适合日常编码任务", Teal, delegate { ActivateDeepSeek("deepseek-flash"); }));
            cards.Controls.Add(CreateCard("D+", "DeepSeek V4 Pro", "增强推理能力，回答更深入\n适合复杂和长周期任务", Color.FromArgb(103, 78, 190), delegate { ActivateDeepSeek("deepseek-v4-pro"); }));

            RoundedPanel keyPanel = new RoundedPanel();
            keyPanel.Location = new Point(40, 382);
            keyPanel.Size = new Size(1000, 206);
            keyPanel.BackColor = Color.White;
            keyPanel.BorderColor = Color.FromArgb(225, 225, 230);
            keyPanel.Radius = 22;
            contentPanel.Controls.Add(keyPanel);

            Label keyTitle = NewLabel("DeepSeek API Key", 14F, FontStyle.Bold, Ink);
            keyTitle.Location = new Point(28, 18);
            keyTitle.Size = new Size(320, 40);
            keyPanel.Controls.Add(keyTitle);

            keyStatusLabel = NewLabel("", 8.8F, FontStyle.Bold, Muted);
            keyStatusLabel.Location = new Point(28, 56);
            keyStatusLabel.Size = new Size(520, 30);
            keyPanel.Controls.Add(keyStatusLabel);

            keyBox = new TextBox();
            keyBox.Location = new Point(28, 94);
            keyBox.Size = new Size(594, 34);
            keyBox.Font = new Font("Consolas", 11F);
            keyBox.UseSystemPasswordChar = true;
            keyBox.BorderStyle = BorderStyle.FixedSingle;
            keyPanel.Controls.Add(keyBox);

            RoundedButton saveButton = SmallButton("安全保存", Blue, 132);
            saveButton.Location = new Point(646, 89);
            saveButton.Click += delegate { SaveKey(); };
            keyPanel.Controls.Add(saveButton);

            RoundedButton testButton = SmallButton("测试连接", Color.FromArgb(73, 73, 78), 132);
            testButton.Location = new Point(794, 89);
            testButton.Click += delegate { TestConnection(); };
            keyPanel.Controls.Add(testButton);

            Label privacyIcon = NewLabel("◆", 8F, FontStyle.Bold, Blue);
            privacyIcon.Location = new Point(28, 158);
            privacyIcon.Size = new Size(18, 20);
            keyPanel.Controls.Add(privacyIcon);

            Label privacy = NewLabel("密钥由 Windows 当前用户加密保存，不会以明文写入 Codex 配置。", 8.8F, FontStyle.Regular, Muted);
            privacy.Location = new Point(50, 151);
            privacy.Size = new Size(890, 36);
            privacy.TextAlign = ContentAlignment.MiddleLeft;
            keyPanel.Controls.Add(privacy);

            Label restartHint = NewLabel("切换后如果 Codex 已打开，请从任务栏托盘完全退出再启动。", 8.7F, FontStyle.Regular, Muted);
            restartHint.Location = new Point(44, 614);
            restartHint.Size = new Size(760, 32);
            contentPanel.Controls.Add(restartHint);

            statusLabel = NewLabel("就绪 · 请选择一个模型", 9.5F, FontStyle.Bold, Muted);
            statusLabel.Location = new Point(44, 656);
            statusLabel.Size = new Size(760, 42);
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            contentPanel.Controls.Add(statusLabel);

            RoundedButton openButton = SmallButton("打开 Codex  →", Ink, 174);
            openButton.Location = new Point(866, 657);
            openButton.Click += delegate { OpenCodex(); };
            contentPanel.Controls.Add(openButton);

            UpdateKeyStatus();
            PositionContent();
        }

        private void PositionContent()
        {
            if (positioningContent) return;
            positioningContent = true;
            try
            {
                const float designWidth = 1080F;
                const float designHeight = 720F;
                float targetScale = Math.Min(ClientSize.Width / designWidth, ClientSize.Height / designHeight);
                targetScale = Math.Max(0.72F, targetScale);
                if (Math.Abs(targetScale - contentScale) > 0.002F)
                {
                    float relative = targetScale / contentScale;
                    contentPanel.SuspendLayout();
                    contentPanel.Scale(new SizeF(relative, relative));
                    contentPanel.Size = new Size((int)Math.Round(designWidth * targetScale), (int)Math.Round(designHeight * targetScale));
                    contentPanel.ResumeLayout(true);
                    contentScale = targetScale;
                }
                int x = Math.Max(0, (ClientSize.Width - contentPanel.Width) / 2);
                int y = Math.Max(0, (ClientSize.Height - contentPanel.Height) / 2);
                contentPanel.Location = new Point(x, y);
                AutoScrollMinSize = contentPanel.Size;
            }
            finally
            {
                positioningContent = false;
            }
        }

        private Label NewLabel(string text, float size, FontStyle style, Color color)
        {
            Label label = new Label();
            label.Text = text;
            label.Font = new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Point);
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            label.AutoEllipsis = false;
            label.UseCompatibleTextRendering = true;
            return label;
        }

        private ProviderCard CreateCard(string glyph, string title, string description, Color accent, EventHandler click)
        {
            ProviderCard card = new ProviderCard();
            card.Size = new Size(313, 230);
            card.Margin = new Padding(0, 0, 20, 0);
            card.Glyph = glyph;
            card.TitleText = title;
            card.DescriptionText = description;
            card.AccentColor = accent;
            card.Click += click;
            return card;
        }

        private RoundedButton SmallButton(string text, Color color, int width)
        {
            RoundedButton button = new RoundedButton();
            button.Text = text;
            button.Size = new Size(width, 40);
            button.BackColor = color;
            button.ForeColor = Color.White;
            button.Cursor = Cursors.Hand;
            button.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            button.Radius = 9;
            return button;
        }

        private void SaveKey()
        {
            string key = keyBox.Text.Trim();
            if (!key.StartsWith("sk-", StringComparison.Ordinal))
            {
                MessageBox.Show(this, "DeepSeek API Key 应以 sk- 开头。", "格式不正确", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                SecretStore.Save(key);
                keyBox.Clear();
                UpdateKeyStatus();
                SetStatus("DeepSeek API Key 已加密保存。", Teal);
            }
            catch (Exception ex)
            {
                ShowError("保存密钥失败", ex);
            }
        }

        private void TestConnection()
        {
            if (!SecretStore.Exists())
            {
                MessageBox.Show(this, "请先输入并保存 DeepSeek API Key。", "尚未配置", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Cursor = Cursors.WaitCursor;
            SetStatus("正在测试 DeepSeek API…", Blue);
            Application.DoEvents();
            try
            {
                DeepSeekApi.Test(SecretStore.Load("deepseek"));
                SetStatus("DeepSeek API 连接成功。", Teal);
                MessageBox.Show(this, "连接成功，API Key 可用。", "DeepSeek", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                SetStatus("连接测试失败。", Color.Firebrick);
                ShowError("DeepSeek API 连接失败", ex);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void ActivateOpenAI()
        {
            try
            {
                SwitchResult result = switcher.RestoreOpenAI();
                SetStatus(result.Message, Blue);
                LaunchAfterSwitch();
            }
            catch (Exception ex)
            {
                ShowError("恢复 GPT 配置失败", ex);
            }
        }

        private void ActivateDeepSeek(string model)
        {
            if (!SecretStore.Exists())
            {
                MessageBox.Show(this, "请先在下方输入并安全保存 DeepSeek API Key。", "需要 API Key", MessageBoxButtons.OK, MessageBoxIcon.Information);
                keyBox.Focus();
                return;
            }
            Cursor = Cursors.WaitCursor;
            SetStatus("正在生成并验证 DeepSeek 配置…", Blue);
            Application.DoEvents();
            try
            {
                SwitchResult result = switcher.ActivateDeepSeek(model);
                SetStatus(result.Message, Teal);
                LaunchAfterSwitch();
            }
            catch (Exception ex)
            {
                ShowError("切换 DeepSeek 失败", ex);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void LaunchAfterSwitch()
        {
            if (CodexLauncher.IsRunning())
            {
                MessageBox.Show(this,
                    "配置已经切换成功。\r\n\r\nCodex 当前仍在运行，必须从任务栏托盘菜单中选择 Quit 完全退出，然后再点击“打开 Codex”，新配置才会生效。",
                    "需要重启 Codex", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            OpenCodex();
        }

        private void OpenCodex()
        {
            try
            {
                CodexLauncher.Launch();
                SetStatus("已请求打开 Codex。", Blue);
            }
            catch (Exception ex)
            {
                ShowError("无法自动打开 Codex", ex);
            }
        }

        private void UpdateKeyStatus()
        {
            keyStatusLabel.Text = SecretStore.Exists()
                ? "已配置密钥（当前 Windows 用户加密保存）"
                : "尚未配置密钥";
            keyStatusLabel.ForeColor = SecretStore.Exists() ? Teal : Muted;
        }

        private void SetStatus(string text, Color color)
        {
            statusLabel.Text = text;
            statusLabel.ForeColor = color;
        }

        private void ShowError(string title, Exception ex)
        {
            SetStatus(title, Color.Firebrick);
            MessageBox.Show(this, ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    internal sealed class ModelManagerForm : Form
    {
        private readonly Switcher switcher;
        private readonly ListBox profileList;
        private readonly TextBox nameBox;
        private readonly TextBox modelBox;
        private readonly TextBox baseUrlBox;
        private readonly TextBox keyBox;
        private readonly TextBox usageUrlBox;
        private readonly Label statusLabel;
        private readonly Panel contentPanel;
        private ProviderProfile current;
        private float contentScale = 1F;
        private bool positioningContent;

        public ModelManagerForm(Switcher modelSwitcher)
        {
            switcher = modelSwitcher;
            Text = "导入与管理模型";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(900, 620);
            MinimumSize = new Size(820, 600);
            BackColor = Color.FromArgb(245, 245, 247);
            Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScroll = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            contentPanel = new Panel { Size = new Size(900, 620), BackColor = Color.FromArgb(245, 245, 247) };
            Controls.Add(contentPanel);
            AutoScrollMinSize = contentPanel.Size;
            Resize += delegate { PositionContent(); };

            contentPanel.Controls.Add(LabelAt("导入与管理模型", 28F, FontStyle.Bold, Color.FromArgb(29, 29, 31), 32, 18, 600, 62));
            contentPanel.Controls.Add(LabelAt("添加任何兼容 Responses API 的模型提供商", 9.5F, FontStyle.Regular, Color.FromArgb(110, 110, 115), 35, 82, 650, 30));

            RoundedPanel left = PanelAt(30, 120, 250, 460);
            contentPanel.Controls.Add(left);
            left.Controls.Add(LabelAt("已导入模型", 12F, FontStyle.Bold, Color.FromArgb(29, 29, 31), 20, 18, 190, 34));
            profileList = new ListBox();
            profileList.Location = new Point(20, 62);
            profileList.Size = new Size(210, 320);
            profileList.BorderStyle = BorderStyle.None;
            profileList.Font = new Font("Microsoft YaHei UI", 10F);
            profileList.SelectedIndexChanged += delegate { SelectProfile(); };
            left.Controls.Add(profileList);
            RoundedButton newButton = ButtonAt("＋ 新建模型", Color.FromArgb(0, 113, 227), 20, 400, 210);
            newButton.Click += delegate { ClearEditor(); };
            left.Controls.Add(newButton);

            RoundedPanel editor = PanelAt(300, 120, 570, 460);
            contentPanel.Controls.Add(editor);
            editor.Controls.Add(LabelAt("提供商名称", 8.7F, FontStyle.Bold, Color.FromArgb(110, 110, 115), 24, 15, 180, 26));
            nameBox = InputAt(editor, 24, 42, 245);
            editor.Controls.Add(LabelAt("模型 ID", 8.7F, FontStyle.Bold, Color.FromArgb(110, 110, 115), 297, 15, 180, 26));
            modelBox = InputAt(editor, 297, 42, 245);

            editor.Controls.Add(LabelAt("Responses API Base URL", 8.7F, FontStyle.Bold, Color.FromArgb(110, 110, 115), 24, 91, 260, 26));
            baseUrlBox = InputAt(editor, 24, 118, 518);

            editor.Controls.Add(LabelAt("API Key（留空表示保留现有密钥）", 8.7F, FontStyle.Bold, Color.FromArgb(110, 110, 115), 24, 167, 330, 26));
            keyBox = InputAt(editor, 24, 194, 518);
            keyBox.UseSystemPasswordChar = true;

            editor.Controls.Add(LabelAt("用量 URL（可选，用于余额/限额监视）", 8.7F, FontStyle.Bold, Color.FromArgb(110, 110, 115), 24, 243, 360, 26));
            usageUrlBox = InputAt(editor, 24, 270, 518);

            Label note = LabelAt("仅支持 Codex 当前认可的 Responses API 协议。导入前请确认服务商兼容 /responses；API Key 使用 Windows DPAPI 加密。", 8.4F, FontStyle.Regular, Color.FromArgb(110, 110, 115), 24, 319, 518, 48);
            editor.Controls.Add(note);

            RoundedButton saveButton = ButtonAt("保存模型", Color.FromArgb(0, 113, 227), 24, 382, 120);
            saveButton.Click += delegate { try { SaveProfile(true); } catch (Exception ex) { ShowError(ex); } };
            editor.Controls.Add(saveButton);
            RoundedButton testButton = ButtonAt("测试连接", Color.FromArgb(73, 73, 78), 156, 382, 120);
            testButton.Click += delegate { TestProfile(); };
            editor.Controls.Add(testButton);
            RoundedButton activateButton = ButtonAt("切换并启动", Color.FromArgb(0, 145, 130), 288, 382, 132);
            activateButton.Click += delegate { ActivateProfile(); };
            editor.Controls.Add(activateButton);
            RoundedButton deleteButton = ButtonAt("删除", Color.FromArgb(190, 45, 45), 432, 382, 110);
            deleteButton.Click += delegate { DeleteProfile(); };
            editor.Controls.Add(deleteButton);

            statusLabel = LabelAt("填写信息后保存；模型会存入当前 Windows 用户配置。", 8.8F, FontStyle.Bold, Color.FromArgb(110, 110, 115), 35, 585, 820, 30);
            contentPanel.Controls.Add(statusLabel);
            LoadProfiles();
            PositionContent();
        }

        private void PositionContent()
        {
            if (positioningContent) return;
            positioningContent = true;
            try
            {
                const float designWidth = 900F;
                const float designHeight = 620F;
                float targetScale = Math.Max(0.72F, Math.Min(ClientSize.Width / designWidth, ClientSize.Height / designHeight));
                if (Math.Abs(targetScale - contentScale) > 0.002F)
                {
                    float relative = targetScale / contentScale;
                    contentPanel.SuspendLayout();
                    contentPanel.Scale(new SizeF(relative, relative));
                    contentPanel.Size = new Size((int)Math.Round(designWidth * targetScale), (int)Math.Round(designHeight * targetScale));
                    contentPanel.ResumeLayout(true);
                    contentScale = targetScale;
                }
                contentPanel.Location = new Point(Math.Max(0, (ClientSize.Width - contentPanel.Width) / 2), Math.Max(0, (ClientSize.Height - contentPanel.Height) / 2));
                AutoScrollMinSize = contentPanel.Size;
            }
            finally { positioningContent = false; }
        }

        private static RoundedPanel PanelAt(int x, int y, int w, int h)
        {
            return new RoundedPanel { Location = new Point(x, y), Size = new Size(w, h), BackColor = Color.White, BorderColor = Color.FromArgb(225, 225, 230), Radius = 20 };
        }

        private static Label LabelAt(string text, float size, FontStyle style, Color color, int x, int y, int w, int h)
        {
            return new Label { Text = text, Font = new Font("Microsoft YaHei UI", size, style), ForeColor = color, BackColor = Color.Transparent, Location = new Point(x, y), Size = new Size(w, h), UseCompatibleTextRendering = true };
        }

        private static TextBox InputAt(Control parent, int x, int y, int width)
        {
            TextBox box = new TextBox { Location = new Point(x, y), Size = new Size(width, 31), Font = new Font("Microsoft YaHei UI", 10F), BorderStyle = BorderStyle.FixedSingle };
            parent.Controls.Add(box);
            return box;
        }

        private static RoundedButton ButtonAt(string text, Color color, int x, int y, int width)
        {
            return new RoundedButton { Text = text, BackColor = color, ForeColor = Color.White, Location = new Point(x, y), Size = new Size(width, 40), Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold), Cursor = Cursors.Hand, Radius = 9 };
        }

        private void LoadProfiles()
        {
            profileList.Items.Clear();
            foreach (ProviderProfile profile in ProviderStore.Load()) profileList.Items.Add(profile);
            if (profileList.Items.Count == 0) ClearEditor();
        }

        private void SelectProfile()
        {
            current = profileList.SelectedItem as ProviderProfile;
            if (current == null) return;
            nameBox.Text = current.Name;
            modelBox.Text = current.Model;
            baseUrlBox.Text = current.BaseUrl;
            usageUrlBox.Text = current.UsageUrl;
            keyBox.Clear();
            ShowStatus("正在编辑 “" + current.Name + " / " + current.Model + "”。", Color.FromArgb(0, 113, 227));
        }

        private void ClearEditor()
        {
            current = null;
            profileList.ClearSelected();
            nameBox.Clear(); modelBox.Clear(); baseUrlBox.Clear(); keyBox.Clear(); usageUrlBox.Clear();
            ShowStatus("新建模型：请填写提供商、模型 ID、Base URL 和 API Key。", Color.FromArgb(110, 110, 115));
            nameBox.Focus();
        }

        private ProviderProfile SaveProfile(bool showMessage)
        {
            string name = nameBox.Text.Trim();
            string model = modelBox.Text.Trim();
            string baseUrl = baseUrlBox.Text.Trim().TrimEnd('/');
            string usageUrl = usageUrlBox.Text.Trim();
            Uri parsed;
            if (name.Length == 0 || model.Length == 0 || !Uri.TryCreate(baseUrl, UriKind.Absolute, out parsed) || (parsed.Scheme != "https" && parsed.Scheme != "http"))
                throw new InvalidOperationException("请填写提供商名称、模型 ID，以及有效的 HTTP/HTTPS Base URL。");
            if (usageUrl.Length > 0 && (!Uri.TryCreate(usageUrl, UriKind.Absolute, out parsed) || (parsed.Scheme != "https" && parsed.Scheme != "http")))
                throw new InvalidOperationException("用量 URL 不是有效的 HTTP/HTTPS 地址。");
            ProviderProfile profile = current ?? new ProviderProfile { Id = ProviderStore.NewId(name, model) };
            profile.Name = name; profile.Model = model; profile.BaseUrl = baseUrl; profile.UsageUrl = usageUrl;
            if (keyBox.Text.Trim().Length > 0) SecretStore.Save(profile.Id, keyBox.Text.Trim());
            if (!SecretStore.Exists(profile.Id)) throw new InvalidOperationException("新模型必须填写 API Key。");
            ProviderStore.Save(profile);
            current = profile;
            keyBox.Clear();
            LoadProfiles();
            for (int i = 0; i < profileList.Items.Count; i++) if (((ProviderProfile)profileList.Items[i]).Id == profile.Id) profileList.SelectedIndex = i;
            if (showMessage) ShowStatus("模型已保存，API Key 已加密。", Color.FromArgb(0, 145, 130));
            return profile;
        }

        private void TestProfile()
        {
            try
            {
                ProviderProfile profile = SaveProfile(false);
                Cursor = Cursors.WaitCursor;
                ShowStatus("正在测试模型接口…", Color.FromArgb(0, 113, 227));
                Application.DoEvents();
                GenericProviderApi.Test(profile, SecretStore.Load(profile.Id));
                ShowStatus("连接成功；提供商接受当前 API Key。", Color.FromArgb(0, 145, 130));
            }
            catch (Exception ex) { ShowError(ex); }
            finally { Cursor = Cursors.Default; }
        }

        private void ActivateProfile()
        {
            try
            {
                ProviderProfile profile = SaveProfile(false);
                SwitchResult result = switcher.ActivateCustom(profile);
                ShowStatus(result.Message, Color.FromArgb(0, 145, 130));
                if (CodexLauncher.IsRunning())
                    MessageBox.Show(this, "配置已切换。请从任务栏托盘完全退出 Codex 后再重新打开。", "需要重启 Codex", MessageBoxButtons.OK, MessageBoxIcon.Information);
                else CodexLauncher.Launch();
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void DeleteProfile()
        {
            if (current == null) return;
            if (MessageBox.Show(this, "删除模型 “" + current.Name + " / " + current.Model + "”？", "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            ProviderStore.Delete(current.Id);
            LoadProfiles();
            ClearEditor();
            ShowStatus("模型已删除。", Color.FromArgb(190, 45, 45));
        }

        private void ShowError(Exception ex) { ShowStatus(ex.Message, Color.FromArgb(190, 45, 45)); MessageBox.Show(this, ex.Message, "操作失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        private void ShowStatus(string text, Color color) { statusLabel.Text = text; statusLabel.ForeColor = color; }
    }

    internal sealed class UsageForm : Form
    {
        private readonly ListView list;
        private readonly Label refreshedLabel;
        private readonly System.Windows.Forms.Timer timer;
        private readonly Panel contentPanel;
        private bool refreshing;
        private float contentScale = 1F;
        private bool positioningContent;

        public UsageForm()
        {
            Text = "模型用量与限额";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(820, 540);
            MinimumSize = new Size(720, 500);
            BackColor = Color.FromArgb(245, 245, 247);
            Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScroll = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            contentPanel = new Panel { Size = new Size(820, 540), BackColor = Color.FromArgb(245, 245, 247) };
            Controls.Add(contentPanel);
            AutoScrollMinSize = contentPanel.Size;
            Resize += delegate { PositionContent(); };

            Label title = new Label { Text = "用量与限额", Font = new Font("Microsoft YaHei UI", 24F, FontStyle.Bold), ForeColor = Color.FromArgb(29, 29, 31), Location = new Point(32, 18), Size = new Size(400, 58), UseCompatibleTextRendering = true };
            contentPanel.Controls.Add(title);
            Label note = new Label { Text = "GPT 限额由官方 Codex App Server 读取；每 5 分钟自动刷新。", Font = new Font("Microsoft YaHei UI", 9F), ForeColor = Color.FromArgb(110, 110, 115), Location = new Point(35, 78), Size = new Size(650, 30), UseCompatibleTextRendering = true };
            contentPanel.Controls.Add(note);
            RoundedButton refresh = new RoundedButton { Text = "立即刷新", BackColor = Color.FromArgb(0, 113, 227), ForeColor = Color.White, Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold), Location = new Point(664, 36), Size = new Size(120, 40), Radius = 9, Cursor = Cursors.Hand };
            refresh.Click += delegate { RefreshUsage(); };
            contentPanel.Controls.Add(refresh);

            list = new ListView { Location = new Point(32, 112), Size = new Size(752, 360), View = View.Details, FullRowSelect = true, GridLines = false, BorderStyle = BorderStyle.None, Font = new Font("Microsoft YaHei UI", 9.5F) };
            list.Columns.Add("模型", 200); list.Columns.Add("提供商", 150); list.Columns.Add("余额 / 限额状态", 380);
            list.Resize += delegate { ResizeColumns(); };
            contentPanel.Controls.Add(list);
            refreshedLabel = new Label { Text = "尚未刷新", ForeColor = Color.FromArgb(110, 110, 115), Location = new Point(35, 490), Size = new Size(740, 28), UseCompatibleTextRendering = true };
            contentPanel.Controls.Add(refreshedLabel);
            timer = new System.Windows.Forms.Timer(); timer.Interval = 300000; timer.Tick += delegate { RefreshUsage(); }; timer.Start();
            LoadRows();
            Shown += delegate { ResizeColumns(); RefreshUsage(); };
            PositionContent();
        }

        private void PositionContent()
        {
            if (positioningContent) return;
            positioningContent = true;
            try
            {
                const float designWidth = 820F;
                const float designHeight = 540F;
                float targetScale = Math.Max(0.72F, Math.Min(ClientSize.Width / designWidth, ClientSize.Height / designHeight));
                if (Math.Abs(targetScale - contentScale) > 0.002F)
                {
                    float relative = targetScale / contentScale;
                    contentPanel.SuspendLayout();
                    contentPanel.Scale(new SizeF(relative, relative));
                    contentPanel.Size = new Size((int)Math.Round(designWidth * targetScale), (int)Math.Round(designHeight * targetScale));
                    contentPanel.ResumeLayout(true);
                    contentScale = targetScale;
                }
                ResizeColumns();
                contentPanel.Location = new Point(Math.Max(0, (ClientSize.Width - contentPanel.Width) / 2), Math.Max(0, (ClientSize.Height - contentPanel.Height) / 2));
                AutoScrollMinSize = contentPanel.Size;
            }
            finally { positioningContent = false; }
        }

        private void ResizeColumns()
        {
            if (list == null || list.Columns.Count != 3 || list.ClientSize.Width < 100) return;
            int width = list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4;
            list.Columns[0].Width = (int)(width * 0.26F);
            list.Columns[1].Width = (int)(width * 0.20F);
            list.Columns[2].Width = Math.Max(120, width - list.Columns[0].Width - list.Columns[1].Width);
        }

        private void LoadRows()
        {
            list.Items.Clear();
            list.Items.Add(new ListViewItem(new string[] { "GPT / OpenAI", "ChatGPT 账号", "等待刷新（官方 Codex App Server）" }) { Name = "openai" });
            list.Items.Add(new ListViewItem(new string[] { "DeepSeek Flash", "DeepSeek", SecretStore.Exists() ? "等待刷新" : "尚未配置 API Key" }) { Name = "deepseek-flash" });
            list.Items.Add(new ListViewItem(new string[] { "DeepSeek V4 Pro", "DeepSeek", SecretStore.Exists() ? "等待刷新" : "尚未配置 API Key" }) { Name = "deepseek-v4-pro" });
            foreach (ProviderProfile p in ProviderStore.Load())
                list.Items.Add(new ListViewItem(new string[] { p.Model, p.Name, p.UsageUrl.Length > 0 ? "等待刷新" : "提供商未配置用量接口" }) { Name = p.Id });
        }

        private void RefreshUsage()
        {
            if (refreshing) return;
            refreshing = true;
            refreshedLabel.Text = "正在读取提供商数据…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                Dictionary<string, string> values = new Dictionary<string, string>();
                try { values["openai"] = CodexAppServerUsage.Read().ToDisplayString(); }
                catch (Exception ex) { values["openai"] = "读取失败：" + ex.Message; }
                if (SecretStore.Exists())
                {
                    try { string balance = DeepSeekApi.GetBalance(SecretStore.Load("deepseek")); values["deepseek-flash"] = balance; values["deepseek-v4-pro"] = balance + "（共享账户）"; }
                    catch (Exception ex) { values["deepseek-flash"] = values["deepseek-v4-pro"] = "读取失败：" + ex.Message; }
                }
                foreach (ProviderProfile p in ProviderStore.Load())
                {
                    if (p.UsageUrl.Length == 0 || !SecretStore.Exists(p.Id)) continue;
                    try { values[p.Id] = GenericProviderApi.GetUsage(p, SecretStore.Load(p.Id)); }
                    catch (Exception ex) { values[p.Id] = "读取失败：" + ex.Message; }
                }
                if (IsDisposed) return;
                BeginInvoke((MethodInvoker)delegate
                {
                    foreach (KeyValuePair<string, string> item in values) if (list.Items.ContainsKey(item.Key)) list.Items[item.Key].SubItems[2].Text = item.Value;
                    refreshedLabel.Text = "上次刷新：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    refreshing = false;
                });
            });
        }

        protected override void Dispose(bool disposing) { if (disposing && timer != null) timer.Dispose(); base.Dispose(disposing); }
    }

    internal sealed class ChatGptLimitWindow
    {
        public int UsedPercent;
        public int DurationMinutes;
        public long ResetsAt;

        public string ToDisplayString()
        {
            int remaining = Math.Max(0, Math.Min(100, 100 - UsedPercent));
            string label = DurationMinutes == 300 ? "5小时" : DurationMinutes == 10080 ? "1周" : FormatDuration(DurationMinutes);
            DateTime reset = DateTimeOffset.FromUnixTimeSeconds(ResetsAt).LocalDateTime;
            string resetText = DurationMinutes <= 1440 ? reset.ToString("HH:mm") : reset.ToString("M月d日");
            return label + "剩余" + remaining + "%（" + resetText + "重置）";
        }

        private static string FormatDuration(int minutes)
        {
            if (minutes % 10080 == 0) return (minutes / 10080) + "周";
            if (minutes % 1440 == 0) return (minutes / 1440) + "天";
            if (minutes % 60 == 0) return (minutes / 60) + "小时";
            return minutes + "分钟";
        }
    }

    internal sealed class ChatGptUsageResult
    {
        public string PlanType = "";
        public ChatGptLimitWindow Primary;
        public ChatGptLimitWindow Secondary;

        public string ToDisplayString()
        {
            List<string> parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(PlanType)) parts.Add(char.ToUpperInvariant(PlanType[0]) + PlanType.Substring(1));
            if (Primary != null) parts.Add(Primary.ToDisplayString());
            if (Secondary != null) parts.Add(Secondary.ToDisplayString());
            return parts.Count > 0 ? string.Join(" · ", parts.ToArray()) : "登录有效，但未返回限额窗口";
        }
    }

    internal static class CodexAppServerUsage
    {
        public static ChatGptUsageResult Read()
        {
            string executable = FindCodexExecutable();
            if (string.IsNullOrWhiteSpace(executable)) throw new InvalidOperationException("未找到 Codex App Server");

            Process process = new Process();
            ManualResetEvent initReady = new ManualResetEvent(false);
            ManualResetEvent limitsReady = new ManualResetEvent(false);
            string initResponse = null;
            string limitsResponse = null;
            string lastError = null;
            try
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = "app-server",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (string.IsNullOrWhiteSpace(e.Data)) return;
                    if (Regex.IsMatch(e.Data, "\\\"id\\\"\\s*:\\s*0(?:[,}])")) { initResponse = e.Data; initReady.Set(); }
                    if (Regex.IsMatch(e.Data, "\\\"id\\\"\\s*:\\s*6(?:[,}])")) { limitsResponse = e.Data; limitsReady.Set(); }
                };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (!string.IsNullOrWhiteSpace(e.Data)) lastError = e.Data; };
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                WriteLine(process, "{\"method\":\"initialize\",\"id\":0,\"params\":{\"clientInfo\":{\"name\":\"codex_model_switcher\",\"title\":\"Codex Model Switcher\",\"version\":\"1.5.0\"}}}");
                if (!initReady.WaitOne(10000)) throw new TimeoutException("Codex 初始化超时" + ErrorSuffix(lastError));
                if (initResponse != null && initResponse.Contains("\"error\"")) throw new InvalidOperationException("Codex 初始化失败");
                WriteLine(process, "{\"method\":\"initialized\",\"params\":{}}");
                WriteLine(process, "{\"method\":\"account/rateLimits/read\",\"id\":6,\"params\":{}}");
                if (!limitsReady.WaitOne(15000)) throw new TimeoutException("GPT 限额读取超时" + ErrorSuffix(lastError));
                if (limitsResponse.Contains("\"error\"")) throw new InvalidOperationException("当前 Codex 登录无法读取 ChatGPT 限额");
                return Parse(limitsResponse);
            }
            finally
            {
                try { if (!process.HasExited) process.Kill(); } catch { }
                try { process.WaitForExit(2000); } catch { }
                process.Dispose();
                initReady.Dispose();
                limitsReady.Dispose();
            }
        }

        private static void WriteLine(Process process, string line)
        {
            process.StandardInput.WriteLine(line);
            process.StandardInput.Flush();
        }

        internal static ChatGptUsageResult Parse(string json)
        {
            ChatGptUsageResult result = new ChatGptUsageResult();
            Match plan = Regex.Match(json, "\\\"planType\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase);
            if (plan.Success) result.PlanType = plan.Groups[1].Value;
            result.Primary = ParseWindow(json, "primary");
            result.Secondary = ParseWindow(json, "secondary");
            if (result.Primary == null && result.Secondary == null) throw new InvalidOperationException("服务未返回 GPT 限额窗口");
            return result;
        }

        private static ChatGptLimitWindow ParseWindow(string json, string name)
        {
            Match block = Regex.Match(json, "\\\"" + name + "\\\"\\s*:\\s*\\{([^{}]*)\\}", RegexOptions.IgnoreCase);
            if (!block.Success) return null;
            Match used = Regex.Match(block.Groups[1].Value, "\\\"usedPercent\\\"\\s*:\\s*(\\d+)", RegexOptions.IgnoreCase);
            Match duration = Regex.Match(block.Groups[1].Value, "\\\"windowDurationMins\\\"\\s*:\\s*(\\d+)", RegexOptions.IgnoreCase);
            Match resets = Regex.Match(block.Groups[1].Value, "\\\"resetsAt\\\"\\s*:\\s*(\\d+)", RegexOptions.IgnoreCase);
            if (!used.Success || !duration.Success || !resets.Success) return null;
            return new ChatGptLimitWindow { UsedPercent = int.Parse(used.Groups[1].Value), DurationMinutes = int.Parse(duration.Groups[1].Value), ResetsAt = long.Parse(resets.Groups[1].Value) };
        }

        private static string FindCodexExecutable()
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
            if (Directory.Exists(root))
            {
                string[] files = Directory.GetFiles(root, "codex.exe", SearchOption.AllDirectories);
                string newest = null;
                DateTime newestTime = DateTime.MinValue;
                foreach (string file in files)
                {
                    DateTime time = File.GetLastWriteTimeUtc(file);
                    if (time > newestTime) { newest = file; newestTime = time; }
                }
                if (newest != null) return newest;
            }
            string pathValue = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string folder in pathValue.Split(Path.PathSeparator))
            {
                try { string candidate = Path.Combine(folder.Trim(), "codex.exe"); if (File.Exists(candidate)) return candidate; } catch { }
            }
            return null;
        }

        private static string ErrorSuffix(string error)
        {
            return string.IsNullOrWhiteSpace(error) ? "" : "：" + error.Trim();
        }
    }

    internal static class UiShapes
    {
        public static System.Drawing.Drawing2D.GraphicsPath RoundedRectangle(Rectangle rectangle, int radius)
        {
            int diameter = Math.Max(2, radius * 2);
            System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class BrandMark : Control
    {
        public BrandMark()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.Clear(Parent == null ? Color.FromArgb(245, 245, 247) : Parent.BackColor);
            float zoom = Math.Min(Width / 56F, Height / 56F);
            Rectangle box = new Rectangle((int)zoom, (int)zoom, Width - (int)(3 * zoom), Height - (int)(3 * zoom));
            using (System.Drawing.Drawing2D.GraphicsPath path = UiShapes.RoundedRectangle(box, Math.Max(4, (int)(16 * zoom))))
            using (System.Drawing.Drawing2D.LinearGradientBrush brush = new System.Drawing.Drawing2D.LinearGradientBrush(box, Color.FromArgb(35, 105, 255), Color.FromArgb(0, 178, 155), 40F))
                e.Graphics.FillPath(brush, path);

            using (Pen pen = new Pen(Color.White, 4F * zoom))
            {
                pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                Point[] left = new Point[] { new Point((int)(22 * zoom), (int)(16 * zoom)), new Point((int)(14 * zoom), (int)(28 * zoom)), new Point((int)(22 * zoom), (int)(40 * zoom)) };
                Point[] right = new Point[] { new Point((int)(34 * zoom), (int)(16 * zoom)), new Point((int)(42 * zoom), (int)(28 * zoom)), new Point((int)(34 * zoom), (int)(40 * zoom)) };
                e.Graphics.DrawLines(pen, left);
                e.Graphics.DrawLines(pen, right);
            }
        }
    }

    internal sealed class ProviderCard : Control
    {
        private bool hovering;
        public string Glyph { get; set; }
        public string TitleText { get; set; }
        public string DescriptionText { get; set; }
        public Color AccentColor { get; set; }

        public ProviderCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            TabStop = true;
            Glyph = "";
            TitleText = "";
            DescriptionText = "";
            AccentColor = Color.RoyalBlue;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hovering = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hovering = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            {
                OnClick(EventArgs.Empty);
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.Clear(Parent == null ? Color.FromArgb(245, 245, 247) : Parent.BackColor);
            float zoom = Width / 313F;
            int p2 = Math.Max(1, (int)(2 * zoom));
            int p4 = Math.Max(2, (int)(4 * zoom));
            int p5 = Math.Max(2, (int)(5 * zoom));
            int p7 = Math.Max(3, (int)(7 * zoom));
            int p8 = Math.Max(4, (int)(8 * zoom));
            int p9 = Math.Max(4, (int)(9 * zoom));
            int radius = Math.Max(8, (int)(22 * zoom));
            Rectangle shadow = new Rectangle(p4, p5, Width - p9, Height - p9);
            using (System.Drawing.Drawing2D.GraphicsPath shadowPath = UiShapes.RoundedRectangle(shadow, radius))
            using (Brush shadowBrush = new SolidBrush(Color.FromArgb(18, 0, 0, 0)))
                e.Graphics.FillPath(shadowBrush, shadowPath);

            Rectangle card = new Rectangle(p2, p2, Width - p7, Height - p8);
            using (System.Drawing.Drawing2D.GraphicsPath path = UiShapes.RoundedRectangle(card, radius))
            using (Brush background = new SolidBrush(hovering ? Color.FromArgb(250, 250, 252) : Color.White))
            using (Pen border = new Pen(hovering ? AccentColor : Color.FromArgb(229, 229, 234), (hovering ? 1.6F : 1F) * zoom))
            {
                e.Graphics.FillPath(background, path);
                e.Graphics.DrawPath(border, path);
            }

            if (hovering)
            {
                using (Brush wash = new SolidBrush(Color.FromArgb(12, AccentColor)))
                using (System.Drawing.Drawing2D.GraphicsPath washPath = UiShapes.RoundedRectangle(new Rectangle((int)(3 * zoom), (int)(3 * zoom), Width - p9, Height - (int)(10 * zoom)), Math.Max(8, (int)(21 * zoom))))
                    e.Graphics.FillPath(wash, washPath);
            }

            using (Brush badge = new SolidBrush(AccentColor))
                e.Graphics.FillEllipse(badge, (int)(22 * zoom), (int)(20 * zoom), (int)(48 * zoom), (int)(48 * zoom));
            using (Brush white = new SolidBrush(Color.White))
            using (Font glyphFont = new Font("Microsoft YaHei UI", (Glyph.Length > 1 ? 10.5F : 17F) * zoom, FontStyle.Bold))
            {
                SizeF glyphSize = e.Graphics.MeasureString(Glyph, glyphFont);
                e.Graphics.DrawString(Glyph, glyphFont, white, 46 * zoom - glyphSize.Width / 2F, 44 * zoom - glyphSize.Height / 2F);
            }

            using (Font titleFont = new Font("Microsoft YaHei UI", 12.5F * zoom, FontStyle.Bold))
                TextRenderer.DrawText(e.Graphics, TitleText, titleFont, new Rectangle((int)(22 * zoom), (int)(82 * zoom), Width - (int)(44 * zoom), (int)(38 * zoom)), Color.FromArgb(29, 29, 31), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            using (Font descriptionFont = new Font("Microsoft YaHei UI", 9F * zoom, FontStyle.Regular))
                TextRenderer.DrawText(e.Graphics, DescriptionText, descriptionFont, new Rectangle((int)(22 * zoom), (int)(124 * zoom), Width - (int)(44 * zoom), (int)(58 * zoom)), Color.FromArgb(110, 110, 115), TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);

            using (Font actionFont = new Font("Microsoft YaHei UI", 8.8F * zoom, FontStyle.Bold))
                TextRenderer.DrawText(e.Graphics, "切换并启动   →", actionFont, new Rectangle((int)(22 * zoom), (int)(192 * zoom), Width - (int)(44 * zoom), (int)(28 * zoom)), AccentColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            if (Focused)
            {
                Rectangle focus = new Rectangle(p8, p8, Width - (int)(17 * zoom), Height - (int)(17 * zoom));
                ControlPaint.DrawFocusRectangle(e.Graphics, focus, AccentColor, Color.White);
            }
        }
    }

    internal sealed class RoundedPanel : Panel
    {
        public int Radius { get; set; }
        public Color BorderColor { get; set; }

        public RoundedPanel()
        {
            Radius = 14;
            BorderColor = Color.LightGray;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.Clear(Parent == null ? Color.FromArgb(245, 245, 247) : Parent.BackColor);
            Rectangle shadow = new Rectangle(4, 5, Width - 9, Height - 9);
            using (System.Drawing.Drawing2D.GraphicsPath shadowPath = UiShapes.RoundedRectangle(shadow, Radius))
            using (Brush shadowBrush = new SolidBrush(Color.FromArgb(16, 0, 0, 0)))
                e.Graphics.FillPath(shadowBrush, shadowPath);
            Rectangle box = new Rectangle(1, 1, Width - 7, Height - 8);
            using (System.Drawing.Drawing2D.GraphicsPath path = UiShapes.RoundedRectangle(box, Radius))
            using (Brush brush = new SolidBrush(BackColor))
            using (Pen pen = new Pen(BorderColor, 1F))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }
        }
    }

    internal sealed class RoundedButton : Button
    {
        private bool hovering;
        public int Radius { get; set; }

        public RoundedButton()
        {
            Radius = 8;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { hovering = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovering = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.Clear(Parent == null ? Color.White : Parent.BackColor);
            Rectangle box = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill = hovering ? ControlPaint.Light(BackColor, 0.08F) : BackColor;
            using (System.Drawing.Drawing2D.GraphicsPath path = UiShapes.RoundedRectangle(box, Radius))
            using (Brush brush = new SolidBrush(fill))
                e.Graphics.FillPath(brush, path);
            TextRenderer.DrawText(e.Graphics, Text, Font, box, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused)
                ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(5, 5, Width - 11, Height - 11), ForeColor, fill);
        }
    }

    internal static class SecretStore
    {
        private static byte[] Entropy(string provider)
        {
            return Encoding.UTF8.GetBytes(string.Equals(provider, "deepseek", StringComparison.OrdinalIgnoreCase)
                ? "CodexModelSwitcher.DeepSeek.v1"
                : "CodexModelSwitcher.ProviderSecrets.v2");
        }

        private static string SecretPath(string provider)
        {
            if (!Regex.IsMatch(provider ?? "", "^[a-zA-Z0-9_-]{1,80}$")) throw new InvalidOperationException("未知的密钥名称。");
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CodexModelSwitcher", provider.ToLowerInvariant() + ".secret");
        }

        public static bool Exists() { return Exists("deepseek"); }
        public static bool Exists(string provider) { return File.Exists(SecretPath(provider)); }
        public static void Save(string secret) { Save("deepseek", secret); }

        public static void Save(string provider, string secret)
        {
            string path = SecretPath(provider);
            string dir = Path.GetDirectoryName(path);
            Directory.CreateDirectory(dir);
            byte[] plain = Encoding.UTF8.GetBytes(secret);
            byte[] encrypted = ProtectedData.Protect(plain, Entropy(provider), DataProtectionScope.CurrentUser);
            File.WriteAllBytes(path, encrypted);
            Array.Clear(plain, 0, plain.Length);
        }

        public static string Load(string provider)
        {
            byte[] encrypted = File.ReadAllBytes(SecretPath(provider));
            byte[] plain = ProtectedData.Unprotect(encrypted, Entropy(provider), DataProtectionScope.CurrentUser);
            try
            {
                return Encoding.UTF8.GetString(plain);
            }
            finally
            {
                Array.Clear(plain, 0, plain.Length);
            }
        }

        public static void Delete(string provider)
        {
            string path = SecretPath(provider);
            if (File.Exists(path)) File.Delete(path);
        }

    }

    internal sealed class ProviderProfile
    {
        public string Id = "";
        public string Name = "";
        public string Model = "";
        public string BaseUrl = "";
        public string UsageUrl = "";
        public override string ToString() { return Name + "  ·  " + Model; }
    }

    internal static class ProviderStore
    {
        private static string StorePath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CodexModelSwitcher", "providers.bin"); } }

        public static string NewId(string name, string model) { return "cms_" + Guid.NewGuid().ToString("N").Substring(0, 12); }

        public static List<ProviderProfile> Load()
        {
            List<ProviderProfile> profiles = new List<ProviderProfile>();
            if (!File.Exists(StorePath)) return profiles;
            using (FileStream stream = File.OpenRead(StorePath))
            using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8))
            {
                if (reader.ReadString() != "CMSP1") throw new InvalidOperationException("模型列表文件版本无法识别。");
                int count = reader.ReadInt32();
                if (count < 0 || count > 200) throw new InvalidOperationException("模型列表文件已损坏。");
                for (int i = 0; i < count; i++) profiles.Add(new ProviderProfile { Id = reader.ReadString(), Name = reader.ReadString(), Model = reader.ReadString(), BaseUrl = reader.ReadString(), UsageUrl = reader.ReadString() });
            }
            return profiles;
        }

        public static void Save(ProviderProfile profile)
        {
            List<ProviderProfile> profiles = Load();
            int index = profiles.FindIndex(delegate(ProviderProfile p) { return p.Id == profile.Id; });
            if (index >= 0) profiles[index] = profile; else profiles.Add(profile);
            Write(profiles);
        }

        public static void Delete(string id)
        {
            List<ProviderProfile> profiles = Load();
            profiles.RemoveAll(delegate(ProviderProfile p) { return p.Id == id; });
            Write(profiles);
            SecretStore.Delete(id);
        }

        private static void Write(List<ProviderProfile> profiles)
        {
            string path = StorePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            using (FileStream stream = File.Create(temp))
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write("CMSP1"); writer.Write(profiles.Count);
                foreach (ProviderProfile p in profiles) { writer.Write(p.Id); writer.Write(p.Name); writer.Write(p.Model); writer.Write(p.BaseUrl); writer.Write(p.UsageUrl ?? ""); }
            }
            if (File.Exists(path)) { string backup = path + ".old"; if (File.Exists(backup)) File.Delete(backup); File.Replace(temp, path, backup, true); File.Delete(backup); }
            else File.Move(temp, path);
        }
    }

    internal static class DeepSeekApi
    {
        public static void Test(string apiKey)
        {
            GetBalance(apiKey);
        }

        public static string GetBalance(string apiKey)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://api.deepseek.com/user/balance");
            request.Method = "GET";
            request.Timeout = 15000;
            request.ReadWriteTimeout = 15000;
            request.Headers[HttpRequestHeader.Authorization] = "Bearer " + apiKey;
            request.UserAgent = "CodexModelSwitcher/1.0";
            try
            {
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    if ((int)response.StatusCode < 200 || (int)response.StatusCode >= 300)
                        throw new InvalidOperationException("DeepSeek 返回 HTTP " + (int)response.StatusCode + "。");
                    string json;
                    using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) json = reader.ReadToEnd();
                    if (Regex.IsMatch(json, "\\\"is_available\\\"\\s*:\\s*false", RegexOptions.IgnoreCase)) return "账户余额不可用";
                    MatchCollection amounts = Regex.Matches(json, "\\\"total_balance\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"");
                    MatchCollection currencies = Regex.Matches(json, "\\\"currency\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"");
                    List<string> parts = new List<string>();
                    for (int i = 0; i < amounts.Count; i++) parts.Add(amounts[i].Groups[1].Value + (i < currencies.Count ? " " + currencies[i].Groups[1].Value : ""));
                    return parts.Count > 0 ? "可用余额：" + string.Join(" / ", parts.ToArray()) : "连接正常（接口未返回可显示余额）";
                }
            }
            catch (WebException ex)
            {
                HttpWebResponse response = ex.Response as HttpWebResponse;
                if (response != null)
                    throw new InvalidOperationException("DeepSeek 返回 HTTP " + (int)response.StatusCode + "，请检查 API Key 和账户状态。", ex);
                throw new InvalidOperationException("无法连接 DeepSeek API：" + ex.Message, ex);
            }
        }
    }

    internal static class GenericProviderApi
    {
        public static void Test(ProviderProfile profile, string apiKey)
        {
            string url = profile.BaseUrl.TrimEnd('/') + "/models";
            Get(url, apiKey, "模型列表");
        }

        public static string GetUsage(ProviderProfile profile, string apiKey)
        {
            if (string.IsNullOrWhiteSpace(profile.UsageUrl)) return "提供商未配置用量接口";
            string text = Regex.Replace(Get(profile.UsageUrl, apiKey, "用量接口"), "\\s+", " ").Trim();
            if (text.Length > 180) text = text.Substring(0, 177) + "…";
            return text;
        }

        private static string Get(string url, string apiKey, string label)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET"; request.Timeout = 15000; request.ReadWriteTimeout = 15000;
            request.Headers[HttpRequestHeader.Authorization] = "Bearer " + apiKey;
            request.UserAgent = "CodexModelSwitcher/1.3";
            try
            {
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                    return reader.ReadToEnd();
            }
            catch (WebException ex)
            {
                HttpWebResponse response = ex.Response as HttpWebResponse;
                if (response != null) throw new InvalidOperationException(label + "返回 HTTP " + (int)response.StatusCode + "。", ex);
                throw new InvalidOperationException("无法连接" + label + "：" + ex.Message, ex);
            }
        }
    }

    internal sealed class SwitchResult
    {
        public string Message;
        public string BackupPath;
    }

    internal sealed class SwitchState
    {
        public bool OriginalConfigExisted;
        public readonly List<string> OriginalAssignments = new List<string>();
        public string OriginalProviderSection = "";
    }

    internal sealed class Switcher
    {
        private const string BeginMarker = "# >>> Codex Model Switcher managed settings";
        private const string EndMarker = "# <<< Codex Model Switcher managed settings";
        private static readonly string[] ManagedKeys = new string[]
        {
            "model", "model_provider", "preferred_auth_method", "forced_login_method",
            "model_reasoning_effort", "web_search", "model_catalog_json"
        };

        private readonly string codexHome;
        private readonly string appData;
        private readonly string executablePath;
        private readonly bool allowNetwork;

        public Switcher()
            : this(GetCodexHome(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CodexModelSwitcher"), Application.ExecutablePath, true)
        {
        }

        internal Switcher(string codexHomePath, string appDataPath, string exePath, bool network)
        {
            codexHome = codexHomePath;
            appData = appDataPath;
            executablePath = exePath;
            allowNetwork = network;
        }

        private static string GetCodexHome()
        {
            string configured = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (!string.IsNullOrWhiteSpace(configured))
                return Path.GetFullPath(configured);
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        }

        private string ConfigPath { get { return Path.Combine(codexHome, "config.toml"); } }
        private string StatePath { get { return Path.Combine(appData, "switch-state.bin"); } }
        private string CatalogPath { get { return Path.Combine(appData, "deepseek-models.json"); } }
        private string BackupDirectory { get { return Path.Combine(appData, "backups"); } }

        public SwitchResult ActivateDeepSeek(string model)
        {
            if (model != "deepseek-flash" && model != "deepseek-v4-pro")
                throw new ArgumentException("不支持的 DeepSeek 模型。", "model");

            Directory.CreateDirectory(codexHome);
            Directory.CreateDirectory(appData);
            string current = File.Exists(ConfigPath) ? File.ReadAllText(ConfigPath, Encoding.UTF8) : "";
            string backup = Backup(current, File.Exists(ConfigPath));

            if (!File.Exists(StatePath))
            {
                SwitchState state = CaptureState(current, File.Exists(ConfigPath));
                SaveState(state);
            }

            EnsureCatalog();
            string clean = RemoveManagedBlock(current);
            clean = RemoveProviderSections(clean, null);
            clean = RemoveTopLevelAssignments(clean, null);

            string catalog = TomlString(CatalogPath.Replace('\\', '/'));
            StringBuilder managed = new StringBuilder();
            managed.AppendLine(BeginMarker);
            managed.AppendLine("model = " + TomlString(model));
            managed.AppendLine("model_provider = \"deepseek\"");
            managed.AppendLine("preferred_auth_method = \"apikey\"");
            managed.AppendLine("forced_login_method = \"api\"");
            managed.AppendLine("model_reasoning_effort = \"high\"");
            managed.AppendLine("web_search = \"disabled\"");
            managed.AppendLine("model_catalog_json = " + catalog);
            managed.AppendLine(EndMarker);
            managed.AppendLine();

            StringBuilder provider = new StringBuilder();
            provider.AppendLine();
            provider.AppendLine("[model_providers.deepseek]");
            provider.AppendLine("name = \"DeepSeek\"");
            provider.AppendLine("base_url = \"https://api.deepseek.com/\"");
            provider.AppendLine("wire_api = \"responses\"");
            provider.AppendLine();
            provider.AppendLine("[model_providers.deepseek.auth]");
            provider.AppendLine("command = " + TomlString(executablePath.Replace('\\', '/')));
            provider.AppendLine("args = [\"--print-secret\", \"deepseek\"]");
            provider.AppendLine("timeout_ms = 5000");
            provider.AppendLine("refresh_interval_ms = 0");

            string result = managed.ToString() + clean.TrimStart('\r', '\n') + provider.ToString();
            WriteAtomic(ConfigPath, Normalize(result).TrimEnd() + Environment.NewLine);
            return new SwitchResult { Message = "已切换到 " + DisplayName(model) + "。", BackupPath = backup };
        }

        public SwitchResult ActivateCustom(ProviderProfile profile)
        {
            if (profile == null || !Regex.IsMatch(profile.Id ?? "", "^cms_[a-zA-Z0-9_-]+$") || string.IsNullOrWhiteSpace(profile.Model) || string.IsNullOrWhiteSpace(profile.BaseUrl))
                throw new InvalidOperationException("导入模型配置无效。");
            if (allowNetwork && !SecretStore.Exists(profile.Id)) throw new InvalidOperationException("该模型尚未保存 API Key。");
            Directory.CreateDirectory(codexHome);
            Directory.CreateDirectory(appData);
            bool existed = File.Exists(ConfigPath);
            string currentConfig = existed ? File.ReadAllText(ConfigPath, Encoding.UTF8) : "";
            string backup = Backup(currentConfig, existed);
            if (!File.Exists(StatePath)) SaveState(CaptureState(currentConfig, existed));

            string catalogPath = EnsureCustomCatalog(profile);
            string clean = RemoveTopLevelAssignments(RemoveProviderSections(RemoveManagedBlock(currentConfig), null), null);
            StringBuilder managed = new StringBuilder();
            managed.AppendLine(BeginMarker);
            managed.AppendLine("model = " + TomlString(profile.Model));
            managed.AppendLine("model_provider = " + TomlString(profile.Id));
            managed.AppendLine("preferred_auth_method = \"apikey\"");
            managed.AppendLine("forced_login_method = \"api\"");
            managed.AppendLine("web_search = \"disabled\"");
            managed.AppendLine("model_catalog_json = " + TomlString(catalogPath.Replace('\\', '/')));
            managed.AppendLine(EndMarker);
            managed.AppendLine();

            StringBuilder provider = new StringBuilder();
            provider.AppendLine();
            provider.AppendLine("[model_providers." + profile.Id + "]");
            provider.AppendLine("name = " + TomlString(profile.Name));
            provider.AppendLine("base_url = " + TomlString(profile.BaseUrl.TrimEnd('/') + "/"));
            provider.AppendLine("wire_api = \"responses\"");
            provider.AppendLine();
            provider.AppendLine("[model_providers." + profile.Id + ".auth]");
            provider.AppendLine("command = " + TomlString(executablePath.Replace('\\', '/')));
            provider.AppendLine("args = [\"--print-secret\", " + TomlString(profile.Id) + "]");
            provider.AppendLine("timeout_ms = 5000");
            provider.AppendLine("refresh_interval_ms = 0");
            WriteAtomic(ConfigPath, Normalize(managed.ToString() + clean.TrimStart('\r', '\n') + provider.ToString()).TrimEnd() + Environment.NewLine);
            return new SwitchResult { Message = "已切换到 " + profile.Name + " / " + profile.Model + "。", BackupPath = backup };
        }

        public SwitchResult RestoreOpenAI()
        {
            Directory.CreateDirectory(codexHome);
            Directory.CreateDirectory(appData);
            bool exists = File.Exists(ConfigPath);
            string current = exists ? File.ReadAllText(ConfigPath, Encoding.UTF8) : "";
            bool looksDeepSeek = Regex.IsMatch(current, "(?m)^\\s*model_provider\\s*=\\s*[\\\"']deepseek[\\\"']") || current.Contains(BeginMarker);
            if (!File.Exists(StatePath) && !looksDeepSeek)
                return new SwitchResult { Message = "GPT / OpenAI 配置未改变，正在使用现有账号设置。", BackupPath = "" };

            string backup = Backup(current, exists);
            string clean = RemoveManagedBlock(current);
            clean = RemoveProviderSections(clean, null);
            clean = RemoveTopLevelAssignments(clean, null);

            List<string> restore = new List<string>();
            if (File.Exists(StatePath))
            {
                SwitchState state = LoadState();
                restore.AddRange(state.OriginalAssignments);
                if (!string.IsNullOrWhiteSpace(state.OriginalProviderSection))
                    clean = clean.TrimEnd() + Environment.NewLine + Environment.NewLine + state.OriginalProviderSection.Trim() + Environment.NewLine;
            }
            else
            {
                restore.Add("model_provider = \"openai\"");
            }

            StringBuilder output = new StringBuilder();
            foreach (string line in restore)
                output.AppendLine(line);
            if (restore.Count > 0)
                output.AppendLine();
            output.Append(clean.TrimStart('\r', '\n'));
            WriteAtomic(ConfigPath, Normalize(output.ToString()).TrimEnd() + Environment.NewLine);
            if (File.Exists(StatePath))
                File.Delete(StatePath);
            return new SwitchResult { Message = "已恢复 GPT / OpenAI 配置，ChatGPT 登录缓存保持不变。", BackupPath = backup };
        }

        private SwitchState CaptureState(string text, bool existed)
        {
            SwitchState state = new SwitchState();
            state.OriginalConfigExisted = existed;
            string normalized = Normalize(RemoveManagedBlock(text));
            List<string> assignments = new List<string>();
            RemoveTopLevelAssignments(normalized, assignments);
            state.OriginalAssignments.AddRange(assignments);
            StringBuilder provider = new StringBuilder();
            RemoveProviderSections(normalized, provider);
            state.OriginalProviderSection = provider.ToString();
            return state;
        }

        private string Backup(string content, bool existed)
        {
            Directory.CreateDirectory(BackupDirectory);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            string path = Path.Combine(BackupDirectory, existed ? "config-" + stamp + ".toml" : "config-" + stamp + "-did-not-exist.txt");
            File.WriteAllText(path, content, new UTF8Encoding(false));
            return path;
        }

        private void EnsureCatalog()
        {
            string json = null;
            if (allowNetwork)
            {
                try
                {
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                    using (WebClient client = new WebClient())
                    {
                        client.Headers[HttpRequestHeader.UserAgent] = "CodexModelSwitcher/1.0";
                        string script = client.DownloadString("https://cdn.deepseek.com/api-docs/codex-deepseek-setup-en.ps1");
                        string startToken = "$ModelsJson = @'";
                        int start = script.IndexOf(startToken, StringComparison.Ordinal);
                        if (start >= 0)
                        {
                            start += startToken.Length;
                            if (start < script.Length && script[start] == '\r') start++;
                            if (start < script.Length && script[start] == '\n') start++;
                            int end = script.IndexOf("\n'@", start, StringComparison.Ordinal);
                            if (end > start)
                                json = script.Substring(start, end - start).Trim();
                        }
                    }
                }
                catch
                {
                    json = null;
                }
            }

            if (string.IsNullOrWhiteSpace(json) || !json.Contains("\"deepseek-flash\"") || !json.Contains("\"deepseek-v4-pro\""))
                json = FallbackCatalog;
            WriteAtomic(CatalogPath, json.Trim() + Environment.NewLine);
        }

        private string EnsureCustomCatalog(ProviderProfile profile)
        {
            string path = Path.Combine(appData, "model-" + profile.Id + ".json");
            string json = "{\n  \"models\": [\n    {\n" +
                "      \"slug\": " + JsonString(profile.Model) + ",\n" +
                "      \"display_name\": " + JsonString(profile.Name + " · " + profile.Model) + ",\n" +
                "      \"description\": \"Imported Responses API compatible model\",\n" +
                "      \"prefer_websockets\": false,\n" +
                "      \"support_verbosity\": false,\n" +
                "      \"apply_patch_tool_type\": \"freeform\",\n" +
                "      \"web_search_tool_type\": \"text\",\n" +
                "      \"input_modalities\": [\"text\"],\n" +
                "      \"supports_parallel_tool_calls\": true,\n" +
                "      \"context_window\": 128000,\n" +
                "      \"max_context_window\": 128000,\n" +
                "      \"effective_context_window_percent\": 90,\n" +
                "      \"shell_type\": \"shell_command\",\n" +
                "      \"visibility\": \"list\",\n" +
                "      \"supported_in_api\": true,\n" +
                "      \"priority\": 1\n" +
                "    }\n  ]\n}";
            WriteAtomic(path, json + Environment.NewLine);
            return path;
        }

        private static string RemoveManagedBlock(string text)
        {
            string normalized = Normalize(text);
            int start = normalized.IndexOf(BeginMarker, StringComparison.Ordinal);
            while (start >= 0)
            {
                int end = normalized.IndexOf(EndMarker, start, StringComparison.Ordinal);
                if (end < 0)
                    throw new InvalidOperationException("检测到不完整的模型启动器配置标记；为避免损坏配置，操作已停止。");
                end += EndMarker.Length;
                if (end < normalized.Length && normalized[end] == '\n') end++;
                normalized = normalized.Remove(start, end - start);
                start = normalized.IndexOf(BeginMarker, StringComparison.Ordinal);
            }
            return normalized;
        }

        private static string RemoveTopLevelAssignments(string text, List<string> captured)
        {
            string[] lines = Normalize(text).Split('\n');
            StringBuilder output = new StringBuilder();
            bool topLevel = true;
            foreach (string raw in lines)
            {
                string trimmed = raw.Trim();
                if (trimmed.StartsWith("[", StringComparison.Ordinal))
                    topLevel = false;
                bool managed = false;
                if (topLevel && !trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    foreach (string key in ManagedKeys)
                    {
                        if (Regex.IsMatch(raw, "^\\s*" + Regex.Escape(key) + "\\s*="))
                        {
                            managed = true;
                            if (captured != null)
                                captured.Add(raw.Trim());
                            break;
                        }
                    }
                }
                if (!managed)
                    output.AppendLine(raw);
            }
            return output.ToString().TrimEnd('\r', '\n') + Environment.NewLine;
        }

        private static string RemoveProviderSections(string text, StringBuilder captured)
        {
            string[] lines = Normalize(text).Split('\n');
            StringBuilder output = new StringBuilder();
            bool skip = false;
            foreach (string raw in lines)
            {
                string trimmed = raw.Trim();
                if (trimmed.StartsWith("[", StringComparison.Ordinal))
                {
                    string header = trimmed.Trim('[', ']', ' ', '\t', '\r');
                    bool managedSection = header.Equals("model_providers.deepseek", StringComparison.OrdinalIgnoreCase)
                        || header.StartsWith("model_providers.deepseek.", StringComparison.OrdinalIgnoreCase)
                        || header.StartsWith("model_providers.cms_", StringComparison.OrdinalIgnoreCase);
                    skip = managedSection;
                }
                if (skip)
                {
                    if (captured != null)
                        captured.AppendLine(raw);
                }
                else
                {
                    output.AppendLine(raw);
                }
            }
            return output.ToString().TrimEnd('\r', '\n') + Environment.NewLine;
        }

        private void SaveState(SwitchState state)
        {
            Directory.CreateDirectory(appData);
            using (FileStream stream = File.Create(StatePath))
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write("CMS1");
                writer.Write(state.OriginalConfigExisted);
                writer.Write(state.OriginalAssignments.Count);
                foreach (string line in state.OriginalAssignments)
                    writer.Write(line);
                writer.Write(state.OriginalProviderSection ?? "");
            }
        }

        private SwitchState LoadState()
        {
            using (FileStream stream = File.OpenRead(StatePath))
            using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8))
            {
                if (reader.ReadString() != "CMS1")
                    throw new InvalidOperationException("切换状态文件版本无法识别。");
                SwitchState state = new SwitchState();
                state.OriginalConfigExisted = reader.ReadBoolean();
                int count = reader.ReadInt32();
                if (count < 0 || count > 100)
                    throw new InvalidOperationException("切换状态文件已损坏。");
                for (int i = 0; i < count; i++)
                    state.OriginalAssignments.Add(reader.ReadString());
                state.OriginalProviderSection = reader.ReadString();
                return state;
            }
        }

        private static void WriteAtomic(string path, string content)
        {
            string dir = Path.GetDirectoryName(path);
            Directory.CreateDirectory(dir);
            string temp = path + ".cms-tmp";
            File.WriteAllText(temp, content, new UTF8Encoding(false));
            if (File.Exists(path))
            {
                string old = path + ".cms-old";
                if (File.Exists(old)) File.Delete(old);
                File.Replace(temp, path, old, true);
                File.Delete(old);
            }
            else
            {
                File.Move(temp, path);
            }
        }

        private static string DisplayName(string model)
        {
            return model == "deepseek-v4-pro" ? "DeepSeek V4 Pro" : "DeepSeek Flash";
        }

        private static string Normalize(string text)
        {
            return (text ?? "").Replace("\r\n", "\n").Replace("\r", "\n");
        }

        private static string TomlString(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static string JsonString(string value)
        {
            return "\"" + (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
        }

        private const string FallbackCatalog = @"{
  ""models"": [
    {
      ""slug"": ""deepseek-flash"",
      ""display_name"": ""DeepSeek-Flash"",
      ""description"": ""Latest frontier agentic coding model with image input."",
      ""prefer_websockets"": false,
      ""support_verbosity"": true,
      ""default_verbosity"": ""low"",
      ""apply_patch_tool_type"": ""freeform"",
      ""web_search_tool_type"": ""text"",
      ""input_modalities"": [""text"", ""image""],
      ""supports_image_detail_original"": true,
      ""supports_parallel_tool_calls"": true,
      ""context_window"": 1048576,
      ""max_context_window"": 1048576,
      ""effective_context_window_percent"": 95,
      ""default_reasoning_level"": ""high"",
      ""supported_reasoning_levels"": [
        {""effort"": ""low"", ""description"": ""Fast responses with lighter reasoning""},
        {""effort"": ""high"", ""description"": ""Extra high reasoning depth for complex problems""},
        {""effort"": ""max"", ""description"": ""Maximum reasoning depth for the hardest problems""}
      ],
      ""shell_type"": ""shell_command"",
      ""visibility"": ""list"",
      ""minimal_client_version"": ""0.144.0"",
      ""supported_in_api"": true,
      ""priority"": 1
    },
    {
      ""slug"": ""deepseek-v4-pro"",
      ""display_name"": ""DeepSeek-V4-Pro"",
      ""description"": ""DeepSeek reasoning model for complex agentic coding tasks."",
      ""prefer_websockets"": false,
      ""support_verbosity"": true,
      ""default_verbosity"": ""low"",
      ""apply_patch_tool_type"": ""freeform"",
      ""web_search_tool_type"": ""text"",
      ""input_modalities"": [""text""],
      ""supports_parallel_tool_calls"": true,
      ""context_window"": 1048576,
      ""max_context_window"": 1048576,
      ""effective_context_window_percent"": 95,
      ""default_reasoning_level"": ""high"",
      ""supported_reasoning_levels"": [
        {""effort"": ""low"", ""description"": ""Fast responses with lighter reasoning""},
        {""effort"": ""high"", ""description"": ""Extra high reasoning depth for complex problems""},
        {""effort"": ""max"", ""description"": ""Maximum reasoning depth for the hardest problems""}
      ],
      ""shell_type"": ""shell_command"",
      ""visibility"": ""list"",
      ""minimal_client_version"": ""0.144.0"",
      ""supported_in_api"": true,
      ""priority"": 2
    }
  ]
}";
    }

    internal static class CodexLauncher
    {
        public static bool IsRunning()
        {
            return Process.GetProcessesByName("ChatGPT").Length > 0;
        }

        public static void Launch()
        {
            string appId = FindStartAppId();
            if (!string.IsNullOrWhiteSpace(appId))
            {
                LaunchAppId(appId.Trim());
                return;
            }

            try
            {
                // Current Microsoft Store package family used by Codex on Windows.
                LaunchAppId("OpenAI.Codex_2p2nqsd0c76g!App");
                return;
            }
            catch
            {
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = "chatgpt://", UseShellExecute = true });
                    return;
                }
                catch
                {
                    throw new InvalidOperationException("没有在开始菜单中找到 ChatGPT/Codex。请先安装并至少启动一次桌面应用。", null);
                }
            }
        }

        private static void LaunchAppId(string appId)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "\"shell:AppsFolder\\" + appId + "\"",
                UseShellExecute = true
            });
        }

        private static string FindStartAppId()
        {
            string ps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            if (!File.Exists(ps)) return "";
            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = ps;
            info.Arguments = "-NoProfile -NonInteractive -Command \"Get-StartApps | Where-Object { $_.Name -match 'ChatGPT|Codex' } | Select-Object -First 1 -ExpandProperty AppID\"";
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            using (Process process = Process.Start(info))
            {
                string result = process.StandardOutput.ReadToEnd();
                process.WaitForExit(5000);
                return process.ExitCode == 0 ? result.Trim() : "";
            }
        }
    }

    internal static class SelfTest
    {
        public static void Run()
        {
            string root = Path.Combine(Path.GetTempPath(), "CodexModelSwitcher-Test-" + Guid.NewGuid().ToString("N"));
            string codex = Path.Combine(root, ".codex");
            string data = Path.Combine(root, "data");
            Directory.CreateDirectory(codex);
            string original = "model = \"gpt-test\"\r\nmodel_reasoning_effort = \"medium\"\r\n\r\n[mcp_servers.demo]\r\ncommand = \"demo\"\r\n";
            File.WriteAllText(Path.Combine(codex, "config.toml"), original, new UTF8Encoding(false));
            try
            {
                Switcher switcher = new Switcher(codex, data, @"C:\\Tools\\CodexModelSwitcher.exe", false);
                switcher.ActivateDeepSeek("deepseek-flash");
                string active = File.ReadAllText(Path.Combine(codex, "config.toml"));
                Assert(active.Contains("model = \"deepseek-flash\""), "DeepSeek model missing");
                Assert(active.Contains("[mcp_servers.demo]"), "Unrelated MCP config was lost");
                Assert(!active.Contains("experimental_bearer_token"), "Secret must not be stored in config");
                Assert(active.Contains("[model_providers.deepseek.auth]"), "Command auth missing");

                switcher.RestoreOpenAI();
                string restored = File.ReadAllText(Path.Combine(codex, "config.toml"));
                Assert(restored.Contains("model = \"gpt-test\""), "Original model was not restored");
                Assert(restored.Contains("model_reasoning_effort = \"medium\""), "Original reasoning setting was not restored");
                Assert(restored.Contains("[mcp_servers.demo]"), "MCP config was not preserved after restore");
                Assert(!restored.Contains("model_providers.deepseek"), "DeepSeek provider remained after restore");

                ProviderProfile custom = new ProviderProfile { Id = "cms_test", Name = "Example Provider", Model = "example-coder", BaseUrl = "https://api.example.com/v1", UsageUrl = "" };
                switcher.ActivateCustom(custom);
                string imported = File.ReadAllText(Path.Combine(codex, "config.toml"));
                Assert(imported.Contains("model = \"example-coder\""), "Imported model missing");
                Assert(imported.Contains("[model_providers.cms_test]"), "Imported provider missing");
                Assert(imported.Contains("args = [\"--print-secret\", \"cms_test\"]"), "Imported provider command auth missing");
                Assert(imported.Contains("[mcp_servers.demo]"), "MCP config was lost after imported model switch");
                switcher.RestoreOpenAI();
                string customRestored = File.ReadAllText(Path.Combine(codex, "config.toml"));
                Assert(!customRestored.Contains("model_providers.cms_test"), "Imported provider remained after restore");
                Assert(customRestored.Contains("model = \"gpt-test\""), "Original model was not restored after imported provider");

                string sampleLimits = "{\"id\":6,\"result\":{\"rateLimits\":{\"primary\":{\"usedPercent\":69,\"windowDurationMins\":300,\"resetsAt\":1789298460},\"secondary\":{\"usedPercent\":11,\"windowDurationMins\":10080,\"resetsAt\":1789894900},\"planType\":\"plus\"}}}";
                ChatGptUsageResult parsedLimits = CodexAppServerUsage.Parse(sampleLimits);
                Assert(parsedLimits.PlanType == "plus", "ChatGPT plan type was not parsed");
                Assert(parsedLimits.Primary != null && parsedLimits.Primary.UsedPercent == 69 && parsedLimits.Primary.DurationMinutes == 300, "Primary ChatGPT limit was not parsed");
                Assert(parsedLimits.Secondary != null && parsedLimits.Secondary.UsedPercent == 11 && parsedLimits.Secondary.DurationMinutes == 10080, "Secondary ChatGPT limit was not parsed");
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        private static void Assert(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }
    }
}
