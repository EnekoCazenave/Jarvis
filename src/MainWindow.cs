using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Jarvis
{
    public sealed class MainWindow : Form
    {
        private readonly TextBox request = new TextBox();
        private readonly ComboBox scenario = new ComboBox();
        private readonly ComboBox operation = new ComboBox();
        private readonly ComboBox direction = new ComboBox();
        private readonly CheckBox requireScreen = new CheckBox();
        private readonly Label status = new Label();
        private readonly TextBox details = new TextBox();
        private readonly Button run = new Button();
        private readonly GroupBox confirmationPreview = new GroupBox();
        private readonly Label confirmationAction = new Label();
        private readonly TextBox confirmationSource = new TextBox();
        private readonly TextBox confirmationDestination = new TextBox();
        private readonly Label confirmationEffects = new Label();
        private readonly Label confirmationRequirement = new Label();
        private readonly Button confirmScreen = new Button();
        private readonly Button confirmVoice = new Button();
        private readonly Button refuse = new Button();
        private readonly string target, renamedTarget;
        private readonly DocumentPolicy policy;
        private readonly WindowsDocumentAdapter adapter;
        private Orchestrator orchestrator;
        private ConfirmationRequest displayedConfirmation;
        private bool busy;
        public TaskResult LastResult { get; private set; }

        public MainWindow(string witnessDirectory)
        {
            target = Path.GetFullPath(Path.Combine(witnessDirectory, "bonjour.txt"));
            renamedTarget = Path.GetFullPath(Path.Combine(witnessDirectory, "bonjour-renomme.txt"));
            policy = new DocumentPolicy(new[] { target, renamedTarget, Path.GetFullPath(Path.Combine(witnessDirectory, "absent.txt")) });
            adapter = new WindowsDocumentAdapter(policy);
            Text = "Jarvis — Démonstration locale";
            ClientSize = new Size(960, 900);
            MinimumSize = new Size(800, 740);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(242, 245, 250);
            Font = new Font("Segoe UI", 10);
            AutoScaleMode = AutoScaleMode.Dpi;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 3 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            header.Controls.Add(new Label { Text = "Jarvis", Font = new Font("Segoe UI", 23, FontStyle.Bold), AutoSize = true }, 0, 0);
            header.Controls.Add(new Label {
                Text = "MODE SIMULÉ • Aucun modèle ni serveur llama.cpp\nOuverture et renommage Windows réels. Accord vocal simulé par un bouton ; aucune voix n’est interprétée.",
                Dock = DockStyle.Fill, ForeColor = Color.FromArgb(42, 80, 140)
            }, 0, 1);
            root.Controls.Add(header, 0, 0);
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty };
            var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 0, Padding = new Padding(0, 0, 12, 6) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            scroll.Controls.Add(layout);
            root.Controls.Add(scroll, 0, 1);

            request.Name = "Request";
            request.AccessibleName = "Demande de démonstration";
            request.Text = "Ouvre le document témoin";
            AddRow(layout, Labeled("Votre demande de démonstration", request), 62);
            ConfigureChoice(operation, "Operation", "Opération", new object[] { "Ouvrir le document témoin", "Renommer le document témoin" });
            ConfigureChoice(direction, "RenameDirection", "Sens du renommage", new object[] {
                "bonjour.txt → bonjour-renomme.txt", "bonjour-renomme.txt → bonjour.txt"
            });
            var choices = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
            choices.Controls.Add(Labeled("Opération", operation), 0, 0);
            choices.Controls.Add(Labeled("Sens du renommage", direction), 1, 0);
            AddRow(layout, choices, 62);
            ConfigureChoice(scenario, "Scenario", "Scénario du moteur simulé", new object[] {
                "Proposer l’opération sélectionnée", "Proposer une cible inexistante", "Proposer une cible non autorisée",
                "Proposer un outil interdit", "Falsifier un accord dans la proposition"
            });
            AddRow(layout, Labeled("Réglage du moteur simulé — scénario déterministe", scenario), 62);
            requireScreen.Name = "RequireScreen";
            requireScreen.Text = "Exiger une confirmation à l’écran — règle de l’application";
            requireScreen.AccessibleName = requireScreen.Text;
            requireScreen.Dock = DockStyle.Fill;
            AddRow(layout, requireScreen, 32);
            AddRow(layout, new Label {
                Text = "Périmètre fixe : documents témoins .txt, lecture de 128 Kio maximum.\nRenommage limité à bonjour.txt ↔ bonjour-renomme.txt, sans écrasement, après accord.",
                Dock = DockStyle.Fill, ForeColor = Color.DimGray
            }, 49);
            run.Name = "Run";
            run.Text = "Exécuter la démonstration";
            run.AutoSize = true;
            run.BackColor = Color.FromArgb(35, 74, 130);
            run.ForeColor = Color.White;
            run.FlatStyle = FlatStyle.Flat;
            run.Click += delegate { RunRequest(); };
            AddRow(layout, run, 42);
            BuildConfirmationPreview();
            AddRow(layout, confirmationPreview, 0);
            AddRow(layout, new Label {
                Text = "Le scénario détermine la proposition, quelle que soit la formulation saisie. Modifier une demande ou un réglage annule la confirmation en attente.",
                Dock = DockStyle.Fill, ForeColor = Color.DimGray
            }, 48);

            var result = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(0, 10, 0, 0) };
            result.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            result.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            result.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            status.Text = "En attente de demande";
            status.Name = "TaskState";
            status.Dock = DockStyle.Fill;
            status.Font = new Font("Segoe UI", 11, FontStyle.Bold);
            result.Controls.Add(status, 0, 0);
            details.Name = "TaskDetails";
            details.Multiline = true;
            details.ReadOnly = true;
            details.Dock = DockStyle.Fill;
            details.ScrollBars = ScrollBars.Vertical;
            details.Text = "Le moteur propose ; Jarvis autorise, exécute et vérifie.\r\nAucune opération sensible n’est exécutée avant l’accord demandé.";
            result.Controls.Add(details, 0, 1);
            root.Controls.Add(result, 0, 2);
            Controls.Add(root);
            request.TextChanged += delegate { InputChanged(); };
            scenario.SelectedIndexChanged += delegate { InputChanged(); };
            operation.SelectedIndexChanged += delegate { InputChanged(); };
            direction.SelectedIndexChanged += delegate { InputChanged(); };
            requireScreen.CheckedChanged += delegate { InputChanged(); };
            FormClosing += delegate { if (orchestrator != null) orchestrator.InvalidateConfirmation(); };
            FormClosed += delegate { adapter.Dispose(); };
            UpdateEnabledControls();
        }

        private void BuildConfirmationPreview()
        {
            confirmationPreview.Name = "ConfirmationPreview";
            confirmationPreview.Text = "Confirmation du renommage";
            confirmationPreview.Dock = DockStyle.Top;
            confirmationPreview.Height = 273;
            confirmationPreview.Padding = new Padding(12, 8, 12, 12);
            confirmationPreview.BackColor = Color.FromArgb(228, 237, 250);
            confirmationPreview.Visible = false;
            var preview = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6 };
            preview.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            preview.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            int[] heights = { 28, 32, 32, 66, 38, 42 };
            foreach (int height in heights) preview.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            AddPreviewField(preview, 0, "Action", confirmationAction, "ConfirmationAction");
            AddPreviewField(preview, 1, "Source", confirmationSource, "ConfirmationSource");
            AddPreviewField(preview, 2, "Destination", confirmationDestination, "ConfirmationDestination");
            AddPreviewField(preview, 3, "Effets", confirmationEffects, "ConfirmationEffects");
            AddPreviewField(preview, 4, "Accord requis", confirmationRequirement, "ConfirmationRequirement");
            confirmationSource.ReadOnly = confirmationDestination.ReadOnly = true;
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            ConfigureConfirmationButton(confirmScreen, "ConfirmScreen", "Confirmer à l’écran");
            ConfigureConfirmationButton(confirmVoice, "ConfirmVoice", "Accord vocal SIMULÉ");
            ConfigureConfirmationButton(refuse, "Refuse", "Refuser");
            confirmScreen.Click += delegate { Confirm(ConfirmationChannel.Screen); };
            confirmVoice.Click += delegate { Confirm(ConfirmationChannel.SimulatedVoice); };
            refuse.Click += delegate {
                if (orchestrator != null && displayedConfirmation != null && !busy)
                    LastResult = orchestrator.Refuse(displayedConfirmation.Id);
            };
            buttons.Controls.AddRange(new Control[] { confirmScreen, confirmVoice, refuse });
            preview.Controls.Add(buttons, 0, 5);
            preview.SetColumnSpan(buttons, 2);
            confirmationPreview.Controls.Add(preview);
        }

        private static void ConfigureChoice(ComboBox choice, string name, string accessibleName, object[] items)
        {
            choice.Name = name;
            choice.AccessibleName = accessibleName;
            choice.DropDownStyle = ComboBoxStyle.DropDownList;
            choice.Items.AddRange(items);
            choice.SelectedIndex = 0;
            choice.Dock = DockStyle.Fill;
        }

        private static Control Labeled(string caption, Control control)
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 0, 6, 0) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(new Label { Text = caption, Dock = DockStyle.Fill }, 0, 0);
            control.Dock = DockStyle.Fill;
            layout.Controls.Add(control, 0, 1);
            return layout;
        }

        private static void AddRow(TableLayoutPanel layout, Control control, int height)
        {
            int row = layout.RowCount++;
            layout.RowStyles.Add(height == 0 ? new RowStyle(SizeType.AutoSize) : new RowStyle(SizeType.Absolute, height));
            layout.Controls.Add(control, 0, row);
        }

        private static void AddPreviewField(TableLayoutPanel layout, int row, string caption, Control value, string name)
        {
            layout.Controls.Add(new Label { Text = caption, Dock = DockStyle.Fill }, 0, row);
            value.Name = name;
            value.AccessibleName = caption + " de la confirmation";
            value.Dock = DockStyle.Fill;
            layout.Controls.Add(value, 1, row);
        }

        private static void ConfigureConfirmationButton(Button button, string name, string caption)
        {
            button.Name = name;
            button.Text = caption;
            button.AutoSize = true;
            button.Height = 32;
            button.Margin = new Padding(0, 0, 10, 0);
        }

        private void InputChanged()
        {
            if (orchestrator != null) LastResult = orchestrator.InvalidateConfirmation();
            UpdateEnabledControls();
        }

        private void RunRequest()
        {
            if (busy) return;
            busy = true;
            UpdateEnabledControls();
            try
            {
                if (orchestrator != null) orchestrator.InvalidateConfirmation();
                orchestrator = new Orchestrator(new SimulatedReasoner((SimulationScenario)scenario.SelectedIndex), policy, adapter,
                    new WindowsWitnessRenameAdapter(target, renamedTarget));
                orchestrator.Changed += ShowResult;
                LastResult = operation.SelectedIndex == 0 ? orchestrator.Run(request.Text, target) :
                    orchestrator.PrepareRename(request.Text, direction.SelectedIndex == 0 ? target : renamedTarget,
                        direction.SelectedIndex == 0 ? renamedTarget : target, requireScreen.Checked);
            }
            finally { busy = false; UpdateEnabledControls(); }
        }

        private void Confirm(ConfirmationChannel channel)
        {
            if (orchestrator == null || displayedConfirmation == null || busy) return;
            Guid id = displayedConfirmation.Id;
            busy = true;
            UpdateEnabledControls();
            try { LastResult = orchestrator.Confirm(id, channel); }
            finally { busy = false; UpdateEnabledControls(); }
        }

        private void ShowResult(TaskResult task)
        {
            LastResult = task;
            status.Text = StateLabel(task.State);
            details.Text = "Cible proposée : " + task.Target + "\r\n" + task.Message;
            displayedConfirmation = task.State == TaskState.AwaitingConfirmation ? task.Confirmation : null;
            confirmationPreview.Visible = displayedConfirmation != null;
            if (displayedConfirmation != null)
            {
                confirmationAction.Text = displayedConfirmation.Action;
                confirmationSource.Text = displayedConfirmation.Source;
                confirmationDestination.Text = displayedConfirmation.Destination;
                confirmationEffects.Text = displayedConfirmation.Effects;
                confirmationRequirement.Text = displayedConfirmation.Requirement == ConfirmationRequirement.ScreenOnly ?
                    "À l’écran obligatoire. Un accord vocal simulé sera refusé." : "À l’écran ou par accord vocal simulé pour ce renommage limité.";
            }
            UpdateEnabledControls();
            status.Refresh(); details.Refresh();
        }

        private void UpdateEnabledControls()
        {
            run.Enabled = request.Enabled = scenario.Enabled = operation.Enabled = !busy;
            direction.Enabled = requireScreen.Enabled = !busy && operation.SelectedIndex == 1;
            confirmScreen.Enabled = confirmVoice.Enabled = refuse.Enabled = !busy && displayedConfirmation != null;
            run.Text = operation.SelectedIndex == 0 ? "Exécuter la démonstration" : "Préparer le renommage";
        }

        private static string StateLabel(TaskState state)
        {
            switch (state)
            {
                case TaskState.Preparing: return "Préparation — moteur simulé";
                case TaskState.AwaitingConfirmation: return "En attente de votre confirmation";
                case TaskState.Refused: return "Refusé — aucun changement";
                case TaskState.Cancelled: return "Confirmation périmée — aucun changement";
                case TaskState.Ready: return "Prêt à exécuter — cible autorisée";
                case TaskState.Executing: return "Exécution et vérification";
                case TaskState.Succeeded: return "Réussite vérifiée";
                case TaskState.Uncertain: return "Résultat incertain";
                case TaskState.Failed: return "Échec — opération non exécutée";
                default: return "En attente de demande";
            }
        }
    }
}
