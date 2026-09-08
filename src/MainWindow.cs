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
        private readonly Label status = new Label();
        private readonly TextBox details = new TextBox();
        private readonly Button run = new Button();
        private readonly string target;
        private readonly DocumentPolicy policy;
        private readonly WindowsDocumentAdapter adapter;
        public TaskResult LastResult { get; private set; }

        public MainWindow(string witnessDirectory)
        {
            target = Path.GetFullPath(Path.Combine(witnessDirectory, "bonjour.txt"));
            policy = new DocumentPolicy(new[] { target, Path.GetFullPath(Path.Combine(witnessDirectory, "absent.txt")) });
            adapter = new WindowsDocumentAdapter(policy);
            Text = "Jarvis — Démonstration locale";
            ClientSize = new Size(850, 680);
            MinimumSize = new Size(760, 700);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(242, 245, 250);
            Font = new Font("Segoe UI", 10);
            AutoScaleMode = AutoScaleMode.Dpi;

            var layout = new TableLayoutPanel {
                Dock = DockStyle.Fill, Padding = new Padding(28), ColumnCount = 1, RowCount = 11,
                AutoScroll = true
            };
            int[] heights = { 45, 62, 32, 38, 32, 42, 65, 44, 38, 110 };
            foreach (int height in heights) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(new Label { Text = "Jarvis", Font = new Font("Segoe UI", 23, FontStyle.Bold), AutoSize = true }, 0, 0);
            layout.Controls.Add(new Label {
                Text = "MODE SIMULÉ • Aucun modèle ni serveur llama.cpp\nOuverture de fichier Windows réelle. Aucune compréhension vocale ou libre n’est validée.",
                Dock = DockStyle.Fill, ForeColor = Color.FromArgb(42, 80, 140)
            }, 0, 1);
            layout.Controls.Add(new Label { Text = "Votre demande de démonstration", AutoSize = true }, 0, 2);
            request.Name = "Request";
            request.AccessibleName = "Demande de démonstration";
            request.Text = "Ouvre le document témoin";
            request.Dock = DockStyle.Fill;
            layout.Controls.Add(request, 0, 3);
            layout.Controls.Add(new Label { Text = "Réglage du moteur simulé — scénario déterministe", AutoSize = true }, 0, 4);
            scenario.Name = "Scenario";
            scenario.AccessibleName = "Scénario du moteur simulé";
            scenario.DropDownStyle = ComboBoxStyle.DropDownList;
            scenario.Items.AddRange(new object[] { "Ouvrir le document témoin", "Proposer une cible inexistante", "Proposer une cible non autorisée", "Proposer un outil interdit" });
            scenario.SelectedIndex = 0;
            scenario.Dock = DockStyle.Fill;
            layout.Controls.Add(scenario, 0, 5);
            layout.Controls.Add(new Label {
                Text = "Document sélectionné : " + target + "\nPermission fixe : témoins .txt uniquement, lecture seule, 128 Kio maximum.",
                Dock = DockStyle.Fill, AutoEllipsis = true
            }, 0, 6);
            run.Name = "Run";
            run.Text = "Exécuter la démonstration";
            run.AutoSize = true;
            run.BackColor = Color.FromArgb(35, 74, 130);
            run.ForeColor = Color.White;
            run.FlatStyle = FlatStyle.Flat;
            run.Click += delegate { RunRequest(); };
            layout.Controls.Add(run, 0, 7);
            status.Text = "En attente de demande";
            status.Name = "TaskState";
            status.Dock = DockStyle.Fill;
            status.Font = new Font("Segoe UI", 11, FontStyle.Bold);
            layout.Controls.Add(status, 0, 8);
            details.Name = "TaskDetails";
            details.Multiline = true;
            details.ReadOnly = true;
            details.Dock = DockStyle.Fill;
            details.ScrollBars = ScrollBars.Vertical;
            details.Text = "Le moteur propose ; Jarvis autorise, exécute et vérifie.\r\nLe scénario choisi détermine la proposition, quelle que soit la formulation saisie.";
            layout.Controls.Add(details, 0, 9);
            layout.Controls.Add(new Label {
                Text = "Le lecteur intégré affiche le texte sans exécuter de contenu. Les réglages de voix, mémoire et modèles réels seront ajoutés dans les tickets suivants.",
                Dock = DockStyle.Fill, ForeColor = Color.DimGray
            }, 0, 10);
            Controls.Add(layout);
            FormClosed += delegate { adapter.Dispose(); };
        }

        private void RunRequest()
        {
            run.Enabled = request.Enabled = scenario.Enabled = false;
            try
            {
                var orchestrator = new Orchestrator(new SimulatedReasoner((SimulationScenario)scenario.SelectedIndex), policy, adapter);
                orchestrator.Changed += task => {
                    status.Text = StateLabel(task.State);
                    details.Text = "Cible proposée : " + task.Target + "\r\n" + task.Message;
                    status.Refresh(); details.Refresh();
                };
                LastResult = orchestrator.Run(request.Text, target);
            }
            finally { run.Enabled = request.Enabled = scenario.Enabled = true; }
        }

        private static string StateLabel(TaskState state)
        {
            switch (state)
            {
                case TaskState.Preparing: return "Préparation — moteur simulé";
                case TaskState.Ready: return "Prêt à exécuter — cible autorisée";
                case TaskState.Executing: return "Exécution et vérification";
                case TaskState.Succeeded: return "Réussite vérifiée";
                case TaskState.Uncertain: return "Résultat incertain";
                case TaskState.Failed: return "Échec — aucune ouverture";
                default: return "En attente de demande";
            }
        }
    }
}
