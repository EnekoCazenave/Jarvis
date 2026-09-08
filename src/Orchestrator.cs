using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

namespace Jarvis
{
    public enum TaskState { Waiting, Preparing, Ready, Executing, Uncertain, Succeeded, Failed }
    public enum SimulationScenario { Open, MissingTarget, UnauthorizedTarget, UnauthorizedTool }

    public sealed class ReasoningInput
    {
        public readonly string Request;
        public readonly string Target;
        public readonly ReadOnlyCollection<string> Context;
        public readonly ReadOnlyCollection<string> AvailableTools;
        public ReasoningInput(string request, string target)
        {
            Request = request;
            Target = target;
            Context = Array.AsReadOnly(new[] { "Document témoin sélectionné par l’utilisateur : " + target });
            AvailableTools = Array.AsReadOnly(new[] { "open_document(path)" });
        }
    }

    public sealed class ActionProposal
    {
        public readonly string Tool;
        public readonly Dictionary<string, string> Parameters;
        public ActionProposal(string tool, Dictionary<string, string> parameters)
        { Tool = tool; Parameters = parameters; }
    }

    public interface IReasoner { ActionProposal Propose(ReasoningInput input); }
    public interface IDocumentAdapter { DocumentEvidence Open(string path); }

    public sealed class SimulatedReasoner : IReasoner
    {
        private readonly SimulationScenario scenario;
        public SimulatedReasoner(SimulationScenario scenario) { this.scenario = scenario; }
        public ActionProposal Propose(ReasoningInput input)
        {
            string target = input.Target;
            if (scenario == SimulationScenario.MissingTarget) target = Path.Combine(Path.GetDirectoryName(target), "absent.txt");
            if (scenario == SimulationScenario.UnauthorizedTarget) target = Path.Combine(Path.GetTempPath(), "hors-perimetre.txt");
            return new ActionProposal(scenario == SimulationScenario.UnauthorizedTool ? "run_command" : "open_document",
                new Dictionary<string, string> { { "path", target } });
        }
    }

    public sealed class DocumentEvidence
    {
        public readonly string Target;
        public readonly bool Visible;
        public readonly string Description;
        public DocumentEvidence(string target, bool visible, string description)
        { Target = target; Visible = visible; Description = description; }
    }

    public sealed class TaskResult
    {
        public readonly TaskState State;
        public readonly string Target;
        public readonly string Message;
        public readonly DocumentEvidence Evidence;
        public TaskResult(TaskState state, string target, string message, DocumentEvidence evidence = null)
        { State = state; Target = target; Message = message; Evidence = evidence; }
    }

    public sealed class DocumentPolicy
    {
        private readonly HashSet<string> allowed;
        public DocumentPolicy(IEnumerable<string> paths)
        { allowed = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase); }
        public string Authorize(ActionProposal proposal)
        {
            string target;
            if (proposal == null || proposal.Tool != "open_document" || proposal.Parameters == null ||
                proposal.Parameters.Count != 1 || !proposal.Parameters.TryGetValue("path", out target) || String.IsNullOrWhiteSpace(target))
                throw new InvalidOperationException("Outil ou paramètres non autorisés.");
            return AuthorizePath(target);
        }
        public string AuthorizePath(string target)
        {
            if (!Path.IsPathRooted(target) || target.StartsWith(@"\\") || target.IndexOf(':', 2) >= 0)
                throw new InvalidOperationException("Seul un chemin local absolu est autorisé.");
            string full = Path.GetFullPath(target);
            if (!allowed.Contains(full) || !String.Equals(Path.GetExtension(full), ".txt", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Cible non autorisée : seuls les documents témoins sont accessibles.");
            if (!File.Exists(full)) throw new FileNotFoundException("Le document témoin n’existe pas.", full);
            return full;
        }
    }

    public sealed class Orchestrator
    {
        private readonly IReasoner reasoner;
        private readonly DocumentPolicy policy;
        private readonly IDocumentAdapter adapter;
        private bool running;
        public event Action<TaskResult> Changed;
        public TaskResult Current { get; private set; }
        public Orchestrator(IReasoner reasoner, DocumentPolicy policy, IDocumentAdapter adapter)
        {
            this.reasoner = reasoner; this.policy = policy; this.adapter = adapter;
            Current = new TaskResult(TaskState.Waiting, "", "En attente de demande.");
        }
        public TaskResult Run(string request, string selectedTarget)
        {
            if (running) return Current;
            running = true;
            string target = selectedTarget;
            bool executionStarted = false;
            try
            {
                Publish(TaskState.Preparing, target, "Préparation par le moteur simulé.");
                if (String.IsNullOrWhiteSpace(request)) throw new InvalidOperationException("Saisissez une demande de démonstration.");
                ActionProposal proposal = reasoner.Propose(new ReasoningInput(request, selectedTarget));
                string proposedTarget;
                if (proposal != null && proposal.Parameters != null && proposal.Parameters.TryGetValue("path", out proposedTarget)) target = proposedTarget;
                target = policy.Authorize(proposal);
                if (!String.Equals(target, Path.GetFullPath(selectedTarget), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("La proposition ne correspond pas au document sélectionné.");
                Publish(TaskState.Ready, target, "Ouverture du document témoin autorisée.");
                Publish(TaskState.Executing, target, "Lecture du fichier et vérification de la fenêtre Windows.");
                executionStarted = true;
                DocumentEvidence evidence = adapter.Open(target);
                if (evidence == null || !evidence.Visible || !String.Equals(evidence.Target, target, StringComparison.OrdinalIgnoreCase) || String.IsNullOrWhiteSpace(evidence.Description))
                    return Publish(TaskState.Uncertain, target, "Ouverture non vérifiée. Aucune relance automatique.");
                return Publish(TaskState.Succeeded, target, "Réussite vérifiée. " + evidence.Description, evidence);
            }
            catch (Exception error)
            {
                return Publish(executionStarted ? TaskState.Uncertain : TaskState.Failed, target,
                    error.Message + (executionStarted ? " Effet éventuel non vérifié ; aucune relance automatique." : " Aucune ouverture exécutée."));
            }
            finally { running = false; }
        }
        private TaskResult Publish(TaskState state, string target, string message, DocumentEvidence evidence = null)
        {
            Current = new TaskResult(state, target, message, evidence);
            var changed = Changed;
            if (changed != null) changed(Current);
            return Current;
        }
    }
}
