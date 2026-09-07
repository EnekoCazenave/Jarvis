using System;
using System.IO;
using System.Windows.Forms;
using System.Drawing;

namespace Jarvis.Tests
{
    internal static class WindowsTests
    {
        public static void Run()
        {
            string folder = Path.Combine(Path.GetTempPath(), "jarvis-windows-" + Guid.NewGuid());
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "bonjour.txt");
            File.WriteAllText(path, "Preuve Windows réelle : été, café, français.\r\nTicket #2.");
            try
            {
                using (var adapter = new WindowsDocumentAdapter(new DocumentPolicy(new[] { path })))
                {
                    var orchestrator = new Orchestrator(new SimulatedReasoner(SimulationScenario.Open), new DocumentPolicy(new[] { path }), adapter);
                    var result = orchestrator.Run("Ouvre le document témoin", path);
                    TestRunner.Check(result.State == TaskState.Succeeded, result.Message);
                    TestRunner.Check(result.Evidence.Description.EndsWith("E2CD8A32A9DE6ACCD010249992907DD56E1CE8B900CB6BCF80B0D5AAB627191D."), "Known real document content fingerprint");
                    TestRunner.Check(Application.OpenForms.Count == 1, "A real Windows document window opened");
                    foreach (Control control in Application.OpenForms[0].Controls)
                    {
                        var text = control as TextBox;
                        if (text != null) TestRunner.Check(text.Visible && text.ReadOnly && text.Text == "Preuve Windows réelle : été, café, français.\r\nTicket #2.", "Real reader displays the expected text read-only");
                    }
                    Console.WriteLine("PASS Windows real file -> visible document window: " + result.Evidence.Description);
                }
                using (var window = new MainWindow(folder))
                {
                    window.Show();
                    Application.DoEvents();
                    var request = (TextBox)window.Controls.Find("Request", true)[0];
                    var scenario = (ComboBox)window.Controls.Find("Scenario", true)[0];
                    var button = (Button)window.Controls.Find("Run", true)[0];
                    request.Text = "Ouvre mon document témoin";
                    button.PerformClick();
                    TestRunner.Check(window.LastResult.State == TaskState.Succeeded, "Full UI to Windows path succeeds");
                    using (var screenshot = new Bitmap(window.Width, window.Height))
                    {
                        window.DrawToBitmap(screenshot, new Rectangle(Point.Empty, screenshot.Size));
                        screenshot.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "windows-ui.png"));
                    }
                    int opened = Application.OpenForms.Count;
                    for (int index = 1; index <= 3; index++)
                    {
                        scenario.SelectedIndex = index;
                        button.PerformClick();
                        TestRunner.Check(window.LastResult.State == TaskState.Failed, "UI simulator setting changes the actual proposal");
                        TestRunner.Check(Application.OpenForms.Count == opened, "Refused UI scenario opens no new window");
                    }
                    window.Close();
                    TestRunner.Check(Application.OpenForms.Count == 0, "Test closes only its own readers");
                    Console.WriteLine("PASS real Windows UI request -> orchestrator -> policy -> reader, plus three refusal settings");
                }
            }
            finally { Directory.Delete(folder, true); }
        }
    }
}
