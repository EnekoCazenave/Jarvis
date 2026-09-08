using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Jarvis.Tests
{
    internal static class ConfirmationWindowTests
    {
        public static void Run()
        {
            string folder = Path.Combine(Path.GetTempPath(), "jarvis-confirmation-ui-" + Guid.NewGuid());
            Directory.CreateDirectory(folder);
            string source = Path.Combine(folder, "bonjour.txt");
            string destination = Path.Combine(folder, "bonjour-renomme.txt");
            const string content = "Témoin de confirmation : été, café, français.\r\nTicket #3.";
            File.WriteAllText(source, content);
            try
            {
                using (var window = new MainWindow(folder))
                {
                    window.Show();
                    Application.DoEvents();
                    var operation = Find<ComboBox>(window, "Operation");
                    var direction = Find<ComboBox>(window, "RenameDirection");
                    var request = Find<TextBox>(window, "Request");
                    var scenario = Find<ComboBox>(window, "Scenario");
                    var requireScreen = Find<CheckBox>(window, "RequireScreen");
                    var run = Find<Button>(window, "Run");
                    var confirmScreen = Find<Button>(window, "ConfirmScreen");
                    var confirmVoice = Find<Button>(window, "ConfirmVoice");
                    var refuse = Find<Button>(window, "Refuse");
                    var preview = Find<GroupBox>(window, "ConfirmationPreview");
                    operation.SelectedIndex = 1;
                    request.Text = "Renomme le document témoin";
                    run.PerformClick();
                    TestRunner.Check(window.LastResult.State == TaskState.AwaitingConfirmation && preview.Visible, "UI prepares and shows confirmation before the effect");
                    TestRunner.Check(File.Exists(source) && !File.Exists(destination) && File.ReadAllText(source) == content, "Preparing has no file effect");
                    TestRunner.Check(Find<TextBox>(window, "ConfirmationSource").Text == source &&
                        Find<TextBox>(window, "ConfirmationDestination").Text == destination &&
                        Find<Label>(window, "ConfirmationAction").Text.Contains("Renommer") &&
                        Find<Label>(window, "ConfirmationEffects").Text.Contains("sans écrasement"), "UI identifies exact action, targets and effects");
                    TestRunner.Check(request.Enabled && operation.Enabled && run.Enabled, "Pending confirmation allows editing the request");
                    refuse.PerformClick();
                    TestRunner.Check(window.LastResult.State == TaskState.Refused && !preview.Visible && !confirmScreen.Enabled,
                        "Refusal hides and disables the pending agreement");
                    TestRunner.Check(File.Exists(source) && !File.Exists(destination) && File.ReadAllText(source) == content, "UI refusal changes no file");

                    requireScreen.Checked = true;
                    run.PerformClick();
                    Application.DoEvents();
                    using (var screenshot = new Bitmap(window.Width, window.Height))
                    {
                        window.DrawToBitmap(screenshot, new Rectangle(Point.Empty, screenshot.Size));
                        screenshot.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "confirmation-ui.png"));
                    }
                    confirmVoice.PerformClick();
                    TestRunner.Check(window.LastResult.State == TaskState.AwaitingConfirmation && preview.Visible &&
                        window.LastResult.Message.Contains("écran"), "Screen-only rule rejects simulated voice visibly");
                    TestRunner.Check(File.Exists(source) && !File.Exists(destination), "Rejected oral channel has no effect");
                    confirmScreen.PerformClick();
                    TestRunner.Check(window.LastResult.State == TaskState.Succeeded && !preview.Visible &&
                        !File.Exists(source) && File.ReadAllText(destination) == content, "Screen agreement renames the actual witness");
                    confirmScreen.PerformClick();
                    TestRunner.Check(!File.Exists(source) && File.ReadAllText(destination) == content, "Completed agreement button cannot execute again");

                    direction.SelectedIndex = 1;
                    requireScreen.Checked = false;
                    run.PerformClick();
                    TestRunner.Check(window.LastResult.State == TaskState.AwaitingConfirmation &&
                        window.LastResult.Confirmation.Source == destination && window.LastResult.Confirmation.Destination == source,
                        "Reverse rename requires its own confirmation");
                    confirmVoice.PerformClick();
                    TestRunner.Check(window.LastResult.State == TaskState.Succeeded && File.ReadAllText(source) == content &&
                        !File.Exists(destination), "Limited reverse rename accepts simulated voice after a new agreement");

                    direction.SelectedIndex = 0;
                    run.PerformClick();
                    TestRunner.Check(preview.Visible, "New confirmation is visible before request edit");
                    request.Text = "Demande modifiée après préparation";
                    TestRunner.Check(window.LastResult.State == TaskState.Cancelled && !preview.Visible &&
                        !confirmScreen.Enabled && !confirmVoice.Enabled, "Request edit expires and disables the confirmation");
                    confirmScreen.PerformClick();
                    TestRunner.Check(File.Exists(source) && !File.Exists(destination) && File.ReadAllText(source) == content,
                        "Expired UI agreement changes no file");
                    Action[] edits = {
                        delegate { requireScreen.Checked = !requireScreen.Checked; },
                        delegate { direction.SelectedIndex = 1; },
                        delegate { scenario.SelectedIndex = 4; },
                        delegate { operation.SelectedIndex = 0; }
                    };
                    foreach (Action edit in edits)
                    {
                        operation.SelectedIndex = 1;
                        direction.SelectedIndex = 0;
                        scenario.SelectedIndex = 0;
                        run.PerformClick();
                        edit();
                        TestRunner.Check(window.LastResult.State == TaskState.Cancelled && !preview.Visible && !confirmScreen.Enabled,
                            "Changing the operation, target, scenario or screen rule expires the pending agreement");
                    }
                    operation.SelectedIndex = 1;
                    scenario.SelectedIndex = 4;
                    run.PerformClick();
                    TestRunner.Check(window.LastResult.State == TaskState.Failed && !preview.Visible &&
                        File.ReadAllText(source) == content && !File.Exists(destination), "A forged model agreement from the UI cannot rename the witness");
                    scenario.SelectedIndex = 0;
                    run.PerformClick();
                    window.Close();
                    TestRunner.Check(window.LastResult.State == TaskState.Cancelled && File.Exists(source) && !File.Exists(destination),
                        "Closing the window invalidates a pending confirmation without effect");
                }
                Console.WriteLine("PASS Windows confirmation UI: preview, refusal, screen rule, oral simulation, inverse, expiration and close");
            }
            finally { Directory.Delete(folder, true); }
        }

        private static T Find<T>(Control parent, string name) where T : Control
        { return (T)parent.Controls.Find(name, true)[0]; }
    }
}
