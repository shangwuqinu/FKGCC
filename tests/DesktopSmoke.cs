// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace Fkgcc.Tests
{
    // Run on an STA thread without a visible window or touching a device.
    // A regression that enables an Apply button in preview must fail this test.
    internal static class DesktopSmoke
    {
        [STAThread]
        private static int Main()
        {
            try
            {
                Application.EnableVisualStyles();
                Type formType = Assembly.GetExecutingAssembly().GetType("Fkgcc.LightingForm");
                Require(formType != null, "Desktop preview is not implemented.");
                using (Form form = (Form)Activator.CreateInstance(formType, new object[] { true }))
                {
                    Button apply = Find<Button>(form, "applyLighting");
                    Button off = Find<Button>(form, "turnOffLighting");
                    Button probe = Find<Button>(form, "probeDevice");
                    Button hardware = Find<Button>(form, "takeControl");
                    Require(!apply.Enabled && !off.Enabled && !probe.Enabled && !hardware.Enabled,
                        "Preview must never expose device actions.");

                    Find<TextBox>(form, "colorHex").Text = "#12ABEF";
                    Find<TrackBar>(form, "brightness").Value = 24;
                    Find<ComboBox>(form, "lightingMode").SelectedIndex = 2;
                    Require(!apply.Enabled && !off.Enabled,
                        "Editing a preview must not enable hardware actions.");

                    // An invalid, disabled single-color field must not prevent
                    // turning lights off or using an automatic color cycle.
                    Find<TextBox>(form, "colorHex").Text = "#ZZZZZZ";
                    Find<ComboBox>(form, "lightingMode").SelectedIndex = 1;
                    MethodInfo capture = formType.GetMethod("CaptureSetting", BindingFlags.Instance | BindingFlags.NonPublic);
                    LightingSetting offSetting = (LightingSetting)capture.Invoke(form, new object[] { false });
                    Require(offSetting.Mode == LightingMode.Off && offSetting.ColorHex == "#FFFFFF",
                        "Off mode must normalize the unused invalid color instead of blocking the action.");
                    Find<ComboBox>(form, "lightingMode").SelectedIndex = 4;
                    LightingSetting cycleSetting = (LightingSetting)capture.Invoke(form, new object[] { false });
                    Require(cycleSetting.Mode == LightingMode.Cycle && cycleSetting.ColorHex == "#FFFFFF",
                        "Color cycle must normalize the unused invalid color.");
                    Find<TextBox>(form, "colorHex").Text = "#12ABEF";
                    Find<ComboBox>(form, "lightingMode").SelectedIndex = 2;

                    foreach (HeaderDefinition header in BoardLayout.Headers)
                        Require(Find<CheckBox>(form, "header_" + header.Key).Checked,
                            "Every physical header must be selected initially: " + header.Key);

                    form.ClientSize = new System.Drawing.Size(960, 680);
                    form.PerformLayout();
                    Require(Find<Button>(form, "saveProfile").Enabled,
                        "Preview should still allow users to prepare a local profile.");
                    string previewPath = Path.Combine(Path.GetTempPath(), "FKGCC-DesktopSmoke-" + Guid.NewGuid().ToString("N") + ".png");
                    try
                    {
                        MethodInfo render = formType.GetMethod("RenderPreview", BindingFlags.Instance | BindingFlags.NonPublic);
                        render.Invoke(form, new object[] { previewPath });
                        Require(File.Exists(previewPath) && new FileInfo(previewPath).Length > 0,
                            "Preview rendering must create a PNG without requiring a device.");
                    }
                    finally { if (File.Exists(previewPath)) File.Delete(previewPath); }
                }
#if DESKTOP_SMOKE_STUBS
                Require(LightingClient.ProbeCalls == 0 && LightingClient.ApplyCalls == 0,
                    "Constructing, editing and rendering a preview must not access hardware.");
#endif
                Console.WriteLine("PASS: preview is hardware-free, physical headers are editable, and unused colors do not block Off/Cycle.");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("FAIL: " + exception.GetBaseException().Message);
                return 1;
            }
        }

        private static T Find<T>(Control parent, string name) where T : Control
        {
            Control[] controls = parent.Controls.Find(name, true);
            Require(controls.Length == 1 && controls[0] is T, "Required control is missing: " + name);
            return (T)controls[0];
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}

#if DESKTOP_SMOKE_STUBS
// Hardware is the external dependency: these complete contract fixtures throw
// if UI code crosses that boundary. The real form and its event handlers run.
namespace Fkgcc
{
    public enum LightingMode { Off = 0, Static = 1, Breathing = 2, Blink = 3, Cycle = 4 }
    public class HeaderDefinition
    {
        public string Key;
        public string Label;
        public int Channel;
        public bool Addressable;
    }
    public static class BoardLayout
    {
        public static readonly string BoardName = "B650M AORUS ELITE AX ICE";
        public static HeaderDefinition[] Headers
        {
            get
            {
                return new HeaderDefinition[] {
                    new HeaderDefinition { Key = "D_LED1", Label = "D_LED1", Channel = 5, Addressable = true },
                    new HeaderDefinition { Key = "D_LED2", Label = "D_LED2", Channel = 6, Addressable = true },
                    new HeaderDefinition { Key = "LED_C1", Label = "LED_C1", Channel = 1, Addressable = false },
                    new HeaderDefinition { Key = "LED_C2", Label = "LED_C2", Channel = 4, Addressable = false },
                    new HeaderDefinition { Key = "CPU_LED", Label = "CPU_LED", Channel = 2, Addressable = false }
                };
            }
        }
    }
    public class LightingSetting
    {
        public string[] Headers;
        public LightingMode Mode;
        public string ColorHex = "#7C5CFF";
        public int BrightnessPercent = 65;
        public int Speed = 5;
        public void Validate() { }
    }
    public class ProbeResult
    {
        public bool IsSupported;
        public string BoardName;
        public string Status;
        public string FirmwareVersion;
        public string ControllerProduct;
        public string DevicePath;
        public string[] Conflicts;
    }
    public static class LightingClient
    {
        public static int ProbeCalls;
        public static int ApplyCalls;
        public static ProbeResult Probe() { ProbeCalls++; throw new InvalidOperationException("Preview called Probe."); }
        public static string Apply(LightingSetting setting) { ApplyCalls++; throw new InvalidOperationException("Preview called Apply."); }
        public static string TakeControl() { ApplyCalls++; throw new InvalidOperationException("Preview called TakeControl."); }
    }
    public static class ProfileStore
    {
        public static void Save(string path, LightingSetting setting) { throw new InvalidOperationException("Not used by smoke test."); }
        public static LightingSetting Load(string path) { throw new InvalidOperationException("Not used by smoke test."); }
    }
}
#endif
