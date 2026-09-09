using System;
using System.IO;

namespace Jarvis
{
    public sealed partial class Orchestrator
    {
        private RenameSnapshot pendingRename;
        private ConfirmationRequest pendingConfirmation;

        // Called by the application. The model receives neither the agreement nor its channel rule.
        public TaskResult PrepareRename(string request, string source, string destination, bool requireScreen = false)
        {
            lock (gate)
            {
                if (running) return Current;
                running = true;
                pendingRename = null;
                pendingConfirmation = null;
                try
                {
                    Publish(TaskState.Preparing, source, "Préparation du renommage par le moteur simulé.");
                    if (String.IsNullOrWhiteSpace(request)) throw new InvalidOperationException("Saisissez une demande de démonstration.");
                    if (renameAdapter == null) throw new InvalidOperationException("Renommage témoin indisponible.");
                    string selectedSource = policy.AuthorizePath(source);
                    string selectedDestination = policy.AuthorizeDestination(destination);
                    var proposal = reasoner.Propose(new ReasoningInput(request, selectedSource, selectedDestination));
                    string proposedSource, proposedDestination;
                    if (proposal == null || proposal.Tool != "rename_witness" || proposal.Parameters == null ||
                        proposal.Parameters.Count != 2 || !proposal.Parameters.TryGetValue("path", out proposedSource) ||
                        !proposal.Parameters.TryGetValue("destination", out proposedDestination))
                        throw new InvalidOperationException("Outil ou paramètres non autorisés ; le moteur ne peut pas fournir un accord.");
                    if (!SamePath(policy.AuthorizePath(proposedSource), selectedSource) ||
                        !SamePath(policy.AuthorizeDestination(proposedDestination), selectedDestination) || SamePath(selectedSource, selectedDestination))
                        throw new InvalidOperationException("La proposition ne correspond pas au renommage sélectionné.");
                    RenameSnapshot snapshot = renameAdapter.Prepare(selectedSource, selectedDestination);
                    if (snapshot == null || !SamePath(snapshot.Source, selectedSource) || !SamePath(snapshot.Destination, selectedDestination) ||
                        String.IsNullOrWhiteSpace(snapshot.Version))
                        throw new InvalidOperationException("La cible du renommage n’a pas pu être vérifiée.");
                    pendingRename = snapshot;
                    pendingConfirmation = new ConfirmationRequest(snapshot,
                        requireScreen ? ConfirmationRequirement.ScreenOnly : ConfirmationRequirement.VoiceOrScreen);
                    return Publish(TaskState.AwaitingConfirmation, source, "Consultez les effets, puis acceptez ou refusez.",
                        confirmation: pendingConfirmation);
                }
                catch (Exception error)
                {
                    pendingRename = null;
                    pendingConfirmation = null;
                    return Publish(TaskState.Failed, source, error.Message + " Aucun renommage exécuté.");
                }
                finally { running = false; }
            }
        }

        public TaskResult Confirm(Guid confirmationId, ConfirmationChannel channel)
        {
            lock (gate)
            {
                if (running || pendingConfirmation == null || pendingConfirmation.Id != confirmationId) return Current;
                running = true;
                bool executionStarted = false;
                RenameSnapshot snapshot = pendingRename;
                try
                {
                    if ((channel != ConfirmationChannel.Screen && channel != ConfirmationChannel.SimulatedVoice) ||
                        (pendingConfirmation.Requirement == ConfirmationRequirement.ScreenOnly && channel != ConfirmationChannel.Screen))
                        return Publish(TaskState.AwaitingConfirmation, snapshot.Source,
                            "Accord refusé pour ce canal. Une confirmation à l’écran est requise.", confirmation: pendingConfirmation);

                    // Consume BEFORE callbacks and execution: replay and reentrancy cannot reuse an agreement.
                    pendingConfirmation = null;
                    pendingRename = null;
                    policy.AuthorizePath(snapshot.Source);
                    policy.AuthorizeDestination(snapshot.Destination);
                    var current = renameAdapter.Prepare(snapshot.Source, snapshot.Destination);
                    if (current == null || !SamePath(current.Source, snapshot.Source) || !SamePath(current.Destination, snapshot.Destination) ||
                        current.Version != snapshot.Version)
                        throw new InvalidOperationException("La cible a changé. Accord périmé ; préparez une nouvelle confirmation.");
                    Publish(TaskState.Ready, snapshot.Source, "Accord reçu pour le renommage affiché.");
                    Publish(TaskState.Executing, snapshot.Source, "Renommage du témoin et vérification du résultat.");
                    executionStarted = true;
                    var evidence = renameAdapter.Execute(snapshot);
                    if (evidence == null || !evidence.Verified || !SamePath(evidence.Source, snapshot.Source) ||
                        !SamePath(evidence.Destination, snapshot.Destination) || String.IsNullOrWhiteSpace(evidence.Description))
                        return Publish(TaskState.Uncertain, snapshot.Destination, "Renommage non vérifié. Aucune relance automatique.");
                    return Publish(TaskState.Succeeded, snapshot.Destination, "Réussite vérifiée. " + evidence.Description, renameEvidence: evidence);
                }
                catch (Exception error)
                {
                    return Publish(executionStarted ? TaskState.Uncertain : TaskState.Failed, snapshot.Source,
                        error.Message + (executionStarted ? " Effet éventuel non vérifié ; aucune relance automatique." : " Aucun renommage exécuté."));
                }
                finally { running = false; }
            }
        }

        public TaskResult Refuse(Guid confirmationId)
        {
            lock (gate)
            {
                if (running || pendingConfirmation == null || pendingConfirmation.Id != confirmationId) return Current;
                return EndConfirmation(TaskState.Refused, "Renommage refusé. Aucun changement effectué.");
            }
        }

        public TaskResult InvalidateConfirmation()
        {
            lock (gate)
            {
                if (running || pendingConfirmation == null) return Current;
                return EndConfirmation(TaskState.Cancelled, "Demande ou réglage modifié. Accord périmé ; préparez une nouvelle confirmation.");
            }
        }

        private TaskResult EndConfirmation(TaskState state, string message)
        {
            pendingConfirmation = null;
            pendingRename = null;
            return Publish(state, Current.Target, message);
        }

        private static bool SamePath(string first, string second)
        { return String.Equals(first, second, StringComparison.OrdinalIgnoreCase); }
    }
}
