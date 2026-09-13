using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
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
        internal const string Version = "1.7";

        [DllImport("shcore.dll")]
        private static extern int SetProcessDpiAwareness(int awareness);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr awarenessContext);

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

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
            {
                Log.Error("界面线程发生未处理异常", e.Exception);
                ShowFailure(e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                Log.Error("发生未处理异常", e.ExceptionObject as Exception);
            };
            Log.Info("启动 Codex 模型启动器 v" + Version + "，进程 " + Process.GetCurrentProcess().Id);

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
            // The bundled manifest declares PerMonitorV2; these calls only matter for source-only
            // builds compiled without it. Never fall back to system-DPI awareness.
            try
            {
                if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; // PER_MONITOR_AWARE_V2
            }
            catch
            {
            }
            try { SetProcessDpiAwareness(2); } catch { }
        }

        private static void ShowFailure(Exception error)
        {
            try
            {
                MessageBox.Show(
                    "程序遇到了未预料的问题，但已被拦截，没有直接退出。\r\n\r\n" +
                    (error == null ? "" : error.Message + "\r\n\r\n") +
                    "日志文件：" + Log.CurrentFilePath,
                    "Codex 模型启动器", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch
            {
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
        private readonly Label statusLabel;
        private readonly Label hintLabel;
        private readonly Switcher switcher;
        private readonly Panel contentPanel;
        private readonly FlowLayoutPanel cardsPanel;
        private float contentScale = 1F;
        private bool positioningContent;

        public MainForm()
        {
            Text = "Codex 模型启动器";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1080, 500);
            MinimumSize = new Size(900, 460);
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
            contentPanel.Size = new Size(1080, 500);
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

            RoundedButton importButton = SmallButton("模型配置", Ink, 120);
            importButton.Location = new Point(920, 43);
            importButton.Click += delegate { OpenModelSettings(); };
            contentPanel.Controls.Add(importButton);

            cardsPanel = new FlowLayoutPanel();
            cardsPanel.Location = new Point(40, 126);
            cardsPanel.Size = new Size(1000, 232);
            cardsPanel.BackColor = Canvas;
            cardsPanel.WrapContents = false;
            cardsPanel.AutoScroll = true;
            cardsPanel.Padding = new Padding(0);
            cardsPanel.Margin = new Padding(0);
            contentPanel.Controls.Add(cardsPanel);
            BuildCards();

            hintLabel = NewLabel("", 8.7F, FontStyle.Regular, Muted);
            hintLabel.Location = new Point(44, 384);
            hintLabel.Size = new Size(900, 30);
            hintLabel.TextAlign = ContentAlignment.MiddleLeft;
            contentPanel.Controls.Add(hintLabel);

            statusLabel = NewLabel("就绪 · 请选择一个模型", 9.5F, FontStyle.Bold, Muted);
            statusLabel.Location = new Point(44, 420);
            statusLabel.Size = new Size(700, 42);
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            contentPanel.Controls.Add(statusLabel);

            RoundedButton logButton = SmallButton("日志", Color.FromArgb(99, 99, 102), 84);
            logButton.Location = new Point(766, 421);
            logButton.Click += delegate { OpenLogFolder(); };
            contentPanel.Controls.Add(logButton);

            RoundedButton openButton = SmallButton("打开 Codex  →", Ink, 174);
            openButton.Location = new Point(866, 421);
            openButton.Click += delegate { OpenCodex(); };
            contentPanel.Controls.Add(openButton);

            UpdateKeyHint();
            PositionContent();
            CheckAuthCommandPath();
            RefreshCatalogInBackground();
        }

        private void PositionContent()
        {
            if (positioningContent) return;
            positioningContent = true;
            try
            {
                const float designWidth = 1080F;
                const float designHeight = 500F;
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
            card.Size = new Size(300, 230);
            card.Margin = new Padding(0, 0, 18, 0);
            card.Glyph = glyph;
            card.TitleText = title;
            card.DescriptionText = description;
            card.AccentColor = accent;
            card.Click += click;
            return card;
        }

        /// <summary>Cards follow the DeepSeek model catalog, so new official models show up on their own.</summary>
        private void BuildCards()
        {
            if (cardsPanel == null) return;
            cardsPanel.SuspendLayout();
            try
            {
                for (int i = cardsPanel.Controls.Count - 1; i >= 0; i--)
                {
                    Control control = cardsPanel.Controls[i];
                    cardsPanel.Controls.RemoveAt(i);
                    control.Dispose();
                }
                cardsPanel.Controls.Add(CreateCard("G", "GPT / OpenAI", "使用现有 ChatGPT 账号\n恢复原来的 Codex 配置", Blue, delegate { ActivateOpenAI(); }));
                List<ModelOption> options = switcher.LoadModelOptions();
                for (int i = 0; i < options.Count; i++)
                {
                    ModelOption option = options[i];
                    Color accent = i == 0 ? Teal : Color.FromArgb(103, 78, 190);
                    string glyph = i == 0 ? "D" : i == 1 ? "D+" : "D" + (i + 1).ToString(CultureInfo.InvariantCulture);
                    cardsPanel.Controls.Add(CreateCard(glyph, option.DisplayName, option.Description, accent, ActivateHandler(option.Slug)));
                }
            }
            finally
            {
                cardsPanel.ResumeLayout(true);
            }
        }

        private EventHandler ActivateHandler(string slug)
        {
            return delegate { ActivateDeepSeek(slug); };
        }

        /// <summary>
        /// Codex fetches the API key by running this program, so moving or renaming the executable
        /// silently breaks DeepSeek until the recorded path is refreshed.
        /// </summary>
        private void CheckAuthCommandPath()
        {
            try
            {
                if (switcher.DetectStaleAuthCommand() == null) return;
                if (switcher.RepairAuthCommand())
                    SetStatus("检测到程序被移动过，已自动修复 Codex 的取密钥路径", Blue);
            }
            catch (Exception ex)
            {
                Log.Warn("修复取密钥路径失败", ex);
            }
        }

        private void RefreshCatalogInBackground()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string before = string.Join("|", ModelSlugs());
                    switcher.RefreshCatalog();
                    string after = string.Join("|", ModelSlugs());
                    if (before == after || IsDisposed) return;
                    BeginInvoke((MethodInvoker)delegate
                    {
                        BuildCards();
                        SetStatus("已从 DeepSeek 官方目录更新模型列表", Blue);
                    });
                }
                catch (Exception ex)
                {
                    Log.Warn("后台刷新模型目录失败", ex);
                }
            });
        }

        private string[] ModelSlugs()
        {
            List<string> slugs = new List<string>();
            foreach (ModelOption option in switcher.LoadModelOptions()) slugs.Add(option.Slug);
            return slugs.ToArray();
        }

        private void OpenLogFolder()
        {
            try
            {
                Directory.CreateDirectory(Log.DirectoryPath);
                Process.Start(new ProcessStartInfo { FileName = Log.DirectoryPath, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ShowError("无法打开日志目录", ex);
            }
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

        /// <summary>Opens the model/key settings dialog and refreshes the summary afterwards.</summary>
        private void OpenModelSettings()
        {
            using (ModelManagerForm form = new ModelManagerForm(switcher)) form.ShowDialog(this);
            UpdateKeyHint();
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
                MessageBox.Show(this, "还没有配置 DeepSeek API Key。\r\n\r\n现在打开「模型配置」填写吗？", "需要 API Key", MessageBoxButtons.OK, MessageBoxIcon.Information);
                OpenModelSettings();
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
            if (!CodexLauncher.IsRunning())
            {
                OpenCodex();
                return;
            }
            RestartCodex();
        }

        /// <summary>Offers to restart Codex so the freshly written provider config is picked up.</summary>
        private void RestartCodex()
        {
            DialogResult answer = MessageBox.Show(this,
                "配置已切换。Codex 需要完全退出后重新打开才会读取新配置。\r\n\r\n" +
                "Codex 关闭后通常会继续驻留在托盘，所以启动器会先请它正常退出，若它仍驻留则直接结束它的进程。\r\n\r\n" +
                "现在重启 Codex 吗？（Codex 中尚未发送的内容可能会丢失）",
                "重启 Codex", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes)
            {
                SetStatus("配置已切换 · 请从任务栏托盘完全退出 Codex 后再打开", Blue);
                return;
            }

            Cursor = Cursors.WaitCursor;
            SetStatus("正在请 Codex 正常退出…", Blue);
            Application.DoEvents();
            try
            {
                if (!CodexLauncher.TryCloseAll(5000))
                {
                    SetStatus("Codex 仍在托盘中驻留，正在结束它的进程…", Blue);
                    Application.DoEvents();
                    CodexLauncher.KillAll();
                    CodexLauncher.WaitUntilStopped(6000);
                }
                else
                {
                    CodexLauncher.WaitUntilStopped(3000);
                }
                OpenCodex();
                SetStatus("已重启 Codex · 新配置已生效", Teal);
            }
            catch (Exception ex)
            {
                ShowError("重启 Codex 失败", ex);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
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

        /// <summary>One-line summary of the DeepSeek credential; the editor now lives in the settings dialog.</summary>
        private void UpdateKeyHint()
        {
            bool configured = SecretStore.Exists();
            hintLabel.Text = configured
                ? "DeepSeek 密钥：已配置（Windows 当前用户加密保存）· 在「模型配置」中管理"
                : "DeepSeek 密钥：尚未配置 · 点击右上角「模型配置」填写";
            hintLabel.ForeColor = configured ? Teal : Color.FromArgb(190, 45, 45);
        }

        private void SetStatus(string text, Color color)
        {
            statusLabel.Text = text;
            statusLabel.ForeColor = color;
        }

        private void ShowError(string title, Exception ex)
        {
            Log.Error(title, ex);
            SetStatus(title, Color.Firebrick);
            MessageBox.Show(this, ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private const int WmDpiChanged = 0x02E0;

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg != WmDpiChanged) return;
            try
            {
                ResetContentScale();
                PositionContent();
                Log.Info("显示器 DPI 变化，界面已重新排布");
            }
            catch (Exception ex)
            {
                Log.Warn("DPI 变化后重新布局失败", ex);
            }
        }

        /// <summary>Undoes accumulated manual scaling so the panel can be fitted to the new DPI.</summary>
        private void ResetContentScale()
        {
            if (contentScale <= 0.01F) { contentScale = 1F; return; }
            if (Math.Abs(contentScale - 1F) > 0.002F)
            {
                float inverse = 1F / contentScale;
                contentPanel.SuspendLayout();
                contentPanel.Scale(new SizeF(inverse, inverse));
                contentPanel.ResumeLayout(true);
            }
            contentPanel.Size = new Size(1080, 500);
            contentScale = 1F;
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
        private readonly TextBox deepSeekKeyBox;
        private readonly Label deepSeekStatusLabel;
        private readonly Panel contentPanel;
        private ProviderProfile current;
        private float contentScale = 1F;
        private bool positioningContent;

        public ModelManagerForm(Switcher modelSwitcher)
        {
            switcher = modelSwitcher;
            Text = "模型配置";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(900, 780);
            MinimumSize = new Size(820, 660);
            BackColor = Color.FromArgb(245, 245, 247);
            Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScroll = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            contentPanel = new Panel { Size = new Size(900, 780), BackColor = Color.FromArgb(245, 245, 247) };
            Controls.Add(contentPanel);
            AutoScrollMinSize = contentPanel.Size;
            Resize += delegate { PositionContent(); };

            contentPanel.Controls.Add(LabelAt("模型配置", 28F, FontStyle.Bold, Color.FromArgb(29, 29, 31), 32, 18, 600, 62));
            contentPanel.Controls.Add(LabelAt("DeepSeek 密钥，以及任何兼容 Responses API 的模型提供商", 9.5F, FontStyle.Regular, Color.FromArgb(110, 110, 115), 35, 82, 700, 30));

            RoundedPanel deepseek = PanelAt(30, 118, 840, 150);
            contentPanel.Controls.Add(deepseek);
            deepseek.Controls.Add(LabelAt("DeepSeek API Key", 12F, FontStyle.Bold, Color.FromArgb(29, 29, 31), 24, 12, 300, 32));
            deepSeekStatusLabel = LabelAt("", 8.6F, FontStyle.Bold, Color.FromArgb(110, 110, 115), 24, 44, 500, 26);
            deepseek.Controls.Add(deepSeekStatusLabel);
            deepSeekKeyBox = InputAt(deepseek, 24, 74, 470);
            deepSeekKeyBox.UseSystemPasswordChar = true;
            deepSeekKeyBox.Font = new Font("Consolas", 10F);
            RoundedButton saveKeyButton = ButtonAt("安全保存", Color.FromArgb(0, 113, 227), 510, 69, 130);
            saveKeyButton.Click += delegate { SaveDeepSeekKey(); };
            deepseek.Controls.Add(saveKeyButton);
            RoundedButton testKeyButton = ButtonAt("测试连接", Color.FromArgb(73, 73, 78), 652, 69, 130);
            testKeyButton.Click += delegate { TestDeepSeekKey(); };
            deepseek.Controls.Add(testKeyButton);
            deepseek.Controls.Add(LabelAt("密钥使用 Windows DPAPI 加密保存，不会以明文写入 Codex 配置。", 8.3F, FontStyle.Regular, Color.FromArgb(110, 110, 115), 24, 116, 780, 24));

            RoundedPanel left = PanelAt(30, 290, 250, 445);
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

            RoundedPanel editor = PanelAt(300, 290, 570, 445);
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

            statusLabel = LabelAt("DeepSeek 密钥与模型都会保存到当前 Windows 用户的加密配置中。", 8.8F, FontStyle.Bold, Color.FromArgb(110, 110, 115), 35, 745, 820, 30);
            contentPanel.Controls.Add(statusLabel);
            LoadProfiles();
            UpdateDeepSeekStatus();
            PositionContent();
        }

        private void PositionContent()
        {
            if (positioningContent) return;
            positioningContent = true;
            try
            {
                const float designWidth = 900F;
                const float designHeight = 780F;
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

        private void SaveDeepSeekKey()
        {
            string key = deepSeekKeyBox.Text.Trim();
            if (!key.StartsWith("sk-", StringComparison.Ordinal))
            {
                ShowStatus("DeepSeek API Key 应以 sk- 开头。", Color.FromArgb(190, 45, 45));
                return;
            }
            try
            {
                SecretStore.Save(key);
                deepSeekKeyBox.Clear();
                UpdateDeepSeekStatus();
                ShowStatus("DeepSeek API Key 已加密保存。", Color.FromArgb(0, 145, 130));
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        private void TestDeepSeekKey()
        {
            if (!SecretStore.Exists())
            {
                ShowStatus("请先保存 DeepSeek API Key。", Color.FromArgb(190, 45, 45));
                return;
            }
            Cursor = Cursors.WaitCursor;
            ShowStatus("正在测试 DeepSeek API…", Color.FromArgb(0, 113, 227));
            Application.DoEvents();
            try
            {
                string balance = DeepSeekApi.GetBalance(SecretStore.Load("deepseek"));
                ShowStatus("DeepSeek 连接成功 · " + balance, Color.FromArgb(0, 145, 130));
            }
            catch (Exception ex)
            {
                ShowStatus("连接测试失败：" + ex.Message, Color.FromArgb(190, 45, 45));
                Log.Warn("DeepSeek 连接测试失败", ex);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void UpdateDeepSeekStatus()
        {
            bool configured = SecretStore.Exists();
            deepSeekStatusLabel.Text = configured ? "已配置密钥（Windows 当前用户加密保存）" : "尚未配置密钥";
            deepSeekStatusLabel.ForeColor = configured ? Color.FromArgb(0, 145, 130) : Color.FromArgb(190, 45, 45);
        }

        private const int WmDpiChanged = 0x02E0;

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg != WmDpiChanged) return;
            try
            {
                ResetContentScale();
                PositionContent();
            }
            catch (Exception ex)
            {
                Log.Warn("DPI 变化后重新布局失败", ex);
            }
        }

        private void ResetContentScale()
        {
            if (contentScale <= 0.01F) { contentScale = 1F; return; }
            if (Math.Abs(contentScale - 1F) > 0.002F)
            {
                float inverse = 1F / contentScale;
                contentPanel.SuspendLayout();
                contentPanel.Scale(new SizeF(inverse, inverse));
                contentPanel.ResumeLayout(true);
            }
            contentPanel.Size = new Size(900, 780);
            contentScale = 1F;
        }
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
        private readonly Switcher switcher = new Switcher();
        private readonly Dictionary<string, string> lastGoodValues = new Dictionary<string, string>();

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
            List<ModelOption> options = switcher.LoadModelOptions();
            foreach (ModelOption option in options)
                list.Items.Add(new ListViewItem(new string[] { option.DisplayName, "DeepSeek", SecretStore.Exists() ? "等待刷新" : "尚未配置 API Key" }) { Name = option.Slug });
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
                Dictionary<string, string> errors = new Dictionary<string, string>();

                try
                {
                    values["openai"] = CodexAppServerUsage.Read().ToDisplayString();
                }
                catch (Exception ex)
                {
                    Log.Warn("读取 GPT 限额失败", ex);
                    errors["openai"] = switcher.IsApiKeyMode()
                        ? "当前处于 DeepSeek / API Key 模式，切回 GPT 卡片后即可读取"
                        : "读取失败：" + ex.Message;
                }

                List<ModelOption> options = switcher.LoadModelOptions();
                if (SecretStore.Exists())
                {
                    string balance = null;
                    string balanceError = null;
                    try { balance = DeepSeekApi.GetBalance(SecretStore.Load("deepseek")); }
                    catch (Exception ex) { balanceError = ex.Message; Log.Warn("读取 DeepSeek 余额失败", ex); }
                    foreach (ModelOption option in options)
                    {
                        if (balance != null) values[option.Slug] = options.Count > 1 ? balance + "（共享账户）" : balance;
                        else errors[option.Slug] = "读取失败：" + balanceError;
                    }
                }
                foreach (ProviderProfile p in ProviderStore.Load())
                {
                    if (p.UsageUrl.Length == 0 || !SecretStore.Exists(p.Id)) continue;
                    try { values[p.Id] = GenericProviderApi.GetUsage(p, SecretStore.Load(p.Id)); }
                    catch (Exception ex) { errors[p.Id] = "读取失败：" + ex.Message; Log.Warn("读取 " + p.Name + " 用量失败", ex); }
                }
                if (IsDisposed) return;
                BeginInvoke((MethodInvoker)delegate
                {
                    foreach (KeyValuePair<string, string> item in values)
                    {
                        if (!list.Items.ContainsKey(item.Key)) continue;
                        list.Items[item.Key].SubItems[2].Text = item.Value;
                        lastGoodValues[item.Key] = item.Value;
                    }
                    foreach (KeyValuePair<string, string> item in errors)
                    {
                        if (!list.Items.ContainsKey(item.Key)) continue;
                        string text = item.Value;
                        string previous;
                        if (lastGoodValues.TryGetValue(item.Key, out previous)) text += "　·　上次成功：" + previous;
                        list.Items[item.Key].SubItems[2].Text = text;
                    }
                    refreshedLabel.Text = "上次刷新：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    refreshing = false;
                });
            });
        }

        protected override void Dispose(bool disposing) { if (disposing && timer != null) timer.Dispose(); base.Dispose(disposing); }

        private const int WmDpiChanged = 0x02E0;

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg != WmDpiChanged) return;
            try
            {
                ResetContentScale();
                PositionContent();
            }
            catch (Exception ex)
            {
                Log.Warn("DPI 变化后重新布局失败", ex);
            }
        }

        private void ResetContentScale()
        {
            if (contentScale <= 0.01F) { contentScale = 1F; return; }
            if (Math.Abs(contentScale - 1F) > 0.002F)
            {
                float inverse = 1F / contentScale;
                contentPanel.SuspendLayout();
                contentPanel.Scale(new SizeF(inverse, inverse));
                contentPanel.ResumeLayout(true);
            }
            contentPanel.Size = new Size(820, 540);
            contentScale = 1F;
        }
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
                if (limitsResponse.Contains("\"error\"")) throw new InvalidOperationException("当前 Codex 登录无法读取 ChatGPT 限额" + ServerErrorSuffix(limitsResponse));
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

        /// <summary>Pulls the JSON-RPC error message out of a failed reply so the UI can explain it.</summary>
        private static string ServerErrorSuffix(string response)
        {
            if (string.IsNullOrWhiteSpace(response)) return "";
            object root;
            if (Json.TryParse(response, out root))
            {
                string message = Json.Text(Json.Member(Json.Member(root, "error"), "message"));
                if (!string.IsNullOrWhiteSpace(message)) return "：" + message;
            }
            return "";
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
        /// <summary>Warns in the usage list when the account balance drops below this amount.</summary>
        internal const double LowBalanceThreshold = 5.0;

        public static void Test(string apiKey)
        {
            GetBalance(apiKey);
        }

        public static string GetBalance(string apiKey)
        {
            string json = GenericProviderApi.Get("https://api.deepseek.com/user/balance", apiKey, "DeepSeek");
            object root;
            if (!Json.TryParse(json, out root))
                throw new InvalidOperationException("DeepSeek 返回的数据无法解析为 JSON。");

            if (string.Equals(Json.Text(Json.Member(root, "is_available")), "false", StringComparison.OrdinalIgnoreCase))
                return "账户余额不可用";

            List<string> parts = new List<string>();
            double lowest = double.MaxValue;
            List<object> infos = Json.Array(Json.Member(root, "balance_infos"));
            if (infos != null)
            {
                foreach (object item in infos)
                {
                    string currency = Json.Text(Json.Member(item, "currency"));
                    string total = Json.Text(Json.Member(item, "total_balance"));
                    string granted = Json.Text(Json.Member(item, "granted_balance"));
                    string topped = Json.Text(Json.Member(item, "topped_up_balance"));
                    if (string.IsNullOrWhiteSpace(total) && string.IsNullOrWhiteSpace(granted) && string.IsNullOrWhiteSpace(topped)) continue;

                    string text = (string.IsNullOrWhiteSpace(total) ? "?" : total) + (string.IsNullOrWhiteSpace(currency) ? "" : " " + currency);
                    List<string> details = new List<string>();
                    if (!string.IsNullOrWhiteSpace(topped)) details.Add("充值 " + topped);
                    if (!string.IsNullOrWhiteSpace(granted)) details.Add("赠金 " + granted);
                    if (details.Count > 0) text += "（" + string.Join(" / ", details.ToArray()) + "）";
                    parts.Add(text);

                    double amount;
                    if (double.TryParse(total, NumberStyles.Float, CultureInfo.InvariantCulture, out amount) && amount < lowest) lowest = amount;
                }
            }
            if (parts.Count == 0) return "连接正常（接口未返回可显示余额）";
            string result = "可用余额：" + string.Join(" / ", parts.ToArray());
            if (lowest < LowBalanceThreshold) result = "⚠ 余额偏低 · " + result;
            return result;
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
            string raw = Get(profile.UsageUrl, apiKey, "用量接口");
            object root;
            if (Json.TryParse(raw, out root))
            {
                List<string> items = Json.Highlights(root, 6);
                if (items.Count > 0) return string.Join(" · ", items.ToArray());
            }
            string text = Regex.Replace(raw, "\\s+", " ").Trim();
            if (text.Length > 180) text = text.Substring(0, 177) + "…";
            return text;
        }

        internal static string Get(string url, string apiKey, string label)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET"; request.Timeout = 15000; request.ReadWriteTimeout = 15000;
            request.Headers[HttpRequestHeader.Authorization] = "Bearer " + apiKey;
            request.UserAgent = "CodexModelSwitcher/" + Program.Version;
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
            if (!IsKnownDeepSeekModel(model))
                throw new ArgumentException("不支持的 DeepSeek 模型。", "model");

            Directory.CreateDirectory(codexHome);
            Directory.CreateDirectory(appData);
            bool existed = File.Exists(ConfigPath);
            string current = existed ? File.ReadAllText(ConfigPath, Encoding.UTF8) : "";
            string backup = Backup(current, existed);

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
            EnsureUnchanged(ConfigPath, current, existed);
            WriteAtomic(ConfigPath, Normalize(result).TrimEnd() + Environment.NewLine);
            Log.Info("已切换到 " + model + "（备份：" + backup + "）");
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
            EnsureUnchanged(ConfigPath, currentConfig, existed);
            WriteAtomic(ConfigPath, Normalize(managed.ToString() + clean.TrimStart('\r', '\n') + provider.ToString()).TrimEnd() + Environment.NewLine);
            Log.Info("已切换到导入模型 " + profile.Id + "（" + profile.Name + " / " + profile.Model + "）");
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
            EnsureUnchanged(ConfigPath, current, exists);
            WriteAtomic(ConfigPath, Normalize(output.ToString()).TrimEnd() + Environment.NewLine);
            if (File.Exists(StatePath))
                File.Delete(StatePath);
            Log.Info("已恢复 GPT / OpenAI 配置（备份：" + backup + "）");
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
            PruneBackups(20);
            return path;
        }

        /// <summary>Keeps the backup folder bounded; the newest <paramref name="keep"/> files survive.</summary>
        private void PruneBackups(int keep)
        {
            try
            {
                string[] files = Directory.GetFiles(BackupDirectory, "config-*");
                if (files.Length <= keep) return;
                Array.Sort(files, delegate(string a, string b) { return File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)); });
                for (int i = keep; i < files.Length; i++)
                {
                    try { File.Delete(files[i]); } catch { }
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// Aborts instead of overwriting when config.toml changed after we read it, which happens
        /// when the Codex desktop app writes its own settings at the same moment.
        /// </summary>
        internal static void EnsureUnchanged(string path, string expectedContent, bool expectedExists)
        {
            bool exists = File.Exists(path);
            if (exists != expectedExists)
                throw new InvalidOperationException("Codex 配置在操作期间被其他程序改动，已中止以免覆盖你的设置。请重新点一次卡片。");
            if (!exists) return;
            string current = File.ReadAllText(path, Encoding.UTF8);
            if (!string.Equals(current, expectedContent, StringComparison.Ordinal))
                throw new InvalidOperationException("Codex 配置在操作期间被其他程序改动，已中止以免覆盖你的设置。请重新点一次卡片。");
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
            {
                if (!string.IsNullOrWhiteSpace(json)) Log.Warn("DeepSeek 官方模型目录内容与预期不符，改用内置目录。");
                json = FallbackCatalog;
            }
            else
            {
                object parsed;
                List<object> models = Json.TryParse(json, out parsed) ? Json.Array(Json.Member(parsed, "models")) : null;
                bool usable = models != null;
                if (usable)
                {
                    usable = false;
                    foreach (object item in models)
                    {
                        if (!string.IsNullOrWhiteSpace(Json.Text(Json.Member(item, "slug")))) { usable = true; break; }
                    }
                }
                if (!usable)
                {
                    Log.Warn("DeepSeek 官方模型目录结构无法识别，改用内置目录。");
                    json = FallbackCatalog;
                }
            }
            WriteAtomic(CatalogPath, json.Trim() + Environment.NewLine);
        }

        /// <summary>Refreshes the DeepSeek model catalog; safe to call from a background thread.</summary>
        public void RefreshCatalog()
        {
            try
            {
                EnsureCatalog();
            }
            catch (Exception ex)
            {
                Log.Warn("刷新 DeepSeek 模型目录失败", ex);
            }
        }

        /// <summary>
        /// DeepSeek models to show as cards. Driven by the catalog the switcher writes, so new
        /// official models appear without a new build; falls back to the built-in pair.
        /// </summary>
        public List<ModelOption> LoadModelOptions()
        {
            List<ModelOption> options = new List<ModelOption>();
            try
            {
                if (File.Exists(CatalogPath))
                {
                    object root;
                    if (Json.TryParse(File.ReadAllText(CatalogPath, Encoding.UTF8), out root))
                    {
                        List<object> models = Json.Array(Json.Member(root, "models"));
                        if (models != null)
                        {
                            foreach (object item in models)
                            {
                                string slug = Json.Text(Json.Member(item, "slug"));
                                if (string.IsNullOrWhiteSpace(slug)) continue;
                                string display = Json.Text(Json.Member(item, "display_name"));
                                options.Add(new ModelOption
                                {
                                    Slug = slug,
                                    DisplayName = ReadableName(slug, display),
                                    Description = DescribeModel(slug)
                                });
                                if (options.Count >= 6) break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("读取 DeepSeek 模型目录失败，使用内置列表", ex);
            }
            if (options.Count == 0)
            {
                options.Add(new ModelOption { Slug = "deepseek-flash", DisplayName = "DeepSeek Flash", Description = DescribeModel("deepseek-flash") });
                options.Add(new ModelOption { Slug = "deepseek-v4-pro", DisplayName = "DeepSeek V4 Pro", Description = DescribeModel("deepseek-v4-pro") });
            }
            return options;
        }

        public bool IsKnownDeepSeekModel(string slug)
        {
            if (string.IsNullOrWhiteSpace(slug)) return false;
            foreach (ModelOption option in LoadModelOptions())
            {
                if (string.Equals(option.Slug, slug, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>True when config.toml is currently forced into API-key auth (third-party mode).</summary>
        public bool IsApiKeyMode()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return false;
                string text = File.ReadAllText(ConfigPath, Encoding.UTF8);
                return Regex.IsMatch(text, "(?m)^\\s*forced_login_method\\s*=\\s*[\\\"']api[\\\"']");
            }
            catch (Exception ex)
            {
                Log.Warn("读取当前登录模式失败", ex);
                return false;
            }
        }

        internal static string DescribeModel(string slug)
        {
            if (slug == "deepseek-flash") return "支持图片输入，速度更快\n适合日常编码任务";
            if (slug == "deepseek-v4-pro") return "增强推理能力，回答更深入\n适合复杂和长周期任务";
            return "DeepSeek 官方模型目录中的模型\n点击切换并启动 Codex";
        }

        /// <summary>The catalog uses hyphenated names ("DeepSeek-V4-Pro"); show something readable.</summary>
        internal static string ReadableName(string slug, string displayName)
        {
            if (slug == "deepseek-flash") return "DeepSeek Flash";
            if (slug == "deepseek-v4-pro") return "DeepSeek V4 Pro";
            if (string.IsNullOrWhiteSpace(displayName)) return (slug ?? "").Replace('-', ' ');
            return displayName.Replace('-', ' ').Trim();
        }

        /// <summary>
        /// Returns the old executable path when config.toml still points at a previous location of
        /// this program, which would leave Codex unable to fetch the API key.
        /// </summary>
        public string DetectStaleAuthCommand()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return null;
                string text = File.ReadAllText(ConfigPath, Encoding.UTF8);
                if (text.IndexOf(BeginMarker, StringComparison.Ordinal) < 0) return null;
                string expected = executablePath.Replace('\\', '/');
                foreach (Match match in Regex.Matches(text, "(?m)^\\s*command\\s*=\\s*\"([^\"]+)\""))
                {
                    string value = match.Groups[1].Value;
                    if (!value.EndsWith("CodexModelSwitcher.exe", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(value, expected, StringComparison.OrdinalIgnoreCase)) continue;
                    // Only stale when the recorded program is really gone; otherwise the user may be
                    // running another copy on purpose and we must not hijack the configuration.
                    if (File.Exists(value.Replace('/', Path.DirectorySeparatorChar)))
                    {
                        Log.Info("配置中的取密钥命令指向另一个有效副本，保持不变：" + value);
                        continue;
                    }
                    return value;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("检查取密钥路径失败", ex);
            }
            return null;
        }

        /// <summary>Rewrites stale取密钥 paths to the current executable; returns true when it changed something.</summary>
        public bool RepairAuthCommand()
        {
            string stale = DetectStaleAuthCommand();
            if (stale == null) return false;
            string current = File.ReadAllText(ConfigPath, Encoding.UTF8);
            string expected = executablePath.Replace('\\', '/');
            string updated = Regex.Replace(
                current,
                "(?m)^(\\s*command\\s*=\\s*\")" + Regex.Escape(stale) + "(\"\\s*)$",
                "${1}" + expected.Replace("$", "$$") + "${2}");
            if (string.Equals(updated, current, StringComparison.Ordinal)) return false;
            Backup(current, true);
            WriteAtomic(ConfigPath, updated);
            Log.Info("已修复取密钥命令路径：" + stale + " → " + expected);
            return true;
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
            Exception last = null;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
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
                    return;
                }
                catch (IOException ex)
                {
                    last = ex;
                }
                catch (UnauthorizedAccessException ex)
                {
                    last = ex;
                }
                Thread.Sleep(150 * (attempt + 1));
            }
            throw new InvalidOperationException("写入配置失败：" + (last == null ? "未知原因" : last.Message), last);
        }

        private string DisplayName(string model)
        {
            foreach (ModelOption option in LoadModelOptions())
            {
                if (option.Slug == model) return option.DisplayName;
            }
            return model;
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
        private static readonly string[] ProcessNames = new string[] { "ChatGPT", "Codex", "codex" };

        private const int WmClose = 0x0010;

        private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        private static List<Process> RunningProcesses()
        {
            List<Process> processes = new List<Process>();
            foreach (string name in ProcessNames)
            {
                try { processes.AddRange(Process.GetProcessesByName(name)); }
                catch (Exception ex) { Log.Warn("枚举进程 " + name + " 失败", ex); }
            }
            return processes;
        }

        public static bool IsRunning()
        {
            List<Process> processes = RunningProcesses();
            int count = processes.Count;
            foreach (Process process in processes)
            {
                try { process.Dispose(); } catch { }
            }
            return count > 0;
        }

        /// <summary>
        /// Asks every Codex window to close and waits. Returns true when nothing is left running.
        ///
        /// The desktop app is an Electron application: Process.MainWindowHandle reports 0 for all of
        /// its processes, so the window has to be located by enumerating top-level windows instead.
        /// </summary>
        public static bool TryCloseAll(int timeoutMs)
        {
            List<Process> processes = RunningProcesses();
            if (processes.Count == 0) return true;

            Dictionary<uint, bool> targets = new Dictionary<uint, bool>();
            foreach (Process process in processes)
            {
                targets[(uint)process.Id] = true;
                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero) process.CloseMainWindow();
                }
                catch (Exception ex)
                {
                    Log.Warn("请求关闭 Codex 失败（PID " + process.Id + "）", ex);
                }
            }

            int posted = PostCloseToWindows(targets);
            Log.Info("已向 " + posted + " 个 Codex 窗口发送关闭请求");

            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                bool alive = false;
                foreach (Process process in processes)
                {
                    try { if (!process.HasExited) { alive = true; break; } } catch { }
                }
                if (!alive) return true;
                Thread.Sleep(250);
            }
            return !IsRunning();
        }

        /// <summary>Sends WM_CLOSE to every visible top-level window owned by the given processes.</summary>
        private static int PostCloseToWindows(Dictionary<uint, bool> processIds)
        {
            int count = 0;
            try
            {
                EnumWindows(delegate(IntPtr window, IntPtr parameter)
                {
                    uint owner;
                    GetWindowThreadProcessId(window, out owner);
                    if (!processIds.ContainsKey(owner)) return true;
                    if (!IsWindowVisible(window)) return true;
                    if (!PostMessage(window, WmClose, IntPtr.Zero, IntPtr.Zero)) return true;
                    count++;
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                Log.Warn("枚举 Codex 窗口失败", ex);
            }
            return count;
        }

        /// <summary>Force-terminates Codex; only called after the user explicitly confirms.</summary>
        public static void KillAll()
        {
            foreach (Process process in RunningProcesses())
            {
                try { process.Kill(); }
                catch (Exception ex) { Log.Warn("强制结束 Codex 失败（PID " + process.Id + "）", ex); }
            }
            Log.Warn("已按用户确认强制结束 Codex 进程");
        }

        /// <summary>Waits until no Codex process is left, so a relaunch cannot attach to a dying one.</summary>
        public static bool WaitUntilStopped(int timeoutMs)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (!IsRunning()) return true;
                Thread.Sleep(200);
            }
            return !IsRunning();
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

    /// <summary>Small rolling file log so failures are diagnosable after the fact.</summary>
    internal static class Log
    {
        private const long MaxBytes = 1024 * 1024;
        private const int KeepFiles = 7;
        private static readonly object Gate = new object();

        public static string DirectoryPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CodexModelSwitcher", "logs"); }
        }

        public static string CurrentFilePath
        {
            get { return Path.Combine(DirectoryPath, "switcher-" + DateTime.Now.ToString("yyyyMMdd") + ".log"); }
        }

        public static void Info(string message) { Write("INFO ", message, null); }
        public static void Warn(string message) { Write("WARN ", message, null); }
        public static void Warn(string message, Exception error) { Write("WARN ", message, error); }
        public static void Error(string message, Exception error) { Write("ERROR", message, error); }

        private static void Write(string level, string message, Exception error)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(DirectoryPath);
                    Rotate();
                    StringBuilder line = new StringBuilder();
                    line.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                    line.Append(" [").Append(level).Append("] ").Append(message);
                    if (error != null) line.Append(Environment.NewLine).Append(error);
                    line.Append(Environment.NewLine);
                    File.AppendAllText(CurrentFilePath, line.ToString(), new UTF8Encoding(false));
                }
            }
            catch
            {
                // Logging must never take the application down.
            }
        }

        private static void Rotate()
        {
            FileInfo current = new FileInfo(CurrentFilePath);
            if (!current.Exists || current.Length < MaxBytes) return;
            try { File.Move(CurrentFilePath, Path.Combine(DirectoryPath, "switcher-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log")); }
            catch { return; }

            string[] files = Directory.GetFiles(DirectoryPath, "switcher-*.log");
            if (files.Length <= KeepFiles) return;
            Array.Sort(files, delegate(string a, string b) { return File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)); });
            for (int i = KeepFiles; i < files.Length; i++)
            {
                try { File.Delete(files[i]); } catch { }
            }
        }
    }

    /// <summary>
    /// Minimal JSON reader. Enough to read provider balance/usage payloads without pulling in
    /// extra framework references, and deliberately strict: anything malformed fails the parse
    /// instead of producing half-guessed values.
    /// </summary>
    internal static class Json
    {
        public static bool TryParse(string text, out object value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            int index = 0;
            try
            {
                object parsed = ParseValue(text, ref index);
                Skip(text, ref index);
                if (index != text.Length) return false;
                value = parsed;
                return true;
            }
            catch
            {
                value = null;
                return false;
            }
        }

        public static Dictionary<string, object> Object(object node)
        {
            return node as Dictionary<string, object>;
        }

        public static List<object> Array(object node)
        {
            return node as List<object>;
        }

        public static object Member(object node, string key)
        {
            Dictionary<string, object> map = Object(node);
            if (map == null) return null;
            object value;
            return map.TryGetValue(key, out value) ? value : null;
        }

        public static string Text(object node)
        {
            if (node == null) return null;
            if (node is string) return (string)node;
            if (node is bool) return ((bool)node) ? "true" : "false";
            if (node is double)
            {
                double number = (double)node;
                if (Math.Abs(number) < 1e15 && number == Math.Floor(number)) return ((long)number).ToString(CultureInfo.InvariantCulture);
                return number.ToString("0.####", CultureInfo.InvariantCulture);
            }
            return null;
        }

        public static string TextAt(object node, string firstKey, string secondKey)
        {
            object value = Member(node, firstKey);
            if (value == null && secondKey != null) value = Member(node, secondKey);
            return Text(value);
        }

        /// <summary>
        /// Flattens scalar values into human-readable "name: value" pairs, preferring keys that
        /// look like balance/quota/usage numbers. Used for provider usage endpoints whose shape
        /// we cannot know in advance.
        /// </summary>
        public static List<string> Highlights(object root, int maxItems)
        {
            List<string> preferred = new List<string>();
            List<string> others = new List<string>();
            Collect(root, null, preferred, others);
            List<string> result = new List<string>();
            foreach (string item in preferred)
            {
                if (result.Count >= maxItems) return result;
                result.Add(item);
            }
            foreach (string item in others)
            {
                if (result.Count >= maxItems) return result;
                result.Add(item);
            }
            return result;
        }

        private static readonly string[] InterestingWords = new string[]
        {
            "balance", "credit", "quota", "limit", "remain", "usage", "used", "total", "amount", "available", "left"
        };

        private static void Collect(object node, string name, List<string> preferred, List<string> others)
        {
            Dictionary<string, object> map = Object(node);
            if (map != null)
            {
                foreach (KeyValuePair<string, object> pair in map)
                {
                    if (pair.Key.StartsWith("@", StringComparison.Ordinal) || pair.Key.StartsWith("_", StringComparison.Ordinal)) continue;
                    Collect(pair.Value, pair.Key, preferred, others);
                }
                return;
            }
            List<object> array = Array(node);
            if (array != null)
            {
                foreach (object item in array) Collect(item, name, preferred, others);
                return;
            }
            string text = Text(node);
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(name)) return;
            if (text.Length > 60) text = text.Substring(0, 57) + "…";
            string entry = name + ": " + text;
            foreach (string word in InterestingWords)
            {
                if (name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    preferred.Add(entry);
                    return;
                }
            }
            others.Add(entry);
        }

        private static void Skip(string text, ref int index)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
        }

        private static object ParseValue(string text, ref int index)
        {
            Skip(text, ref index);
            if (index >= text.Length) throw new FormatException("JSON 意外结束");
            char c = text[index];
            if (c == '{') return ParseObject(text, ref index);
            if (c == '[') return ParseArray(text, ref index);
            if (c == '"') return ParseString(text, ref index);
            if (c == 't') { Expect(text, ref index, "true"); return true; }
            if (c == 'f') { Expect(text, ref index, "false"); return false; }
            if (c == 'n') { Expect(text, ref index, "null"); return null; }
            return ParseNumber(text, ref index);
        }

        private static Dictionary<string, object> ParseObject(string text, ref int index)
        {
            Dictionary<string, object> map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            index++;
            Skip(text, ref index);
            if (index < text.Length && text[index] == '}') { index++; return map; }
            while (true)
            {
                Skip(text, ref index);
                if (index >= text.Length || text[index] != '"') throw new FormatException("JSON 对象缺少键名");
                string key = ParseString(text, ref index);
                Skip(text, ref index);
                if (index >= text.Length || text[index] != ':') throw new FormatException("JSON 对象缺少冒号");
                index++;
                map[key] = ParseValue(text, ref index);
                Skip(text, ref index);
                if (index >= text.Length) throw new FormatException("JSON 对象未闭合");
                if (text[index] == ',') { index++; continue; }
                if (text[index] == '}') { index++; return map; }
                throw new FormatException("JSON 对象中出现意外字符");
            }
        }

        private static List<object> ParseArray(string text, ref int index)
        {
            List<object> list = new List<object>();
            index++;
            Skip(text, ref index);
            if (index < text.Length && text[index] == ']') { index++; return list; }
            while (true)
            {
                list.Add(ParseValue(text, ref index));
                Skip(text, ref index);
                if (index >= text.Length) throw new FormatException("JSON 数组未闭合");
                if (text[index] == ',') { index++; continue; }
                if (text[index] == ']') { index++; return list; }
                throw new FormatException("JSON 数组中出现意外字符");
            }
        }

        private static string ParseString(string text, ref int index)
        {
            StringBuilder builder = new StringBuilder();
            index++;
            while (index < text.Length)
            {
                char c = text[index++];
                if (c == '"') return builder.ToString();
                if (c != '\\') { builder.Append(c); continue; }
                if (index >= text.Length) break;
                char escape = text[index++];
                switch (escape)
                {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case '/': builder.Append('/'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'u':
                        if (index + 4 > text.Length) throw new FormatException("JSON 转义不完整");
                        builder.Append((char)Convert.ToInt32(text.Substring(index, 4), 16));
                        index += 4;
                        break;
                    default: throw new FormatException("JSON 中出现未知转义");
                }
            }
            throw new FormatException("JSON 字符串未闭合");
        }

        private static double ParseNumber(string text, ref int index)
        {
            int start = index;
            while (index < text.Length && (char.IsDigit(text[index]) || text[index] == '-' || text[index] == '+' || text[index] == '.' || text[index] == 'e' || text[index] == 'E')) index++;
            string token = text.Substring(start, index - start);
            double value;
            if (token.Length == 0 || !double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                throw new FormatException("JSON 中出现非法数字");
            return value;
        }

        private static void Expect(string text, ref int index, string literal)
        {
            if (index + literal.Length > text.Length || string.CompareOrdinal(text, index, literal, 0, literal.Length) != 0)
                throw new FormatException("JSON 中出现非法字面量");
            index += literal.Length;
        }
    }

    internal sealed class ModelOption
    {
        public string Slug;
        public string DisplayName;
        public string Description;
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

                RunV16Checks(codex, data);
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

        /// <summary>Covers the 1.6 additions: JSON reader, concurrent-edit guard, path repair, backup retention.</summary>
        private static void RunV16Checks(string codex, string data)
        {
            object parsed;
            Assert(Json.TryParse("{\"a\":[1,2,{\"b\":\"c\\u0041\"}],\"d\":true,\"e\":null}", out parsed), "JSON 解析失败");
            Assert(Json.Text(Json.Member(parsed, "d")) == "true", "JSON 布尔解析错误");
            List<object> array = Json.Array(Json.Member(parsed, "a"));
            Assert(array != null && array.Count == 3, "JSON 数组解析错误");
            Assert(Json.Text(Json.Member(array[2], "b")) == "cA", "JSON 转义解析错误");
            Assert(!Json.TryParse("{bad}", out parsed), "损坏的 JSON 未被拒绝");
            Assert(!Json.TryParse("{\"a\":1} trailing", out parsed), "带尾部内容的 JSON 未被拒绝");
            Assert(!Json.TryParse("", out parsed), "空串不应被当作 JSON");

            string catalogSample = "{\"currency\":\"CNY\",\"balance_infos\":[{\"total_balance\":\"42.50\",\"granted_balance\":\"2.50\",\"topped_up_balance\":\"40.00\",\"currency\":\"CNY\"}]}";
            Assert(Json.TryParse(catalogSample, out parsed), "余额样例解析失败");
            List<string> highlights = Json.Highlights(parsed, 6);
            Assert(highlights.Count > 0, "用量摘要为空");
            Assert(string.Join(" ", highlights.ToArray()).Contains("total_balance: 42.5"), "用量摘要未包含余额");

            string configPath = Path.Combine(codex, "config.toml");
            string snapshot = File.ReadAllText(configPath);
            File.AppendAllText(configPath, "# changed by another program" + Environment.NewLine);
            bool blocked = false;
            try { Switcher.EnsureUnchanged(configPath, snapshot, true); }
            catch (InvalidOperationException) { blocked = true; }
            Assert(blocked, "并发修改配置未被拦截");
            File.WriteAllText(configPath, snapshot, new UTF8Encoding(false));
            Switcher.EnsureUnchanged(configPath, snapshot, true);

            Switcher first = new Switcher(codex, data, @"C:\Tools\One\CodexModelSwitcher.exe", false);
            first.ActivateDeepSeek("deepseek-flash");
            Assert(first.IsApiKeyMode(), "DeepSeek 模式未被识别为 API Key 模式");

            // A recorded path that still exists must be left alone, even if it is not the current exe.
            string survivingCopy = Path.Combine(data, "CodexModelSwitcher.exe");
            File.WriteAllText(survivingCopy, "not a real binary", new UTF8Encoding(false));
            new Switcher(codex, data, survivingCopy, false).ActivateDeepSeek("deepseek-flash");
            Switcher otherBuild = new Switcher(codex, data, @"C:\Tools\Two\CodexModelSwitcher.exe", false);
            Assert(otherBuild.DetectStaleAuthCommand() == null, "指向仍存在的副本时不应判定为失效");
            Assert(!otherBuild.RepairAuthCommand(), "指向仍存在的副本时不应改写配置");

            first.ActivateDeepSeek("deepseek-flash");
            Switcher moved = new Switcher(codex, data, @"C:\Tools\Two\CodexModelSwitcher.exe", false);
            Assert(moved.DetectStaleAuthCommand() != null, "未检测到失效的取密钥路径");
            Assert(moved.RepairAuthCommand(), "未能修复取密钥路径");
            Assert(File.ReadAllText(configPath).Contains("C:/Tools/Two/CodexModelSwitcher.exe"), "修复后的取密钥路径不正确");
            Assert(moved.DetectStaleAuthCommand() == null, "修复后仍报告失效路径");
            Assert(moved.RepairAuthCommand() == false, "无失效路径时不应再改动配置");

            for (int i = 0; i < 24; i++) moved.ActivateDeepSeek("deepseek-flash");
            string[] backups = Directory.GetFiles(Path.Combine(data, "backups"), "config-*");
            Assert(backups.Length <= 20, "备份数量没有按上限清理，当前 " + backups.Length + " 份");
            Assert(Directory.GetFiles(codex, "config.toml.cms-tmp").Length == 0, "临时文件未被清理");
            Assert(Directory.GetFiles(codex, "config.toml.cms-old").Length == 0, "临时文件未被清理");

            List<ModelOption> builtIn = new Switcher(codex, data, "x", false).LoadModelOptions();
            Assert(builtIn.Count == 2 && builtIn[0].Slug == "deepseek-flash", "内置模型列表不正确");
            string catalogPath = Path.Combine(data, "deepseek-models.json");
            File.WriteAllText(catalogPath,
                "{\"models\":[{\"slug\":\"deepseek-flash\",\"display_name\":\"DeepSeek Flash\"}," +
                "{\"slug\":\"deepseek-v4-pro\",\"display_name\":\"DeepSeek V4 Pro\"}," +
                "{\"slug\":\"deepseek-next\",\"display_name\":\"DeepSeek Next\"}]}", new UTF8Encoding(false));
            Switcher catalogDriven = new Switcher(codex, data, "x", false);
            List<ModelOption> options = catalogDriven.LoadModelOptions();
            Assert(options.Count == 3 && options[2].Slug == "deepseek-next", "目录驱动的模型列表不正确");
            Assert(catalogDriven.IsKnownDeepSeekModel("deepseek-next"), "目录中的新模型未被识别");
            Assert(!catalogDriven.IsKnownDeepSeekModel("deepseek-unknown"), "未知模型不应被识别");
        }
    }
}
