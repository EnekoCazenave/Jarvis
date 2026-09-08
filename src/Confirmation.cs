using System;

namespace Jarvis
{
    public enum ConfirmationChannel { Screen, SimulatedVoice }
    public enum ConfirmationRequirement { VoiceOrScreen, ScreenOnly }

    public sealed class RenameSnapshot
    {
        public readonly string Source, Destination, Version;
        public RenameSnapshot(string source, string destination, string version)
        { Source = source; Destination = destination; Version = version; }
    }

    public sealed class RenameEvidence
    {
        public readonly string Source, Destination, Description;
        public readonly bool Verified;
        public RenameEvidence(string source, string destination, bool verified, string description)
        { Source = source; Destination = destination; Verified = verified; Description = description; }
    }

    public interface IWitnessRenameAdapter
    {
        RenameSnapshot Prepare(string source, string destination);
        RenameEvidence Execute(RenameSnapshot snapshot);
    }

    public sealed class ConfirmationRequest
    {
        public readonly Guid Id;
        public readonly string Action, Source, Destination, Effects;
        public readonly ConfirmationRequirement Requirement;
        internal ConfirmationRequest(RenameSnapshot snapshot, ConfirmationRequirement requirement)
        {
            Id = Guid.NewGuid();
            Action = "Renommer le document témoin";
            Source = snapshot.Source;
            Destination = snapshot.Destination;
            Effects = "Le nom actuel disparaîtra ; le même document portera le nouveau nom, sans écrasement. " +
                "Le renommage inverse permet de revenir en arrière après un nouvel accord.";
            Requirement = requirement;
        }
    }
}
